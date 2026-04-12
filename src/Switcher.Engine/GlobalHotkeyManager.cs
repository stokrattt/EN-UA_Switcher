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

    private Thread? _messageThread;
    private IntPtr _hwnd = IntPtr.Zero;
    private volatile bool _running;

    private HotkeyDescriptor _lastWordHotkey;
    private HotkeyDescriptor _selectionHotkey;

    public event Action? LastWordHotkeyPressed;
    public event Action? SelectionHotkeyPressed;

    public GlobalHotkeyManager(HotkeyDescriptor lastWordHotkey, HotkeyDescriptor selectionHotkey)
    {
        _lastWordHotkey = lastWordHotkey;
        _selectionHotkey = selectionHotkey;
    }

    public void Start()
    {
        if (_messageThread != null) return;
        _running = true;
        _messageThread = new Thread(MessageLoop) { IsBackground = true, Name = "HotkeyMsgLoop" };
        _messageThread.Start();
    }

    public void Stop()
    {
        _running = false;
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdLastWord);
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdSelection);
            // Post WM_QUIT to unblock message loop
            PostQuitMessage();
        }
        _messageThread?.Join(2000);
        _messageThread = null;
    }

    public void UpdateHotkeys(HotkeyDescriptor lastWord, HotkeyDescriptor selection)
    {
        _lastWordHotkey = lastWord;
        _selectionHotkey = selection;
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdLastWord);
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdSelection);
            RegisterAll();
        }
    }

    private void MessageLoop()
    {
        // Create a message-only window for receiving WM_HOTKEY
        _hwnd = CreateMessageWindow();
        if (_hwnd == IntPtr.Zero) return;

        RegisterAll();

        while (_running && GetMessage(out var msg))
        {
            if (msg.message == NativeMethods.WM_HOTKEY)
            {
                int id = (int)(msg.wParam.ToInt64());
                if (id == HotkeyIdLastWord) LastWordHotkeyPressed?.Invoke();
                else if (id == HotkeyIdSelection) SelectionHotkeyPressed?.Invoke();
            }
            TranslateAndDispatch(ref msg);
        }

        DestroyMessageWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }

    private void RegisterAll()
    {
        if (_hwnd == IntPtr.Zero) return;
        NativeMethods.RegisterHotKey(_hwnd, HotkeyIdLastWord,
            _lastWordHotkey.Modifiers | NativeMethods.MOD_NOREPEAT, _lastWordHotkey.VirtualKey);
        NativeMethods.RegisterHotKey(_hwnd, HotkeyIdSelection,
            _selectionHotkey.Modifiers | NativeMethods.MOD_NOREPEAT, _selectionHotkey.VirtualKey);
    }

    // ─── Win32 message loop plumbing ─────────────────────────────────────────
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG msg, IntPtr hWnd = default, uint wMsgFilterMin = 0, uint wMsgFilterMax = 0);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode = 0);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG msg);

    private static void TranslateAndDispatch(ref MSG msg)
    {
        TranslateMessage(ref msg);
        DispatchMessage(ref msg);
    }

    // Message-only window
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private static IntPtr CreateMessageWindow()
    {
        return CreateWindowEx(0, "STATIC", "SwitcherHotkey",
            0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
    }

    private static void DestroyMessageWindow(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero) DestroyWindow(hwnd);
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
