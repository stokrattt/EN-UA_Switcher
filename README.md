# EN-UA Switcher

EN-UA Switcher is a Windows tray app for switching mistyped English/Ukrainian text.

If you type `руддщ` instead of `hello`, EN-UA Switcher can fix it automatically after a delimiter or manually through global hotkeys.

## What It Does

- Auto-corrects the last typed word on `Space`, `Enter`, or `Tab`
- Fixes the last word or selected text with global hotkeys
- Works with native Win32 editors, many browser inputs, and a range of Electron apps
- Keeps a live diagnostics log for troubleshooting
- Supports process exclusions and word exclusions
- Lets you undo the last auto-correction with `Backspace`

## Modes

### Auto Mode

Auto Mode watches the current word buffer and decides whether the word should be layout-switched.

- It evaluates both `EN` and `UA` interpretations of the typed token
- It uses a local scoring model with layout mapping, short-text heuristics, character patterns, and dictionary boosts
- It does not rely on a giant hardcoded word list alone
- It only runs on delimiter keys that you enable in settings

Example:

- typed: `руддщ`
- auto-corrected to: `hello`

### Safe Hotkeys

Safe hotkeys do not "think". They always do a direct layout toggle.

| Hotkey | Action |
| --- | --- |
| `Ctrl+Shift+K` | Toggle the last word before the caret |
| `Ctrl+Shift+L` | Toggle the selected text |

This path is intentionally dumb and deterministic:

- no language detection
- no heuristics
- no confidence threshold

## Tray Menu

EN-UA Switcher runs in the system tray.

From the tray menu you can:

- open the settings window
- toggle `Auto Mode`
- open diagnostics
- exit the app

Double-clicking the tray icon opens settings.

## Settings

The settings window has 4 tabs.

### General

- `Enable Auto Mode`: turns automatic correction on or off
- `Correct on Space`: tries auto-correction when `Space` is pressed
- `Correct on Enter`: tries auto-correction when `Enter` is pressed
- `Correct on Tab`: tries auto-correction when `Tab` is pressed
- `Cancel on Backspace`: clears the current word buffer so pending auto-correction will not fire
- `Cancel on Left Arrow`: same idea when you move the caret left
- `Undo correction on Backspace`: immediately rolls back the last auto-correction
- `Start minimized to system tray`: app starts hidden
- `Enable diagnostics logging`: enables the diagnostics window and file logging

### Hotkeys

- Shows whether global hotkeys are currently registered
- Shows the current hotkey bindings
- Current defaults are:
  - `Ctrl+Shift+K` for the last word
  - `Ctrl+Shift+L` for selected text

### Exclusions

There are two exclusion lists.

- `Excluded processes`: disables all correction logic inside selected apps
  - add from the running process list
  - add manually by process name without `.exe`
  - remove selected items from the visible list
- `Excluded words`: disables Auto Mode for specific words
  - add the word in either layout
  - the opposite layout form is excluded too
  - safe hotkeys still work because they are manual

Example:

- add `привіт`
- `ghbdsn` will also be skipped by Auto Mode

### About

Contains a short summary, supported targets, hotkey reminder, and contact info.

## Diagnostics

Diagnostics help explain why a correction happened or why it was skipped.

The diagnostics window shows:

- process name
- adapter used
- operation type
- original text
- converted text
- result
- reason

When file logging is enabled, logs are written to:

- `%APPDATA%\Switcher\switcher-yyyy-MM-dd.log`

Settings are stored in:

- `%APPDATA%\Switcher\settings.json`

## Supported Targets

| Target | Status |
| --- | --- |
| Notepad, WordPad, classic `EDIT` / `RichEdit` controls | Good support in Auto Mode and hotkeys |
| Browser text inputs and textareas | Usually good, with UIA and clipboard fallback paths |
| Electron apps such as Telegram, Element, Codex-like editors | Mixed runtime quality depending on the editor control |
| `contenteditable`, Monaco, CodeMirror and similar custom web editors | Weakest path, especially in Auto Mode |

## Known Limitations

- Auto Mode still works through injected input and replacement paths, so in some editors you may briefly see the word tail move or flicker
- Browser and Electron behavior depends on what the target exposes through UI Automation, selection APIs, and clipboard behavior
- Custom editors are much less predictable than native text boxes
- One-letter words are intentionally handled conservatively in Auto Mode to avoid breaking normal English typing

## Download

Portable release artifacts are published on the GitHub releases page:

- [Releases](https://github.com/stokrattt/EN-UA_Switcher/releases)

Release notes:

- `bin\Switcher.App.exe` is only the normal build output for local development
- `EN-UA-Switcher.exe` is the full self-contained Windows build and does not require a separate .NET install
- `EN-UA-Switcher-small.exe` is the smaller runtime-dependent build and requires the .NET 8 Desktop Runtime

## Build From Source

Requirements:

- Windows 10 or 11
- .NET 8 SDK

Build:

```powershell
dotnet build .\src\Switcher.App\Switcher.App.csproj -v minimal
```

Create a portable release package:

```powershell
.\scripts\publish-release.ps1
```

Run from source:

```powershell
dotnet run --project .\src\Switcher.App\Switcher.App.csproj
```

Run tests:

```powershell
dotnet test .\tests\Switcher.Core.Tests\Switcher.Core.Tests.csproj -v minimal
dotnet test .\tests\Switcher.Engine.Tests\Switcher.Engine.Tests.csproj -v minimal
```

Run the proactive bulk audit:

```powershell
.\scripts\bulk-auto-eval.ps1 -FetchLargeWordLists -EnLimit 50000 -UaLimit 50000 -ContextSample 1500
```

This fetches `50k` English and Ukrainian corpora into `artifacts\bulk-audit` and runs the `tools\BulkEval` scenario audit over word, sentence, chat, short-phrase, brand, and mixed tech contexts.

## Where The EXE Is

This repo is configured to place the app build output into the repository-level `bin` folder.

After a normal build, the main executable is typically here:

- `bin\Switcher.App.exe`

Other build outputs such as `.dll`, `.pdb`, and runtime config files are placed in the same folder.

This file is not the packaged end-user release by itself.

To create the actual release artifact, run the publish script above. It creates:

- `artifacts\release\EN-UA-Switcher.exe`
- `artifacts\release\EN-UA-Switcher-small.exe`
- `artifacts\release\SHA256SUMS.txt`

The GitHub Actions release workflow uses the same script when you push a `v*` tag.

## Project Structure

```text
src/
  Switcher.App/             WPF UI, tray menu, diagnostics window
  Switcher.Core/            settings, heuristics, dictionaries, layout mapping
  Switcher.Engine/          keyboard hook, auto mode, safe mode, exclusions
  Switcher.Infrastructure/  Win32 interop, UIA/native adapters, input replacement
  Switcher.TestTarget/      manual test app
tests/
  Switcher.Core.Tests/      heuristics and layout mapping tests
  Switcher.Engine.Tests/    runtime-path and input simulation tests
```

## Security / Privacy Notes

- The app does not require cloud services
- The app does not use API tokens
- Settings and optional diagnostics logs are stored locally under `%APPDATA%\Switcher`
- Diagnostics are intended for troubleshooting, not for capturing full documents

## License

MIT
