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

    // ─── Focus tracking ──────────────────────────────────────────────────────
    // Clear the buffer when the foreground APPLICATION changes (different PID).
    // We track by process ID, not HWND, because apps like Chrome have multiple
    // internal windows (autocomplete popups, tooltips) that cycle through
    // GetForegroundWindow() — clearing the buffer on every keystroke.
    private uint _lastForegroundPid;

    // ─── Modifier state tracking ─────────────────────────────────────────────
    // Tracked manually from hook events because GetKeyState()/GetAsyncKeyState()
    // can return stale values inside WH_KEYBOARD_LL callbacks.
    private bool _shiftHeld;
    private bool _ctrlHeld;
    private bool _altHeld;
    private bool _capsLock;

    // ─── Raw keystroke buffer ────────────────────────────────────────────────
    // Stores BOTH scan codes AND VK codes for each keystroke. Scan codes are
    // the primary source (hardware-level, layout-independent), but we also
    // keep VK codes as fallback because scan codes may be unreliable in
    // some apps (e.g., Chrome with certain keyboard configurations).
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

    /// <summary>Checks whether the VK is a delimiter key according to current settings.</summary>
    private bool IsDelimiterKey(uint vk)
    {
        if (_settings == null)
        {
            // Fallback for tests: only Space
            return vk == NativeMethods.VK_SPACE;
        }
        var s = _settings.Current;
        return (vk == NativeMethods.VK_SPACE && s.CorrectOnSpace)
            || (vk == NativeMethods.VK_RETURN && s.CorrectOnEnter)
            || (vk == NativeMethods.VK_TAB && s.CorrectOnTab);
    }

    private bool IsCancelOnBackspace => _settings?.Current.CancelOnBackspace ?? true;
    private bool IsCancelOnLeftArrow => _settings?.Current.CancelOnLeftArrow ?? true;

    private bool IsConfiguredSafeHotkeyChord(uint vk)
    {
        if (_settings == null)
            return false;

        return MatchesHotkey(_settings.Current.SafeLastWordHotkey, vk)
            || MatchesHotkey(_settings.Current.SafeSelectionHotkey, vk);
    }

    private bool MatchesHotkey(HotkeyDescriptor hotkey, uint vk)
    {
        if (hotkey.VirtualKey != vk)
            return false;

        bool requiresCtrl = (hotkey.Modifiers & 2) != 0;
        bool requiresAlt = (hotkey.Modifiers & 1) != 0;
        bool requiresShift = (hotkey.Modifiers & 4) != 0;

        return _ctrlHeld == requiresCtrl
            && _altHeld == requiresAlt
            && _shiftHeld == requiresShift;
    }

    /// <summary>EN interpretation of the last completed word.</summary>
    public string CurrentWordEN => _lastWordEN;
    /// <summary>UA interpretation of the last completed word.</summary>
    public string CurrentWordUA => _lastWordUA;
    /// <summary>Best-effort exact visible word from the active keyboard layout.</summary>
    public string CurrentVisibleWord => _lastVisibleWord;
    /// <summary>Backward-compatible: returns the EN interpretation.</summary>
    public string CurrentWord => _lastWordEN;
    /// <summary>Debug string of raw VK codes (hex) for diagnostics.</summary>
    public string LastVkDebug => _lastVkDebug;
    /// <summary>
    /// Number of keys in the last completed word where MapVirtualKeyEx
    /// changed the scan code (indicates unreliable raw scan codes, e.g. Chrome).
    /// </summary>
    public int LastScanRecoveryCount { get; private set; }
    /// <summary>EN interpretation using ONLY VK codes (ignoring scan codes). Used as fallback.</summary>
    public string CurrentWordEN_VkOnly { get; private set; } = "";
    /// <summary>UA interpretation based on VK-only EN. Used as fallback.</summary>
    public string CurrentWordUA_VkOnly { get; private set; } = "";
    /// <summary>
    /// True if the foreground thread's keyboard layout was Ukrainian
    /// at the moment the delimiter key was pressed. Used by AutoModeHandler
    /// to decide which conversion direction to try.
    /// </summary>
    public bool LastLayoutWasUkrainian { get; private set; }
    /// <summary>
    /// Number of non-modifier key-down events that were NOT added to the buffer
    /// in the last completed word (keys silently dropped by the acceptance filter).
    /// </summary>
    public int LastDroppedKeyCount { get; private set; }
    /// <summary>
    /// Number of adjacent scan code pairs in the last completed word that are
    /// sequential (differ by exactly 1). High values indicate Chrome-like
    /// garbage scan codes (sequential counters instead of real hardware codes).
    /// </summary>
    public int LastSequentialScanCount { get; private set; }

    /// <summary>Clears the saved completed word.</summary>
    public void ClearBuffer()
    {
        _lastWordEN = "";
        _lastWordUA = "";
        _lastVisibleWord = "";
        _lastVkDebug = "";
    }

    /// <summary>
    /// Returns the word currently adjacent to the caret as it is expected to appear on screen.
    /// Prefers the in-progress buffered word and falls back to the last completed word.
    /// </summary>
    public string GetVisibleWordNearCaret(ForegroundContext? context = null)
    {
        string buffered = GetVisibleBufferedWord(context);
        if (!string.IsNullOrEmpty(buffered))
            return buffered;

        return !string.IsNullOrEmpty(_lastVisibleWord)
            ? _lastVisibleWord
            : (LastLayoutWasUkrainian ? _lastWordUA : _lastWordEN);
    }

    private string GetVisibleBufferedWord(ForegroundContext? context)
    {
        if (_scanBuffer.Count == 0)
            return string.Empty;

        string en = PickBestEN(_scanBuffer);
        string ua = PickBestUA(_scanBuffer);
        bool useUkrainian = TryIsUkrainianLayout(context, out bool ukrainianLayout)
            ? ukrainianLayout
            : LastLayoutWasUkrainian;

        string preferred = useUkrainian ? ua : en;
        string fallback = useUkrainian ? en : ua;
        return !string.IsNullOrEmpty(preferred) ? preferred : fallback;
    }

    private static bool TryIsUkrainianLayout(ForegroundContext? context, out bool ukrainianLayout)
    {
        ukrainianLayout = false;

        IntPtr hwnd = context?.FocusedControlHwnd ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero)
            hwnd = context?.Hwnd ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero)
            return false;

        uint threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        if (threadId == 0)
            return false;

        IntPtr hkl = NativeMethods.GetKeyboardLayout(threadId);
        ukrainianLayout = ((long)hkl & 0xFFFF) == 0x0422;
        return true;
    }

    /// <summary>Clears the scan buffer and fires BufferCleared if there was content.</summary>
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

    // ─── Delimiter suppression ────────────────────────────────────────────────
    private bool _suppressDelimiter;
    private uint _lastDelimiterVk;

    public void SuppressCurrentDelimiter() => _suppressDelimiter = true;
    public uint LastDelimiterVk => _lastDelimiterVk;

    // ─── Undo suppression ─────────────────────────────────────────────────────
    private bool _suppressBackspace;

    /// <summary>
    /// Call from UndoRequested handler to swallow the Backspace keystroke
    /// that triggered the undo (so it doesn't reach the target app).
    /// </summary>
    public void SuppressCurrentBackspace() => _suppressBackspace = true;

    public event Action<int>? WordBoundaryDetected;
    public event Action? BufferReset;
    /// <summary>
    /// Fired when the buffer is cleared with a reason string (for diagnostics).
    /// Only fires if the buffer actually had content.
    /// </summary>
    public event Action<string>? BufferCleared;
    /// <summary>
    /// Fired on every key-down that is added to or dropped from the buffer.
    /// Format: "vk=XX scan=XX eff=XX buf=N dropped=N" (for diagnostics).
    /// </summary>
    public event Action<string>? KeyProcessed;
    /// <summary>
    /// Fired when Backspace is pressed immediately after a word boundary
    /// (no new characters typed yet). The int parameter is always 0.
    /// The handler can call SuppressCurrentBackspace() to swallow the key.
    /// </summary>
    public event Action? UndoRequested;

    public bool IsInstalled => _hookHandle != IntPtr.Zero;

    public void Install()
    {
        if (_hookHandle != IntPtr.Zero) return;
        _hookProc = HookCallback;
        IntPtr hMod = NativeMethods.GetModuleHandle(null);
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _hookProc, hMod, 0);

        // Initialize CapsLock state from current keyboard state
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

            // Track modifier state from ALL events (including injected) for accuracy
            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                if (vk == NativeMethods.VK_SHIFT || vk == 0xA0 || vk == 0xA1) // VK_LSHIFT, VK_RSHIFT
                    _shiftHeld = true;
                else if (vk == NativeMethods.VK_CONTROL || vk == 0xA2 || vk == 0xA3) // VK_LCONTROL, VK_RCONTROL
                    _ctrlHeld = true;
                else if (vk == NativeMethods.VK_MENU || vk == 0xA4 || vk == 0xA5) // VK_LMENU, VK_RMENU
                    _altHeld = true;
                else if (vk == 0x14) // VK_CAPITAL (CapsLock)
                    _capsLock = !_capsLock;
            }
            else if (msg == NativeMethods.WM_KEYUP || msg == 0x0105) // WM_SYSKEYUP
            {
                if (vk == NativeMethods.VK_SHIFT || vk == 0xA0 || vk == 0xA1)
                    _shiftHeld = false;
                else if (vk == NativeMethods.VK_CONTROL || vk == 0xA2 || vk == 0xA3)
                    _ctrlHeld = false;
                else if (vk == NativeMethods.VK_MENU || vk == 0xA4 || vk == 0xA5)
                    _altHeld = false;
            }

            // Buffer logic: only process non-injected key-down events
            if (!injected && (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN))
            {
                // ─── Focus change detection ──────────────────────────────────
                // If the foreground APPLICATION changed (different PID), clear buffer.
                // We compare PIDs, not HWNDs, because Chrome has internal popup
                // windows that cause GetForegroundWindow() to return different
                // handles within the same app — clearing the buffer on every key.
                IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
                uint fgThread = NativeMethods.GetWindowThreadProcessId(fgHwnd, out uint fgPid);
                if (fgPid != 0 && fgPid != _lastForegroundPid)
                {
                    if (_scanBuffer.Count > 0)
                    {
                        string lost = ScanBufferToEN(_scanBuffer);
                        BufferCleared?.Invoke($"App changed (pid {_lastForegroundPid}→{fgPid}), dropped '{lost}' ({_scanBuffer.Count} keys)");
                    }
                    _lastForegroundPid = fgPid;
                    _keysSinceDelimiter = 0;
                    _scanBuffer.Clear();
                    _scanRecoveryCount = 0;
                    _droppedKeySinceDelimiter = 0;
                    _lastWordEN = "";
                    _lastWordUA = "";
                    _lastVkDebug = "";
                }

                if (IsDelimiterKey(vk))
                {
                    int wordLen = _keysSinceDelimiter;
                    _keysSinceDelimiter = 0;
                    _lastWordEN = PickBestEN(_scanBuffer);
                    _lastWordUA = PickBestUA(_scanBuffer);
                    _lastVisibleWord = fgThread != 0
                        ? BufferToVisible(_scanBuffer, NativeMethods.GetKeyboardLayout(fgThread))
                        : string.Empty;
                    _lastVkDebug = ScanBufferToDebug(_scanBuffer);
                    LastScanRecoveryCount = _scanRecoveryCount;
                    LastDroppedKeyCount = _droppedKeySinceDelimiter;
                    LastSequentialScanCount = CountSequentialScans(_scanBuffer);
                    // VK-only fallback interpretations (useful when scan codes are unreliable)
                    string vkEN = VkBufferToEN(_scanBuffer);
                    CurrentWordEN_VkOnly = vkEN;
                    CurrentWordUA_VkOnly = EnToUA(vkEN);
                    _scanBuffer.Clear();
                    _scanRecoveryCount = 0;
                    _droppedKeySinceDelimiter = 0;
                    _lastDelimiterVk = vk;
                    _suppressDelimiter = false;

                    // Detect active keyboard layout for correction direction.
                    // With scan codes we always get the QWERTY interpretation, so we
                    // need to know which layout is active to decide whether the EN or
                    // UA interpretation is what appears on screen.
                    if (fgThread != 0)
                    {
                        IntPtr hkl = NativeMethods.GetKeyboardLayout(fgThread);
                        ushort langId = (ushort)((long)hkl & 0xFFFF);
                        LastLayoutWasUkrainian = langId == 0x0422;
                    }

                    if (wordLen > 0)
                        WordBoundaryDetected?.Invoke(wordLen);
                    if (_suppressDelimiter)
                    {
                        _suppressDelimiter = false;
                        return (IntPtr)1;
                    }
                }
                else if (vk == NativeMethods.VK_BACK)
                {
                    if (_keysSinceDelimiter == 0 && _scanBuffer.Count == 0)
                    {
                        // Backspace right after word boundary — potential undo
                        _suppressBackspace = false;
                        UndoRequested?.Invoke();
                        if (_suppressBackspace)
                        {
                            _suppressBackspace = false;
                            return (IntPtr)1; // swallow the Backspace
                        }
                    }
                    else if (IsCancelOnBackspace)
                    {
                        // CancelOnBackspace enabled: clear entire buffer (word won't be corrected)
                        ClearScanBuffer("Backspace cancel");
                        BufferReset?.Invoke();
                    }
                    else
                    {
                        // CancelOnBackspace disabled: just remove last char from buffer
                        if (_keysSinceDelimiter > 0) _keysSinceDelimiter--;
                        if (_scanBuffer.Count > 0) _scanBuffer.RemoveAt(_scanBuffer.Count - 1);
                        BufferReset?.Invoke();
                    }
                }
                else if (vk is NativeMethods.VK_DELETE or NativeMethods.VK_ESCAPE)
                {
                    ClearScanBuffer(vk == NativeMethods.VK_DELETE ? "Delete" : "Escape");
                    BufferReset?.Invoke();
                }
                else if ((vk is NativeMethods.VK_RETURN or NativeMethods.VK_TAB) && !IsDelimiterKey(vk))
                {
                    // Enter/Tab not configured as delimiters — just reset buffer
                    ClearScanBuffer(vk == NativeMethods.VK_RETURN ? "Enter" : "Tab");
                    BufferReset?.Invoke();
                }
                else if (vk == 0x25 && IsCancelOnLeftArrow) // VK_LEFT
                {
                    // Left arrow cancels current word
                    ClearScanBuffer("Left arrow cancel");
                    BufferReset?.Invoke();
                }
                else if (NavigationKeys.Contains(vk))
                {
                    // Cursor moved — buffer no longer matches text under caret
                    ClearScanBuffer($"Navigation key 0x{vk:X2}");
                    BufferReset?.Invoke();
                }
                else if (IsScanTypable(scan) || (vk >= 0x30 && vk <= 0x5A) || IsOemVk(vk))
                {
                    // Accept if scan code is typable OR if VK looks like a letter/digit
                    // OR if VK is an OEM code (localized layouts like UA ЙЦУКЕН may
                    // send OEM VK codes for letter keys, e.g. 0xBA instead of 0x48).

                    // Skip keys when Ctrl or Alt is held — those are shortcuts (Ctrl+C, Ctrl+A,
                    // Ctrl+L, Alt+D, etc.), not text input. Without this filter, pressing Ctrl+L
                    // to focus Chrome address bar would insert 'l' into the buffer.
                    if (_ctrlHeld || _altHeld)
                    {
                        if (IsConfiguredSafeHotkeyChord(vk))
                            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);

                        // Some Ctrl shortcuts change the text (Ctrl+A selects all, then next key
                        // replaces selection). Clear buffer since we can't track the text state.
                        ClearScanBuffer($"Modifier+key (vk=0x{vk:X2})");
                        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
                    }

                    // Recover the true scan code using MapVirtualKeyEx when the raw scan
                    // code looks suspicious. This fixes Chrome which sometimes sends
                    // sequential/garbage scan codes instead of real hardware codes.
                    uint effectiveScan = scan;
                    if (fgThread != 0)
                    {
                        IntPtr hkl = NativeMethods.GetKeyboardLayout(fgThread);
                        uint mappedScan = NativeMethods.MapVirtualKeyEx(vk, NativeMethods.MAPVK_VK_TO_VSC, hkl);
                        if (mappedScan != 0 && IsScanTypable(mappedScan))
                        {
                            // Prefer mapped scan if it produces a different (likely correct) result
                            // and the raw scan doesn't produce a letter where mapped does
                            if (mappedScan != scan)
                            {
                                effectiveScan = mappedScan;
                                _scanRecoveryCount++;
                            }
                        }
                    }

                    _keysSinceDelimiter++;
                    bool effectiveShift = _shiftHeld ^ (_capsLock && IsScanLetterKey(effectiveScan));
                    _scanBuffer.Add((effectiveScan, scan, vk, effectiveShift, flags));
                    KeyProcessed?.Invoke($"+vk={vk:X2} scan={scan:X2} eff={effectiveScan:X2} buf={_scanBuffer.Count} ch={ScanToEN(effectiveScan, effectiveShift)}/{VkToEN(vk, effectiveShift)}");
                }
                else if (!IsModifierOrLockVk(vk))
                {
                    // Non-modifier key not accepted into buffer — track for diagnostics.
                    // This catches keys with both non-typable scan codes AND VK codes
                    // outside our known ranges.
                    _droppedKeySinceDelimiter++;
                    KeyProcessed?.Invoke($"DROP vk={vk:X2} scan={scan:X2} flags={flags:X2} buf={_scanBuffer.Count} dropped={_droppedKeySinceDelimiter}");
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    // ─── Scan code classification ───────────────────────────────────────────
    // Scan codes are hardware-level: physical key position on US 101/104 keyboard.
    // They NEVER change regardless of the active keyboard layout.

    /// <summary>Returns true if the VK code is an OEM key that may represent a letter on localized layouts.</summary>
    private static bool IsOemVk(uint vk) =>
        (vk >= 0xBA && vk <= 0xC0) || (vk >= 0xDB && vk <= 0xDE);

    /// <summary>Returns true if the VK is a modifier or lock key (not a typable character).</summary>
    private static bool IsModifierOrLockVk(uint vk) =>
        vk is NativeMethods.VK_SHIFT or 0xA0 or 0xA1  // L/R Shift
            or NativeMethods.VK_CONTROL or 0xA2 or 0xA3  // L/R Ctrl
            or NativeMethods.VK_MENU or 0xA4 or 0xA5     // L/R Alt
            or 0x5B or 0x5C   // L/R Win
            or 0x14           // CapsLock
            or 0x90 or 0x91;  // NumLock, ScrollLock

    /// <summary>Returns true if the scan code corresponds to a typable character key.</summary>
    private static bool IsScanTypable(uint scan) =>
        (scan >= 0x02 && scan <= 0x0D) || // 1-0, -, =
        (scan >= 0x10 && scan <= 0x1B) || // Q-P, [, ]
        (scan >= 0x1E && scan <= 0x29) || // A-L, ;, ', `
        scan == 0x2B ||                    // backslash
        (scan >= 0x2C && scan <= 0x35);   // Z-M, comma, period, /

    /// <summary>Returns true for letter keys A-Z (affected by CapsLock).</summary>
    private static bool IsScanLetterKey(uint scan) =>
        (scan >= 0x10 && scan <= 0x19) || // Q W E R T Y U I O P
        (scan >= 0x1E && scan <= 0x26) || // A S D F G H J K L
        (scan >= 0x2C && scan <= 0x32);   // Z X C V B N M

    // ─── Scan code → EN QWERTY char mapping (static, hardware-level) ────────

    /// <summary>Maps scan code + shift to EN QWERTY character. Returns '\0' if unmappable.</summary>
    private static char ScanToEN(uint scan, bool shift)
    {
        // Letters (scan codes are constant physical positions)
        if (IsScanLetterKey(scan))
        {
            char lower = scan switch
            {
                0x10 => 'q', 0x11 => 'w', 0x12 => 'e', 0x13 => 'r', 0x14 => 't',
                0x15 => 'y', 0x16 => 'u', 0x17 => 'i', 0x18 => 'o', 0x19 => 'p',
                0x1E => 'a', 0x1F => 's', 0x20 => 'd', 0x21 => 'f', 0x22 => 'g',
                0x23 => 'h', 0x24 => 'j', 0x25 => 'k', 0x26 => 'l',
                0x2C => 'z', 0x2D => 'x', 0x2E => 'c', 0x2F => 'v', 0x30 => 'b',
                0x31 => 'n', 0x32 => 'm',
                _ => '\0'
            };
            return shift ? char.ToUpper(lower) : lower;
        }

        // Digits (top row)
        return (scan, shift) switch
        {
            (0x02, false) => '1', (0x02, true) => '!',
            (0x03, false) => '2', (0x03, true) => '@',
            (0x04, false) => '3', (0x04, true) => '#',
            (0x05, false) => '4', (0x05, true) => '$',
            (0x06, false) => '5', (0x06, true) => '%',
            (0x07, false) => '6', (0x07, true) => '^',
            (0x08, false) => '7', (0x08, true) => '&',
            (0x09, false) => '8', (0x09, true) => '*',
            (0x0A, false) => '9', (0x0A, true) => '(',
            (0x0B, false) => '0', (0x0B, true) => ')',
            (0x0C, false) => '-', (0x0C, true) => '_',
            (0x0D, false) => '=', (0x0D, true) => '+',
            // OEM keys
            (0x1A, false) => '[',  (0x1A, true) => '{',
            (0x1B, false) => ']',  (0x1B, true) => '}',
            (0x27, false) => ';',  (0x27, true) => ':',
            (0x28, false) => '\'', (0x28, true) => '"',
            (0x29, false) => '`',  (0x29, true) => '~',
            (0x2B, false) => '\\', (0x2B, true) => '|',
            (0x33, false) => ',',  (0x33, true) => '<',
            (0x34, false) => '.',  (0x34, true) => '>',
            (0x35, false) => '/',  (0x35, true) => '?',
            _ => '\0'
        };
    }

    // ─── VK code → EN letter mapping (fallback for unreliable scan codes) ────

    /// <summary>Maps VK code + shift to EN QWERTY letter. Returns '\0' for non-letter VKs.</summary>
    private static char VkToEN(uint vk, bool shift)
    {
        // VK_A(0x41) through VK_Z(0x5A) — these are stable across all layouts
        if (vk >= 0x41 && vk <= 0x5A)
        {
            char lower = (char)('a' + (vk - 0x41));
            return shift ? char.ToUpper(lower) : lower;
        }
        return '\0';
    }

    /// <summary>Converts buffer to EN QWERTY string using scan codes.</summary>
    private static string ScanBufferToEN(List<(uint scan, uint rawScan, uint vk, bool shift, uint flags)> buffer)
    {
        var sb = new StringBuilder(buffer.Count);
        foreach (var (scan, rawScan, vk, shift, _) in buffer)
        {
            char c = ScanToEN(scan, shift);
            if (c != '\0') sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Converts buffer to EN string using VK codes (letters only).</summary>
    private static string VkBufferToEN(List<(uint scan, uint rawScan, uint vk, bool shift, uint flags)> buffer)
    {
        var sb = new StringBuilder(buffer.Count);
        foreach (var (scan, rawScan, vk, shift, _) in buffer)
        {
            char c = VkToEN(vk, shift);
            if (c != '\0') sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Converts buffer to UA ЙЦУКЕН string from an EN source.</summary>
    private static string EnToUA(string en)
    {
        var enMap = KeyboardLayoutMap.GetEnToUaMap();
        var sb = new StringBuilder(en.Length);
        foreach (char c in en)
        {
            if (enMap.TryGetValue(c, out char uaChar))
                sb.Append(uaChar);
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Counts adjacent scan code pairs that differ by exactly 1 (sequential).
    /// Chrome sends garbage scan codes as incrementing counters (0x24,0x25,0x26...)
    /// which makes this a reliable Chrome-garbage detector.
    /// </summary>
    private static int CountSequentialScans(List<(uint scan, uint rawScan, uint vk, bool shift, uint flags)> buffer)
    {
        if (buffer.Count < 2) return 0;
        int count = 0;
        for (int i = 1; i < buffer.Count; i++)
        {
            // Use rawScan (original from hook) — effectiveScan may have been "recovered"
            uint prev = buffer[i - 1].rawScan;
            uint curr = buffer[i].rawScan;
            if (curr == prev + 1)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Picks the best EN interpretation: prefers scan-code-based, but falls back
    /// to VK-based if scan codes produced non-letter garbage for a word that should
    /// be all letters (VK codes are letters but scan codes aren't).
    /// </summary>
    private static string PickBestEN(List<(uint scan, uint rawScan, uint vk, bool shift, uint flags)> buffer)
    {
        string scanEN = ScanBufferToEN(buffer);
        string vkEN = VkBufferToEN(buffer);

        // If VK-based and scan-based agree, or VK produced nothing useful → use scan
        if (vkEN.Length == 0 || scanEN == vkEN)
            return scanEN;

        // If VK says it's all letters but scan produced characters that are NOT
        // keyboard-mapped chars (chars like [, ], \, ;, etc. map to UA letters
        // х, ї, ж, etc. and must NOT be dropped) → scan codes are wrong.
        // Only prefer VK if scan has genuine garbage (digits, symbols that don't
        // map to any UA letter).
        bool vkAllLetters = vkEN.Length > 0 && vkEN.All(char.IsLetter);
        bool scanHasGarbage = scanEN.Length > 0 && scanEN.Any(c =>
            !char.IsLetter(c)
            && !KeyboardLayoutMap.IsLayoutLetterChar(c)
            && !KeyboardLayoutMap.IsWordConnector(c));
        if (vkAllLetters && scanHasGarbage)
            return vkEN;

        // Default: prefer scan codes (they're layout-independent)
        return scanEN;
    }

    /// <summary>Picks the best UA interpretation based on the best EN.</summary>
    private static string PickBestUA(List<(uint scan, uint rawScan, uint vk, bool shift, uint flags)> buffer)
    {
        return EnToUA(PickBestEN(buffer));
    }

    /// <summary>Debug string showing both scan and VK codes for every key.</summary>
    private static string ScanBufferToDebug(List<(uint scan, uint rawScan, uint vk, bool shift, uint flags)> buffer)
    {
        var parts = new StringBuilder();
        foreach (var (scan, rawScan, vk, shift, flags) in buffer)
        {
            char sc = ScanToEN(scan, shift);
            char vc = VkToEN(vk, shift);
            if (parts.Length > 0) parts.Append(' ');
            if (rawScan != scan)
                parts.Append($"{(sc != '\0' ? sc : '?')}/{(vc != '\0' ? vc : '?')}(s{scan:X2}/rs{rawScan:X2}/v{vk:X2}/f{flags:X2})");
            else
                parts.Append($"{(sc != '\0' ? sc : '?')}/{(vc != '\0' ? vc : '?')}(s{scan:X2}/v{vk:X2}/f{flags:X2})");
        }
        return parts.ToString();
    }

    private static string BufferToVisible(List<(uint scan, uint rawScan, uint vk, bool shift, uint flags)> buffer, IntPtr hkl)
    {
        if (buffer.Count == 0 || hkl == IntPtr.Zero)
            return string.Empty;

        var output = new StringBuilder(buffer.Count);

        foreach (var (scan, rawScan, vk, shift, flags) in buffer)
        {
            var keyState = new byte[256];
            if (shift)
                keyState[NativeMethods.VK_SHIFT] = 0x80;

            uint unicodeScan = rawScan != 0 ? rawScan : scan;
            if ((flags & 0x01) != 0)
                unicodeScan |= 0xE000;

            var chars = new StringBuilder(8);
            int rc = NativeMethods.ToUnicodeEx(vk, unicodeScan, keyState, chars, chars.Capacity, 0, hkl);
            if (rc > 0)
            {
                output.Append(chars.ToString(0, rc));
                continue;
            }

            char fallback = ScanToEN(scan, shift);
            if (fallback != '\0')
                output.Append(fallback);
        }

        return output.ToString();
    }

    public void ResetBuffer()
    {
        _keysSinceDelimiter = 0;
        _scanBuffer.Clear();
        _scanRecoveryCount = 0;
        _droppedKeySinceDelimiter = 0;
        _lastWordEN = "";
        _lastWordUA = "";
        _lastVisibleWord = "";
        _lastVkDebug = "";
    }

    public void Dispose()
    {
        Uninstall();
        GC.SuppressFinalize(this);
    }
}
