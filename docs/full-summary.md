# Switcher — Full Application & Problem Summary

## 1. Application Overview

**Switcher** is a Windows desktop app (.NET 8.0, WPF) that detects when text is typed in the wrong keyboard layout (English QWERTY / Ukrainian ЙЦУКЕН) and corrects it automatically or on demand via hotkeys. It runs as a system tray application.

**Repository:** https://github.com/stokrattt/EN-UA_Switcher

---

## 2. Architecture

```
┌────────────────────────────────────────────────────────────┐
│  Switcher.App (WPF)                                        │
│  - MainWindow (settings UI, 4 tabs)                        │
│  - DiagnosticsWindow (real-time correction log)            │
│  - TrayIcon (system tray with context menu)                │
├────────────────────────────────────────────────────────────┤
│  Switcher.Engine                                           │
│  - SwitcherEngine (orchestrator, owns all components)      │
│  - KeyboardObserver (WH_KEYBOARD_LL hook, keystroke buffer)│
│  - AutoModeHandler (auto-correct on word boundary)         │
│  - SafeModeHandler (hotkey-triggered correction)           │
│  - TextTargetCoordinator (routes to correct text adapter)  │
│  - GlobalHotkeyManager (RegisterHotKey for safe mode)      │
│  - ExclusionManager (per-process skip list)                │
│  - DiagnosticsLogger (correction event log)                │
│  - SendInputAdapter (keyboard simulation via SendInput)    │
├────────────────────────────────────────────────────────────┤
│  Switcher.Infrastructure                                   │
│  - NativeMethods (all Win32 P/Invoke)                      │
│  - ForegroundContextProvider (GetForegroundWindow info)     │
│  - NativeEditTargetAdapter (Win32 EDIT/RichEdit via WM_*)  │
│  - UIAutomationTargetAdapter (UIA for browser inputs)      │
│  - ITextTargetAdapter (interface for text targets)          │
├────────────────────────────────────────────────────────────┤
│  Switcher.Core                                             │
│  - CorrectionHeuristics (the brain: scoring & evaluation)  │
│  - KeyboardLayoutMap (EN↔UA character mapping tables)      │
│  - AppSettings / SettingsManager (JSON persistence)        │
│  - WordList (dictionary loader for en-common.txt/ua-common)│
│  - CorrectionCandidate (result DTO)                        │
│  - DiagnosticEntry (log entry DTO)                         │
│  - Dictionaries/en-common.txt, ua-common.txt               │
└────────────────────────────────────────────────────────────┘
```

---

## 3. Two Operating Modes

### Auto Mode

- **Trigger:** Space, Enter, or Tab (configurable which ones)
- **Flow:**
  1. `WH_KEYBOARD_LL` hook captures every non-injected key-down
  2. Keys go into `KeyboardObserver._scanBuffer` as tuples: `(effectiveScan, rawScan, vk, shift, flags)`
  3. On delimiter key, observer builds two word interpretations:
     - `CurrentWordEN` — scan codes mapped to QWERTY (e.g., scan 0x23→'h', 0x12→'e', etc.)
     - `CurrentWordUA` — EN chars converted through `KeyboardLayoutMap.GetEnToUaMap()`
  4. `AutoModeHandler.OnWordBoundary()` is called synchronously in the hook callback
  5. Heuristics evaluate both interpretations → if one converts better, queue replacement on threadpool
  6. Replacement via `SendInput`: Backspace×N to erase + Unicode chars to type replacement
  7. After replacement, switch input language via `PostMessage(WM_INPUTLANGCHANGEREQUEST)` + fallback toggle simulation

### Safe Mode

- **Trigger:** Global hotkeys (Ctrl+Shift+K for last word, Ctrl+Shift+L for selection)
- **Flow:** Uses `TextTargetCoordinator` → `NativeEditTargetAdapter` or `UIAutomationTargetAdapter` to read/replace text in the focused control

---

## 4. Key Components Detail

### KeyboardObserver (keystroke buffer)

- Installs `WH_KEYBOARD_LL` hook
- Tracks modifier state (Shift, Ctrl, Alt, CapsLock) manually from hook events
- Tracks foreground app by PID (not HWND — Chrome has internal popup windows that change HWND)
- On each key-down:
  - Skips injected events (from our own SendInput)
  - Skips keys when Ctrl or Alt held (shortcuts, not text)
  - For typable keys: recovers scan code via `MapVirtualKeyEx(vk, MAPVK_VK_TO_VSC, hkl)` if raw scan looks suspicious
  - Adds to buffer: `(effectiveScan, rawScan, vk, shift, flags)`
- On delimiter: builds EN/UA words, fires `WordBoundaryDetected(wordLen)`
- Has separate VK-only interpretations (`CurrentWordEN_VkOnly`, `CurrentWordUA_VkOnly`) as fallback
- `PickBestEN()` chooses between scan-based and VK-based: prefers scan unless scan has genuine garbage while VK is all letters
- `CountSequentialScans()` — counts adjacent rawScan pairs differing by ±1 (Chrome garbage detector)

### CorrectionHeuristics (the brain)

- Conservative: false negatives OK, false positives NOT OK
- Mixed-script words → never convert
- Short words (≤2 letters) → skip in Auto mode
- Two thresholds: AutoThreshold=0.75, SafeThreshold=0.50
- Scoring based on: dictionary lookup, bigram frequency (60 EN + 80 UA bigrams), letter patterns
- For Auto mode EN→UA: requires converted text in UA dictionary
- For Auto mode UA→EN: normal threshold, no extra dictionary gate

### NativeMethods (P/Invoke)

- All Win32 API declarations in one file
- `SendInput` with proper INPUT struct (includes MOUSEINPUT for correct struct size on x64)
- `MakeKeyInput()` — standard key simulation
- `MakeExtKeyInput()` — with `KEYEVENTF_EXTENDEDKEY` flag (required for arrow keys in Chrome)
- `MakeUnicodeInput()` — Unicode character simulation
- Clipboard API: `GetClipboardText()`, `SetClipboardText()`
- Language switching: `SwitchInputLanguage()` with PostMessage + simulated hotkey fallback

---

## 5. THE CHROME PROBLEM — Current Status

### What Works

- Auto mode works perfectly in **Notepad, WordPad, any native Win32 EDIT control**
- Auto mode works in **Electron apps** (Telegram, VS Code, Slack) — they pass real scan/VK codes
- Safe mode (hotkeys) works in **all apps** including Chrome, Edge, Electron, contenteditable
- All 169 unit tests pass (152 Core + 17 Engine)

### What's Broken: Auto Mode in Chrome/Edge (Chromium browsers)

#### Root Cause Discovery

Chrome sends **COMPLETELY FAKE scan AND VK codes** through the `WH_KEYBOARD_LL` hook:

**Example:** User types "hello" (h, e, l, l, o) in Chrome:

```
Key pressed: H  →  hook receives: scan=0x24, vk=0x4A  (should be scan=0x23, vk=0x48)
Key pressed: E  →  hook receives: scan=0x25, vk=0x48  (should be scan=0x12, vk=0x45)
Key pressed: L  →  hook receives: scan=0x26, vk=0x4C  (should be scan=0x26, vk=0x4C) ← coincidentally correct
Key pressed: L  →  hook receives: scan=0x27, vk=0x4C  (should be scan=0x26, vk=0x4C) ← scan wrong
Key pressed: O  →  hook receives: scan=0x28, vk=0x05  (should be scan=0x18, vk=0x4F)
```

**Key observations:**

- **Scan codes are sequential counters** (0x24, 0x25, 0x26, 0x27, 0x28 — each +1)
- **VK codes are also garbage** (random values, not corresponding to actual keys)
- `MapVirtualKeyEx(fakeVk) → fakeScan` — recovery doesn't help because the VK is also fake
- This is NOT a rare edge case — it happens **consistently** in Chrome/Edge for every word

#### Detection Mechanism (Working)

`CountSequentialScans()` counts adjacent rawScan pairs that differ by exactly 1. When `seq ≥ 3`, both scan and VK data is definitively garbage. This detection is **reliable and confirmed working** via diagnostics screenshots.

#### Current Workaround: Clipboard Fallback (Partially Working)

When Chrome garbage is detected, the normal Backspace+Type replacement can't work (we don't know what was typed). Instead, `ClipboardFallback` tries:

1. **Wait 150ms** for Chrome to finish processing typed characters
2. **Shift+Left×N** to select the word (using `KEYEVENTF_EXTENDEDKEY` for arrow keys)
3. **Ctrl+C** to copy selected text to clipboard
4. **Read clipboard** to get the actual text that appeared on screen
5. **Evaluate heuristics** on the real text
6. **Shift+Left×N again** to re-select, then **type replacement** (Unicode chars overwrite selection)
7. **Re-inject the delimiter** (space/enter) that was suppressed

#### What's Happening in Practice

From user testing on April 12, 2026:

- The user reports: "it works but poorly" — selection is **visually visible** (flickers), which is jarring
- One word converted successfully, subsequent ones did not
- Example output typed: `привіт привіт hello zr d nt,t cghfdb`
- Expected: each word should have been corrected independently
- The clipboard fallback is **sometimes succeeding** (one word corrected) but **inconsistent**

#### Specific Technical Issues

1. **Visible selection flicker** — The Shift+Left selection is visible to the user. There's no way to hide it since we're using SendInput (not direct text manipulation).

2. **Timing sensitivity** — Chrome's async input pipeline means:
   - The 150ms initial wait may not be enough for all machines
   - The 100ms wait after selection may not be enough
   - The 150-200ms clipboard polling may miss the window

3. **Multi-word failure** — After the first correction, the subsequent words may fail because:
   - The buffer's `_keysSinceDelimiter` count may be wrong after clipboard operations
   - The language switch after first correction may confuse subsequent word detection
   - Chrome may process our SendInput arrow keys differently after a replacement

4. **The clipboard approach is fundamentally fragile** because:
   - It's a multi-step async sequence (select → copy → read → re-select → type)
   - Each step depends on Chrome processing the previous SendInput call
   - Any timing mismatch breaks the entire chain
   - The user's clipboard gets momentarily cleared (saved/restored, but race conditions possible)

### What Would Actually Fix This

The fundamental problem is that Chrome's keyboard hook data is garbage. Possible approaches:

1. **IME-level integration** — Register as an IME/TIP (Text Input Processor) to get the actual text Chrome commits, bypassing the broken hook data entirely. This is the "proper" fix but very complex.

2. **UI Automation ValuePattern** — Read the text field value via UIA after each word boundary, diff against previous value to determine what was typed. Avoids clipboard entirely but UIA in Chrome is slow and asynchronous.

3. **Accessibility API (IAccessible2 / IA2)** — Chrome supports IA2 which may provide text content more reliably than the generic UIA path.

4. **Chrome DevTools Protocol** — For Chrome specifically, could connect via CDP to read input field values directly. Very Chrome-specific.

5. **Improved clipboard fallback timing** — The current approach could work more reliably with:
   - Adaptive timing based on machine speed
   - Verification loops (did the selection actually happen?)
   - Falling back gracefully when any step fails
   - Making the selection invisible somehow (offscreen window technique?)

---

## 6. File-by-File Reference

| File                                                       | Lines | Purpose                                                                          |
| ---------------------------------------------------------- | ----- | -------------------------------------------------------------------------------- |
| `src/Switcher.Core/CorrectionHeuristics.cs`                | ~300  | Scoring engine: bigram analysis, dictionary lookup, script classification        |
| `src/Switcher.Core/KeyboardLayoutMap.cs`                   | ~100  | Static EN↔UA char mapping tables                                                 |
| `src/Switcher.Core/AppSettings.cs`                         | ~80   | Settings POCO + JSON file manager (%APPDATA%\Switcher\settings.json)             |
| `src/Switcher.Core/WordList.cs`                            | ~30   | Loads dictionaries from embedded txt files                                       |
| `src/Switcher.Core/Dictionaries/en-common.txt`             | ~5000 | English word list                                                                |
| `src/Switcher.Core/Dictionaries/ua-common.txt`             | ~5000 | Ukrainian word list                                                              |
| `src/Switcher.Engine/KeyboardObserver.cs`                  | ~630  | WH_KEYBOARD_LL hook, keystroke buffer, scan/VK mapping, Chrome garbage detection |
| `src/Switcher.Engine/AutoModeHandler.cs`                   | ~420  | Auto-correction logic: L1 (scan), L2 (VK), L3 (clipboard fallback)               |
| `src/Switcher.Engine/SafeModeHandler.cs`                   | ~220  | Hotkey-triggered correction via TextTargetCoordinator                            |
| `src/Switcher.Engine/SwitcherEngine.cs`                    | ~145  | Lifecycle orchestrator, wires all components                                     |
| `src/Switcher.Engine/TextTargetCoordinator.cs`             | ~80   | Routes to NativeEdit or UIA adapter                                              |
| `src/Switcher.Engine/SendInputAdapter.cs`                  | ~60   | Keyboard simulation helpers                                                      |
| `src/Switcher.Engine/GlobalHotkeyManager.cs`               | ~100  | Win32 RegisterHotKey on background thread                                        |
| `src/Switcher.Engine/ExclusionManager.cs`                  | ~30   | Process exclusion list                                                           |
| `src/Switcher.Engine/DiagnosticsLogger.cs`                 | ~50   | Thread-safe event log                                                            |
| `src/Switcher.Infrastructure/NativeMethods.cs`             | ~440  | All P/Invoke: hooks, SendInput, clipboard, language switching                    |
| `src/Switcher.Infrastructure/NativeEditTargetAdapter.cs`   | ~180  | Win32 EDIT/RichEdit text manipulation via WM_GETTEXT/EM_REPLACESEL               |
| `src/Switcher.Infrastructure/UIAutomationTargetAdapter.cs` | ~120  | UI Automation ValuePattern for browser inputs                                    |
| `src/Switcher.Infrastructure/ForegroundContextProvider.cs` | ~60   | Gets foreground window info (HWND, PID, class, process name)                     |
| `src/Switcher.App/App.xaml.cs`                             | ~115  | WPF App: startup, tray icon, exit with settings save                             |
| `src/Switcher.App/MainWindow.xaml.cs`                      | ~110  | Settings window code-behind                                                      |
| `src/Switcher.App/MainWindow.xaml`                         | ~200  | Settings UI (4 tabs: General, Hotkeys, Exclusions, About)                        |
| `src/Switcher.App/DiagnosticsWindow.xaml.cs`               | ~60   | Real-time log viewer                                                             |
| `tests/Switcher.Core.Tests/CorrectionHeuristicsTests.cs`   | ~700  | 152 unit tests for heuristics                                                    |
| `tests/Switcher.Engine.Tests/SendInputAdapterTests.cs`     | ~100  | 17 unit tests for input simulation                                               |

---

## 7. Critical Data Flow (Auto Mode in Chrome)

```
User types "руддщ" + Space in Chrome (UA layout active)
    │
    ▼
WH_KEYBOARD_LL hook fires 5+1 times (5 keys + space)
    │  Chrome sends garbage: scan=0x24,0x25,0x26,0x27,0x28  vk=random
    │  MapVirtualKeyEx(fakeVk) → same fake scan (no recovery)
    │
    ▼
KeyboardObserver buffer: 5 entries with garbage scan+vk
    │  PickBestEN → garbage string (not "hello")
    │  CountSequentialScans → seq=4 (4 adjacent pairs differ by 1)
    │
    ▼
WordBoundaryDetected(5) → AutoModeHandler.OnWordBoundary(5)
    │  chromeGarbage = (seq=4 ≥ 3) → TRUE
    │  SuppressCurrentDelimiter() (space swallowed)
    │
    ▼
Task.Run → ClipboardFallback(wordLen=5, delimVk=SPACE)
    │
    ├─ Sleep(150ms) — wait for Chrome to commit characters
    ├─ Save clipboard, clear it
    ├─ SendInput: Shift↓, (ExtLeft↓↑)×5, Shift↑  — select 5 chars
    ├─ Sleep(100ms) — wait for selection
    ├─ SendInput: Ctrl↓, C↓↑, Ctrl↑ — copy
    ├─ Poll clipboard 5× (150ms, then 200ms each)
    │     └─ Read clipboard → "руддщ" (hopefully)
    ├─ Restore saved clipboard
    ├─ SendInput: ExtRight↓↑ — deselect
    │
    ├─ CorrectionHeuristics.Evaluate("руддщ", Auto)
    │     └─ Script=Cyrillic, convert UA→EN via layout map → "hello"
    │     └─ Score > 0.75, "hello" in EN dictionary → candidate found
    │
    ├─ SendInput: Shift↓, (ExtLeft↓↑)×5, Shift↑ — re-select
    ├─ Sleep(50ms)
    ├─ SendInput: Unicode "hello" — overwrites selection
    ├─ SwitchInputLanguage(toUkrainian=false)
    ├─ Sleep(30ms)
    └─ ReinjectDelimiter(SPACE)
```

**The problem is that multiple steps in this chain can fail due to Chrome's async processing, and each failure mode produces different symptoms.**
