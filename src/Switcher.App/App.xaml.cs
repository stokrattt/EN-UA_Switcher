using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using Switcher.Engine;

namespace Switcher.App;

public partial class App : System.Windows.Application
{
    // Per-user tray app: Local mutex avoids cross-session/global namespace edge cases on startup.
    private const string SingleInstanceMutexName = @"Local\Switcher_EN_UA_SingleInstance";

    private NotifyIcon? _trayIcon;
    private ContextMenuStrip? _trayMenu;
    private ToolStripItem? _openItem;
    private ToolStripMenuItem? _autoItem;
    private ToolStripItem? _diagItem;
    private ToolStripItem? _exitItem;
    private SwitcherEngine? _engine;
    private MainWindow? _mainWindow;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
            if (!createdNew)
            {
                System.Windows.MessageBox.Show(
                    "EN-UA Switcher is already running in the system tray.",
                    "EN-UA Switcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Current.Shutdown();
                return;
            }
            _ownsSingleInstanceMutex = true;

            _engine = new SwitcherEngine();
            _engine.SettingsApplied += UpdateTrayText;
            _engine.Start();

            BuildTrayIcon();

            if (!_engine.Settings.Current.StartMinimized)
                ShowMainWindow();
        }
        catch (Exception ex)
        {
            _engine?.Dispose();
            if (_engine != null)
                _engine.SettingsApplied -= UpdateTrayText;
            _engine = null;
            _trayIcon?.Dispose();
            _trayIcon = null;

            if (_ownsSingleInstanceMutex)
                _singleInstanceMutex?.ReleaseMutex();

            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
            _ownsSingleInstanceMutex = false;

            System.Windows.MessageBox.Show(
                $"EN-UA Switcher could not start.\n\n{ex.Message}",
                "EN-UA Switcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Current.Shutdown();
        }
    }

    private void BuildTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Text = "EN-UA Switcher",
            Visible = true,
            Icon = TrayIconHelper.CreateIcon()
        };

        _trayMenu = new ContextMenuStrip
        {
            Renderer = new MaterialMenuRenderer(),
            BackColor = Color.FromArgb(32, 29, 29),
            ForeColor = Color.FromArgb(244, 247, 251),
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Padding = new Padding(6),
            Font = new System.Drawing.Font("Segoe UI", 10f, System.Drawing.FontStyle.Regular),
            AutoSize = true
        };

        _openItem = _trayMenu.Items.Add(AppLocalizer.T("Tray.OpenSettings", _engine!.Settings.Current.InterfaceLanguage));
        _openItem.Padding = new Padding(18, 10, 26, 10);
        _openItem.TextAlign = ContentAlignment.MiddleLeft;
        _openItem.Click += (_, _) => ShowMainWindow();

        _autoItem = new ToolStripMenuItem(AppLocalizer.T("Tray.EnableAuto", _engine.Settings.Current.InterfaceLanguage))
        {
            CheckOnClick = false,
            Checked = _engine!.Settings.Current.AutoModeEnabled,
            ForeColor = Color.FromArgb(244, 247, 251),
            Padding = new Padding(18, 10, 26, 10)
        };
        _autoItem.TextAlign = ContentAlignment.MiddleLeft;
        _autoItem.Click += (_, _) =>
        {
            _engine!.Settings.Current.AutoModeEnabled = !_engine.Settings.Current.AutoModeEnabled;
            _engine.ApplySettings();
            UpdateTrayText();
        };
        _trayMenu.Items.Add(_autoItem);

        _diagItem = _trayMenu.Items.Add(AppLocalizer.T("Tray.ViewDiagnostics", _engine.Settings.Current.InterfaceLanguage));
        _diagItem.Padding = new Padding(18, 10, 26, 10);
        _diagItem.TextAlign = ContentAlignment.MiddleLeft;
        _diagItem.Click += (_, _) => ShowDiagnosticsWindow();

        _exitItem = _trayMenu.Items.Add(AppLocalizer.T("Tray.Exit", _engine.Settings.Current.InterfaceLanguage));
        _exitItem.Padding = new Padding(18, 10, 18, 10);
        _exitItem.TextAlign = ContentAlignment.MiddleLeft;
        _exitItem.Click += (_, _) => ExitApp();

        ResizeTrayMenu();
        _trayIcon.ContextMenuStrip = _trayMenu;
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
        UpdateTrayText();
    }

    private void UpdateTrayText()
    {
        if (_trayIcon == null || _engine == null) return;
        string language = _engine.Settings.Current.InterfaceLanguage;
        bool autoEnabled = _engine.Settings.Current.AutoModeEnabled;
        _trayIcon.Text = "EN-UA Switcher";

        if (_openItem != null)
            _openItem.Text = AppLocalizer.T("Tray.OpenSettings", language);

        if (_autoItem != null)
        {
            _autoItem.Checked = autoEnabled;
            _autoItem.Text = AppLocalizer.T(autoEnabled ? "Tray.DisableAuto" : "Tray.EnableAuto", language);
        }

        if (_diagItem != null)
            _diagItem.Text = AppLocalizer.T("Tray.ViewDiagnostics", language);

        if (_exitItem != null)
            _exitItem.Text = AppLocalizer.T("Tray.Exit", language);

        ResizeTrayMenu();
    }

    private void ResizeTrayMenu()
    {
        if (_trayMenu == null
            || _openItem == null
            || _autoItem == null
            || _diagItem == null
            || _exitItem == null)
        {
            return;
        }

        ToolStripItem[] textItems = [_openItem, _autoItem, _diagItem, _exitItem];
        int textWidth = textItems
            .Max(item => TextRenderer.MeasureText(item.Text ?? string.Empty, _trayMenu.Font).Width);
        int itemWidth = textWidth + 44;
        foreach (ToolStripItem item in textItems)
        {
            item.AutoSize = false;
            item.Width = itemWidth;
        }

        _trayMenu.AutoSize = false;
        _trayMenu.Width = itemWidth + 12;
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null || !_mainWindow.IsLoaded)
        {
            _mainWindow = new MainWindow(_engine!);
            _mainWindow.Show();
        }
        else
        {
            if (!_mainWindow.IsVisible)
                _mainWindow.Show();
            _mainWindow.Activate();
            _mainWindow.WindowState = WindowState.Normal;
        }
    }

    private void ShowDiagnosticsWindow()
    {
        var win = new DiagnosticsWindow(_engine!);
        win.Show();
    }

    private void ExitApp()
    {
        _engine?.Settings.Save();
        _engine?.Stop();
        if (_engine != null)
            _engine.SettingsApplied -= UpdateTrayText;
        _engine?.Dispose();
        _trayIcon?.Dispose();
        Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _engine?.Settings.Save();
        _engine?.Stop();
        if (_engine != null)
            _engine.SettingsApplied -= UpdateTrayText;
        _engine?.Dispose();
        _trayIcon?.Dispose();
        if (_ownsSingleInstanceMutex)
            _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
