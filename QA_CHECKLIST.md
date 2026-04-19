# EN-UA Switcher QA Checklist

Last updated: 2026-04-16

Status legend:
- `PASS (auto)` = confirmed by automated tests in this repo
- `PASS (live)` = confirmed by local runtime/UI smoke check
- `MANUAL` = still needs a real app/runtime pass in the target environment
- `RISK` = known weak area, even if partial coverage exists

## 1. Build and launch

| Area | Case | Method | Status | Notes |
| --- | --- | --- | --- | --- |
| Build | `Switcher.App` builds | `dotnet build .\src\Switcher.App\Switcher.App.csproj -v minimal` | PASS (auto) | Current branch builds cleanly |
| Build | `Switcher.Engine.Tests` pass | `dotnet test .\tests\Switcher.Engine.Tests\Switcher.Engine.Tests.csproj -v minimal` | PASS (auto) | `71/71` |
| Build | `Switcher.Core.Tests` pass | `dotnet test .\tests\Switcher.Core.Tests\Switcher.Core.Tests.csproj -v minimal` | PASS (auto) | `224/224` |
| App startup | Settings window opens | Local UI smoke | PASS (live) | Window detected via UIAutomation |
| Single instance | Second app instance does not conflict with hotkeys | Regression tests + real second-launch message | PASS (auto) / MANUAL | Hotkey manager side is covered; second-launch UX should still be rechecked manually |

## 2. Settings UI

| Area | Case | Method | Status | Notes |
| --- | --- | --- | --- | --- |
| General | Main tabs render: General / Hotkeys / Exclusions / About | Local UI smoke | PASS (live) | Visible in current build |
| General | `Force safe-only Auto Mode` toggle is present | PASS (auto) / MANUAL | Bound in settings UI; still needs direct UX pass |
| General | Selector shadow / export toggles are present | PASS (auto) / MANUAL | Bound in settings UI; still needs direct UX pass |
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
| Tray | Tooltip reflects experimental broad vs safe-only mode | PASS (auto) / MANUAL | Build verified; tray UX still needs a live pass |
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
| Browser fallback | Browser path classifies `BrowserValuePatternSafe` / `BrowserBestEffort` / `UnsafeSkip` | PASS (auto) | Runtime classifier regressions added |
| Browser fallback | Best-effort path aborts on stale interaction, focus drift, and exact-slice mismatch | PASS (auto) / RISK | Guardrails are covered by deterministic tests; live Chrome/Electron still required |
| Custom editors | `contenteditable` / Monaco / CodeMirror` -> skip` | PASS (auto) / MANUAL | Unsafe-skip classifier exists; still needs real target validation |
| Regression | `цікаве?` never becomes `ццікаве` | PASS (auto) / MANUAL | Punctuation-preserving transaction logic is covered; still requires live matrix pass |
| Release gate | Broad auto default can be rolled back with a single `safe-only` toggle | PASS (auto) | Setting is wired through app + engine |

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
| Auto mode logs | Auto mode writes safety profile / abort-path entries | PASS (auto) / MANUAL | Logic is covered; diagnostics window still needs a live pass |
| Selector export | Structured selector examples write only when opt-in export is enabled | PASS (auto) / MANUAL | Separate export path is wired; file UX still needs a live pass |

## 9. Runtime matrix release gate

Release signoff is not complete unless both the automated suite and this live matrix are green:

1. WinForms test target
2. Chrome `input`
3. Chrome `textarea`
4. One Electron app
5. One known-unsafe custom editor surface

Required live checks in those targets:

1. `цікаве?` never becomes `ццікаве`
2. Browser/Electron never delete neighboring text
3. `вщпі -> dogs`
4. `луні -> keys`
5. `вщпб -> dog,`
6. `вщпю -> dog.`
7. `фіч` is skipped
8. Undo restores the exact original
9. Fast typing after delimiter causes abort, not stale replace

## 10. Known manual-only passes still needed

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
7. Turn `Force safe-only Auto Mode` ON, retry Chrome/Electron auto mode, expect browser best-effort to skip cleanly instead of replacing
8. Turn selector export ON, trigger a few borderline auto-corrections, confirm a structured export file appears under `%AppData%\Switcher`
7. In selection test box: select `руддщ`, press `Ctrl+Shift+L`, expect `hello`
8. Open tray menu: verify `Open Settings`, `Auto Mode`, `View Diagnostics`, `Exit`
9. Repeat the hotkey checks in Chrome textarea and in Codex/Electron input
