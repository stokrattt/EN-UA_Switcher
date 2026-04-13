using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

    public MainWindow(SwitcherEngine engine)
    {
        _engine = engine;
        InitializeComponent();

        LstExclusions.ItemsSource = _exclusions;
        LstExcludedWords.ItemsSource = _excludedWords;
        CmbRunningProcesses.ItemsSource = _runningProcesses;

        LoadSettings();

        using var icon = TrayIconHelper.CreateIcon();
        Icon = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle, Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());

        Closing += (_, e) => { e.Cancel = true; SaveCurrentState(); Hide(); };
    }

    private void LoadSettings()
    {
        var s = _engine.Settings.Current;
        ChkAutoMode.IsChecked = s.AutoModeEnabled;
        ChkCorrectOnSpace.IsChecked = s.CorrectOnSpace;
        ChkCorrectOnEnter.IsChecked = s.CorrectOnEnter;
        ChkCorrectOnTab.IsChecked = s.CorrectOnTab;
        ChkCancelOnBackspace.IsChecked = s.CancelOnBackspace;
        ChkCancelOnLeftArrow.IsChecked = s.CancelOnLeftArrow;
        ChkUndoOnBackspace.IsChecked = s.UndoOnBackspace;
        ChkStartMinimized.IsChecked = s.StartMinimized;
        ChkDiagnostics.IsChecked = s.DiagnosticsEnabled;
        TxtLastWordHotkey.Text = s.SafeLastWordHotkey.FriendlyName;
        TxtSelectionHotkey.Text = s.SafeSelectionHotkey.FriendlyName;

        _exclusions.Clear();
        foreach (string name in s.ExcludedProcessNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            _exclusions.Add(name);

        _excludedWords.Clear();
        foreach (string word in s.ExcludedWords.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            _excludedWords.Add(word);

        RefreshRunningProcesses();
        UpdateStatusBar();
    }

    private void UpdateStatusBar()
    {
        bool auto = _engine.Settings.Current.AutoModeEnabled;
        bool hotkeysReady = _engine.SafeHotkeysAvailable;
        StatusText.Text = auto
            ? (hotkeysReady ? "Engine running — Auto Mode ON, hotkeys ON" : "Engine running — Auto Mode ON, hotkeys unavailable")
            : (hotkeysReady ? "Engine running — Auto Mode OFF, hotkeys ON" : "Engine running — Auto Mode OFF, hotkeys unavailable");

        TxtHotkeyStatus.Text = hotkeysReady
            ? "Hotkeys status: active globally. They do not depend on Auto Mode."
            : $"Hotkeys status: unavailable. {_engine.SafeHotkeysError ?? "Registration failed."}";
    }

    private void SaveCurrentState()
    {
        var s = _engine.Settings.Current;
        s.AutoModeEnabled = ChkAutoMode.IsChecked == true;
        s.CorrectOnSpace = ChkCorrectOnSpace.IsChecked == true;
        s.CorrectOnEnter = ChkCorrectOnEnter.IsChecked == true;
        s.CorrectOnTab = ChkCorrectOnTab.IsChecked == true;
        s.CancelOnBackspace = ChkCancelOnBackspace.IsChecked == true;
        s.CancelOnLeftArrow = ChkCancelOnLeftArrow.IsChecked == true;
        s.UndoOnBackspace = ChkUndoOnBackspace.IsChecked == true;
        s.StartMinimized = ChkStartMinimized.IsChecked == true;
        s.DiagnosticsEnabled = ChkDiagnostics.IsChecked == true;
        s.ExcludedProcessNames = _exclusions.ToList();
        s.ExcludedWords = _excludedWords.ToList();
        _engine.ApplySettings();
        UpdateStatusBar();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentState();
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
}
