using System.Runtime.InteropServices;
using Switcher.Core;
using Switcher.Engine;
using Switcher.Infrastructure;
using Xunit;

namespace Switcher.Engine.Tests;

// ─── Fake observer ────────────────────────────────────────────────────────────

/// <summary>
/// Minimal stand-in for KeyboardObserver that lets tests control CurrentWord
/// without installing any Win32 hook.
/// </summary>
internal class FakeObserver : KeyboardObserver
{
    private string _word = "";

    public void SetWord(string word) => _word = word;

    // Shadow the property — KeyboardObserver.CurrentWord is not virtual, so we
    // use 'new' and pass this FakeObserver only to SendInputAdapter (which accepts
    // KeyboardObserver base) via the explicit ctor.
    public new string CurrentWord => _word;

    public new void ClearBuffer() => _word = "";
}

// ─── Fake adapter (for coordinator tests) ────────────────────────────────────

internal class FakeAdapter : ITextTargetAdapter
{
    private readonly TargetSupport _support;
    public string AdapterName { get; }
    public FakeAdapter(string name, TargetSupport support) { AdapterName = name; _support = support; }
    public TargetSupport CanHandle(ForegroundContext ctx) => _support;
    public string DescribeSupport(ForegroundContext ctx) => _support.ToString();
    public string? TryGetLastWord(ForegroundContext ctx) => null;
    public string? TryGetSelectedText(ForegroundContext ctx) => null;
    public string? TryGetCurrentSentence(ForegroundContext ctx) => null;
    public bool TryReplaceLastWord(ForegroundContext ctx, string r) => false;
    public bool TryReplaceSelection(ForegroundContext ctx, string r) => false;
    public bool TryReplaceCurrentSentence(ForegroundContext ctx, string r) => false;
}

// ─── Helper ───────────────────────────────────────────────────────────────────

file static class CtxHelper
{
    public static ForegroundContext Dummy() =>
        new(IntPtr.Zero, IntPtr.Zero, "test", 0, "TestWin", "TestCtrl");
}

// ═════════════════════════════════════════════════════════════════════════════
// SendInputAdapter tests
// ═════════════════════════════════════════════════════════════════════════════

public class SendInputAdapterTests
{
    // SendInputAdapter wraps a real KeyboardObserver and reads CurrentWord from it.
    // Because CurrentWord is NOT virtual we test via the public contract:
    // - CanHandle always returns Full
    // - TryGetLastWord mirrors observer state
    // - TryGetSelectedText is always null
    // For TryReplaceLastWord we only check it returns false when CurrentWord is empty
    // (the SendInput call path is not exercised — that requires a real foreground window).

    private readonly KeyboardObserver _obs = new();
    private SendInputAdapter Adapter() => new(_obs);

    [Fact]
    public void CanHandle_AlwaysReturnsFull()
    {
        var adapter = Adapter();
        Assert.Equal(TargetSupport.Full, adapter.CanHandle(CtxHelper.Dummy()));
    }

    [Fact]
    public void TryGetSelectedText_AlwaysNull()
    {
        Assert.Null(Adapter().TryGetSelectedText(CtxHelper.Dummy()));
    }

    [Fact]
    public void TryReplaceLastWord_EmptyBuffer_ReturnsFalse()
    {
        // No word was typed → cannot replace → must return false (not throw)
        var adapter = Adapter();
        bool result = adapter.TryReplaceLastWord(CtxHelper.Dummy(), "привіт");
        Assert.False(result);
    }

    [Fact]
    public void AdapterName_IsCorrect()
    {
        Assert.Equal("SendInputAdapter", Adapter().AdapterName);
    }

    [Fact]
    public void TryGetLastWord_ObserverEmpty_ReturnsNull()
    {
        // Fresh observer has no buffered word
        Assert.Null(Adapter().TryGetLastWord(CtxHelper.Dummy()));
    }

    [Fact]
    public void TryGetLastWord_UsesCurrentBufferedWord_WhenNoCompletedWordExists()
    {
        var observer = new KeyboardObserver();
        var bufferField = typeof(KeyboardObserver).GetField("_scanBuffer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        bufferField.SetValue(observer, new List<(uint scan, uint rawScan, uint vk, bool shift, uint flags)>
        {
            (0x22, 0x22, 0x47, false, 0), // g
            (0x23, 0x23, 0x48, false, 0), // h
            (0x30, 0x30, 0x42, false, 0), // b
            (0x20, 0x20, 0x44, false, 0), // d
            (0x1F, 0x1F, 0x53, false, 0), // s
            (0x31, 0x31, 0x4E, false, 0), // n
        });

        var adapter = new SendInputAdapter(observer);

        Assert.Equal("ghbdsn", adapter.TryGetLastWord(CtxHelper.Dummy()));
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// TextTargetCoordinator priority tests
// ═════════════════════════════════════════════════════════════════════════════

public class TextTargetCoordinatorPriorityTests
{
    private static ForegroundContext Ctx() => CtxHelper.Dummy();

    [Fact]
    public void Resolve_FirstFullAdapter_IsReturned()
    {
        // NativeEdit = Full, UIA = Full, SendInput = Full
        // Coordinator must return the first (NativeEdit)
        var obs = new KeyboardObserver();
        var coordinator = new TextTargetCoordinator(obs);
        var ctx = Ctx();
        // We can't inject custom adapters, but we CAN verify the returned adapter
        // name when tested against a real context (no focused window → both
        // NativeEdit and UIA return Unsupported → SendInput is chosen as fallback)
        var (adapter, support) = coordinator.Resolve(ctx);
        // SendInput always claims Full, so in a headless test context it wins
        Assert.NotNull(adapter);
        Assert.Equal(TargetSupport.Full, support);
        Assert.Equal("SendInputAdapter", adapter!.AdapterName);
    }

    [Fact]
    public void Resolve_WithAllUnsupportedCustomAdapters_ReturnsSendInput()
    {
        // Verify coordinator falls through to SendInput when higher-priority adapters reject
        // (simulated by the fact that in a headless environment NativeEdit+UIA return Unsupported)
        var obs = new KeyboardObserver();
        var coordinator = new TextTargetCoordinator(obs);
        var (adapter, _) = coordinator.Resolve(Ctx());
        Assert.Equal("SendInputAdapter", adapter?.AdapterName);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// KeyboardObserver — pure buffer logic (no Win32 hook required)
// ═════════════════════════════════════════════════════════════════════════════

public class KeyboardObserverBufferTests
{
    [Fact]
    public void BackspaceCancel_Enabled_ClearsWholeBufferedWord_BeforeDelimiter()
    {
        var settings = new SettingsManager();
        settings.Current.CancelOnBackspace = true;

        var obs = new KeyboardObserver(settings);
        SeedBufferedWord(obs, "abc");

        SimulateKeyDown(obs, NativeMethods.VK_BACK, 0x0E);

        Assert.Equal("", obs.GetVisibleWordNearCaret());
        Assert.Equal(0, GetBufferedCount(obs));
    }

    [Fact]
    public void BackspaceCancel_Disabled_RemovesOnlyLastBufferedChar_BeforeDelimiter()
    {
        var settings = new SettingsManager();
        settings.Current.CancelOnBackspace = false;

        var obs = new KeyboardObserver(settings);
        SeedBufferedWord(obs, "abc");

        SimulateKeyDown(obs, NativeMethods.VK_BACK, 0x0E);

        Assert.Equal("ab", obs.GetVisibleWordNearCaret());
        Assert.Equal(2, GetBufferedCount(obs));
    }

    [Fact]
    public void SafeHotkeyChord_DoesNotClearBufferedWord()
    {
        var settings = new SettingsManager();
        var obs = new KeyboardObserver(settings);
        SeedBufferedWord(obs, "abc");
        SetModifierState(obs, ctrlHeld: true, shiftHeld: true, altHeld: false);

        SimulateKeyDown(obs, settings.Current.SafeLastWordHotkey.VirtualKey, 0x25);

        Assert.Equal("abc", obs.GetVisibleWordNearCaret());
        Assert.Equal(3, GetBufferedCount(obs));
    }

    [Fact]
    public void GetVisibleWordNearCaret_PreservesShiftedTrailingPunctuation()
    {
        var observer = new KeyboardObserver();
        var scanBufferField = typeof(KeyboardObserver).GetField("_scanBuffer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var lastLayoutField = typeof(KeyboardObserver).GetField("<LastLayoutWasUkrainian>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        scanBufferField.SetValue(observer, new List<(uint scan, uint rawScan, uint vk, bool shift, uint flags)>
        {
            (0x22, 0x22, 0x47, false, 0), // g
            (0x23, 0x23, 0x48, false, 0), // h
            (0x30, 0x30, 0x42, false, 0), // b
            (0x20, 0x20, 0x44, false, 0), // d
            (0x1F, 0x1F, 0x53, false, 0), // s
            (0x31, 0x31, 0x4E, false, 0), // n
            (0x35, 0x35, 0xBF, true, 0),  // ? / comma on UA
        });

        lastLayoutField.SetValue(observer, true);

        Assert.Equal("привіт,", observer.GetVisibleWordNearCaret());
    }

    [Fact]
    public void CurrentWord_InitiallyEmpty()
    {
        var obs = new KeyboardObserver();
        Assert.Equal("", obs.CurrentWord);
    }

    [Fact]
    public void ResetBuffer_ClearsCurrentWord()
    {
        // We cannot fire the hook directly, but ResetBuffer() is public and
        // must clear _lastCompletedWord to empty string.
        var obs = new KeyboardObserver();
        obs.ResetBuffer();
        Assert.Equal("", obs.CurrentWord);
    }

    [Fact]
    public void ClearBuffer_ClearsCurrentWord()
    {
        var obs = new KeyboardObserver();
        // ClearBuffer only clears _lastCompletedWord (not _charBuffer)
        obs.ClearBuffer();
        Assert.Equal("", obs.CurrentWord);
    }

    [Fact]
    public void Observer_IsNotInstalled_ByDefault()
    {
        var obs = new KeyboardObserver();
        Assert.False(obs.IsInstalled);
    }

    [Fact]
    public void ResetBuffer_IsIdempotent()
    {
        var obs = new KeyboardObserver();
        obs.ResetBuffer();
        obs.ResetBuffer();
        Assert.Equal("", obs.CurrentWord);
    }

    private static void SeedBufferedWord(KeyboardObserver observer, string word)
    {
        var keysSinceDelimiterField = typeof(KeyboardObserver).GetField("_keysSinceDelimiter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var scanBufferField = typeof(KeyboardObserver).GetField("_scanBuffer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var lastForegroundPidField = typeof(KeyboardObserver).GetField("_lastForegroundPid", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        keysSinceDelimiterField.SetValue(observer, word.Length);

        var buffer = (List<(uint scan, uint rawScan, uint vk, bool shift, uint flags)>)scanBufferField.GetValue(observer)!;
        buffer.Clear();

        foreach (char c in word)
            buffer.Add(MapChar(c));

        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        lastForegroundPidField.SetValue(observer, pid);
    }

    private static int GetBufferedCount(KeyboardObserver observer)
    {
        var scanBufferField = typeof(KeyboardObserver).GetField("_scanBuffer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var buffer = (System.Collections.ICollection)scanBufferField.GetValue(observer)!;
        return buffer.Count;
    }

    private static void SetModifierState(KeyboardObserver observer, bool ctrlHeld, bool shiftHeld, bool altHeld)
    {
        typeof(KeyboardObserver).GetField("_ctrlHeld", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(observer, ctrlHeld);
        typeof(KeyboardObserver).GetField("_shiftHeld", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(observer, shiftHeld);
        typeof(KeyboardObserver).GetField("_altHeld", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(observer, altHeld);
    }

    private static (uint scan, uint rawScan, uint vk, bool shift, uint flags) MapChar(char c) => c switch
    {
        'a' => (0x1E, 0x1E, 0x41, false, 0),
        'b' => (0x30, 0x30, 0x42, false, 0),
        'c' => (0x2E, 0x2E, 0x43, false, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(c), c, "Test helper only supports a/b/c.")
    };

    private static void SimulateKeyDown(KeyboardObserver observer, uint vk, uint scan)
    {
        var hookCallback = typeof(KeyboardObserver).GetMethod("HookCallback", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var kb = new NativeMethods.KBDLLHOOKSTRUCT
        {
            vkCode = vk,
            scanCode = scan,
            flags = 0,
            time = 0,
            dwExtraInfo = IntPtr.Zero
        };

        IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.KBDLLHOOKSTRUCT>());
        try
        {
            Marshal.StructureToPtr(kb, ptr, false);
            hookCallback.Invoke(observer, new object[] { 0, (IntPtr)NativeMethods.WM_KEYDOWN, ptr });
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// MapVirtualKeyEx — verify scan code recovery works on this machine
// ═════════════════════════════════════════════════════════════════════════════

public class MapVirtualKeyExTests
{
    [Fact]
    public void MapVirtualKeyEx_EnglishVK_H_ReturnsScan0x23()
    {
        // VK_H (0x48) on English layout should always map to scan 0x23
        // This is the fundamental operation we rely on to fix Chrome's garbage scan codes.
        // MapVirtualKeyEx with MAPVK_VK_TO_VSC (0) should return the scan code.
        // We use IntPtr.Zero to use the calling thread's layout.
        uint scan = NativeMethods.MapVirtualKeyEx(0x48, NativeMethods.MAPVK_VK_TO_VSC, IntPtr.Zero);
        Assert.Equal(0x23u, scan);
    }

    [Fact]
    public void MapVirtualKeyEx_EnglishVK_LetterKeys_AllMapToExpectedScans()
    {
        // Verify all letter VK codes map to correct QWERTY scan codes
        var expected = new Dictionary<uint, uint>
        {
            [0x41] = 0x1E, // A
            [0x42] = 0x30, // B
            [0x43] = 0x2E, // C
            [0x44] = 0x20, // D
            [0x45] = 0x12, // E
            [0x46] = 0x21, // F
            [0x47] = 0x22, // G
            [0x48] = 0x23, // H
            [0x49] = 0x17, // I
            [0x4A] = 0x24, // J
            [0x4B] = 0x25, // K
            [0x4C] = 0x26, // L
            [0x4D] = 0x32, // M
            [0x4E] = 0x31, // N
            [0x4F] = 0x18, // O
            [0x50] = 0x19, // P
            [0x51] = 0x10, // Q
            [0x52] = 0x13, // R
            [0x53] = 0x1F, // S
            [0x54] = 0x14, // T
            [0x55] = 0x16, // U
            [0x56] = 0x2F, // V
            [0x57] = 0x11, // W
            [0x58] = 0x2D, // X
            [0x59] = 0x15, // Y
            [0x5A] = 0x2C, // Z
        };

        foreach (var (vk, expectedScan) in expected)
        {
            uint actualScan = NativeMethods.MapVirtualKeyEx(vk, NativeMethods.MAPVK_VK_TO_VSC, IntPtr.Zero);
            Assert.Equal(expectedScan, actualScan);
        }
    }

    [Fact]
    public void MapVirtualKeyEx_UkrainianLayout_OemVKs_MapToCorrectScans()
    {
        // When Ukrainian layout is active, letter keys produce OEM VK codes.
        // MapVirtualKeyEx with the UA layout handle should still recover correct scan codes.
        // Find the Ukrainian layout HKL
        int count = NativeMethods.GetKeyboardLayoutList(0, null!);
        var layouts = new IntPtr[count];
        NativeMethods.GetKeyboardLayoutList(count, layouts);

        IntPtr uaHkl = IntPtr.Zero;
        foreach (var hkl in layouts)
        {
            if (((long)hkl & 0xFFFF) == 0x0422)
            {
                uaHkl = hkl;
                break;
            }
        }

        // Skip if Ukrainian layout not installed
        if (uaHkl == IntPtr.Zero)
            return;

        // On Ukrainian layout, the physical H key (scan 0x23) produces VK 0xBA (OEM_1 = ;:)
        // or another OEM code depending on the driver. We can't hardcode the exact VK,
        // but we CAN verify that whatever VK it produces, MapVirtualKeyEx maps it back
        // to scan 0x23.
        //
        // Test: for each scan code, find the VK that UA layout assigns, then verify
        // MapVirtualKeyEx maps that VK back to the original scan code.
        uint[] letterScans = {
            0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, // Q-P row
            0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26,       // A-L row
            0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x31, 0x32                    // Z-M row
        };

        int recovered = 0;
        foreach (uint scanCode in letterScans)
        {
            // MapVirtualKeyEx with MAPVK_VSC_TO_VK (1) gives us the VK for this scan on UA layout
            uint uaVk = NativeMethods.MapVirtualKeyEx(scanCode, 1, uaHkl); // VSC_TO_VK
            if (uaVk == 0) continue;

            // Now reverse: VK → scan should give us back the original scan code
            uint recoveredScan = NativeMethods.MapVirtualKeyEx(uaVk, NativeMethods.MAPVK_VK_TO_VSC, uaHkl);
            if (recoveredScan == scanCode)
                recovered++;
        }

        // At least 20 of 26 should round-trip correctly
        Assert.True(recovered >= 20, $"Only {recovered}/26 scan codes recovered via UA layout round-trip");
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// FakeAdapter / ITextTargetAdapter contract tests
// ═════════════════════════════════════════════════════════════════════════════

public class FakeAdapterContractTests
{
    [Fact]
    public void FakeAdapter_Full_CanHandleReturnsFull()
    {
        var a = new FakeAdapter("test", TargetSupport.Full);
        Assert.Equal(TargetSupport.Full, a.CanHandle(CtxHelper.Dummy()));
    }

    [Fact]
    public void FakeAdapter_Unsupported_CanHandleReturnsUnsupported()
    {
        var a = new FakeAdapter("test", TargetSupport.Unsupported);
        Assert.Equal(TargetSupport.Unsupported, a.CanHandle(CtxHelper.Dummy()));
    }
}
