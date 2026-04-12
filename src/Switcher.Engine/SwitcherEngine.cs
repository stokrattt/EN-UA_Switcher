using Switcher.Core;
using Switcher.Infrastructure;

namespace Switcher.Engine;

/// <summary>
/// Main engine lifecycle orchestrator.
/// Owns all engine components and wires them together.
/// Call Start() once to activate. Call Stop() to shut down cleanly.
/// </summary>
public class SwitcherEngine : IDisposable
{
    public SettingsManager Settings { get; }
    public DiagnosticsLogger Diagnostics { get; }

    private readonly ForegroundContextProvider _contextProvider;
    private readonly TextTargetCoordinator _coordinator;
    private readonly ExclusionManager _exclusions;
    private readonly SafeModeHandler _safeMode;
    private readonly AutoModeHandler _autoMode;
    private readonly KeyboardObserver _keyboardObserver;
    private GlobalHotkeyManager? _hotkeyManager;

    private bool _started;

    public SwitcherEngine()
    {
        Settings = new SettingsManager();
        Settings.Load();

        Diagnostics = new DiagnosticsLogger();
        Diagnostics.Configure(Settings.Current.DiagnosticsEnabled);

        _contextProvider = new ForegroundContextProvider();
        _keyboardObserver = new KeyboardObserver(Settings);
        _coordinator = new TextTargetCoordinator(_keyboardObserver);
        _exclusions = new ExclusionManager(Settings);

        _safeMode = new SafeModeHandler(_contextProvider, _coordinator, _exclusions, Diagnostics, Settings);
        _autoMode = new AutoModeHandler(_contextProvider, _exclusions, Diagnostics, Settings, _keyboardObserver);
    }

    /// <summary>Starts the engine: installs keyboard hook, registers hotkeys.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;

        // Wire keyboard observer → auto mode
        _keyboardObserver.WordBoundaryDetected += OnWordBoundary;
        _keyboardObserver.BufferReset += OnBufferReset;
        _keyboardObserver.UndoRequested += OnUndoRequested;
        _keyboardObserver.BufferCleared += OnBufferCleared;
        _keyboardObserver.KeyProcessed += OnKeyProcessed;
        _keyboardObserver.Install();

        // Wire hotkeys → safe mode
        _hotkeyManager = new GlobalHotkeyManager(
            Settings.Current.SafeLastWordHotkey,
            Settings.Current.SafeSelectionHotkey);
        _hotkeyManager.LastWordHotkeyPressed += OnLastWordHotkey;
        _hotkeyManager.SelectionHotkeyPressed += OnSelectionHotkey;
        _hotkeyManager.Start();
    }

    /// <summary>Stops the engine and releases all resources.</summary>
    public void Stop()
    {
        if (!_started) return;
        _started = false;

        _keyboardObserver.WordBoundaryDetected -= OnWordBoundary;
        _keyboardObserver.BufferReset -= OnBufferReset;
        _keyboardObserver.UndoRequested -= OnUndoRequested;
        _keyboardObserver.BufferCleared -= OnBufferCleared;
        _keyboardObserver.KeyProcessed -= OnKeyProcessed;
        _keyboardObserver.Uninstall();

        if (_hotkeyManager != null)
        {
            _hotkeyManager.LastWordHotkeyPressed -= OnLastWordHotkey;
            _hotkeyManager.SelectionHotkeyPressed -= OnSelectionHotkey;
            _hotkeyManager.Stop();
        }
        _hotkeyManager = null;
    }

    /// <summary>Saves current settings and re-registers hotkeys if needed.</summary>
    public void ApplySettings()
    {
        Settings.Save();
        Diagnostics.Configure(Settings.Current.DiagnosticsEnabled);
        _hotkeyManager?.UpdateHotkeys(
            Settings.Current.SafeLastWordHotkey,
            Settings.Current.SafeSelectionHotkey);
    }

    // ─── Event handlers ──────────────────────────────────────────────────────

    private void OnWordBoundary(int wordLen)
    {
        if (!Settings.Current.AutoModeEnabled) return;
        _autoMode.OnWordBoundary(wordLen);
    }

    private void OnBufferReset()
    {
        // Nothing specific needed — the observer already cleared its count
    }

    private void OnUndoRequested()
    {
        _autoMode.OnUndoRequested();
    }

    private void OnBufferCleared(string reason)
    {
        if (!Settings.Current.DiagnosticsEnabled) return;
        Diagnostics.Log("—", "—", "—", false, OperationType.AutoMode,
            "—", null, DiagnosticResult.Skipped, $"Buffer cleared: {reason}");
    }

    private void OnKeyProcessed(string info)
    {
        if (!Settings.Current.DiagnosticsEnabled) return;
        Diagnostics.Log("·", "·", "·", false, OperationType.AutoMode,
            "·", null, DiagnosticResult.Skipped, info);
    }

    private void OnLastWordHotkey()
    {
        _safeMode.FixLastWord();
    }

    private void OnSelectionHotkey()
    {
        _safeMode.FixSelection();
    }

    public void Dispose()
    {
        Stop();
        _keyboardObserver.Dispose();
        _hotkeyManager?.Dispose();
        GC.SuppressFinalize(this);
    }
}
