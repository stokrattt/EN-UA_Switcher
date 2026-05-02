using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Switcher.App;

internal static class AppLocalizer
{
    public const string Auto = "Auto";
    public const string Ukrainian = "uk";
    public const string English = "en";

    private static readonly Dictionary<string, string> En = new(StringComparer.Ordinal)
    {
        ["Window.SettingsTitle"] = "EN-UA Switcher - Settings",
        ["Window.DiagnosticsTitle"] = "EN-UA Switcher - Diagnostics",

        ["App.Name"] = "EN-UA Switcher",

        ["Tab.General"] = "General",
        ["Tab.Hotkeys"] = "Hotkeys",
        ["Tab.Exclusions"] = "Exclusions",
        ["Tab.About"] = "About",

        ["Settings.AutomationEyebrow"] = "AUTOMATION",
        ["Settings.GeneralHeader"] = "General",
        ["Settings.GeneralDescription"] = "Choose how EN-UA Switcher reacts while you type. Safe hotkeys remain available even with Auto Mode disabled.",
        ["Settings.EnableAutoMode"] = "Enable Auto Mode (automatic correction after Space / Enter / Tab)",
        ["Settings.SafeOnlyAutoMode"] = "Safe-only browser auto-replace (requires Auto Mode)",
        ["Settings.ElectronUiaPath"] = "Electron app path: UIA read + Backspace replace (requires Auto Mode)",
        ["Settings.AddressBarSkip"] = "Skip Auto Mode in browser address bars",
        ["Settings.DelimiterKeys"] = "Delimiter keys",
        ["Settings.DelimiterDescription"] = "Choose which keys trigger auto-correction after a word.",
        ["Settings.CorrectOnSpace"] = "Correct on Space",
        ["Settings.CorrectOnEnter"] = "Correct on Enter",
        ["Settings.CorrectOnTab"] = "Correct on Tab",
        ["Tooltip.Settings.EnableAutoMode"] = "Auto Mode controls automatic corrections while typing. Safe-mode hotkeys work independently.",
        ["Tooltip.Settings.SafeOnlyAutoMode"] = "Uses only exact-slice safe replacement paths. Browser best-effort auto-replace is skipped.",
        ["Tooltip.Settings.ElectronUiaPath"] = "Experimental Electron path: read the live word with UIA, then replace with Backspace+Unicode instead of Shift+Left selection.",
        ["Tooltip.Settings.AddressBarSkip"] = "Safest default: Auto Mode skips detected browser address bars so browser search or URL text is not rewritten while you type. Normal page fields still work.",
        ["Tooltip.Settings.CorrectOnSpace"] = "Trigger auto-correction when Space is pressed.",
        ["Tooltip.Settings.CorrectOnEnter"] = "Trigger auto-correction when Enter is pressed.",
        ["Tooltip.Settings.CorrectOnTab"] = "Trigger auto-correction when Tab is pressed.",

        ["Settings.ControlEyebrow"] = "CONTROL",
        ["Settings.CancelUndoHeader"] = "Cancel and undo",
        ["Settings.CancelUndoDescription"] = "These options protect the current word buffer and let you roll back an accidental auto-correction.",
        ["Settings.CancelOnBackspace"] = "Cancel on Backspace",
        ["Settings.CancelOnLeftArrow"] = "Cancel on Left Arrow",
        ["Settings.UndoOnBackspace"] = "Undo correction on Backspace",
        ["Tooltip.Settings.CancelOnBackspace"] = "Pressing Backspace clears the current word buffer, so Auto Mode will not correct that edited word.",
        ["Tooltip.Settings.CancelOnLeftArrow"] = "Pressing Left Arrow clears the current word buffer, so Auto Mode will not correct after caret movement.",
        ["Tooltip.Settings.UndoOnBackspace"] = "Press Backspace immediately after an auto-correction to restore the original text.",

        ["Settings.AppEyebrow"] = "APP",
        ["Settings.StartupDiagnosticsHeader"] = "Startup and diagnostics",
        ["Settings.StartupDiagnosticsDescription"] = "A few practical toggles for startup behavior and troubleshooting.",
        ["Settings.InterfaceLanguageHeader"] = "Interface language",
        ["Settings.InterfaceLanguageDescription"] = "Auto follows the Windows display language; manual choices are saved.",
        ["Settings.RunAtStartup"] = "Run at Windows startup",
        ["Settings.StartMinimized"] = "Start minimized to system tray",
        ["Settings.Diagnostics"] = "Enable diagnostics logging",
        ["Settings.SelectorExport"] = "Export selector examples for future ML analysis (experimental)",
        ["Settings.LearnedSelectorGate"] = "Use learned selector to reject doubtful auto-corrections (experimental)",
        ["Settings.StandardDiagnostics"] = "Standard diagnostics stay redacted. Structured selector export writes examples only when you enable it.",
        ["Tooltip.Settings.RunAtStartup"] = "Automatically start EN-UA Switcher when you log in to Windows.",
        ["Tooltip.Settings.SelectorExport"] = "Writes opt-in structured selector examples to a separate diagnostics file for future ML analysis.",
        ["Tooltip.Settings.LearnedSelectorGate"] = "Keeps heuristics as the primary decision maker and lets a loaded learned selector reject only low-confidence borderline candidates.",

        ["Hotkeys.SafeModeEyebrow"] = "SAFE MODE",
        ["Hotkeys.GlobalHotkeysHeader"] = "Global hotkeys",
        ["Hotkeys.Description"] = "These hotkeys act on the focused text target even if Auto Mode is turned off.",
        ["Hotkeys.StatusChecking"] = "Hotkeys status: checking...",
        ["Hotkeys.FixLastWord"] = "Fix last word",
        ["Hotkeys.FixSelectedText"] = "Fix selected text",
        ["Hotkeys.ChangeShortcut"] = "Change",
        ["Hotkeys.NoteEyebrow"] = "NOTE",
        ["Hotkeys.CurrentBehaviorHeader"] = "Current behavior",
        ["Hotkeys.Note"] = "Click a hotkey field and press your desired combination (modifier + letter/key). At least one of Ctrl, Shift, or Alt is required. Changes take effect after Save.",
        ["Tooltip.Hotkeys.LastWordHotkeyField"] = "Click Change or focus this field, then press a new modifier+key combination.",
        ["Tooltip.Hotkeys.SelectionHotkeyField"] = "Click Change or focus this field, then press a new modifier+key combination.",
        ["Tooltip.Hotkeys.ChangeShortcut"] = "Focus the shortcut field so the next key combination can be captured.",

        ["Exclusions.ScopeEyebrow"] = "SCOPE",
        ["Exclusions.ProcessesHeader"] = "Excluded processes",
        ["Exclusions.ProcessesDescription"] = "Choose a running app from the list or type a process name manually. Use names without .exe.",
        ["Exclusions.AddSelected"] = "Add Selected",
        ["Exclusions.Refresh"] = "Refresh",
        ["Exclusions.ManualProcess"] = "Manual add: type a name like chrome, telegram, element or codex and press Enter.",
        ["Exclusions.AddManual"] = "Add Manual",
        ["Exclusions.RemoveSelected"] = "Remove Selected",
        ["Exclusions.AutoModeEyebrow"] = "AUTO MODE",
        ["Exclusions.WordsHeader"] = "Excluded words",
        ["Exclusions.WordsDescription"] = "Words in this list are never auto-corrected. Add them in either layout: the opposite layout form is excluded too.",
        ["Exclusions.WordsExamples"] = "Examples: add either привіт or ghbdsn - both forms will be skipped by Auto Mode.",
        ["Exclusions.AddWord"] = "Add Word",
        ["Tooltip.Exclusions.RunningProcesses"] = "Choose a running process from the list.",
        ["Tooltip.Exclusions.NewProcess"] = "Example: chrome, notepad, code.",
        ["Tooltip.Exclusions.NewWord"] = "Example: привіт or ghbdsn.",

        ["About.ProductEyebrow"] = "PRODUCT",
        ["About.Subtitle"] = "EN / UA Keyboard Layout Corrector",
        ["About.Description"] = "EN-UA Switcher detects text typed in the wrong keyboard layout and corrects it across native editors, browser inputs and selected fallback paths.",
        ["About.TargetsEyebrow"] = "TARGETS",
        ["About.ContextsHeader"] = "Supported contexts",
        ["About.TargetNative"] = "Notepad, WordPad and other classic Win32 EDIT/RichEdit controls.",
        ["About.TargetBrowser"] = "Browser inputs and textareas when UI Automation exposes a writable field.",
        ["About.TargetElectron"] = "Electron apps and many desktop editors via SendInput-based correction paths.",
        ["About.ShortcutsEyebrow"] = "SHORTCUTS",
        ["About.SafeActionsHeader"] = "Safe mode actions",
        ["About.LastWordHotkey"] = "Ctrl+Shift+K - fix last word before caret",
        ["About.SelectionHotkey"] = "Ctrl+Shift+L - fix selected text",
        ["About.ContactEyebrow"] = "CONTACT",
        ["About.AuthorHeader"] = "Author",
        ["About.BuildEyebrow"] = "BUILD",
        ["About.VersionHeader"] = "Version",
        ["About.DetectingVersion"] = "Detecting version...",
        ["About.VersionUnavailable"] = "Version unavailable",

        ["Action.ViewDiagnostics"] = "View Diagnostics",
        ["Action.Cancel"] = "Cancel",
        ["Action.Save"] = "Save",
        ["Action.Saved"] = "Saved",

        ["Status.AutoOnSafeOnly"] = "Auto Mode ON (safe-only)",
        ["Status.AutoOnBroad"] = "Auto Mode ON (experimental broad)",
        ["Status.AutoOff"] = "Auto Mode OFF",
        ["Status.RunningHotkeysOn"] = "Engine running - {0}, hotkeys ON",
        ["Status.RunningHotkeysUnavailable"] = "Engine running - {0}, hotkeys unavailable",
        ["Hotkeys.StatusActive"] = "Hotkeys status: active globally. They do not depend on Auto Mode.",
        ["Hotkeys.StatusUnavailable"] = "Hotkeys status: unavailable. {0}",
        ["Hotkeys.RegistrationFailed"] = "Registration failed.",

        ["Tray.OpenSettings"] = "Open Settings",
        ["Tray.EnableAuto"] = "Enable automatic keyboard layout switching",
        ["Tray.DisableAuto"] = "Disable automatic keyboard layout switching",
        ["Tray.ViewDiagnostics"] = "View Diagnostics",
        ["Tray.Exit"] = "Exit",

        ["Diagnostics.Clear"] = "Clear",
        ["Diagnostics.CopySelected"] = "Copy Selected",
        ["Diagnostics.AutoScroll"] = "Auto-scroll",
        ["Diagnostics.EntryCount"] = "{0} entries",
        ["Diagnostics.Time"] = "Time",
        ["Diagnostics.Process"] = "Process",
        ["Diagnostics.Adapter"] = "Adapter",
        ["Diagnostics.Operation"] = "Operation",
        ["Diagnostics.Original"] = "Original",
        ["Diagnostics.Converted"] = "Converted",
        ["Diagnostics.Result"] = "Result",
        ["Diagnostics.Reason"] = "Reason"
    };

    private static readonly Dictionary<string, string> Uk = new(StringComparer.Ordinal)
    {
        ["Window.SettingsTitle"] = "EN-UA Switcher - Налаштування",
        ["Window.DiagnosticsTitle"] = "EN-UA Switcher - Діагностика",

        ["App.Name"] = "EN-UA Switcher",

        ["Tab.General"] = "Загальні",
        ["Tab.Hotkeys"] = "Гарячі клавіші",
        ["Tab.Exclusions"] = "Винятки",
        ["Tab.About"] = "Про застосунок",

        ["Settings.AutomationEyebrow"] = "АВТОМАТИЗАЦІЯ",
        ["Settings.GeneralHeader"] = "Загальні",
        ["Settings.GeneralDescription"] = "Оберіть, як EN-UA Switcher реагує під час набору. Безпечні гарячі клавіші працюють навіть коли Auto Mode вимкнений.",
        ["Settings.EnableAutoMode"] = "Увімкнути Auto Mode (автокорекція після Space / Enter / Tab)",
        ["Settings.SafeOnlyAutoMode"] = "Безпечна автозаміна в браузері (потребує Auto Mode)",
        ["Settings.ElectronUiaPath"] = "Шлях для Electron: читання UIA + заміна Backspace (потребує Auto Mode)",
        ["Settings.AddressBarSkip"] = "Пропускати Auto Mode в адресному рядку браузера",
        ["Settings.DelimiterKeys"] = "Клавіші межі слова",
        ["Settings.DelimiterDescription"] = "Оберіть клавіші, після яких запускається автокорекція слова.",
        ["Settings.CorrectOnSpace"] = "Виправляти після Space",
        ["Settings.CorrectOnEnter"] = "Виправляти після Enter",
        ["Settings.CorrectOnTab"] = "Виправляти після Tab",
        ["Tooltip.Settings.EnableAutoMode"] = "Auto Mode керує тільки автоматичними виправленнями під час набору. Безпечні гарячі клавіші працюють окремо.",
        ["Tooltip.Settings.SafeOnlyAutoMode"] = "Використовує лише шляхи заміни з точно перевіреним фрагментом. Browser best-effort автозаміна пропускається.",
        ["Tooltip.Settings.ElectronUiaPath"] = "Експериментальний шлях для Electron: читає живе слово через UIA і замінює його Backspace+Unicode замість Shift+Left виділення.",
        ["Tooltip.Settings.AddressBarSkip"] = "Найбезпечніший дефолт: Auto Mode пропускає знайдені адресні рядки браузера, щоб пошуковий або URL-текст не переписувався під час набору. Звичайні поля на сторінках працюють далі.",
        ["Tooltip.Settings.CorrectOnSpace"] = "Запускати автокорекцію після натискання Space.",
        ["Tooltip.Settings.CorrectOnEnter"] = "Запускати автокорекцію після натискання Enter.",
        ["Tooltip.Settings.CorrectOnTab"] = "Запускати автокорекцію після натискання Tab.",

        ["Settings.ControlEyebrow"] = "КЕРУВАННЯ",
        ["Settings.CancelUndoHeader"] = "Скасування й відкат",
        ["Settings.CancelUndoDescription"] = "Ці параметри захищають поточний буфер слова та дають швидко відкотити випадкову автокорекцію.",
        ["Settings.CancelOnBackspace"] = "Скасувати буфер після Backspace",
        ["Settings.CancelOnLeftArrow"] = "Скасувати буфер після стрілки вліво",
        ["Settings.UndoOnBackspace"] = "Відкотити виправлення через Backspace",
        ["Tooltip.Settings.CancelOnBackspace"] = "Backspace очищає буфер поточного слова, тому Auto Mode не виправлятиме відредаговане слово.",
        ["Tooltip.Settings.CancelOnLeftArrow"] = "Стрілка вліво очищає буфер поточного слова, тому Auto Mode не виправлятиме після руху курсора.",
        ["Tooltip.Settings.UndoOnBackspace"] = "Натисніть Backspace одразу після автокорекції, щоб повернути початковий текст.",

        ["Settings.AppEyebrow"] = "ЗАСТОСУНОК",
        ["Settings.StartupDiagnosticsHeader"] = "Запуск і діагностика",
        ["Settings.StartupDiagnosticsDescription"] = "Практичні перемикачі для автозапуску й діагностики.",
        ["Settings.InterfaceLanguageHeader"] = "Мова інтерфейсу",
        ["Settings.InterfaceLanguageDescription"] = "Auto бере мову від Windows; ручний вибір зберігається.",
        ["Settings.RunAtStartup"] = "Запускати разом із Windows",
        ["Settings.StartMinimized"] = "Запускати згорнутим у трей",
        ["Settings.Diagnostics"] = "Увімкнути діагностичний журнал",
        ["Settings.SelectorExport"] = "Експортувати приклади для майбутнього ML-аналізу (експериментально)",
        ["Settings.LearnedSelectorGate"] = "Увімкнути ML-фільтр сумнівних автозамін (експериментально)",
        ["Settings.StandardDiagnostics"] = "Стандартна діагностика залишається знеособленою. Структурований експорт пише приклади лише коли ви його вмикаєте.",
        ["Tooltip.Settings.RunAtStartup"] = "Автоматично запускати EN-UA Switcher після входу у Windows.",
        ["Tooltip.Settings.SelectorExport"] = "Пише opt-in структуровані selector-приклади в окремий файл діагностики для майбутнього ML-аналізу.",
        ["Tooltip.Settings.LearnedSelectorGate"] = "Залишає евристику головним рішенням і дозволяє завантаженому learned selector лише відхиляти низьковпевнені borderline-кандидати.",

        ["Hotkeys.SafeModeEyebrow"] = "БЕЗПЕЧНИЙ РЕЖИМ",
        ["Hotkeys.GlobalHotkeysHeader"] = "Глобальні гарячі клавіші",
        ["Hotkeys.Description"] = "Ці клавіші працюють із поточним текстовим полем навіть коли Auto Mode вимкнений.",
        ["Hotkeys.StatusChecking"] = "Стан гарячих клавіш: перевірка...",
        ["Hotkeys.FixLastWord"] = "Виправити останнє слово",
        ["Hotkeys.FixSelectedText"] = "Виправити виділений текст",
        ["Hotkeys.ChangeShortcut"] = "Змінити",
        ["Hotkeys.NoteEyebrow"] = "ПРИМІТКА",
        ["Hotkeys.CurrentBehaviorHeader"] = "Поточна поведінка",
        ["Hotkeys.Note"] = "Клацніть поле гарячої клавіші й натисніть потрібну комбінацію (модифікатор + літера/клавіша). Потрібен Ctrl, Shift або Alt. Зміни набудуть чинності після збереження.",
        ["Tooltip.Hotkeys.LastWordHotkeyField"] = "Натисніть Змінити або сфокусуйте це поле, а потім введіть нову комбінацію модифікатор+клавіша.",
        ["Tooltip.Hotkeys.SelectionHotkeyField"] = "Натисніть Змінити або сфокусуйте це поле, а потім введіть нову комбінацію модифікатор+клавіша.",
        ["Tooltip.Hotkeys.ChangeShortcut"] = "Фокусує поле гарячої клавіші, щоб наступна комбінація була захоплена.",

        ["Exclusions.ScopeEyebrow"] = "ОБЛАСТЬ",
        ["Exclusions.ProcessesHeader"] = "Виключені процеси",
        ["Exclusions.ProcessesDescription"] = "Оберіть запущений застосунок зі списку або введіть назву процесу вручну. Використовуйте назви без .exe.",
        ["Exclusions.AddSelected"] = "Додати вибране",
        ["Exclusions.Refresh"] = "Оновити",
        ["Exclusions.ManualProcess"] = "Додати вручну: введіть назву на кшталт chrome, telegram, element або codex і натисніть Enter.",
        ["Exclusions.AddManual"] = "Додати вручну",
        ["Exclusions.RemoveSelected"] = "Видалити вибране",
        ["Exclusions.AutoModeEyebrow"] = "AUTO MODE",
        ["Exclusions.WordsHeader"] = "Виключені слова",
        ["Exclusions.WordsDescription"] = "Слова в цьому списку ніколи не виправляються Auto Mode. Додайте слово в будь-якій розкладці: форма в іншій розкладці теж буде виключена.",
        ["Exclusions.WordsExamples"] = "Приклад: додайте привіт або ghbdsn - обидві форми буде пропущено в Auto Mode.",
        ["Exclusions.AddWord"] = "Додати слово",
        ["Tooltip.Exclusions.RunningProcesses"] = "Оберіть запущений процес зі списку.",
        ["Tooltip.Exclusions.NewProcess"] = "Приклад: chrome, notepad, code.",
        ["Tooltip.Exclusions.NewWord"] = "Приклад: привіт або ghbdsn.",

        ["About.ProductEyebrow"] = "ПРОДУКТ",
        ["About.Subtitle"] = "Коректор EN / UA розкладки клавіатури",
        ["About.Description"] = "EN-UA Switcher визначає текст, набраний у неправильній розкладці, і виправляє його в нативних редакторах, браузерних полях та вибраних fallback-шляхах.",
        ["About.TargetsEyebrow"] = "ЦІЛІ",
        ["About.ContextsHeader"] = "Підтримувані контексти",
        ["About.TargetNative"] = "Notepad, WordPad та інші класичні Win32 EDIT/RichEdit поля.",
        ["About.TargetBrowser"] = "Браузерні inputs і textareas, коли UI Automation відкриває поле для запису.",
        ["About.TargetElectron"] = "Electron-застосунки та багато desktop-редакторів через SendInput-шляхи виправлення.",
        ["About.ShortcutsEyebrow"] = "СКОРОЧЕННЯ",
        ["About.SafeActionsHeader"] = "Дії безпечного режиму",
        ["About.LastWordHotkey"] = "Ctrl+Shift+K - виправити останнє слово перед курсором",
        ["About.SelectionHotkey"] = "Ctrl+Shift+L - виправити виділений текст",
        ["About.ContactEyebrow"] = "КОНТАКТ",
        ["About.AuthorHeader"] = "Автор",
        ["About.BuildEyebrow"] = "ЗБІРКА",
        ["About.VersionHeader"] = "Версія",
        ["About.DetectingVersion"] = "Визначення версії...",
        ["About.VersionUnavailable"] = "Версія недоступна",

        ["Action.ViewDiagnostics"] = "Відкрити діагностику",
        ["Action.Cancel"] = "Скасувати",
        ["Action.Save"] = "Зберегти",
        ["Action.Saved"] = "Збережено",

        ["Status.AutoOnSafeOnly"] = "Auto Mode увімкнено (safe-only)",
        ["Status.AutoOnBroad"] = "Auto Mode увімкнено (експериментально широкий)",
        ["Status.AutoOff"] = "Auto Mode вимкнено",
        ["Status.RunningHotkeysOn"] = "Рушій працює - {0}, гарячі клавіші увімкнені",
        ["Status.RunningHotkeysUnavailable"] = "Рушій працює - {0}, гарячі клавіші недоступні",
        ["Hotkeys.StatusActive"] = "Стан гарячих клавіш: глобально активні. Вони не залежать від Auto Mode.",
        ["Hotkeys.StatusUnavailable"] = "Стан гарячих клавіш: недоступні. {0}",
        ["Hotkeys.RegistrationFailed"] = "Реєстрація не вдалася.",

        ["Tray.OpenSettings"] = "Відкрити налаштування",
        ["Tray.EnableAuto"] = "Увімкнути автоматичне перемикання розкладки",
        ["Tray.DisableAuto"] = "Вимкнути автоматичне перемикання розкладки",
        ["Tray.ViewDiagnostics"] = "Відкрити діагностику",
        ["Tray.Exit"] = "Вийти",

        ["Diagnostics.Clear"] = "Очистити",
        ["Diagnostics.CopySelected"] = "Копіювати вибране",
        ["Diagnostics.AutoScroll"] = "Автопрокрутка",
        ["Diagnostics.EntryCount"] = "{0} записів",
        ["Diagnostics.Time"] = "Час",
        ["Diagnostics.Process"] = "Процес",
        ["Diagnostics.Adapter"] = "Адаптер",
        ["Diagnostics.Operation"] = "Операція",
        ["Diagnostics.Original"] = "Було",
        ["Diagnostics.Converted"] = "Стало",
        ["Diagnostics.Result"] = "Результат",
        ["Diagnostics.Reason"] = "Reason"
    };

    public static string ResolveLanguage(string? configuredLanguage)
    {
        if (string.Equals(configuredLanguage, Ukrainian, StringComparison.OrdinalIgnoreCase))
            return Ukrainian;

        if (string.Equals(configuredLanguage, English, StringComparison.OrdinalIgnoreCase))
            return English;

        string current = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (!string.Equals(current, Ukrainian, StringComparison.OrdinalIgnoreCase))
            current = CultureInfo.CurrentCulture.TwoLetterISOLanguageName;

        return string.Equals(current, Ukrainian, StringComparison.OrdinalIgnoreCase)
            ? Ukrainian
            : English;
    }

    public static string NormalizeConfiguredLanguage(string? configuredLanguage) =>
        string.Equals(configuredLanguage, Ukrainian, StringComparison.OrdinalIgnoreCase)
            ? Ukrainian
            : string.Equals(configuredLanguage, English, StringComparison.OrdinalIgnoreCase)
                ? English
                : Auto;

    public static string T(string key, string? configuredLanguage)
    {
        string language = ResolveLanguage(configuredLanguage);
        var table = language == Ukrainian ? Uk : En;
        return table.TryGetValue(key, out string? value)
            ? value
            : En.TryGetValue(key, out value)
                ? value
                : key;
    }

    private static bool TryT(string key, string? configuredLanguage, out string text)
    {
        string language = ResolveLanguage(configuredLanguage);
        var table = language == Ukrainian ? Uk : En;
        if (table.TryGetValue(key, out string? value))
        {
            text = value;
            return true;
        }

        if (En.TryGetValue(key, out value))
        {
            text = value;
            return true;
        }

        text = key;
        return false;
    }

    public static string Format(string key, string? configuredLanguage, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key, configuredLanguage), args);

    public static void Apply(DependencyObject root, string? configuredLanguage)
    {
        var visited = new HashSet<DependencyObject>();
        ApplyRecursive(root, configuredLanguage, visited);
    }

    private static void ApplyRecursive(
        DependencyObject node,
        string? configuredLanguage,
        HashSet<DependencyObject> visited)
    {
        if (!visited.Add(node))
            return;

        if (node is FrameworkElement { Tag: string key } element
            && key.Contains('.', StringComparison.Ordinal))
        {
            ApplyText(element, T(key, configuredLanguage));
        }

        if (node is FrameworkElement frameworkElement)
            ApplyToolTip(frameworkElement, configuredLanguage);

        foreach (object child in LogicalTreeHelper.GetChildren(node))
        {
            if (child is DependencyObject childObject)
                ApplyRecursive(childObject, configuredLanguage, visited);
        }

        int visualChildren = 0;
        try
        {
            visualChildren = VisualTreeHelper.GetChildrenCount(node);
        }
        catch
        {
            // Some WPF objects are logical-only.
        }

        for (int i = 0; i < visualChildren; i++)
            ApplyRecursive(VisualTreeHelper.GetChild(node, i), configuredLanguage, visited);
    }

    private static void ApplyText(FrameworkElement element, string text)
    {
        switch (element)
        {
            case TextBlock textBlock:
                textBlock.Text = text;
                return;
            case HeaderedContentControl headered:
                headered.Header = text;
                return;
            case ContentControl { Content: string } content:
                content.Content = text;
                return;
        }
    }

    private static readonly DependencyProperty LocalizedToolTipKeyProperty =
        DependencyProperty.RegisterAttached(
            "LocalizedToolTipKey",
            typeof(string),
            typeof(AppLocalizer));

    private static void ApplyToolTip(FrameworkElement element, string? configuredLanguage)
    {
        string? key = element.GetValue(LocalizedToolTipKeyProperty) as string;
        if (string.IsNullOrWhiteSpace(key)
            && element.ToolTip is string tooltip
            && tooltip.StartsWith("Tooltip.", StringComparison.Ordinal))
        {
            key = tooltip;
            element.SetValue(LocalizedToolTipKeyProperty, key);
        }

        if (!string.IsNullOrWhiteSpace(key) && TryT(key, configuredLanguage, out string text))
            element.ToolTip = text;
    }
}
