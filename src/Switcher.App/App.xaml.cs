using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using Switcher.Engine;

namespace Switcher.App;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Global\Switcher_EN_UA_SingleInstance";

    private NotifyIcon? _trayIcon;
    private SwitcherEngine? _engine;
    private MainWindow? _mainWindow;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
        _engine.Start();

        BuildTrayIcon();

        if (!_engine.Settings.Current.StartMinimized)
            ShowMainWindow();
    }

    private void BuildTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Text = "EN-UA Switcher — Layout Corrector",
            Visible = true,
            Icon = TrayIconHelper.CreateIcon()
        };

        var menu = new ContextMenuStrip
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

        var openItem = menu.Items.Add("Open Settings");
        openItem.Padding = new Padding(18, 10, 26, 10);
        openItem.TextAlign = ContentAlignment.MiddleLeft;
        openItem.Tag = "chevron";
        openItem.Click += (_, _) => ShowMainWindow();

        var autoItem = new ToolStripMenuItem("Auto Mode")
        {
            CheckOnClick = true,
            Checked = _engine!.Settings.Current.AutoModeEnabled,
            ForeColor = Color.FromArgb(244, 247, 251),
            Padding = new Padding(18, 10, 26, 10)
        };
        autoItem.TextAlign = ContentAlignment.MiddleLeft;
        autoItem.Click += (_, _) =>
        {
            _engine!.Settings.Current.AutoModeEnabled = autoItem.Checked;
            _engine.ApplySettings();
            UpdateTrayTooltip();
        };
        menu.Items.Add(autoItem);

        var diagItem = menu.Items.Add("View Diagnostics");
        diagItem.Padding = new Padding(18, 10, 26, 10);
        diagItem.TextAlign = ContentAlignment.MiddleLeft;
        diagItem.Tag = "chevron";
        diagItem.Click += (_, _) => ShowDiagnosticsWindow();

        var exitItem = menu.Items.Add("Exit");
        exitItem.Padding = new Padding(18, 10, 18, 10);
        exitItem.TextAlign = ContentAlignment.MiddleLeft;
        exitItem.Click += (_, _) => ExitApp();

        ToolStripItem[] textItems = [openItem, autoItem, diagItem, exitItem];
        int textWidth = textItems
            .Max(item => TextRenderer.MeasureText(item.Text ?? string.Empty, menu.Font).Width);
        int itemWidth = textWidth + 44;
        foreach (ToolStripItem item in textItems)
        {
            item.AutoSize = false;
            item.Width = itemWidth;
        }

        menu.AutoSize = false;
        menu.Width = itemWidth + 12;

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
        UpdateTrayTooltip();
    }

    private void UpdateTrayTooltip()
    {
        if (_trayIcon == null || _engine == null) return;
        bool autoEnabled = _engine.Settings.Current.AutoModeEnabled;
        bool safeOnly = _engine.Settings.Current.SafeOnlyAutoMode;
        bool hotkeysReady = _engine.SafeHotkeysAvailable;
        _trayIcon.Text = autoEnabled
            ? (hotkeysReady
                ? (safeOnly ? "EN-UA Switcher — Auto safe-only, hotkeys ON" : "EN-UA Switcher — Auto broad, hotkeys ON")
                : (safeOnly ? "EN-UA Switcher — Auto safe-only, hotkeys unavailable" : "EN-UA Switcher — Auto broad, hotkeys unavailable"))
            : (hotkeysReady ? "EN-UA Switcher — Auto OFF, hotkeys ON" : "EN-UA Switcher — Auto OFF, hotkeys unavailable");
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
        _engine?.Dispose();
        _trayIcon?.Dispose();
        Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _engine?.Settings.Save();
        _engine?.Stop();
        _engine?.Dispose();
        _trayIcon?.Dispose();
        if (_ownsSingleInstanceMutex)
            _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
