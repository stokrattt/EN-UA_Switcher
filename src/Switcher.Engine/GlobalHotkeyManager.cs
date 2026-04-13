using System.Windows.Forms;
using Switcher.Core;
using Switcher.Infrastructure;

namespace Switcher.Engine;

/// <summary>
/// Registers and manages global hotkeys via RegisterHotKey/UnregisterHotKey.
/// Requires a message pump (runs in a background thread with its own message loop).
/// </summary>
public class GlobalHotkeyManager : IDisposable
{
    private const int HotkeyIdLastWord = 1;
    private const int HotkeyIdSelection = 2;
    private const uint WM_APP_RELOAD_HOTKEYS = 0x8000 + 1;
    private const uint WM_APP_EXIT_HOTKEYS = 0x8000 + 2;
    private readonly IHotkeyPlatform _platform;

    private Thread? _messageThread;
    private uint _messageThreadId;
    private volatile bool _running;
    private readonly ManualResetEventSlim _startupReady = new(false);

    private HotkeyDescriptor _lastWordHotkey;
    private HotkeyDescriptor _selectionHotkey;

    public event Action? LastWordHotkeyPressed;
    public event Action? SelectionHotkeyPressed;
    public bool IsRegistered { get; private set; }
    public string? LastRegistrationError { get; private set; }

    public GlobalHotkeyManager(HotkeyDescriptor lastWordHotkey, HotkeyDescriptor selectionHotkey)
        : this(lastWordHotkey, selectionHotkey, new Win32HotkeyPlatform())
    {
    }

    internal GlobalHotkeyManager(
        HotkeyDescriptor lastWordHotkey,
        HotkeyDescriptor selectionHotkey,
        IHotkeyPlatform platform)
    {
        _lastWordHotkey = lastWordHotkey;
        _selectionHotkey = selectionHotkey;
        _platform = platform;
    }

    public void Start()
    {
        if (_messageThread != null) return;
        _running = true;
        IsRegistered = false;
        LastRegistrationError = null;
        _startupReady.Reset();
        _messageThread = new Thread(MessageLoop) { IsBackground = true, Name = "HotkeyMsgLoop" };
        _messageThread.SetApartmentState(ApartmentState.STA);
        _messageThread.Start();
        _startupReady.Wait(TimeSpan.FromSeconds(2));
    }

    public void Stop()
    {
        _running = false;
        if (_messageThreadId != 0)
        {
            _platform.PostThreadMessage(_messageThreadId, unchecked((int)WM_APP_EXIT_HOTKEYS), IntPtr.Zero, IntPtr.Zero);
        }
        _messageThread?.Join(2000);
        _messageThread = null;
        _messageThreadId = 0;
        IsRegistered = false;
    }

    public void UpdateHotkeys(HotkeyDescriptor lastWord, HotkeyDescriptor selection)
    {
        _lastWordHotkey = lastWord;
        _selectionHotkey = selection;
        if (_messageThreadId != 0)
        {
            _platform.PostThreadMessage(_messageThreadId, unchecked((int)WM_APP_RELOAD_HOTKEYS), IntPtr.Zero, IntPtr.Zero);
        }
    }

    private void MessageLoop()
    {
        _messageThreadId = NativeMethods.GetCurrentThreadId();
        EnsureMessageQueue();

        RegisterAll();
        _startupReady.Set();

        while (_running)
        {
            int getMessageResult = GetMessage(out var msg);
            if (getMessageResult <= 0)
                break;

            if (msg.message == NativeMethods.WM_HOTKEY)
            {
                int id = (int)(msg.wParam.ToInt64());
                if (id == HotkeyIdLastWord) LastWordHotkeyPressed?.Invoke();
                else if (id == HotkeyIdSelection) SelectionHotkeyPressed?.Invoke();
            }
            else if (ProcessControlMessage(msg.message))
            {
                if (msg.message == WM_APP_EXIT_HOTKEYS)
                    break;
                continue;
            }
            TranslateAndDispatch(ref msg);
        }
    }

    private void RegisterAll()
    {
        IsRegistered = false;
        LastRegistrationError = null;

        bool lastWordRegistered = _platform.RegisterHotKey(IntPtr.Zero, HotkeyIdLastWord,
            _lastWordHotkey.Modifiers | NativeMethods.MOD_NOREPEAT, _lastWordHotkey.VirtualKey);
        int lastWordError = lastWordRegistered ? 0 : _platform.GetLastWin32Error();

        bool selectionRegistered = _platform.RegisterHotKey(IntPtr.Zero, HotkeyIdSelection,
            _selectionHotkey.Modifiers | NativeMethods.MOD_NOREPEAT, _selectionHotkey.VirtualKey);
        int selectionError = selectionRegistered ? 0 : _platform.GetLastWin32Error();

        if (lastWordRegistered && selectionRegistered)
        {
            IsRegistered = true;
            return;
        }

        if (lastWordRegistered)
            _platform.UnregisterHotKey(IntPtr.Zero, HotkeyIdLastWord);
        if (selectionRegistered)
            _platform.UnregisterHotKey(IntPtr.Zero, HotkeyIdSelection);

        string lastWordStatus = lastWordRegistered
            ? $"{_lastWordHotkey.FriendlyName}=OK"
            : $"{_lastWordHotkey.FriendlyName}=error {lastWordError}";
        string selectionStatus = selectionRegistered
            ? $"{_selectionHotkey.FriendlyName}=OK"
            : $"{_selectionHotkey.FriendlyName}=error {selectionError}";
        LastRegistrationError = $"Global hotkeys unavailable: {lastWordStatus}; {selectionStatus}. " +
            "Another app or another Switcher instance may already be using them.";
    }

    private void UnregisterAll()
    {
        _platform.UnregisterHotKey(IntPtr.Zero, HotkeyIdLastWord);
        _platform.UnregisterHotKey(IntPtr.Zero, HotkeyIdSelection);
        IsRegistered = false;
    }

    private bool ProcessControlMessage(uint message)
    {
        if (message == WM_APP_RELOAD_HOTKEYS)
        {
            UnregisterAll();
            RegisterAll();
            return true;
        }

        if (message == WM_APP_EXIT_HOTKEYS)
        {
            UnregisterAll();
            return true;
        }

        return false;
    }

    internal void RegisterAllForTesting(IntPtr hwnd)
    {
        if (_messageThreadId == 0)
            _messageThreadId = 1;
        RegisterAll();
    }

    internal void ProcessControlMessageForTesting(uint message)
    {
        ProcessControlMessage(message);
    }

    internal static uint ReloadMessageForTesting => WM_APP_RELOAD_HOTKEYS;
    internal static uint ExitMessageForTesting => WM_APP_EXIT_HOTKEYS;

    // ─── Win32 message loop plumbing ─────────────────────────────────────────
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, IntPtr hWnd = default, uint wMsgFilterMin = 0, uint wMsgFilterMax = 0);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint removeMsg);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG msg);

    private static void TranslateAndDispatch(ref MSG msg)
    {
        TranslateMessage(ref msg);
        DispatchMessage(ref msg);
    }

    private const uint PM_NOREMOVE = 0x0000;

    private static void EnsureMessageQueue()
    {
        PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public System.Drawing.Point pt;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}

internal interface IHotkeyPlatform
{
    bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);
    bool UnregisterHotKey(IntPtr hwnd, int id);
    bool PostThreadMessage(uint threadId, int msg, IntPtr wParam, IntPtr lParam);
    int GetLastWin32Error();
}

internal sealed class Win32HotkeyPlatform : IHotkeyPlatform
{
    public bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey) =>
        NativeMethods.RegisterHotKey(hwnd, id, modifiers, virtualKey);

    public bool UnregisterHotKey(IntPtr hwnd, int id) =>
        NativeMethods.UnregisterHotKey(hwnd, id);

    public bool PostThreadMessage(uint threadId, int msg, IntPtr wParam, IntPtr lParam) =>
        NativeMethods.PostThreadMessage(threadId, msg, wParam, lParam);

    public int GetLastWin32Error() =>
        System.Runtime.InteropServices.Marshal.GetLastWin32Error();
}
