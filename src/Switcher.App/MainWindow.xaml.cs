using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Switcher.Engine;

namespace Switcher.App;

public partial class MainWindow : Window
{
    private readonly SwitcherEngine _engine;

    public MainWindow(SwitcherEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        LoadSettings();

        // Set window icon from the same programmatic icon as the tray
        using var icon = TrayIconHelper.CreateIcon();
        Icon = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle, Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());

        Closing += (_, e) => { e.Cancel = true; SaveCurrentState(); Hide(); }; // close → save & hide to tray
    }

    private void LoadSettings()
    {
        var s = _engine.Settings.Current;
        ChkAutoMode.IsChecked        = s.AutoModeEnabled;
        ChkStrictAuto.IsChecked      = s.StrictAutoMode;
        ChkCorrectOnSpace.IsChecked  = s.CorrectOnSpace;
        ChkCorrectOnEnter.IsChecked  = s.CorrectOnEnter;
        ChkCorrectOnTab.IsChecked    = s.CorrectOnTab;
        ChkCancelOnBackspace.IsChecked = s.CancelOnBackspace;
        ChkCancelOnLeftArrow.IsChecked = s.CancelOnLeftArrow;
        ChkUndoOnBackspace.IsChecked = s.UndoOnBackspace;
        ChkStartMinimized.IsChecked  = s.StartMinimized;
        ChkDiagnostics.IsChecked     = s.DiagnosticsEnabled;
        TxtLastWordHotkey.Text       = s.SafeLastWordHotkey.FriendlyName;
        TxtSelectionHotkey.Text      = s.SafeSelectionHotkey.FriendlyName;

        LstExclusions.ItemsSource    = null;
        LstExclusions.ItemsSource    = new System.Collections.ObjectModel.ObservableCollection<string>(s.ExcludedProcessNames);

        UpdateStatusBar();
    }

    private void UpdateStatusBar()
    {
        bool auto = _engine.Settings.Current.AutoModeEnabled;
        StatusText.Text = auto ? "Engine running — Auto mode ON" : "Engine running — Safe mode only";
    }

    private void SaveCurrentState()
    {
        var s = _engine.Settings.Current;
        s.AutoModeEnabled   = ChkAutoMode.IsChecked == true;
        s.StrictAutoMode    = ChkStrictAuto.IsChecked == true;
        s.CorrectOnSpace    = ChkCorrectOnSpace.IsChecked == true;
        s.CorrectOnEnter    = ChkCorrectOnEnter.IsChecked == true;
        s.CorrectOnTab      = ChkCorrectOnTab.IsChecked == true;
        s.CancelOnBackspace = ChkCancelOnBackspace.IsChecked == true;
        s.CancelOnLeftArrow = ChkCancelOnLeftArrow.IsChecked == true;
        s.UndoOnBackspace   = ChkUndoOnBackspace.IsChecked == true;
        s.StartMinimized    = ChkStartMinimized.IsChecked == true;
        s.DiagnosticsEnabled = ChkDiagnostics.IsChecked == true;
        s.ExcludedProcessNames = new System.Collections.Generic.List<string>(
            LstExclusions.ItemsSource as System.Collections.ObjectModel.ObservableCollection<string>
            ?? Enumerable.Empty<string>());
        _engine.ApplySettings();
        UpdateStatusBar();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentState();
        Hide();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) { LoadSettings(); Hide(); }

    private void BtnDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var win = new DiagnosticsWindow(_engine);
        win.Show();
    }

    private void BtnAddExclusion_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtNewExclusion.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(name)) return;
        var list = LstExclusions.ItemsSource as System.Collections.ObjectModel.ObservableCollection<string>;
        if (list != null && !list.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(name);
            TxtNewExclusion.Clear();
        }
    }

    private void BtnRemoveExclusion_Click(object sender, RoutedEventArgs e)
    {
        var selected = LstExclusions.SelectedItem as string;
        if (selected == null) return;
        var list = LstExclusions.ItemsSource as System.Collections.ObjectModel.ObservableCollection<string>;
        list?.Remove(selected);
    }
}
