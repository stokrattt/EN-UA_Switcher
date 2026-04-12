using System.Runtime.InteropServices;
using System.Text;

namespace Switcher.Infrastructure;

/// <summary>All Win32 P/Invoke declarations.</summary>
public static class NativeMethods
{
    // ─── Window / Process ────────────────────────────────────────────────────
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern IntPtr GetFocus();
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();

    // ─── Edit control messages ───────────────────────────────────────────────
    public const int WM_GETTEXT = 0x000D;
    public const int WM_GETTEXTLENGTH = 0x000E;
    public const int EM_GETSEL = 0x00B0;
    public const int EM_SETSEL = 0x00B1;
    public const int EM_REPLACESEL = 0x00C2;
    public const int EM_EXGETSEL = 0x0434; // RichEdit extended

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, StringBuilder lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, out int wParam, out int lParam);

    // ─── Hotkeys ─────────────────────────────────────────────────────────────
    public const int WM_HOTKEY = 0x0312;
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ─── Keyboard hook ───────────────────────────────────────────────────────
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // Flags for KBDLLHOOKSTRUCT
    public const uint LLKHF_INJECTED = 0x10;
    public const uint LLKHF_LOWER_IL_INJECTED = 0x02;

    // ─── Virtual keys (common) ───────────────────────────────────────────────
    public const uint VK_SPACE = 0x20;
    public const uint VK_RETURN = 0x0D;
    public const uint VK_TAB = 0x09;
    public const uint VK_BACK = 0x08;
    public const uint VK_DELETE = 0x2E;
    public const uint VK_ESCAPE = 0x1B;
    public const uint VK_SHIFT = 0x10;
    public const uint VK_CONTROL = 0x11;
    public const uint VK_MENU = 0x12; // Alt
    public const uint VK_OEM_PERIOD = 0xBE;
    public const uint VK_OEM_COMMA = 0xBC;
    public const uint VK_OEM_1 = 0xBA; // ;:
    public const uint VK_OEM_2 = 0xBF; // /?
    public const uint VK_OEM_3 = 0xC0; // `~
    public const uint VK_OEM_4 = 0xDB; // [{
    public const uint VK_OEM_5 = 0xDC; // \|
    public const uint VK_OEM_6 = 0xDD; // ]}
    public const uint VK_OEM_7 = 0xDE; // '"

    // Modifier bit masks for RegisterHotKey
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    // ─── CHARRANGE for RichEdit ──────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public struct CHARRANGE
    {
        public int cpMin;
        public int cpMax;
    }

    // ─── Input language switching ────────────────────────────────────────────
    public const int WM_INPUTLANGCHANGEREQUEST = 0x0050;

    // Standard HKL values: high word = low word = LANGID
    public static readonly IntPtr HKL_EN_US = new IntPtr(0x04090409); // English (US)
    public static readonly IntPtr HKL_UK_UA = new IntPtr(0x04220422); // Ukrainian

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetKeyboardLayoutList(int nBuff, IntPtr[] lpList);

    /// <summary>
    /// Switches the input language of the target window's thread to EN or UA.
    /// Works with any number of installed system layouts.
    ///
    /// Strategy:
    ///   1. PostMessage WM_INPUTLANGCHANGEREQUEST to the window (native Win32 apps).
    ///   2. Find the real HKL from GetKeyboardLayoutList.
    ///   3. Verify + simulate the system's configured toggle hotkey (Alt+Shift or Ctrl+Shift)
    ///      read from the registry, with verification after each toggle.
    /// </summary>
    public static void SwitchInputLanguage(IntPtr hwnd, IntPtr focusedHwnd, bool toUkrainian)
    {
        uint targetThread = GetWindowThreadProcessId(hwnd, out _);
        if (targetThread == 0) return;

        ushort targetLang = toUkrainian ? (ushort)0x0422 : (ushort)0x0409;

        // Check if already on the correct layout
        IntPtr currentHkl = GetKeyboardLayout(targetThread);
        if (((long)currentHkl & 0xFFFF) == targetLang) return;

        // Find the real HKL for the target language from installed layouts
        IntPtr realHkl = FindInstalledHkl(targetLang);
        IntPtr hklToPost = realHkl != IntPtr.Zero ? realHkl : (toUkrainian ? HKL_UK_UA : HKL_EN_US);

        // Strategy 1: PostMessage (works for native Win32 windows)
        PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hklToPost);
        if (focusedHwnd != IntPtr.Zero && focusedHwnd != hwnd)
            PostMessage(focusedHwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hklToPost);

        // Strategy 2: Async verify + toggle (for Electron, Chrome, etc.)
        Task.Run(async () =>
        {
            await Task.Delay(80);

            // Check if PostMessage worked
            if (((long)GetKeyboardLayout(targetThread) & 0xFFFF) == targetLang) return;

            // Read which hotkey the system uses: Alt+Shift (1), Ctrl+Shift (2), or none (3)
            bool useCtrlShift = IsToggleHotkeyCtrlShift();

            // Toggle up to 5 times, verifying after each
            for (int attempt = 0; attempt < 5; attempt++)
            {
                SimulateLayoutToggle(useCtrlShift);
                await Task.Delay(80);

                if (((long)GetKeyboardLayout(targetThread) & 0xFFFF) == targetLang) return;
            }
        });
    }

    /// <summary>Overload for backward compatibility (single HWND).</summary>
    public static void SwitchInputLanguage(IntPtr hwnd, bool toUkrainian)
        => SwitchInputLanguage(hwnd, IntPtr.Zero, toUkrainian);

    /// <summary>Finds the actual installed HKL matching a language ID.</summary>
    private static IntPtr FindInstalledHkl(ushort langId)
    {
        int count = GetKeyboardLayoutList(0, null!);
        if (count <= 0) return IntPtr.Zero;
        var list = new IntPtr[count];
        GetKeyboardLayoutList(count, list);
        foreach (var hkl in list)
            if (((long)hkl & 0xFFFF) == langId) return hkl;
        return IntPtr.Zero;
    }

    /// <summary>
    /// Reads the system toggle hotkey from registry.
    /// HKCU\Keyboard Layout\Toggle\Language Hotkey:
    ///   "1" = Left Alt + Shift (default), "2" = Ctrl + Shift, "3" = none
    /// </summary>
    private static bool IsToggleHotkeyCtrlShift()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"Keyboard Layout\Toggle");
            var val = key?.GetValue("Language Hotkey") as string;
            return val == "2"; // Ctrl+Shift
        }
        catch { return false; } // Default to Alt+Shift
    }

    /// <summary>Simulates one press of the layout toggle hotkey.</summary>
    private static void SimulateLayoutToggle(bool ctrlShift)
    {
        var toggle = new INPUT[4];
        if (ctrlShift)
        {
            toggle[0] = MakeKeyInput(VK_CONTROL, keyUp: false);
            toggle[1] = MakeKeyInput(VK_SHIFT, keyUp: false);
            toggle[2] = MakeKeyInput(VK_SHIFT, keyUp: true);
            toggle[3] = MakeKeyInput(VK_CONTROL, keyUp: true);
        }
        else
        {
            toggle[0] = MakeKeyInput(VK_MENU, keyUp: false);
            toggle[1] = MakeKeyInput(VK_SHIFT, keyUp: false);
            toggle[2] = MakeKeyInput(VK_SHIFT, keyUp: true);
            toggle[3] = MakeKeyInput(VK_MENU, keyUp: true);
        }
        SendInput(4, toggle, Marshal.SizeOf<INPUT>());
    }

    // ─── SendInput (universal keyboard injection) ────────────────────────────

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    /// <summary>
    /// INPUT struct for SendInput. The union MUST include MOUSEINPUT (the largest member)
    /// so that Marshal.SizeOf&lt;INPUT&gt;() matches the native sizeof(INPUT).
    /// On x64: MOUSEINPUT = 32 bytes → union = 32 → INPUT = 4 + 4(pad) + 32 = 40 bytes.
    /// Without MOUSEINPUT the union is only 24 bytes (KEYBDINPUT), giving 32 bytes total,
    /// and SendInput silently fails (returns 0) because cbSize != sizeof(INPUT).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint Type;         // INPUT_KEYBOARD = 1
        public INPUT_UNION U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUT_UNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public static INPUT MakeKeyInput(uint vk, bool keyUp) => new INPUT
    {
        Type = INPUT_KEYBOARD,
        U = new INPUT_UNION
        {
            ki = new KEYBDINPUT
            {
                wVk = (ushort)vk,
                dwFlags = keyUp ? KEYEVENTF_KEYUP : 0u,
            }
        }
    };

    /// <summary>
    /// Makes a key input with KEYEVENTF_EXTENDEDKEY set.
    /// Required for arrow keys (Left/Right/Up/Down), Home, End, Insert, Delete,
    /// Page Up/Down — otherwise some apps (Chrome) ignore them.
    /// </summary>
    public static INPUT MakeExtKeyInput(uint vk, bool keyUp) => new INPUT
    {
        Type = INPUT_KEYBOARD,
        U = new INPUT_UNION
        {
            ki = new KEYBDINPUT
            {
                wVk = (ushort)vk,
                dwFlags = KEYEVENTF_EXTENDEDKEY | (keyUp ? KEYEVENTF_KEYUP : 0u),
            }
        }
    };

    public static INPUT MakeUnicodeInput(char c, bool keyUp) => new INPUT
    {
        Type = INPUT_KEYBOARD,
        U = new INPUT_UNION
        {
            ki = new KEYBDINPUT
            {
                wScan = c,
                dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0u),
            }
        }
    };

    // ─── Keyboard layout helpers (for char buffering in observer) ────────────

    [DllImport("user32.dll")] public static extern bool GetKeyboardState(byte[] lpKeyState);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint idThread);
    [DllImport("user32.dll")] public static extern short GetKeyState(uint nVirtKey);
    [DllImport("user32.dll")] public static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

    /// <summary>MAPVK_VK_TO_VSC: Virtual-key code → scan code.</summary>
    public const uint MAPVK_VK_TO_VSC = 0;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode,
        byte[] lpKeyState, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff,
        int cchBuff, uint wFlags, IntPtr dwhkl);

    // ─── Clipboard API ──────────────────────────────────────────────────────

    public const uint VK_LEFT = 0x25;
    public const uint VK_RIGHT = 0x27;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    public static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    public static extern UIntPtr GlobalSize(IntPtr hMem);

    public const uint CF_UNICODETEXT = 13;
    public const uint GMEM_MOVEABLE = 0x0002;

    /// <summary>Read current clipboard text. Returns null if clipboard is empty or not text.</summary>
    public static string? GetClipboardText()
    {
        if (!OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            IntPtr hData = GetClipboardData(CF_UNICODETEXT);
            if (hData == IntPtr.Zero) return null;
            IntPtr pData = GlobalLock(hData);
            if (pData == IntPtr.Zero) return null;
            try
            {
                return Marshal.PtrToStringUni(pData);
            }
            finally
            {
                GlobalUnlock(hData);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Set clipboard text. Returns true on success.</summary>
    public static bool SetClipboardText(string? text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return false;
        try
        {
            EmptyClipboard();
            if (string.IsNullOrEmpty(text)) return true;
            int byteCount = (text.Length + 1) * 2; // UTF-16 + null terminator
            IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)byteCount);
            if (hGlobal == IntPtr.Zero) return false;
            IntPtr pGlobal = GlobalLock(hGlobal);
            if (pGlobal == IntPtr.Zero) return false;
            try
            {
                Marshal.Copy(text.ToCharArray(), 0, pGlobal, text.Length);
                Marshal.WriteInt16(pGlobal, text.Length * 2, 0); // null terminator
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }
            SetClipboardData(CF_UNICODETEXT, hGlobal);
            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }
}
