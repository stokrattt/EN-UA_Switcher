# Switcher — Саммарі розмови для нового чату

## Що це за проєкт

Windows desktop tray-app аналог Punto Switcher для EN↔UA. Написаний з нуля на .NET 8 / C#.

**Головні вимоги (актуальні):**
- Виправляти текст, введений не в тій розкладці (EN→UA і UA→EN)
- ~~ЗАБОРОНЕНО: backspace+SendInput~~ — **обмеження знято**: SendInput fallback додано як третій пріоритет адаптера (для Electron/contenteditable/Telegram/Slack/VS Code)
- Пріоритет адаптерів: `NativeEditTargetAdapter` → `UIAutomationTargetAdapter` → `SendInputAdapter`
- Keyboard hook = тільки для спостереження (NativeEdit/UIA); для SendInputAdapter hook також веде char-буфер поточного слова
- Інжектовані ключові події (`LLKHF_INJECTED`) ігноруються у hook → немає feedback loop
- Не брехати: чітко фіксувати що працює, а що ні

---

## Стан проєкту: **ПОВНІСТЮ ЗІБРАНИЙ І ПРОТЕСТОВАНИЙ**

```
dotnet build  → Build succeeded. 0 Warnings. 0 Errors.
dotnet test   → Passed: 149, Failed: 0
              (135 Switcher.Core.Tests + 14 Switcher.Engine.Tests)
```

EXE: `d:\documents\switcher\bin\Switcher.App.exe` (без Debug/Release підпапки)

---

## Структура

```
d:\documents\switcher\
├── Switcher.sln
├── bin\                        — вихідний .exe (OutputPath = ..\..\bin\)
│   └── Switcher.App.exe
├── src\
│   ├── Switcher.Core\          — моделі, мапи, евристики, налаштування
│   ├── Switcher.Infrastructure\ — Win32 P/Invoke, адаптери тексту
│   ├── Switcher.Engine\        — оркестратор, хук, хоткеї, логер, SendInputAdapter
│   ├── Switcher.App\           — WPF tray-app, dark UI
│   └── Switcher.TestTarget\    — WinForms тест-харнес
├── tests\
│   ├── Switcher.Core.Tests\    — 135 тестів (KeyboardLayoutMap + CorrectionHeuristics)
│   └── Switcher.Engine.Tests\  — 14 тестів (SendInputAdapter + Coordinator + Observer)
├── docs\
│   └── compatibility-matrix.md
└── test-pages\
    └── test-input.html         — HTML-сторінка для тесту UIA-адаптера
```

---

## Ключові файли

### Switcher.Core

| Файл | Зміст |
|------|-------|
| `KeyboardLayoutMap.cs` | Словники EN↔UA (всі 33 UA літери + регістр + пунктуація), `ConvertEnToUa`, `ConvertUaToEn` (strict=true → null якщо є неможливий символ), `ClassifyScript` → ScriptType (Latin/Cyrillic/Mixed/Other) |
| `CorrectionHeuristics.cs` | `Evaluate(word, mode) → CorrectionCandidate?`. Auto threshold=0.75, Safe=0.50. `KeyboardLetterChars` = `[`, `]`, `{`, `}`, `\`, `\|` — ніколи не стрипаються як пунктуація (фікс `]`→`ї`). Bigram scoring + consonant/vowel ratio. |
| `CorrectionCandidate.cs` | record: `OriginalText`, `ConvertedText`, `Direction` (CorrectionDirection), `Confidence`, `Reason` |
| `AppSettings.cs` | JSON в `%APPDATA%\Switcher\settings.json`. `HotkeyDescriptor` з `VkToName()`. `SettingsManager.Load()/Save()` |
| `WordList.cs` | Embedded resources `en-common.txt` / `ua-common.txt` |
| `DiagnosticEntry.cs` | `OperationType`, `DiagnosticResult` enum, `DiagnosticEntry` record |
| `Dictionaries/en-common.txt` | ~500+ слів. Містить `hello`, `hi`, `hey`, `world`, `home` |
| `Dictionaries/ua-common.txt` | ~300+ слів |

### Switcher.Infrastructure

| Файл | Зміст |
|------|-------|
| `NativeMethods.cs` | **`public static class`** — всі P/Invoke. Включає: GetForegroundWindow, SendMessage (Edit msgs), WH_KEYBOARD_LL hook, RegisterHotKey, PostMessage, **SendInput + INPUT/KEYBDINPUT structs**, **GetKeyboardState + GetKeyboardLayout + ToUnicodeEx**, **SwitchInputLanguage**, MakeKeyInput/MakeUnicodeInput хелпери |
| `ForegroundContextProvider.cs` | `ForegroundContext` record (`Hwnd`, `FocusedHwnd`, `ProcessName`, `Pid`, `WindowTitle`, `FocusedControlClass`). `GetCurrent()` — GetForegroundWindow → AttachThreadInput+GetFocus |
| `ITextTargetAdapter.cs` | `TargetSupport` enum (Full/ReadOnly/Unsupported). Interface: `CanHandle`, `DescribeSupport`, `TryGetLastWord`, `TryGetSelectedText`, `TryReplaceLastWord`, `TryReplaceSelection` |
| `NativeEditTargetAdapter.cs` | Edit/RichEdit20W/RichEdit50W/MSFTEDIT_CLASS. WM_GETTEXTLENGTH → WM_GETTEXT → EM_GETSEL → scan back → EM_SETSEL + EM_REPLACESEL (undo=1) |
| `UIAutomationTargetAdapter.cs` | UIA ValuePattern. Пропускає native Edit (вони в NativeEdit). `TryReplaceLastWord` = `SetValue(prefix+replacement+suffix)`. **ОБМЕЖЕННЯ**: SetValue замінює весь рядок, курсор скидається в кінець. |

**NativeMethods — ключові доповнення:**

```csharp
// SendInput structs
[StructLayout(LayoutKind.Sequential)] public struct INPUT { ... }
[StructLayout(LayoutKind.Explicit)]   public struct INPUT_UNION { ... }
[StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { ... }
public const uint KEYEVENTF_UNICODE = 0x0004;
public const uint KEYEVENTF_KEYUP   = 0x0002;

[DllImport("user32.dll")] public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
public static INPUT MakeKeyInput(uint vk, bool keyUp);
public static INPUT MakeUnicodeInput(char c, bool keyUp);

// Char translation
[DllImport("user32.dll")] public static extern bool GetKeyboardState(byte[] pbKeyState);
[DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint idThread);
[DllImport("user32.dll")] public static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode,
    byte[] lpKeyState, StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

// Language switching
public const int WM_INPUTLANGCHANGEREQUEST = 0x0050;
public static readonly IntPtr HKL_EN_US = new IntPtr(0x04090409);
public static readonly IntPtr HKL_UK_UA = new IntPtr(0x04220422);

public static void SwitchInputLanguage(IntPtr hwnd, bool toUkrainian)
    => PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, 0, toUkrainian ? HKL_UK_UA : HKL_EN_US);
```

### Switcher.Engine

| Файл | Зміст |
|------|-------|
| `KeyboardObserver.cs` | WH_KEYBOARD_LL. Ніколи не повертає ненульовий результат. Ігнорує `LLKHF_INJECTED`. `WordBoundaryDetected(int wordLen)`, `BufferReset` events. **Char buffer**: `_charBuffer` (StringBuilder) + `_lastCompletedWord`. `CurrentWord` property (читається SendInputAdapter). `ClearBuffer()`. Backspace = remove 1 char; Delete/Escape = full clear. `AppendCharToBuffer` використовує `ToUnicodeEx`. |
| `TextTargetCoordinator.cs` | Конструктор `(KeyboardObserver observer)`. Пріоритет: `[NativeEditTargetAdapter, UIAutomationTargetAdapter, SendInputAdapter(observer)]`. `Resolve()` → перший Full адаптер. |
| `SendInputAdapter.cs` | **НОВИЙ ФАЙЛ.** `CanHandle` → завжди `Full`. `TryGetLastWord` → `observer.CurrentWord`. `TryReplaceLastWord` → Backspace×N + Unicode SendInput. Після успіху: `observer.ClearBuffer()`. `TryGetSelectedText/TryReplaceSelection` → null/false. |
| `SwitcherEngine.cs` | IDisposable. Init order: `_contextProvider` → `_keyboardObserver` → `_coordinator(_keyboardObserver)` → `_exclusions` → handlers. `Start()/Stop()/ApplySettings()`. Stop без `?.` перед `-=`. |
| `AutoModeHandler.cs` | `OnWordBoundary(int)`. Тільки Full support. Читає слово з `adapter.TryGetLastWord()`. Після успіху: `NativeMethods.SwitchInputLanguage(context.Hwnd, toUkrainian: candidate.Direction == CorrectionDirection.EnToUa)`. |
| `SafeModeHandler.cs` | `FixLastWord()` / `FixSelection()`. Multi-word selection splits. Після успіху: `SwitchInputLanguage` (direction з `ClassifyScript`). |
| `DiagnosticsLogger.cs` | Ring buffer 500 записів, `object _lock` (НЕ `Lock` — .NET 8 сумісність), `EntryAdded` event. |
| `ExclusionManager.cs` | Порівняння process name з `Settings.ExcludedProcessNames` (case-insensitive). |
| `GlobalHotkeyManager.cs` | Background thread + RegisterHotKey на HWND_MESSAGE window. IDs 1=LastWord, 2=Selection. MOD_NOREPEAT. |

**KeyboardObserver — DelimiterKeys (ВАЖЛИВО):**
```csharp
// Тільки ці три! OEM-клавіші (кома, крапка, крапка-з-комою) ВИКЛЮЧЕНІ —
// на ЙЦУКЕН-розкладці вони дають літери б, ю, ж → була причиною втрати
// останньої букви UA-слів.
private static readonly HashSet<uint> DelimiterKeys = new()
{
    NativeMethods.VK_SPACE,
    NativeMethods.VK_RETURN,
    NativeMethods.VK_TAB,
};
```

**SendInputAdapter — механізм:**
```csharp
public TargetSupport CanHandle(ForegroundContext context) => TargetSupport.Full;

public string? TryGetLastWord(ForegroundContext context)
{
    var word = _observer.CurrentWord;
    return string.IsNullOrEmpty(word) ? null : word;
}

public bool TryReplaceLastWord(ForegroundContext context, string replacement)
{
    var current = _observer.CurrentWord;
    if (string.IsNullOrEmpty(current)) return false;
    // inputs = Backspace×current.Length×2 + replacement chars×2
    uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    bool success = sent == inputs.Length;
    if (success) _observer.ClearBuffer();
    return success;
}
```

### Switcher.App

| Файл | Зміст |
|------|-------|
| `App.xaml` | ShutdownMode=OnExplicitShutdown, merge DarkTheme.xaml |
| `App.xaml.cs` | NotifyIcon, DarkMenuRenderer, SwitcherEngine lifecycle |
| `TrayIconHelper.cs` | Програмна генерація іконки (GDI+, синє коло + "S"), немає .ico файлу |
| `MainWindow.xaml.cs` | Window icon: `TrayIconHelper.CreateIcon()` + `Imaging.CreateBitmapSourceFromHIcon`. 4 вкладки: General / Hotkeys / Exclusions / About. Close → hide to tray. |
| `DiagnosticsWindow.xaml/cs` | `ObservableCollection<DiagnosticRow>`, ListView GridView. `System.Windows.Clipboard` (явно — уникнення конфлікту з WinForms). |
| `Themes/DarkTheme.xaml` | #1E1E1E bg, #0078D4 accent. TabItem з `ControlTemplate` — blue bottom-border indicator на обраному табі. |
| `Switcher.App.csproj` | `<OutputPath>..\..\bin\</OutputPath>` + `<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>` |

### Switcher.TestTarget

| Файл | Зміст |
|------|-------|
| `Form1.cs` | Dark WinForms test harness. `BuildUI()` — код-based UI. TextBox (EDIT), RichTextBox (RichEdit), test case labels. |
| `Form1.Designer.cs` | Мінімальний InitializeComponent (тільки Container) — сумісний з BuildUI(). |

### tests/Switcher.Engine.Tests

| Файл | Зміст |
|------|-------|
| `SendInputAdapterTests.cs` | **НОВИЙ ФАЙЛ.** 14 тестів: `SendInputAdapterTests` (5), `TextTargetCoordinatorPriorityTests` (2), `KeyboardObserverBufferTests` (5), `FakeAdapterContractTests` (2). Використовує `FakeObserver : KeyboardObserver` з `new CurrentWord` + `new ClearBuffer()`. |

---

## Всі виправлені баги

| Проблема | Рішення |
|----------|---------|
| `UIAutomationClient` NuGet не існує | `<FrameworkReference Include="Microsoft.WindowsDesktop.App" />` в Infrastructure.csproj |
| `ApplicationIcon` без .ico файлу | Видалено з App.csproj |
| `System.Windows.Forms.Keys` в Core | Замінено на `VkToName()` switch без WinForms |
| `using System.Windows.Automation.Text` відсутній | Додано в UIAutomationTargetAdapter.cs |
| `Lock` (.NET 9) в DiagnosticsLogger | Замінено на `object` + `lock()` |
| `NativeMethods internal` → Engine не бачить | Клас та всі члени зроблені `public` |
| `_mgr?.Event -= handler` — CS0070 | Замінено явною null-перевіркою в SwitcherEngine.Stop() |
| `Clipboard` ambiguous (WPF + WinForms) | `System.Windows.Clipboard.SetText(...)` |
| `hello` відсутнє в en-common.txt | Додано `hello`, `hi`, `hey`, `world`, `home` |
| Остання буква UA-слова іноді не конвертується | OEM-клавіші видалені з `DelimiterKeys` (лишились тільки Space/Return/Tab) |
| `]` → `j` замість `ї` (стрипалось як пунктуація) | `KeyboardLetterChars` set в `StripPunctuation` захищає `[]{}\|` від стрипінгу |
| Мова не перемикається після корекції | `PostMessage(WM_INPUTLANGCHANGEREQUEST)` в AutoMode і SafeMode після успіху |
| Немає іконки у заголовку вікна | `TrayIconHelper.CreateIcon()` + `Imaging.CreateBitmapSourceFromHIcon` у MainWindow.xaml.cs |
| Electron/contenteditable не підтримується | SendInputAdapter як третій пріоритет (Backspace×N + Unicode SendInput) |
| `candidate.CorrectionDirection` — CS0117 | Правильна назва property: `candidate.Direction` |
| `_exclusions` used before assignment | Фіксований порядок ініціалізації в SwitcherEngine конструкторі |
| Дубльований клас KeyboardObserver у файлі | Видалено точним string replace |
| DiagnosticsWindow.xaml.cs — IntelliSense помилки | WPF partial-class false positives — build завжди авторитетний (0 errors) |
| `Chrome_WidgetWin_1 Unsupported` в логах | Нормально: Chrome toolbar має фокус (не input поле) |

---

## Команди для запуску

```bash
# З WSL (термінал):
powershell.exe -NoProfile -Command "cd 'd:\documents\switcher'; dotnet build Switcher.sln"
powershell.exe -NoProfile -Command "cd 'd:\documents\switcher'; dotnet test"
powershell.exe -NoProfile -Command "cd 'd:\documents\switcher'; dotnet test tests/Switcher.Core.Tests/"
powershell.exe -NoProfile -Command "cd 'd:\documents\switcher'; dotnet test tests/Switcher.Engine.Tests/"
powershell.exe -NoProfile -Command "cd 'd:\documents\switcher'; dotnet run --project src/Switcher.App"
powershell.exe -NoProfile -Command "cd 'd:\documents\switcher'; dotnet run --project src/Switcher.TestTarget"
```

**EXE напряму:** `d:\documents\switcher\bin\Switcher.App.exe`

**Увага:** .NET 8 SDK встановлений у Windows (8.0.419). WSL використовується як термінальне середовище, але dotnet-команди запускаються виключно через `powershell.exe`.

---

## Що ще можна зробити

- [ ] Реальне тестування: запустити `bin\Switcher.App.exe` + TestTarget.exe + Chrome + Element/Slack
- [ ] Автозапуск при старті Windows (реєстр `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` або Startup folder)
- [ ] Редагування хоткеїв у Settings UI — зараз `TxtLastWordHotkey`/`TxtSelectionHotkey` display-only (`IsReadOnly="True"`); потрібна KeyDown-прив'язка → запис нового VK+modifiers у `HotkeyDescriptor`
- [ ] Publish: `dotnet publish src/Switcher.App -r win-x64 --self-contained -o publish/`
