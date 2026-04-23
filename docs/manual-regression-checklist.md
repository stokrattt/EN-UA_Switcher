# Manual Regression Checklist

Use this after engine changes, adapter-routing changes, release builds, and before publishing.

## Automated Coverage

These are already covered by unit/engine tests and should stay green:

- wrong-layout word conversions like `вщп -> dog`, `вщпі -> dogs`, `луні -> keys`, `вщпб -> dog,`
- Ukrainian words typed on EN layout like `ghbdsn -> привіт`, `vs; -> між`, `uskre -> гілку`
- mixed technical words like `юТУЕ -> .NET`, `пщщпду -> google`, `вгкфешщт -> duration`
- diagnostics original-word resolution
- Chrome address-bar route selection
- Electron/Codex process routing to Electron UIA path

## Manual-Only Runtime Checks

Run these on a local desktop build because unit tests do not fully validate live UIAutomation, focus timing, or `SendInput`.

### Chrome

- normal page inputs still auto-correct and are not misclassified as address bar
- address bar corrects wrong-layout words without deleting the whole value
- address bar does not leave weird selection state after replace
- search fields in Google/Gemini/Jira behave like normal inputs, not omnibox

### Electron Apps

- VS Code still converts wrong-layout words in editors
- Codex input still converts wrong-layout words
- Element keeps caret position after auto-replace instead of jumping to the start
- backspace in Element feels normal during regular typing

### Space / Focus / Cursor

- pressing `Space` after a converted word keeps the space
- converted words do not swallow the following space
- focus does not jump out of the target app after `Space`
- caret stays after the converted word instead of moving backward or to the start of the line

### Settings / Exclusions UI

- `Save` keeps the settings popup open but becomes disabled and changes to `Saved` when there are no dirty changes
- Exclusions > Excluded processes editable text is visible while typing
- Exclusions dropdown mouse wheel scroll works inside the popup list

## Release Smoke Pass

Before publishing a release:

- run `dotnet build Switcher.sln -c Debug`
- run `dotnet test Switcher.sln -c Debug --no-build`
- run the manual checks above on the actual packaged app, not only from Visual Studio
- verify release assets match the current commit/tag/version
