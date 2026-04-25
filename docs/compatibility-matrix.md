# Switcher — Compatibility Matrix

**Version:** 2.3.1  
**Last updated:** 2026-04-25  
**Adapter priority:** NativeEditTargetAdapter → UIAutomationTargetAdapter

---

## Legend

| Symbol | Meaning |
|--------|---------|
| ✅ Full | Safe mode (last word + selection) and Auto mode all work |
| ⚠️ Partial | Works with caveats (see Notes) |
| 🔲 Read-only | Can read text; cannot replace |
| ❌ Unsupported | No writable target found; never mutates text |
| — | Not tested |

---

## Native Win32 Controls

| Target App | Control Class | Adapter | Safe Last Word | Safe Selection | Auto Mode | Notes |
|------------|--------------|---------|---------------|---------------|-----------|-------|
| Notepad (Win10/11) | Edit | NativeEdit | ✅ | ✅ | ✅ | Most reliable target |
| WordPad | RichEdit50W | NativeEdit | ✅ | ✅ | ✅ | |
| Windows Run dialog | Edit | NativeEdit | ✅ | ✅ | ⚠️ Partial | Auto mode may fire after submit |
| Windows Search box | Edit | NativeEdit | ✅ | ✅ | ✅ | |
| Sublime Text 4 | Edit | NativeEdit | ✅ | ✅ | ⚠️ Partial | Custom draw may not reflect changes instantly |
| Git Bash / cmd.exe | — | ❌ | ❌ | ❌ | ❌ | Console windows not supported |
| PowerShell console | — | ❌ | ❌ | ❌ | ❌ | Console windows not supported |
| Windows Terminal | — | ❌ | ❌ | ❌ | ❌ | Console windows not supported |

---

## WinForms Applications

| Target App | Control Class | Adapter | Safe Last Word | Safe Selection | Auto Mode | Notes |
|------------|--------------|---------|---------------|---------------|-----------|-------|
| Switcher.TestTarget TextBox | Edit | NativeEdit | ✅ | ✅ | ✅ | Test harness — primary validation target |
| Switcher.TestTarget RichTextBox | RichEdit50W | NativeEdit | ✅ | ✅ | ✅ | Test harness — RichEdit path |
| Custom WinForms app | Edit / RichEdit | NativeEdit | ✅ | ✅ | ✅ | Depends on standard control usage |

---

## WPF Applications

| Target App | Control Class | Adapter | Safe Last Word | Safe Selection | Auto Mode | Notes |
|------------|--------------|---------|---------------|---------------|-----------|-------|
| WPF TextBox | Edit (HwndHost) | NativeEdit | ✅ | ✅ | ✅ | WPF TextBox renders an underlying HWND |
| WPF RichTextBox | RichEdit | NativeEdit | ✅ | ✅ | ✅ | |
| VS Code (WPF build) | — | ❌ | ❌ | ❌ | ❌ | Electron-based; contenteditable; neither adapter works |

---

## Browser Inputs (via UI Automation)

| Target App | Control Type | Adapter | Safe Last Word | Safe Selection | Auto Mode | Notes |
|------------|-------------|---------|---------------|---------------|-----------|-------|
| Chrome — `<input type="text">` | ValuePattern | UIAutomation | ✅ | ⚠️ Partial | ⚠️ Experimental | SetValue replaces entire field; cursor jumps to end |
| Chrome — `<textarea>` | ValuePattern | UIAutomation | ✅ | ⚠️ Partial | ⚠️ Experimental | Same cursor-reset limitation |
| Chrome — `<div contenteditable>` | — | ❌ | ❌ | ❌ | ❌ | No ValuePattern; text never modified |
| Edge — `<input type="text">` | ValuePattern | UIAutomation | ✅ | ⚠️ Partial | ⚠️ Experimental | Same as Chrome |
| Firefox — any input | — | ❌ | ❌ | ❌ | ❌ | UIA accessibility may be disabled by default |
| Electron apps (VS Code, Slack…) | — | ❌ | ❌ | ❌ | ❌ | Chromium contenteditable; not supported |

---

## Office Applications

| Target App | Control Type | Adapter | Safe Last Word | Safe Selection | Auto Mode | Notes |
|------------|-------------|---------|---------------|---------------|-----------|-------|
| Microsoft Word | — | ❌ | ❌ | ❌ | ❌ | Custom document model; not an Edit/RichEdit/ValuePattern control |
| Microsoft Excel (cell editing) | — | ❌ | ❌ | ❌ | ❌ | Custom control |
| LibreOffice Writer | — | ❌ | ❌ | ❌ | ❌ | Custom rendering |
| Outlook message body | — | ❌ | ❌ | ❌ | ❌ | IE/Edge WebView rendering |
| Outlook subject line | Edit | NativeEdit | ✅ | ✅ | ✅ | Standard Edit control in header |

---

## Known Limitations

1. **UIAutomation cursor reset**: `ValuePattern.SetValue()` replaces the entire field value. After any correction, the cursor jumps to the end of the field. This is a Windows UIA API limitation and cannot be worked around without TextPattern caret support (which Chrome does not fully expose).

2. **Console/terminal windows**: Win32 console host (`conhost.exe`) does not use Edit controls and does not expose UIA ValuePattern. Text replacement is impossible without SendInput injection, which is explicitly excluded from this design.

3. **Auto mode reliability**: Auto mode uses word-boundary detection via keyboard hook + reading the actual word from the adapter. For UIA targets, reading the word requires `ValuePattern.Value` which can be slow; performance may degrade on very large text fields.

4. **Injected keystrokes ignored**: `KeyboardObserver` discards all events with `LLKHF_INJECTED` flag set. This prevents feedback loops from other macro/automation tools but may suppress some IME-composed characters.

5. **Strict mode**: When `StrictAutoMode=true` (default), `ConvertEnToUa`/`ConvertUaToEn` returns null for any character not in the layout map (digits, special symbols, accented Latin). Mixed-language text is never converted.

6. **False positive protection**: Words in `en-common.txt` or `ua-common.txt` are never converted, even in Safe mode. The dictionaries use frequency data to prevent corrupting correctly-typed text.

7. **Elevated targets**: The app itself runs `asInvoker` and does not require admin rights for normal startup or HKCU autorun registration. If a target app is already running elevated, Windows may require launching EN-UA Switcher elevated too before input injection or replacement can succeed there.

---

## Exclusion Recommendations

Add to the **Excluded Processes** list in Settings if you experience unwanted corrections:

| Process Name | Reason |
|-------------|--------|
| `code.exe` | VS Code — Electron, never works anyway |
| `slack.exe` | Electron |
| `teams.exe` | Electron / WebView2 |
| `winword.exe` | Word — custom model |
| `excel.exe` | Excel — custom model |
| `powershell.exe` | Console |
| `cmd.exe` | Console |
| `WindowsTerminal.exe` | Console |
| `putty.exe` | SSH client with custom draw |
| `mstsc.exe` | Remote Desktop — sends input to remote; unpredictable |

---

## Testing This Compatibility Matrix

1. Launch `Switcher.App.exe`
2. Open `Switcher.TestTarget.exe` — test native Edit and RichEdit controls
3. Open `test-pages/test-input.html` in Chrome — test UIAutomation adapter
4. Verify diagnostics in the Diagnostics window (`Ctrl+Shift+D` or View Diagnostics menu)
5. Check the `%APPDATA%\Switcher\` folder for the log file if file logging is enabled

---

*Results verified on: Windows 11 22H2, .NET 8.0.25*
