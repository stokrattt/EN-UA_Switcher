using System.Runtime.InteropServices;
using System.Text;
using Switcher.Core;
using Switcher.Infrastructure;

namespace Switcher.Engine;

/// <summary>
/// Monitors keyboard events using a low-level Windows hook (WH_KEYBOARD_LL).
/// Used ONLY for detecting word boundaries (space, enter, tab).
/// Does NOT suppress or rewrite keystrokes.
///
/// Maintains a buffer of raw VK codes + shift state for the current word.
/// At word boundary, converts the VK sequence to both EN QWERTY and UA ЙЦУКЕН
/// interpretations using static mappings (no GetKeyboardLayout API calls).
/// </summary>
public class KeyboardObserver : IDisposable
{
    private IntPtr _hookHandle = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _hookProc; // keep delegate alive
    private readonly SettingsManager? _settings;

    // Keys that reset the buffer because they move the cursor or change context,
    // invalidating our VK→text mapping.
    // Note: Left Arrow (0x25) is handled separately via CancelOnLeftArrow setting.
    private static readonly HashSet<uint> NavigationKeys = new()
    {
        0x26, 0x27, 0x28,               // Up, Right, Down arrows
        0x24, 0x23,                      // Home, End
        0x21, 0x22,                      // Page Up, Page Down
        0x2D,                            // Insert
    };

    private int _keysSinceDelimiter = 0;
    private int _postDelimiterInteractionCount = 0;

    // ─── Focus tracking ──────────────────────────────────────────────────────
    private uint _lastForegroundPid;

    // ─── Modifier state tracking ─────────────────────────────────────────────
    private bool _shiftHeld;
    private bool _ctrlHeld;
    private bool _altHeld;
    private bool _capsLock;

    // ─── Raw keystroke buffer ────────────────────────────────────────────────
    private readonly List<(uint scan, uint rawScan, uint vk, bool shift, uint flags)> _scanBuffer = new();
    private string _lastWordEN = "";
    private string _lastWordUA = "";
    private string _lastVisibleWord = "";
    private string _lastVkDebug = "";
    private int _scanRecoveryCount;
    private int _droppedKeySinceDelimiter;

    public KeyboardObserver(SettingsManager? settings = null)
    {
        _settings = settings;
    }

    private bool IsDelimiterKey(uint vk)
    {
        if (_settings == null) return vk == NativeMethods.VK_SPACE;
        var s = _settings.Current;
        return (vk == NativeMethods.VK_SPACE && s.CorrectOnSpace)
            || (vk == NativeMethods.VK_RETURN && s.CorrectOnEnter)
            || (vk == NativeMethods.VK_TAB && s.CorrectOnTab);
    }

    private bool IsCancelOnBackspace => _settings?.Current.CancelOnBackspace ?? true;
    private bool IsCancelOnLeftArrow => _settings?.Current.CancelOnLeftArrow ?? true;

    private bool IsConfiguredSafeHotkeyChord(uint vk)
    {
        if (_settings == null) return false;
        return MatchesHotkey(_settings.Current.SafeLastWordHotkey, vk)
            || MatchesHotkey(_settings.Current.SafeSelectionHotkey, vk);
    }

    private bool MatchesHotkey(HotkeyDescriptor hotkey, uint vk)
    {
        if (hotkey.VirtualKey != vk) return false;
        bool requiresCtrl = (hotkey.Modifiers & 2) != 0;
        bool requiresAlt = (hotkey.Modifiers & 1) != 0;
        bool requiresShift = (hotkey.Modifiers & 4) != 0;
        return _ctrlHeld == requiresCtrl && _altHeld == requiresAlt && _shiftHeld == requiresShift;
    }

    public string CurrentWordEN => _lastWordEN;
    public string CurrentWordUA => _lastWordUA;
    public string CurrentVisibleWord => _lastVisibleWord;
    public string CurrentWord => _lastWordEN;
    public string LastVkDebug => _lastVkDebug;
    public int LastScanRecoveryCount { get; private set; }
    public string CurrentWordEN_VkOnly { get; private set; } = "";
    public string CurrentWordUA_VkOnly { get; private set; } = "";
    public bool LastLayoutWasUkrainian { get; private set; }
    public int LastDroppedKeyCount { get; private set; }
    public int LastSequentialScanCount { get; private set; }

    /// <summary>
    /// Monotonically increasing counter of user key-down events (excluding modifiers).
    /// Used by async operations to detect if the user typed something while operation was in progress.
    /// </summary>
    public long UserKeyDownCounter => _userKeyDownCounter;
    private long _userKeyDownCounter;

    public void ClearBuffer()
    {
        _lastWordEN = "";
        _lastWordUA = "";
        _lastVisibleWord = "";
        _lastVkDebug = "";
    }

    public string GetVisibleWordNearCaret(ForegroundContext? context = null)
    {
        string buffered = GetVisibleBufferedWord(context);
        if (!string.IsNullOrEmpty(buffered)) return buffered;
        return !string.IsNullOrEmpty(_lastVisibleWord) ? _lastVisibleWord : (LastLayoutWasUkrainian ? _lastWordUA : _lastWordEN);
    }

    private string GetVisibleBufferedWord(ForegroundContext? context)
    {
        if (_scanBuffer.Count == 0) return string.Empty;
        string en = PickBestEN(_scanBuffer);
        string ua = PickBestUA(_scanBuffer);
        bool useUkrainian = TryIsUkrainianLayout(context, out bool ukrainianLayout) ? ukrainianLayout : LastLayoutWasUkrainian;
        string preferred = useUkrainian ? ua : en;
        string fallback = useUkrainian ? en : ua;
        return !string.IsNullOrEmpty(preferred) ? preferred : fallback;
    }

    private static bool TryIsUkrainianLayout(ForegroundContext? context, out bool ukrainianLayout)
    {
        ukrainianLayout = false;
        IntPtr hwnd = context?.FocusedControlHwnd ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) hwnd = context?.Hwnd ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) return false;
        uint threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        if (threadId == 0) return false;
        IntPtr hkl = NativeMethods.GetKeyboardLayout(threadId);
        ukrainianLayout = ((long)hkl & 0xFFFF) == 0x0422;
        return true;
    }

    private void ClearScanBuffer(string reason)
    {
        if (_scanBuffer.Count > 0)
        {
            string lost = PickBestEN(_scanBuffer);
            BufferCleared?.Invoke($"{reason}, dropped '{lost}' ({_scanBuffer.Count} keys)");
        }
        _keysSinceDelimiter = 0;
        _scanBuffer.Clear();
        _scanRecoveryCount = 0;
        _droppedKeySinceDelimiter = 0;
        _lastWordEN = "";
        _lastWordUA = "";
        _lastVisibleWord = "";
        _lastVkDebug = "";
    }

    private bool _suppressDelimiter;
    private uint _lastDelimiterVk;
    public void SuppressCurrentDelimiter() => _suppressDelimiter = true;
    public uint LastDelimiterVk => _lastDelimiterVk;
    public bool HasInteractionSinceLastDelimiter => Volatile.Read(ref _postDelimiterInteractionCount) > 0;

    private bool _suppressBackspace;
    public void SuppressCurrentBackspace() => _suppressBackspace = true;

    public event Action<int>? WordBoundaryDetected;
    public event Action? BufferReset;
    public event Action<string>? BufferCleared;
    public event Action<string>? KeyProcessed;
    public event Action? UndoRequested;

    public bool IsInstalled => _hookHandle != IntPtr.Zero;

    public void Install()
    {
        if (_hookHandle != IntPtr.Zero) return;
        _hookProc = HookCallback;
        IntPtr hMod = NativeMethods.GetModuleHandle(null);
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _hookProc, hMod, 0);
        _capsLock = (NativeMethods.GetKeyState(0x14 /* VK_CAPITAL */) & 1) != 0;
    }

    public void Uninstall()
    {
        if (_hookHandle == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            var kbStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            uint vk = kbStruct.vkCode;
            uint scan = kbStruct.scanCode;
            uint flags = kbStruct.flags;
            bool injected = (flags & (NativeMethods.LLKHF_INJECTED | NativeMethods.LLKHF_LOWER_IL_INJECTED)) != 0;

            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                if (!injected && !IsModifierOrLockVk(vk))
                {
                    Interlocked.Increment(ref _userKeyDownCounter);
                }

                if (vk == NativeMethods.VK_SHIFT || vk == 0xA0 || vk == 0xA1) _shiftHeld = true;
                else if (vk == NativeMethods.VK_CONTROL || vk == 0xA2 || vk == 0xA3) _ctrlHeld = true;
                else if (vk == NativeMethods.VK_MENU || vk == 0xA4 || vk == 0xA5) _altHeld = true;
                else if (vk == 0x14) _capsLock = !_capsLock;
            }
            else if (msg == NativeMethods.WM_KEYUP || msg == 0x0105)
            {
                if (vk == NativeMethods.VK_SHIFT || vk == 0xA0 || vk == 0xA1) _shiftHeld = false;
                else if (vk == NativeMethods.VK_CONTROL || vk == 0xA2 || vk == 0xA3) _ctrlHeld = false;
                else if (vk == NativeMethods.VK_MENU || vk == 0xA4 || vk == 0xA5) _altHeld = false;
            }

            if (!injected && (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN))
            {
                IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
                uint fgThread = NativeMethods.GetWindowThreadProcessId(fgHwnd, out uint fgPid);
                if (fgPid != 0 && fgPid != _lastForegroundPid)
                {
                    ClearScanBuffer($"App changed ({_lastForegroundPid}\u2192{fgPid})");
                    _lastForegroundPid = fgPid;
                }

                if (IsDelimiterKey(vk))
                {
                    int wordLen = _keysSinceDelimiter;
                    _keysSinceDelimiter = 0;
                    _lastWordEN = PickBestEN(_scanBuffer);
                    _lastWordUA = PickBestUA(_scanBuffer);
                    _lastVisibleWord = fgThread != 0 ? BufferToVisible(_scanBuffer, NativeMethods.GetKeyboardLayout(fgThread)) : string.Empty;
                    _lastVkDebug = ScanBufferToDebug(_scanBuffer);
                    LastScanRecoveryCount = _scanRecoveryCount;
                    LastDroppedKeyCount = _droppedKeySinceDelimiter;
                    LastSequentialScanCount = CountSequentialScans(_scanBuffer);
                    string vkEN = VkBufferToEN(_scanBuffer);
                    CurrentWordEN_VkOnly = vkEN;
                    CurrentWordUA_VkOnly = EnToUA(vkEN);
                    _scanBuffer.Clear();
                    _scanRecoveryCount = 0;
                    _droppedKeySinceDelimiter = 0;
                    _lastDelimiterVk = vk;
                    Volatile.Write(ref _postDelimiterInteractionCount, 0);
                    _suppressDelimiter = false;

                    if (fgThread != 0)
                    {
                        IntPtr hkl = NativeMethods.GetKeyboardLayout(fgThread);
                        ushort langId = (ushort)((long)hkl & 0xFFFF);
                        LastLayoutWasUkrainian = langId == 0x0422;
                    }

                    if (wordLen > 0) WordBoundaryDetected?.Invoke(wordLen);
                    if (_suppressDelimiter)
                    {
                        _suppressDelimiter = false;
                        return (IntPtr)1;
                    }
                }
                else if (vk == NativeMethods.VK_BACK)
                {
                    Interlocked.Increment(ref _postDelimiterInteractionCount);
                    if (_keysSinceDelimiter == 0 && _scanBuffer.Count == 0)
                    {
                        _suppressBackspace = false;
                        UndoRequested?.Invoke();
                        if (_suppressBackspace)
                        {
                            _suppressBackspace = false;
                            return (IntPtr)1;
                        }
                    }
                    else if (IsCancelOnBackspace)
                    {
                        ClearScanBuffer("Backspace cancel");
                        BufferReset?.Invoke();
                    }
                    else
                    {
                        if (_keysSinceDelimiter > 0) _keysSinceDelimiter--;
                        if (_scanBuffer.Count > 0) _scanBuffer.RemoveAt(_scanBuffer.Count - 1);
                        BufferReset?.Invoke();
                    }
                }
                else if (vk is NativeMethods.VK_DELETE or NativeMethods.VK_ESCAPE)
                {
                    Interlocked.Increment(ref _postDelimiterInteractionCount);
                    ClearScanBuffer(vk == NativeMethods.VK_DELETE ? "Delete" : "Escape");
                    BufferReset?.Invoke();
                }
                else if ((vk is NativeMethods.VK_RETURN or NativeMethods.VK_TAB) && !IsDelimiterKey(vk))
                {
                    Interlocked.Increment(ref _postDelimiterInteractionCount);
                    ClearScanBuffer(vk == NativeMethods.VK_RETURN ? "Enter" : "Tab");
                    BufferReset?.Invoke();
                }
                else if (vk == 0x25 && IsCancelOnLeftArrow)
                {
                    Interlocked.Increment(ref _postDelimiterInteractionCount);
                    ClearScanBuffer("Left arrow cancel");
                    BufferReset?.Invoke();
                }
                else if (NavigationKeys.Contains(vk))
                {
                    Interlocked.Increment(ref _postDelimiterInteractionCount);
                    ClearScanBuffer($"Navigation key 0x{vk:X2}");
                    BufferReset?.Invoke();
                }
                else if (IsScanTypable(scan) || (vk >= 0x30 && vk <= 0x5A) || IsOemVk(vk))
                {
                    if (_ctrlHeld || _altHeld)
                    {
                        if (IsConfiguredSafeHotkeyChord(vk)) return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
                        ClearScanBuffer($"Modifier+key (vk=0x{vk:X2})");
                        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
                    }

                    uint effectiveScan = scan;
                    if (fgThread != 0 && (!IsScanTypable(scan) || scan == 0))
                    {
                        // Only remap via MapVirtualKeyEx when the raw scan code from
                        // the hook is missing or invalid.  When the scan IS valid we
                        // trust it directly — remapping through the *current* layout
                        // handle corrupts the physical scan code when a non-QWERTY
                        // layout (e.g. Ukrainian) is active.
                        IntPtr hkl = NativeMethods.GetKeyboardLayout(fgThread);
                        uint mappedScan = NativeMethods.MapVirtualKeyEx(vk, NativeMethods.MAPVK_VK_TO_VSC, hkl);
                        if (mappedScan != 0 && IsScanTypable(mappedScan))
                        {
                            if (mappedScan != scan)
                            {
                                effectiveScan = mappedScan;
                                _scanRecoveryCount++;
                            }
                        }
                    }

                    _keysSinceDelimiter++;
                    Interlocked.Increment(ref _postDelimiterInteractionCount);
                    bool effectiveShift = _shiftHeld ^ (_capsLock && IsScanLetterKey(effectiveScan));
                    _scanBuffer.Add((effectiveScan, scan, vk, effectiveShift, flags));
                    KeyProcessed?.Invoke($"+vk={vk:X2} scan={scan:X2} eff={effectiveScan:X2} buf={_scanBuffer.Count} ch={ScanToEN(effectiveScan, effectiveShift)}/{VkToEN(vk, effectiveShift)}");
                }
                else if (!IsModifierOrLockVk(vk))
                {
                    Interlocked.Increment(ref _postDelimiterInteractionCount);
                    _droppedKeySinceDelimiter++;
                    KeyProcessed?.Invoke($"DROP vk={vk:X2} scan={scan:X2} flags={flags:X2} buf={_scanBuffer.Count} dropped={_droppedKeySinceDelimiter}");
                }
            }
        }
        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static bool IsOemVk(uint vk) => (vk >= 0xBA && vk <= 0xC0) || (vk >= 0xDB && vk <= 0xDE);
    private static bool IsModifierOrLockVk(uint vk) => vk is NativeMethods.VK_SHIFT or 0xA0 or 0xA1 or NativeMethods.VK_CONTROL or 0xA2 or 0xA3 or NativeMethods.VK_MENU or 0xA4 or 0xA5 or 0x5B or 0x5C or 0x14 or 0x90 or 0x91;
    private static bool IsScanTypable(uint scan) => (scan >= 0x02 && scan <= 0x0D) || (scan >= 0x10 && scan <= 0x1B) || (scan >= 0x1E && scan <= 0x29) || scan == 0x2B || (scan >= 0x2C && scan <= 0x35);
    private static bool IsScanLetterKey(uint scan) => (scan >= 0x10 && scan <= 0x19) || (scan >= 0x1E && scan <= 0x26) || (scan >= 0x2C && scan <= 0x32);

    private static char ScanToEN(uint scan, bool shift)
    {
        if (IsScanLetterKey(scan))
        {
            char lower = scan switch { 0x10 => 'q', 0x11 => 'w', 0x12 => 'e', 0x13 => 'r', 0x14 => 't', 0x15 => 'y', 0x16 => 'u', 0x17 => 'i', 0x18 => 'o', 0x19 => 'p', 0x1E => 'a', 0x1F => 's', 0x20 => 'd', 0x21 => 'f', 0x22 => 'g', 0x23 => 'h', 0x24 => 'j', 0x25 => 'k', 0x26 => 'l', 0x2C => 'z', 0x2D => 'x', 0x2E => 'c', 0x2F => 'v', 0x30 => 'b', 0x31 => 'n', 0x32 => 'm', _ => '\0' };
            return shift ? char.ToUpper(lower) : lower;
        }
        return (scan, shift) switch { (0x02, false) => '1', (0x02, true) => '!', (0x03, false) => '2', (0x03, true) => '@', (0x04, false) => '3', (0x04, true) => '#', (0x05, false) => '4', (0x05, true) => '$', (0x06, false) => '5', (0x06, true) => '%', (0x07, false) => '6', (0x07, true) => '^', (0x08, false) => '7', (0x08, true) => '&', (0x09, false) => '8', (0x09, true) => '*', (0x0A, false) => '9', (0x0A, true) => '(', (0x0B, false) => '0', (0x0B, true) => ')', (0x0C, false) => '-', (0x0C, true) => '_', (0x0D, false) => '=', (0x0D, true) => '+', (0x1A, false) => '[', (0x1A, true) => '{', (0x1B, false) => ']', (0x1B, true) => '}', (0x27, false) => ';', (0x27, true) => ':', (0x28, false) => '\'', (0x28, true) => '"', (0x29, false) => '`', (0x29, true) => '~', (0x2B, false) => '\\', (0x2B, true) => '|', (0x33, false) => ',', (0x33, true) => '<', (0x34, false) => '.', (0x34, true) => '>', (0x35, false) => '/', (0x35, true) => '?', _ => '\0' };
    }

    private static char VkToEN(uint vk, bool shift)
    {
        if (vk >= 0x41 && vk <= 0x5A)
        {
            char lower = (char)('a' + (vk - 0x41));
            return shift ? char.ToUpper(lower) : lower;
        }

        return (vk, shift) switch
        {
            (0x30, false) => '0', (0x30, true) => ')',
            (0x31, false) => '1', (0x31, true) => '!',
            (0x32, false) => '2', (0x32, true) => '@',
            (0x33, false) => '3', (0x33, true) => '#',
            (0x34, false) => '4', (0x34, true) => '$',
            (0x35, false) => '5', (0x35, true) => '%',
            (0x36, false) => '6', (0x36, true) => '^',
            (0x37, false) => '7', (0x37, true) => '&',
            (0x38, false) => '8', (0x38, true) => '*',
            (0x39, false) => '9', (0x39, true) => '(',
            (0xBA, false) => ';', (0xBA, true) => ':',
            (0xBB, false) => '=', (0xBB, true) => '+',
            (0xBC, false) => ',', (0xBC, true) => '<',
            (0xBD, false) => '-', (0xBD, true) => '_',
            (0xBE, false) => '.', (0xBE, true) => '>',
            (0xBF, false) => '/', (0xBF, true) => '?',
            (0xC0, false) => '`', (0xC0, true) => '~',
            (0xDB, false) => '[', (0xDB, true) => '{',
            (0xDC, false) => '\\', (0xDC, true) => '|',
            (0xDD, false) => ']', (0xDD, true) => '}',
            (0xDE, false) => '\'', (0xDE, true) => '"',
            _ => '\0'
        };
    }

    private static bool IsBufferedWordChar(char c) =>
        char.IsLetterOrDigit(c) || KeyboardLayoutMap.IsLayoutLetterChar(c) || KeyboardLayoutMap.IsWordConnector(c);

    private static string ScanBufferToEN(List<(uint scan, uint rawScan, uint vk, bool shift, uint flags)> buffer)
    {
        var sb = new StringBuilder(buffer.Count);
        foreach (var (scan, rawScan, vk, shift, _) in buffer) { char c = ScanToEN(scan, shift); if (c != '\0') sb.Append(c); }
        return sb.ToString();
    }

    private static string VkBufferToEN(List<(uint scan, uint rawScan, uint vk, bool shift, uint flags)> buffer)
    {
        var sb = new StringBuilder(buffer.Count);
        foreach (var (scan, rawScan, vk, shift, _) in buffer) { char c = VkToEN(vk, shift); if (c != '\0') sb.Append(c); }
        return sb.ToString();
    }

    private static string EnToUA(string en)
    {
        var enMap = KeyboardLayoutMap.GetEnToUaMap();
        var sb = new StringBuilder(en.Length);
        foreach (char c in en) { if (enMap.TryGetValue(c, out char uaChar)) sb.Append(uaChar); else sb.Append(c); }
        return sb.ToString();
    }

    private static int CountSequentialScans(List<(uint scan, uint rawScan, uint vk, bool shift, uint flags)> buffer)
    {
        if (buffer.Count < 2) return 0;
        int count = 0;
        for (int i = 1; i < buffer.Count; i++) { if (buffer[i].rawScan == buffer[i - 1].rawScan + 1) count++; }
        return count;
    }

    private static string PickBestEN(List<(uint scan, uint rawScan, uint vk, bool shift, uint flags)> buffer)
    {
        string scanEN = ScanBufferToEN(buffer);
        string vkEN = VkBufferToEN(buffer);
        if (vkEN.Length == 0 || scanEN == vkEN) return scanEN;
        bool vkLooksValid = vkEN.All(IsBufferedWordChar);
        bool scanLooksValid = scanEN.Length > 0 && scanEN.All(IsBufferedWordChar);
        if (vkLooksValid && CountSequentialScans(buffer) >= 3) return vkEN;
        if (vkLooksValid && (!scanLooksValid || scanEN.Length < vkEN.Length)) return vkEN;
        return scanEN;
    }

    private static string PickBestUA(List<(uint scan, uint rawScan, uint vk, bool shift, uint flags)> buffer) => EnToUA(PickBestEN(buffer));

    private static string ScanBufferToDebug(List<(uint scan, uint rawScan, uint vk, bool shift, uint flags)> buffer)
    {
        var parts = new StringBuilder();
        foreach (var (scan, rawScan, vk, shift, flags) in buffer) { char sc = ScanToEN(scan, shift); char vc = VkToEN(vk, shift); if (parts.Length > 0) parts.Append(' '); if (rawScan != scan) parts.Append($"{(sc != '\0' ? sc : '?')}/{(vc != '\0' ? vc : '?')}(s{scan:X2}/rs{rawScan:X2}/v{vk:X2}/f{flags:X2})"); else parts.Append($"{(sc != '\0' ? sc : '?')}/{(vc != '\0' ? vc : '?')}(s{scan:X2}/v{vk:X2}/f{flags:X2})"); }
        return parts.ToString();
    }

    private static string BufferToVisible(List<(uint scan, uint rawScan, uint vk, bool shift, uint flags)> buffer, IntPtr hkl)
    {
        if (buffer.Count == 0 || hkl == IntPtr.Zero) return string.Empty;
        var output = new StringBuilder(buffer.Count);
        foreach (var (scan, rawScan, vk, shift, flags) in buffer) { var keyState = new byte[256]; if (shift) keyState[NativeMethods.VK_SHIFT] = 0x80; uint unicodeScan = rawScan != 0 ? rawScan : scan; if ((flags & 0x01) != 0) unicodeScan |= 0xE000; var chars = new StringBuilder(8); int rc = NativeMethods.ToUnicodeEx(vk, unicodeScan, keyState, chars, chars.Capacity, 0, hkl); if (rc > 0) { output.Append(chars.ToString(0, rc)); continue; } char fallback = ScanToEN(scan, shift); if (fallback != '\0') output.Append(fallback); }
        return output.ToString();
    }

    public void ResetBuffer() { _keysSinceDelimiter = 0; _scanBuffer.Clear(); _scanRecoveryCount = 0; _droppedKeySinceDelimiter = 0; _lastWordEN = ""; _lastWordUA = ""; _lastVisibleWord = ""; _lastVkDebug = ""; }

    public void Dispose() { Uninstall(); GC.SuppressFinalize(this); }
}
