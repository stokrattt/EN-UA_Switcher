# AGENTS

Last updated: 2026-04-23

## Purpose

Short technical handoff for future agent sessions.
Read this first before doing a full repo walk.

## Project At A Glance

- Windows-only desktop tray app for fixing text typed in the wrong EN/UA keyboard layout.
- Solution: `Switcher.sln`
- Primary app: WPF shell with WinForms tray integration.
- Runtime target today: `.NET 8` on Windows.

## Main Projects

- `src/Switcher.App`
  WPF UI, tray icon, settings window, diagnostics window.
- `src/Switcher.Engine`
  Orchestration layer: hooks, hotkeys, auto/safe correction flows.
- `src/Switcher.Infrastructure`
  Win32 P/Invoke, foreground context, native edit/UIAutomation adapters.
- `src/Switcher.Core`
  Heuristics, layout maps, settings, dictionaries, diagnostics models.
- `src/Switcher.TestTarget`
  Small WinForms harness for testing native `EDIT` and `RichEdit` paths.
- `tests/Switcher.Core.Tests`
- `tests/Switcher.Engine.Tests`

## Architecture Notes

- The app is not "just WPF".
  It deliberately mixes:
  - WPF UI
  - WinForms tray menu and `NotifyIcon`
  - Win32 interop
  - UI Automation
  - `SendInput`
  - global hotkeys
  - low-level keyboard hook

- The hardest and most fragile part of the system is the engine/infrastructure layer, not the UI.
- If future work asks whether to "rewrite the app", remember:
  the expensive knowledge is in text targeting, hotkeys, hooks, browser behavior, and Windows interop.
- Product direction for auto-correction:
  do not treat dictionary growth as the fix strategy.
  Future detector work should prefer layout-aware heuristics, n-gram plausibility, morphology, boundary signals,
  and runtime context rather than adding words to `en-common.txt` / `ua-common.txt` to make cases pass.
- Practical rule:
  if a real user word is skipped, first fix scoring/gating logic.
  Do not patch regressions by stuffing more words into dictionaries unless there is an explicit product decision to do so.

## Files Worth Reading First

- `src/Switcher.App/Switcher.App.csproj`
- `src/Switcher.App/App.xaml.cs`
- `src/Switcher.Infrastructure/UIAutomationTargetAdapter.cs`
- `src/Switcher.Infrastructure/NativeMethods.cs`
- `src/Switcher.Engine/KeyboardObserver.cs`
- `src/Switcher.Engine/GlobalHotkeyManager.cs`
- `src/Switcher.Engine/AutoModeHandler.cs`
- `src/Switcher.Engine/TextTargetCoordinator.cs`
- `tests/Switcher.Engine.Tests/RegressionTests.cs`
- `tests/Switcher.Engine.Tests/SendInputAdapterTests.cs`

## Current Technical Shape

- `Switcher.App` targets `net8.0-windows` with both:
  - `<UseWPF>true</UseWPF>`
  - `<UseWindowsForms>true</UseWindowsForms>`
- `Switcher.TestTarget` is a WinForms test app on `net8.0-windows`.
- `Switcher.Infrastructure` references `Microsoft.WindowsDesktop.App`.
- UIAutomation is used directly through:
  - `System.Windows.Automation`
  - `System.Windows.Automation.Text`

## What Is Risky

- `UIAutomationTargetAdapter`
  Most likely place for browser-specific runtime weirdness.
  Safe at compile time, fragile at runtime.
- `NativeMethods`
  Heavy P/Invoke surface:
  `RegisterHotKey`, `SetWindowsHookEx`, `SendInput`, clipboard APIs, keyboard layout switching.
- `KeyboardObserver`
  Buffering and low-level hook logic are sensitive to focus changes, injected keys, modifiers, and Chrome-like scan-code oddities.
- Any code path that depends on:
  - foreground focus timing
  - caret restoration
  - browser `ValuePattern` support
  - clipboard fallback

## What Is Already Known To Work On .NET 8

- Local environment on 2026-04-17 had only:
  - SDK `8.0.420`
  - no .NET 10 SDK installed
- `dotnet build Switcher.sln -c Debug`
  succeeded with `0 warnings`, `0 errors`
- `dotnet test Switcher.sln -c Debug --no-build`
  passed:
  - `224` core tests
  - `71` engine tests
  - total `295` passing

## Important Testing Caveat

- Tests are useful, but they do not fully validate live desktop behavior.
- In particular, the most fragile real-world scenarios are only partially covered:
  - browser inputs
  - real UIAutomation behavior
  - real `SendInput` against focused windows
  - timing/race conditions around hotkeys and reinjected input

## .NET 10 Upgrade Assessment Snapshot

- Upgrade was assessed but intentionally postponed.
- Current recommendation:
  do not expect `.NET 10` by itself to make the app materially "work better".
- Main expected value of `.NET 10`:
  - support lifecycle
  - newer desktop runtime
  - minor platform fixes
- Main non-value:
  it will not magically solve browser/UIAutomation/SendInput behavior.

## .NET 10 Specific Findings

- No direct red flags were found for the known desktop breaking changes that were checked:
  - no obvious ambiguous WPF/WinForms `ContextMenu` or `MenuItem` usage in app code
  - no empty `Grid.ColumnDefinitions` / `Grid.RowDefinitions`
  - no `DynamicResource` usage found in current XAML
  - no WinForms clipboard `GetData()` usage found in source

- Still, any real upgrade should be treated as:
  - quick retarget
  - then manual desktop smoke pass

## Test Package Drift

- Test stack is behind current packages.
- Outdated packages observed on 2026-04-17:
  - `coverlet.collector` `6.0.0` -> latest `8.0.1`
  - `Microsoft.NET.Test.Sdk` `17.8.0` -> latest `18.4.0`
  - `xunit` `2.5.3` -> latest `2.9.3`
  - `xunit.runner.visualstudio` `2.5.3` -> latest `3.1.5`

## If Someone Asks "Will A Rewrite Be Better?"

- Default answer: probably no.
- Reason:
  rewriting UI is easy compared to reproducing the stable parts of:
  - hook logic
  - adapter routing
  - keyboard layout correction
  - browser/native targeting heuristics

## Useful Commands

```powershell
dotnet build Switcher.sln -c Debug
dotnet test Switcher.sln -c Debug --no-build
dotnet list tests\Switcher.Engine.Tests\Switcher.Engine.Tests.csproj package --outdated
dotnet list tests\Switcher.Core.Tests\Switcher.Core.Tests.csproj package --outdated
```

## Suggested Reading Order For Future Sessions

1. Read this file.
2. Read `docs/chat-summary.md` for historical context.
3. Read `docs/full-summary.md` for architecture detail if needed.
4. Only then walk code, starting from:
   `App.xaml.cs` -> `SwitcherEngine` -> `KeyboardObserver` -> adapters.

## PR #1 Implementation (2026-04-18 → continued)

All 4 cherry-picks from PR #1 are now implemented and tested.

### Changes Made

**`src/Switcher.Core/ElectronProcessCatalog.cs`**

- Added `Processes` property (`IReadOnlyCollection<string>`) for testability.
- Expanded process list: `code - insiders`, `windsurf`, `atom`, `discordcanary`, `discordptb`,
  `ms-teams`, `msteams`, `element-desktop`, `telegram-desktop`, `skype`, `figma-agent`.
- `IsElectronProcess` now accepts `string?` (null-safe).

**`src/Switcher.Engine/SendInputAdapter.cs`**

- Renamed `WordSelectionProcesses` → `BrowserWordSelectionProcesses`.
- Removed all Electron entries from the set (now delegated to catalog).
- `ShouldUseWordSelectionReplace` also calls `ElectronProcessCatalog.IsElectronProcess`.

**`src/Switcher.Engine/AutoModeHandler.cs`**

- `BuildAutoReplacementInputs` now has `int? eraseCountOverride = null`.
  When set, uses override for Shift+Left count (protects digit-bearing words like `Win11`).
- `ExecuteNativeReplacementTransaction` passes `eraseOverride = Math.Max(approxTyped, originalCore.Length)`.
- New execution path: `ElectronUiaBackspaceReplace` (gated by `ElectronUiaPathEnabled`).
  Reads live word via UIA, then Backspace×N + Unicode — no Shift+Left → no selection race condition.

**`src/Switcher.Engine/AutoModeRuntimeModels.cs`**

- Added `ElectronUiaSafe` to `ReplacementSafetyProfile`.
- Added `ElectronUiaBackspaceReplace` to `ReplacementExecutionPath`.

**`src/Switcher.Core/AppSettings.cs`**

- Added `ElectronUiaPathEnabled = false` (opt-in, experimental).

**`src/Switcher.App/MainWindow.xaml` + `MainWindow.xaml.cs`**

- `ChkElectronUiaPath` checkbox added to Settings tab (below Safe-only mode).
- Wired to `ElectronUiaPathEnabled` in `LoadSettings` / `SaveCurrentState`.

### Tests Added

- `tests/Switcher.Core.Tests/ElectronProcessCatalogTests.cs` — 46 tests:
  known Electron apps, .exe normalization, browser exclusions, edge cases, catalog integrity.
- `tests/Switcher.Engine.Tests/RegressionTests.cs` additions:
  - `eraseCountOverride` behavior (2 tests)
  - Trailing suffix variety: `,` `.` `!` `?` `;` `:` `[]` and no-suffix (10 tests)

### Build / Test Status (post-implementation)

- `dotnet build Switcher.sln -c Debug` → 0 warnings, 0 errors
- Tests: **274 core + 67 engine = 341 total, 0 failures**

## UI Fix (2026-04-18)

- Fixed invisible typed text in editable `ComboBox` (Exclusions tab, "Excluded processes" dropdown).
- Root cause: `PART_EditableTextBox` used `{TemplateBinding Foreground}` which can fail to resolve on Win11, and inner `ScrollViewer` had no explicit transparent background.
- Fix: Changed to `Foreground="{StaticResource ForegroundBrush}"` + added `Background="Transparent"` to ScrollViewer in [src/Switcher.App/Themes/DarkTheme.xaml](src/Switcher.App/Themes/DarkTheme.xaml).

## Release / Deployment Notes (2026-04-22)

- GitHub Windows release runs can fail hard if invalid-path junk files such as `{c?.ConvertedText` are tracked.
  Keep them deleted and ignored.
- Canonical release assets are now:
  - `EN-UA-Switcher.exe` — self-contained
  - `EN-UA-Switcher-runtime-dependent.exe` — requires .NET 8 Desktop Runtime
- Release flow source of truth:
  - version defaults from `Directory.Build.props`
  - packaging from `scripts/publish-release.ps1`
  - upload from `.github/workflows/release.yml`
- `StartupHelper.SetStartup(...)` is refreshed on engine startup so upgrades rewrite HKCU autorun to the current exe path.

## Release Discipline (must follow)

- If the user asks to "build a release", "deploy", "publish", "update GitHub", or "update the release",
  do not publish partially updated output.
- Before tagging or publishing, verify that all changed source files, scripts, workflow files, packaging files,
  version files, and release-facing folders are committed and pushed.
- Do not assume "main is enough".
  Confirm that the actual release artifact is built from the latest commit that contains the fixes discussed with the user.
- After release build:
  - verify the produced executable(s) are from the current version
  - verify the uploaded release assets match the current commit/tag
  - verify GitHub release contents are complete and not left on an older build
  - verify no folder or asset group was skipped during packaging/upload
- If version is bumped, make sure version references stay consistent across:
  - `Directory.Build.props`
  - release notes
  - workflow/package output naming
  - README if install/version text depends on it
- If the user asks to "update everything in Git", also check for missing tracked files/folders,
  stale generated artifacts, and mismatches between repo contents and release contents.
- Never leave a state where:
  - code in `main` is newer than the published release asset
  - some folders are updated in Git but omitted from the shipped release
  - the release page shows an older binary than the current documented version
- After publishing, do a final sanity pass:
  - `git status`
  - local build/test
  - tag points at the intended commit
  - release assets and displayed version agree with the repo

## Maintenance Note

- Keep this file short.
- Update it after major architectural findings, runtime investigations, or migration decisions.
- Do not turn it into another giant summary dump.
