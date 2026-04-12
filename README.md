# Switcher — EN/UA Keyboard Layout Corrector

A Windows desktop app that detects text typed in the wrong keyboard layout (English / Ukrainian) and corrects it — either automatically or via hotkeys.

Typed `руддщ` instead of `hello`? Switcher fixes it for you.

## Features

### Auto Mode
- Monitors keystrokes in real time via a low-level keyboard hook
- When you press **Space**, **Enter**, or **Tab**, the last word is evaluated
- If the word looks like it was typed in the wrong layout, it's replaced instantly
- Dictionary-backed heuristics for both EN→UA and UA→EN directions
- Strict mode option for higher-confidence corrections only

### Safe Mode (Manual Hotkeys)
| Hotkey | Action |
|--------|--------|
| **Ctrl+Shift+K** | Correct the last word before the caret |
| **Ctrl+Shift+L** | Correct the selected text |

### Other
- System tray icon with quick access to settings, diagnostics, and mode toggle
- Per-process exclusion list (skip correction in specific apps)
- Undo last correction with Backspace
- Cancel pending correction with Backspace or Left Arrow
- Diagnostics window showing real-time correction logs
- Settings persist across restarts (`%APPDATA%\Switcher\settings.json`)
- Start minimized to tray option

## Supported Targets

| Target | Status |
|--------|--------|
| Notepad, WordPad, classic Win32 EDIT/RichEdit | ✅ Fully supported |
| Browser text inputs (Chrome, Edge) via UI Automation | ⚠️ Partial (clipboard fallback for auto mode) |
| contenteditable, Monaco, CodeMirror, Electron apps | ❌ Not supported |

## Requirements

- Windows 10/11
- .NET 8.0 Desktop Runtime

## Build & Run

```bash
dotnet build Switcher.sln
dotnet run --project src/Switcher.App
```

## Project Structure

```
src/
  Switcher.Core/          # Heuristics, dictionaries, layout maps, settings
  Switcher.Engine/        # Keyboard hook, auto/safe mode handlers, input simulation
  Switcher.Infrastructure/# Win32 interop, text target adapters (native + UI Automation)
  Switcher.App/           # WPF application, tray icon, settings UI, diagnostics window
  Switcher.TestTarget/    # WinForms test app for manual testing
tests/
  Switcher.Core.Tests/    # Unit tests for heuristics and layout mapping
  Switcher.Engine.Tests/  # Unit tests for input simulation
```

## Settings

All settings are configurable from the Settings window (double-click tray icon):

| Setting | Default | Description |
|---------|---------|-------------|
| Auto Mode | Off | Enable automatic correction on word boundaries |
| Strict Auto Mode | On | Require higher confidence for auto corrections |
| Correct on Space | On | Trigger auto correction when Space is pressed |
| Correct on Enter | On | Trigger auto correction when Enter is pressed |
| Correct on Tab | Off | Trigger auto correction when Tab is pressed |
| Cancel on Backspace | On | Cancel pending correction when Backspace is pressed |
| Cancel on Left Arrow | On | Cancel pending correction when Left Arrow is pressed |
| Undo on Backspace | On | Undo last correction with Backspace immediately after |
| Start Minimized | On | Start the app minimized to the system tray |
| Diagnostics | On | Log correction events to the diagnostics window |

## How It Works

1. A global `WH_KEYBOARD_LL` hook captures every keystroke
2. Keys are buffered until a word boundary (space/enter/tab) is detected
3. The buffered scan codes are mapped to both EN and UA characters using `MapVirtualKeyEx`
4. `CorrectionHeuristics.Evaluate()` checks both interpretations against built-in dictionaries
5. If a correction is found, the original text is selected and replaced via `SendInput` (or clipboard fallback for browsers)
6. The keyboard layout is switched to match the corrected text direction

## License

MIT
