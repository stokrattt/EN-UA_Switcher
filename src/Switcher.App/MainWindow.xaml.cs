using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Switcher.Core;
using Switcher.Engine;

namespace Switcher.App;

public partial class MainWindow : Window
{
    private readonly SwitcherEngine _engine;
    private readonly ObservableCollection<string> _exclusions = [];
    private readonly ObservableCollection<string> _excludedWords = [];
    private readonly ObservableCollection<string> _runningProcesses = [];
    private string _savedStateSnapshot = string.Empty;
    private bool _isLoadingState;
    private HotkeyDescriptor _capturedLastWordHotkey = new(Modifiers: 6, VirtualKey: 0x4B);
    private HotkeyDescriptor _capturedSelectionHotkey = new(Modifiers: 6, VirtualKey: 0x4C);

    public MainWindow(SwitcherEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        SetAboutVersion();

        LstExclusions.ItemsSource = _exclusions;
        LstExcludedWords.ItemsSource = _excludedWords;
        CmbRunningProcesses.ItemsSource = _runningProcesses;
        HookDirtyTracking();

        LoadSettings();
        UpdateAutomationOptionState();

        using var icon = TrayIconHelper.CreateIcon();
        Icon = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle, Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());

        Closing += (_, e) => { e.Cancel = true; SaveCurrentState(); Hide(); };
    }

    private void SetAboutVersion()
    {
        try
        {
            var entryAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            string informationalVersion =
                entryAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? entryAssembly.GetName().Version?.ToString()
                ?? "unknown";

            string buildDate = File.Exists(entryAssembly.Location)
                ? File.GetLastWriteTime(entryAssembly.Location).ToString("yyyy-MM-dd")
                : DateTime.Now.ToString("yyyy-MM-dd");

            TxtAboutVersion.Tag = null;
            TxtAboutVersion.Text = $"{informationalVersion} - build {buildDate}";
        }
        catch
        {
            TxtAboutVersion.Tag = "About.VersionUnavailable";
            TxtAboutVersion.Text = AppLocalizer.T("About.VersionUnavailable", GetSelectedInterfaceLanguage());
        }
    }

    private void LoadSettings()
    {
        _isLoadingState = true;

        var s = _engine.Settings.Current;
        SelectInterfaceLanguage(s.InterfaceLanguage);
        ChkAutoMode.IsChecked = s.AutoModeEnabled;
        ChkSafeOnlyAutoMode.IsChecked = s.SafeOnlyAutoMode;
        ChkElectronUiaPath.IsChecked = s.ElectronUiaPathEnabled;
        ChkDisableBrowserAddressBarAutoCorrection.IsChecked = s.DisableBrowserAddressBarAutoCorrection;
        ChkCorrectOnSpace.IsChecked = s.CorrectOnSpace;
        ChkCorrectOnEnter.IsChecked = s.CorrectOnEnter;
        ChkCorrectOnTab.IsChecked = s.CorrectOnTab;
        ChkCancelOnBackspace.IsChecked = s.CancelOnBackspace;
        ChkCancelOnLeftArrow.IsChecked = s.CancelOnLeftArrow;
        ChkUndoOnBackspace.IsChecked = s.UndoOnBackspace;
        ChkRunAtStartup.IsChecked = s.RunAtStartup;
        ChkStartMinimized.IsChecked = s.StartMinimized;
        ChkDiagnostics.IsChecked = s.DiagnosticsEnabled;
        ChkSelectorDiagnosticsExport.IsChecked = s.SelectorDiagnosticsExportEnabled;
        ChkLearnedSelectorGate.IsChecked = s.LearnedSelectorGateEnabled;
        UpdateAutomationOptionState();
        _capturedLastWordHotkey = s.SafeLastWordHotkey;
        _capturedSelectionHotkey = s.SafeSelectionHotkey;
        TxtLastWordHotkey.Text = s.SafeLastWordHotkey.FriendlyName;
        TxtSelectionHotkey.Text = s.SafeSelectionHotkey.FriendlyName;

        _exclusions.Clear();
        foreach (string name in s.ExcludedProcessNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            _exclusions.Add(name);

        _excludedWords.Clear();
        foreach (string word in s.ExcludedWords.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            _excludedWords.Add(word);

        RefreshRunningProcesses();
        ApplyLocalization();
        UpdateStatusBar();
        _savedStateSnapshot = BuildPersistedStateSnapshot();
        _isLoadingState = false;
        UpdateDirtyState();
    }

    private void UpdateStatusBar()
    {
        bool auto = _engine.Settings.Current.AutoModeEnabled;
        bool safeOnly = _engine.Settings.Current.SafeOnlyAutoMode;
        bool hotkeysReady = _engine.SafeHotkeysAvailable;
        string language = GetSelectedInterfaceLanguage();
        string autoState = auto
            ? (safeOnly
                ? AppLocalizer.T("Status.AutoOnSafeOnly", language)
                : AppLocalizer.T("Status.AutoOnBroad", language))
            : AppLocalizer.T("Status.AutoOff", language);
        StatusText.Text = auto
            ? (hotkeysReady
                ? AppLocalizer.Format("Status.RunningHotkeysOn", language, autoState)
                : AppLocalizer.Format("Status.RunningHotkeysUnavailable", language, autoState))
            : (hotkeysReady
                ? AppLocalizer.Format("Status.RunningHotkeysOn", language, autoState)
                : AppLocalizer.Format("Status.RunningHotkeysUnavailable", language, autoState));

        TxtHotkeyStatus.Text = hotkeysReady
            ? AppLocalizer.T("Hotkeys.StatusActive", language)
            : AppLocalizer.Format(
                "Hotkeys.StatusUnavailable",
                language,
                _engine.SafeHotkeysError ?? AppLocalizer.T("Hotkeys.RegistrationFailed", language));
    }

    private void SaveCurrentState()
    {
        var s = _engine.Settings.Current;
        s.InterfaceLanguage = GetSelectedInterfaceLanguage();
        s.AutoModeEnabled = ChkAutoMode.IsChecked == true;
        s.SafeOnlyAutoMode = ChkSafeOnlyAutoMode.IsChecked == true;
        s.ElectronUiaPathEnabled = ChkElectronUiaPath.IsChecked == true;
        s.DisableBrowserAddressBarAutoCorrection = ChkDisableBrowserAddressBarAutoCorrection.IsChecked == true;
        s.CorrectOnSpace = ChkCorrectOnSpace.IsChecked == true;
        s.CorrectOnEnter = ChkCorrectOnEnter.IsChecked == true;
        s.CorrectOnTab = ChkCorrectOnTab.IsChecked == true;
        s.CancelOnBackspace = ChkCancelOnBackspace.IsChecked == true;
        s.CancelOnLeftArrow = ChkCancelOnLeftArrow.IsChecked == true;
        s.UndoOnBackspace = ChkUndoOnBackspace.IsChecked == true;
        s.RunAtStartup = ChkRunAtStartup.IsChecked == true;
        s.StartMinimized = ChkStartMinimized.IsChecked == true;
        s.DiagnosticsEnabled = ChkDiagnostics.IsChecked == true;
        s.SelectorDiagnosticsExportEnabled = ChkSelectorDiagnosticsExport.IsChecked == true;
        s.LearnedSelectorGateEnabled = ChkLearnedSelectorGate.IsChecked == true;
        s.ExcludedProcessNames = _exclusions.ToList();
        s.ExcludedWords = _excludedWords.ToList();
        s.SafeLastWordHotkey = _capturedLastWordHotkey;
        s.SafeSelectionHotkey = _capturedSelectionHotkey;
        _engine.ApplySettings();
        ApplyLocalization();
        UpdateStatusBar();
        _savedStateSnapshot = BuildPersistedStateSnapshot();
        UpdateDirtyState();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentState();
    }

    private void CmbInterfaceLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingState)
            return;

        ApplyLocalization();
        UpdateDirtyState();
    }

    private void BtnChangeLastWordHotkey_Click(object sender, RoutedEventArgs e) =>
        FocusHotkeyEditor(TxtLastWordHotkey);

    private void BtnChangeSelectionHotkey_Click(object sender, RoutedEventArgs e) =>
        FocusHotkeyEditor(TxtSelectionHotkey);

    private static void FocusHotkeyEditor(System.Windows.Controls.TextBox textBox)
    {
        textBox.Focus();
        Keyboard.Focus(textBox);
        textBox.SelectAll();
    }

    private void TxtHotkey_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Ignore lone modifier keys
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System)
        {
            e.Handled = true;
            return;
        }

        bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
        bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        bool alt = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);
        e.Handled = true;

        if (!ctrl && !shift && !alt) return; // no modifier — ignore

        uint modifiers = 0;
        if (ctrl) modifiers |= 2;
        if (alt) modifiers |= 1;
        if (shift) modifiers |= 4;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(e.Key);
        var hotkey = new HotkeyDescriptor(modifiers, vk);
        var txt = (System.Windows.Controls.TextBox)sender;
        if (txt == TxtLastWordHotkey) _capturedLastWordHotkey = hotkey;
        else _capturedSelectionHotkey = hotkey;

        txt.Text = hotkey.FriendlyName;
        UpdateDirtyState();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        LoadSettings();
        Hide();
    }

    private void BtnDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var win = new DiagnosticsWindow(_engine);
        win.Show();
    }

    private void BtnAddExclusion_Click(object sender, RoutedEventArgs e)
    {
        if (AddExclusionName(TxtNewExclusion.Text))
            TxtNewExclusion.Clear();
    }

    private void BtnAddSuggestedExclusion_Click(object sender, RoutedEventArgs e)
    {
        if (CmbRunningProcesses.SelectedItem is string processName && AddExclusionName(processName))
            CmbRunningProcesses.SelectedItem = null;
    }

    private void BtnRefreshProcesses_Click(object sender, RoutedEventArgs e)
    {
        RefreshRunningProcesses();
        CmbRunningProcesses.IsDropDownOpen = true;
        CmbRunningProcesses.Focus();
    }

    private void BtnRemoveExclusion_Click(object sender, RoutedEventArgs e)
    {
        if (LstExclusions.SelectedItem is string selected)
            _exclusions.Remove(selected);
    }

    private void TxtNewExclusion_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (AddExclusionName(TxtNewExclusion.Text))
            TxtNewExclusion.Clear();

        e.Handled = true;
    }

    private void CmbRunningProcesses_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CmbRunningProcesses.IsDropDownOpen = true;
        if (FindDescendant<System.Windows.Controls.TextBox>(CmbRunningProcesses) is System.Windows.Controls.TextBox editableTextBox)
        {
            editableTextBox.Foreground = (System.Windows.Media.Brush)FindResource("ForegroundBrush");
            editableTextBox.CaretBrush = (System.Windows.Media.Brush)FindResource("ForegroundBrush");
            editableTextBox.Background = System.Windows.Media.Brushes.Transparent;
            editableTextBox.SelectAll();
        }
    }

    private void CmbRunningProcesses_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (CmbRunningProcesses.IsDropDownOpen)
            return;

        CmbRunningProcesses.Focus();
        CmbRunningProcesses.IsDropDownOpen = true;
        e.Handled = true;
    }

    private void LstExclusions_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LstExclusions.SelectedItem is string selected)
            _exclusions.Remove(selected);
    }

    private void BtnAddExcludedWord_Click(object sender, RoutedEventArgs e)
    {
        if (AddExcludedWord(TxtNewExcludedWord.Text))
            TxtNewExcludedWord.Clear();
    }

    private void BtnRemoveExcludedWord_Click(object sender, RoutedEventArgs e)
    {
        if (LstExcludedWords.SelectedItem is string selected)
            _excludedWords.Remove(selected);
    }

    private void TxtNewExcludedWord_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (AddExcludedWord(TxtNewExcludedWord.Text))
            TxtNewExcludedWord.Clear();

        e.Handled = true;
    }

    private void LstExcludedWords_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LstExcludedWords.SelectedItem is string selected)
            _excludedWords.Remove(selected);
    }

    private void NestedScrollHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is System.Windows.Controls.ComboBox comboBox && comboBox.IsDropDownOpen)
        {
            if (TryScrollComboDropDown(comboBox, e.Delta))
            {
                e.Handled = true;
                return;
            }

            return;
        }

        if (sender is System.Windows.Controls.ListBox listBox)
        {
            var innerScroll = FindDescendant<ScrollViewer>(listBox);
            if (innerScroll != null && innerScroll.ExtentHeight > innerScroll.ViewportHeight + 1)
                return;
        }

        var parentScroll = FindAncestor<ScrollViewer>(sender as DependencyObject);
        if (parentScroll == null)
            return;

        e.Handled = true;
        var relay = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender
        };
        parentScroll.RaiseEvent(relay);
    }

    private static bool TryScrollComboDropDown(System.Windows.Controls.ComboBox comboBox, int delta)
    {
        if (comboBox.Template.FindName("Popup", comboBox) is not Popup popup
            || popup.Child is not DependencyObject child)
        {
            return false;
        }

        ScrollViewer? popupScroll = FindDescendant<ScrollViewer>(child);
        if (popupScroll is null)
            return false;

        double nextOffset = popupScroll.VerticalOffset - (delta / 3.0);
        nextOffset = Math.Max(0, Math.Min(popupScroll.ScrollableHeight, nextOffset));
        popupScroll.ScrollToVerticalOffset(nextOffset);
        return true;
    }

    private bool AddExclusionName(string rawName)
    {
        string name = NormalizeProcessName(rawName);
        if (string.IsNullOrEmpty(name))
            return false;

        if (_exclusions.Contains(name, StringComparer.OrdinalIgnoreCase))
            return false;

        _exclusions.Add(name);
        SortCollection(_exclusions);
        LstExclusions.SelectedItem = name;
        CmbRunningProcesses.Text = string.Empty;
        return true;
    }

    private bool AddExcludedWord(string rawWord)
    {
        string normalized = KeyboardLayoutMap.NormalizeWordToken(rawWord);
        if (string.IsNullOrEmpty(normalized))
            return false;

        if (_excludedWords.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            return false;

        _excludedWords.Add(normalized);
        SortCollection(_excludedWords);
        LstExcludedWords.SelectedItem = normalized;
        return true;
    }

    private void RefreshRunningProcesses()
    {
        var names = Process.GetProcesses()
            .Select(process =>
            {
                try { return process.ProcessName; }
                catch { return null; }
            })
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(NormalizeProcessName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => name is not ("idle" or "system"))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _runningProcesses.Clear();
        foreach (string name in names)
            _runningProcesses.Add(name);
    }

    private static string NormalizeProcessName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return string.Empty;

        string name = rawName.Trim().ToLowerInvariant();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
    }

    private static void SortCollection(ObservableCollection<string> items)
    {
        var ordered = items.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        items.Clear();
        foreach (string item in ordered)
            items.Add(item);
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        DependencyObject? current = start;
        while (current != null)
        {
            if (current is T match)
                return match;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject? start) where T : DependencyObject
    {
        if (start == null)
            return null;

        int count = VisualTreeHelper.GetChildrenCount(start);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(start, i);
            if (child is T match)
                return match;

            var nested = FindDescendant<T>(child);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private void HookDirtyTracking()
    {
        foreach (var checkBox in new[]
        {
            ChkAutoMode,
            ChkSafeOnlyAutoMode,
            ChkElectronUiaPath,
            ChkDisableBrowserAddressBarAutoCorrection,
            ChkCorrectOnSpace,
            ChkCorrectOnEnter,
            ChkCorrectOnTab,
            ChkCancelOnBackspace,
            ChkCancelOnLeftArrow,
            ChkUndoOnBackspace,
            ChkRunAtStartup,
            ChkStartMinimized,
            ChkDiagnostics,
            ChkSelectorDiagnosticsExport,
            ChkLearnedSelectorGate
        })
        {
            checkBox.Checked += PersistedStateControlChanged;
            checkBox.Unchecked += PersistedStateControlChanged;
        }

        _exclusions.CollectionChanged += PersistedCollectionChanged;
        _excludedWords.CollectionChanged += PersistedCollectionChanged;
    }

    private void UpdateAutomationOptionState()
    {
        bool autoEnabled = ChkAutoMode.IsChecked == true;
        ChkSafeOnlyAutoMode.IsEnabled = autoEnabled;
        ChkElectronUiaPath.IsEnabled = autoEnabled;
        ChkDisableBrowserAddressBarAutoCorrection.IsEnabled = autoEnabled;
    }

    private void PersistedStateControlChanged(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, ChkAutoMode))
            UpdateAutomationOptionState();

        UpdateDirtyState();
    }

    private void PersistedCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateDirtyState();

    private void UpdateDirtyState()
    {
        if (_isLoadingState)
            return;

        bool isDirty = !string.Equals(BuildPersistedStateSnapshot(), _savedStateSnapshot, StringComparison.Ordinal);
        BtnSave.IsEnabled = isDirty;
        BtnSave.Tag = isDirty ? "Action.Save" : "Action.Saved";
        BtnSave.Content = AppLocalizer.T(isDirty ? "Action.Save" : "Action.Saved", GetSelectedInterfaceLanguage());
    }

    private string BuildPersistedStateSnapshot()
    {
        var builder = new StringBuilder(256);
        builder.Append(GetSelectedInterfaceLanguage()).Append('|');
        builder.Append(ChkAutoMode.IsChecked == true).Append('|');
        builder.Append(ChkSafeOnlyAutoMode.IsChecked == true).Append('|');
        builder.Append(ChkElectronUiaPath.IsChecked == true).Append('|');
        builder.Append(ChkDisableBrowserAddressBarAutoCorrection.IsChecked == true).Append('|');
        builder.Append(ChkCorrectOnSpace.IsChecked == true).Append('|');
        builder.Append(ChkCorrectOnEnter.IsChecked == true).Append('|');
        builder.Append(ChkCorrectOnTab.IsChecked == true).Append('|');
        builder.Append(ChkCancelOnBackspace.IsChecked == true).Append('|');
        builder.Append(ChkCancelOnLeftArrow.IsChecked == true).Append('|');
        builder.Append(ChkUndoOnBackspace.IsChecked == true).Append('|');
        builder.Append(ChkRunAtStartup.IsChecked == true).Append('|');
        builder.Append(ChkStartMinimized.IsChecked == true).Append('|');
        builder.Append(ChkDiagnostics.IsChecked == true).Append('|');
        builder.Append(ChkSelectorDiagnosticsExport.IsChecked == true).Append('|');
        builder.Append(ChkLearnedSelectorGate.IsChecked == true).Append('|');
        builder.Append(string.Join(';', _exclusions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))).Append('|');
        builder.Append(string.Join(';', _excludedWords.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))).Append('|');
        builder.Append(_capturedLastWordHotkey.FriendlyName).Append('|');
        builder.Append(_capturedSelectionHotkey.FriendlyName);
        return builder.ToString();
    }

    private void ApplyLocalization()
    {
        string language = GetSelectedInterfaceLanguage();
        AppLocalizer.Apply(this, language);
        Title = AppLocalizer.T("Window.SettingsTitle", language);
        UpdateStatusBar();
        UpdateDirtyState();
    }

    private string GetSelectedInterfaceLanguage()
    {
        if (CmbInterfaceLanguage.SelectedValue is string selected)
            return AppLocalizer.NormalizeConfiguredLanguage(selected);

        return AppLocalizer.Auto;
    }

    private void SelectInterfaceLanguage(string? language)
    {
        string normalized = AppLocalizer.NormalizeConfiguredLanguage(language);
        foreach (var item in CmbInterfaceLanguage.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, normalized, StringComparison.OrdinalIgnoreCase))
            {
                CmbInterfaceLanguage.SelectedItem = item;
                return;
            }
        }

        CmbInterfaceLanguage.SelectedIndex = 0;
    }
}
