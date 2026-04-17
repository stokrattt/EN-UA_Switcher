# EN-UA Switcher QA Checklist

Last updated: 2026-04-13

Status legend:
- `PASS (auto)` = confirmed by automated tests in this repo
- `PASS (live)` = confirmed by local runtime/UI smoke check
- `MANUAL` = still needs a real app/runtime pass in the target environment
- `RISK` = known weak area, even if partial coverage exists

## 1. Build and launch

| Area | Case | Method | Status | Notes |
| --- | --- | --- | --- | --- |
| Build | `Switcher.App` builds | `dotnet build .\src\Switcher.App\Switcher.App.csproj -v minimal` | PASS (auto) | Current branch builds cleanly |
| Build | `Switcher.Engine.Tests` pass | `dotnet test .\tests\Switcher.Engine.Tests\Switcher.Engine.Tests.csproj -v minimal` | PASS (auto) | `41/41` |
| Build | `Switcher.Core.Tests` pass | `dotnet test .\tests\Switcher.Core.Tests\Switcher.Core.Tests.csproj -v minimal` | PASS (auto) | `158/158` |
| App startup | Settings window opens | Local UI smoke | PASS (live) | Window detected via UIAutomation |
| Single instance | Second app instance does not conflict with hotkeys | Regression tests + real second-launch message | PASS (auto) / MANUAL | Hotkey manager side is covered; second-launch UX should still be rechecked manually |

## 2. Settings UI

| Area | Case | Method | Status | Notes |
| --- | --- | --- | --- | --- |
| General | Main tabs render: General / Hotkeys / Exclusions / About | Local UI smoke | PASS (live) | Visible in current build |
| Save | Save applies settings without closing the window | MANUAL | Not re-run in this pass |
| Cancel | Settings `Cancel` reloads saved values and hides window | PASS (live) | Already smoke-checked via UIAutomation |
| Tray reopen | Settings can be reopened after hiding to tray | MANUAL | Needs tray interaction pass |
| About | About tab scroll works | MANUAL | Needs direct UI pass |
| Diagnostics button | Opens diagnostics window | MANUAL | Needs direct UI pass |

## 3. Tray menu

| Area | Case | Method | Status | Notes |
| --- | --- | --- | --- | --- |
| Tray | Icon renders with new runtime icon | PASS (live) | Runtime icon path updated |
| Tray | Context menu styling renders correctly | MANUAL | Needs visual pass |
| Tray | `Open Settings` opens window | MANUAL | Needs tray interaction pass |
| Tray | `Auto Mode` toggle updates state | MANUAL | Needs tray interaction pass |
| Tray | `View Diagnostics` opens diagnostics | MANUAL | Needs tray interaction pass |
| Tray | `Exit` shuts down cleanly | MANUAL | Needs tray interaction pass |

## 4. Auto mode

| Area | Case | Method | Status | Notes |
| --- | --- | --- | --- | --- |
| Heuristics | EN typo converts correctly (`ghbdsn -> привіт`) | PASS (auto) | Covered in core/heuristics regressions |
| Heuristics | UA typo converts correctly (`руддщ -> hello`) | PASS (auto) | Covered in core/heuristics regressions |
| Delimiters | `Correct on Space` path works | PASS (auto) | Hook/delimiter logic covered |
| Delimiters | `Correct on Enter` path works | MANUAL | Needs runtime delimiter pass |
| Delimiters | `Correct on Tab` path works | MANUAL | Needs runtime delimiter pass |
| Exclusions | Excluded process skips correction | MANUAL | No current end-to-end proof in this pass |
| Browser fallback | Browser path serializes operations and falls back UIA -> clipboard | PASS (auto) / RISK | Logic covered, real Chrome behavior still needs manual pass |
| Custom editors | `contenteditable` / Monaco / CodeMirror | MANUAL / RISK | Unit tests cannot prove these contexts |

## 4a. Auto mode — Electron selection-race regression

These scenarios target the bug where in Electron apps (VS Code, Slack, Discord,
Teams, Obsidian) the previous word was briefly selected after Space and, if the
user continued typing, was overwritten by the next character.

| Area | Case | Method | Status | Notes |
| --- | --- | --- | --- | --- |
| Electron / VS Code | Type `ghbdsn ghbdsn ghbdsn ` rapidly (no pauses). None of the words gets eaten by the next letter. | MANUAL | UIA path expected; diagnostics log shows `UIA fallback (UIA read + SendInput write)` |
| Electron / Slack | Type a mistyped EN word, press Space, immediately type the next word. No word-selection flash; correction either applies cleanly or is skipped — never truncated. | MANUAL | Slack Draft.js composer → UIA ReadOnly → SendInput write |
| Electron / Discord | Same as Slack. | MANUAL | |
| Electron / Obsidian | Type `Win11 ` — does NOT collapse to `Win `. | MANUAL | Digit-containing words skip L2; Electron path skips clipboard fallback. |
| Electron skip | When UIA cannot resolve the word in an Electron app, the delimiter is still injected (space appears), but no selection flash occurs. Diagnostics log shows `Electron: UIA unavailable — auto-correction skipped to avoid selection race`. | MANUAL | |
| Cancellation | In Chromium browsers (chrome/edge), keep typing through a pending clipboard fallback. Selection (if any) is collapsed with VK_RIGHT and log shows `Clipboard fallback aborted: user kept typing`. | MANUAL | Cancellation is checked around every sleep. |
| Chromium browser regression | In Chrome address bar / Gmail compose: auto-correction still works for `ghbdsn` → `привіт` + Space. | MANUAL | BrowserAutoFallback path untouched. |
| Safe mode regression | In Notepad and Chrome, press the Safe hotkey to correct the last word. Works as before (atomic Shift+Left+Type). | MANUAL | SendInputAdapter changes are catalog-unification only. |

## 5. Safe hotkeys

| Area | Case | Method | Status | Notes |
| --- | --- | --- | --- | --- |
| Registration | Both global hotkeys register | PASS (auto) | `GlobalHotkeyManagerRegressionTests` |
| Registration | Partial registration failure cleans up | PASS (auto) | `GlobalHotkeyManagerRegressionTests` |
| Reload | Hotkey reload happens on message thread | PASS (auto) | `GlobalHotkeyManagerRegressionTests` |
| Exit | Hotkeys unregister on shutdown | PASS (auto) | `GlobalHotkeyManagerRegressionTests` |
| Last word | `Ctrl+Shift+K` fixes last word | PASS (auto) / MANUAL | Logic covered; physical hotkey still worth manual pass |
| Selection | `Ctrl+Shift+L` fixes selected text only | PASS (auto) / MANUAL | Sentence fallback removed; live pass still needed |
| Adapter fallback | If first full adapter fails, second full adapter is tried | PASS (auto) | `SafeModeHandlerRegressionTests` |
| Native WinForms edit | WindowsForms edit class is recognized as Full | PASS (auto) | `NativeEditTargetAdapterRegressionTests` |
| Electron / Codex | Safe hotkeys in Chromium/Electron-like editors | MANUAL / RISK | Still highest runtime risk |

## 6. Cancel and undo behavior

| Area | Case | Method | Status | Notes |
| --- | --- | --- | --- | --- |
| Cancel on Backspace | With setting ON, Backspace clears the whole pending word buffer | PASS (auto) | New regression in `KeyboardObserverBufferTests` |
| Cancel on Backspace | With setting OFF, Backspace removes only the last buffered char | PASS (auto) | New regression in `KeyboardObserverBufferTests` |
| Cancel on Left Arrow | Left Arrow clears pending word buffer | MANUAL | Needs explicit regression or runtime pass |
| Undo on Backspace | Undo input builder releases modifiers and restores original text | PASS (auto) | `AutoModeHandlerRegressionTests` |
| Undo runtime | Backspace immediately after auto-correction restores original text in target app | MANUAL | Needs full end-to-end pass |

## 7. Text adapters and caret behavior

| Area | Case | Method | Status | Notes |
| --- | --- | --- | --- | --- |
| Coordinator | Full adapters are preferred over read-only adapters | PASS (auto) | `TextTargetCoordinatorRegressionTests` |
| Coordinator | Candidate ordering is stable | PASS (auto) | `ResolveCandidates` regression |
| UIA replace | Exact last-word slice replacement works | PASS (auto) | `UiAutomationTargetAdapterRegressionTests` |
| UIA relocate | Latest exact match relocation works after value changes | PASS (auto) | `UiAutomationTargetAdapterRegressionTests` |
| UIA fail safe | Replace fails cleanly if exact word cannot be found | PASS (auto) | `UiAutomationTargetAdapterRegressionTests` |
| UIA caret restore | Caret restore input builder behaves as expected | PASS (auto) | `UiAutomationTargetAdapterRegressionTests` |
| SendInput fallback | Reads buffered word near caret for fallback replacement | PASS (auto) | Covered in adapter tests |
| Real caret behavior | Browser/Electron caret position after replace | MANUAL / RISK | Needs real live editor verification |

## 8. Diagnostics

| Area | Case | Method | Status | Notes |
| --- | --- | --- | --- | --- |
| Diagnostics logger | Can be enabled/disabled from settings | MANUAL | UI toggle exists; not re-proved end-to-end |
| Diagnostics window | Window opens and lists entries | MANUAL | Needs runtime pass |
| Safe mode logs | Safe mode writes expected adapter/result entries | PASS (auto) | Regression logger assertions exist |
| Auto mode logs | Auto mode writes correction/undo entries | PASS (auto) / MANUAL | Builder/logging logic covered; live UI window not yet rechecked |

## 9. Known manual-only passes still needed

These are the cases a terminal-driven pass still cannot honestly certify without a real user-like session:

1. Physical global hotkeys in live Chromium / Codex / Electron editors
2. Auto mode on real Chrome / Edge inputs and textareas
3. Auto / safe behavior in `contenteditable`, Monaco, CodeMirror
4. Tray menu interaction flow end-to-end
5. Undo runtime in a real target control after an actual auto-correction
6. Diagnostics window UX and live log visibility

## Suggested next manual smoke sequence

1. Launch `Switcher.App`
2. Launch `Switcher.TestTarget`
3. In single-line TextBox: type `ghbdsn`, press `Ctrl+Shift+K`, expect `привіт`
4. In same box: type `ghbdsn`, press `Backspace`, then `Space`, expect no auto-correction
5. In same box with Auto Mode ON: type `ghbdsn` + `Space`, expect `привіт `
6. Immediately press `Backspace`, expect undo to original word
7. In selection test box: select `руддщ`, press `Ctrl+Shift+L`, expect `hello`
8. Open tray menu: verify `Open Settings`, `Auto Mode`, `View Diagnostics`, `Exit`
9. Repeat the hotkey checks in Chrome textarea and in Codex/Electron input
