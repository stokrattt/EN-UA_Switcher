using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Switcher.Engine;

namespace Switcher.App;

public partial class App : System.Windows.Application
{
    private NotifyIcon? _trayIcon;
    private SwitcherEngine? _engine;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
            Text = "Switcher — EN/UA Layout Corrector",
            Visible = true,
            Icon = TrayIconHelper.CreateIcon()
        };

        var menu = new ContextMenuStrip();
        menu.Renderer = new DarkMenuRenderer();
        menu.BackColor = Color.FromArgb(45, 45, 48);

        var openItem = menu.Items.Add("Open Settings");
        openItem.Click += (_, _) => ShowMainWindow();

        var autoItem = new ToolStripMenuItem("Auto Mode")
        {
            CheckOnClick = true,
            Checked = _engine!.Settings.Current.AutoModeEnabled,
            ForeColor = Color.FromArgb(224, 224, 224)
        };
        autoItem.Click += (_, _) =>
        {
            _engine!.Settings.Current.AutoModeEnabled = autoItem.Checked;
            _engine.ApplySettings();
            UpdateTrayTooltip();
        };
        menu.Items.Add(autoItem);

        menu.Items.Add(new ToolStripSeparator());

        var diagItem = menu.Items.Add("View Diagnostics");
        diagItem.Click += (_, _) => ShowDiagnosticsWindow();

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = menu.Items.Add("Exit");
        exitItem.Click += (_, _) => ExitApp();

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
        UpdateTrayTooltip();
    }

    private void UpdateTrayTooltip()
    {
        if (_trayIcon == null || _engine == null) return;
        bool autoEnabled = _engine.Settings.Current.AutoModeEnabled;
        _trayIcon.Text = autoEnabled
            ? "Switcher — Auto mode ON"
            : "Switcher — Safe mode only";
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
        base.OnExit(e);
    }
}

