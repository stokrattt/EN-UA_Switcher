using System.Reflection;
using Switcher.Core;
using Switcher.Engine;
using Switcher.Infrastructure;
using Xunit;

namespace Switcher.Engine.Tests;

public class TextTargetCoordinatorRegressionTests
{
    private static ForegroundContext DummyContext() =>
        new(IntPtr.Zero, IntPtr.Zero, "chrome", 123, "Chrome_WidgetWin_1", "Chrome_RenderWidgetHostHWND");

    [Fact]
    public void Resolve_ReadOnlyAdapterBeforeFullAdapter_PrefersFull()
    {
        var coordinator = new TextTargetCoordinator(new ITextTargetAdapter[]
        {
            new StubAdapter("ReadOnlyUIA", TargetSupport.ReadOnly),
            new StubAdapter("SendInput", TargetSupport.Full),
        });

        var (adapter, support) = coordinator.Resolve(DummyContext());

        Assert.NotNull(adapter);
        Assert.Equal(TargetSupport.Full, support);
        Assert.Equal("SendInput", adapter!.AdapterName);
    }

    [Fact]
    public void Resolve_OnlyReadOnlyAvailable_ReturnsReadOnly()
    {
        var coordinator = new TextTargetCoordinator(new ITextTargetAdapter[]
        {
            new StubAdapter("ReadOnlyUIA", TargetSupport.ReadOnly),
            new StubAdapter("Unsupported", TargetSupport.Unsupported),
        });

        var (adapter, support) = coordinator.Resolve(DummyContext());

        Assert.NotNull(adapter);
        Assert.Equal(TargetSupport.ReadOnly, support);
        Assert.Equal("ReadOnlyUIA", adapter!.AdapterName);
    }

    [Fact]
    public void ResolveCandidates_OrdersFullBeforeReadOnly()
    {
        var coordinator = new TextTargetCoordinator(new ITextTargetAdapter[]
        {
            new StubAdapter("ReadOnlyUIA", TargetSupport.ReadOnly),
            new StubAdapter("SendInput", TargetSupport.Full),
            new StubAdapter("NativeEdit", TargetSupport.Full),
        });

        var candidates = coordinator.ResolveCandidates(DummyContext());

        Assert.Equal(3, candidates.Count);
        Assert.Equal("SendInput", candidates[0].Adapter.AdapterName);
        Assert.Equal(TargetSupport.Full, candidates[0].Support);
        Assert.Equal("NativeEdit", candidates[1].Adapter.AdapterName);
        Assert.Equal(TargetSupport.Full, candidates[1].Support);
        Assert.Equal("ReadOnlyUIA", candidates[2].Adapter.AdapterName);
        Assert.Equal(TargetSupport.ReadOnly, candidates[2].Support);
    }

    private sealed class StubAdapter(string name, TargetSupport support) : ITextTargetAdapter
    {
        public string AdapterName => name;
        public TargetSupport CanHandle(ForegroundContext context) => support;
        public string DescribeSupport(ForegroundContext context) => support.ToString();
        public string? TryGetLastWord(ForegroundContext context) => null;
        public string? TryGetSelectedText(ForegroundContext context) => null;
        public string? TryGetCurrentSentence(ForegroundContext context) => null;
        public bool TryReplaceLastWord(ForegroundContext context, string replacement) => false;
        public bool TryReplaceSelection(ForegroundContext context, string replacement) => false;
        public bool TryReplaceCurrentSentence(ForegroundContext context, string replacement) => false;
    }
}

public class ExclusionManagerRegressionTests
{
    [Fact]
    public void IsWordExcluded_MatchesStoredUkrainianWordInEitherLayout()
    {
        var settings = new SettingsManager();
        settings.Current.ExcludedWords.Add("привіт");
        var exclusions = new ExclusionManager(settings);

        Assert.True(exclusions.IsWordExcluded("привіт"));
        Assert.True(exclusions.IsWordExcluded("ghbdsn"));
    }

    [Fact]
    public void IsWordExcluded_MatchesStoredMistypedWordInEitherLayout()
    {
        var settings = new SettingsManager();
        settings.Current.ExcludedWords.Add("ghbdsn");
        var exclusions = new ExclusionManager(settings);

        Assert.True(exclusions.IsWordExcluded("привіт"));
        Assert.True(exclusions.IsWordExcluded("ghbdsn"));
    }
}

public class UiAutomationTargetAdapterRegressionTests
{
    [Fact]
    public void TryBuildReplacementValue_ReplacesExactCachedSlice()
    {
        bool ok = InvokeTryBuildReplacementValue(
            currentValue: "привіт руддщ",
            cachedValue: "привіт руддщ",
            original: "руддщ",
            cachedStart: 7,
            cachedEnd: 12,
            replacement: "hello",
            out string newValue,
            out int targetCaretIndex);

        Assert.True(ok);
        Assert.Equal("привіт hello", newValue);
        Assert.Equal("привіт hello".Length, targetCaretIndex);
    }

    [Fact]
    public void TryBuildReplacementValue_RelocatesToLatestExactMatch_WhenValueChanged()
    {
        bool ok = InvokeTryBuildReplacementValue(
            currentValue: "старе руддщ нове руддщ",
            cachedValue: "старе руддщ",
            original: "руддщ",
            cachedStart: 6,
            cachedEnd: 11,
            replacement: "hello",
            out string newValue,
            out int targetCaretIndex);

        Assert.True(ok);
        Assert.Equal("старе руддщ нове hello", newValue);
        Assert.Equal("старе руддщ нове hello".Length, targetCaretIndex);
    }

    [Fact]
    public void TryBuildReplacementValue_Fails_WhenExactWordCannotBeRelocated()
    {
        bool ok = InvokeTryBuildReplacementValue(
            currentValue: "старе hit нове",
            cachedValue: "старе руддщ",
            original: "руддщ",
            cachedStart: 6,
            cachedEnd: 11,
            replacement: "hello",
            out _,
            out _);

        Assert.False(ok);
    }

    [Theory]
    [InlineData("hello ", 6, 0, 5)]
    [InlineData("hello world", 11, 6, 11)]
    [InlineData("hello world ", 12, 6, 11)]
    [InlineData(" one", 4, 1, 4)]
    public void FindLastWordBounds_FindsExpectedRange(string text, int caretPos, int expectedStart, int expectedEnd)
    {
        var (start, end) = InvokeFindLastWordBounds(text, caretPos);

        Assert.Equal(expectedStart, start);
        Assert.Equal(expectedEnd, end);
        Assert.Equal(text[expectedStart..expectedEnd], text[start..end]);
    }

    [Theory]
    [InlineData(12, 12, 13)]
    [InlineData(12, 9, 19)]
    public void BuildCaretRestoreInputs_UsesCtrlEndAndExpectedLeftMoves(int textLength, int targetCaretIndex, int expectedInputCount)
    {
        var inputs = InvokeBuildCaretRestoreInputs(textLength, targetCaretIndex);

        Assert.Equal(expectedInputCount, inputs.Length);
        Assert.Equal(NativeMethods.VK_SHIFT, inputs[0].U.ki.wVk);
        Assert.Equal(NativeMethods.KEYEVENTF_KEYUP, inputs[0].U.ki.dwFlags);
        Assert.Equal(NativeMethods.VK_CONTROL, inputs[9].U.ki.wVk);
        Assert.Equal(NativeMethods.VK_END, inputs[10].U.ki.wVk);
    }

    [Fact]
    public void BuildModifierReleaseInputs_ReleasesCommonModifiers()
    {
        var inputs = NativeMethods.BuildModifierReleaseInputs();

        Assert.Equal(9, inputs.Length);
        Assert.All(inputs, input => Assert.Equal(NativeMethods.KEYEVENTF_KEYUP, input.U.ki.dwFlags));
        Assert.Equal(new ushort[]
        {
            (ushort)NativeMethods.VK_SHIFT,
            (ushort)NativeMethods.VK_CONTROL,
            (ushort)NativeMethods.VK_MENU,
            (ushort)NativeMethods.VK_LSHIFT,
            (ushort)NativeMethods.VK_RSHIFT,
            (ushort)NativeMethods.VK_LCONTROL,
            (ushort)NativeMethods.VK_RCONTROL,
            (ushort)NativeMethods.VK_LMENU,
            (ushort)NativeMethods.VK_RMENU,
        }, inputs.Select(i => i.U.ki.wVk).ToArray());
    }

    private static (int Start, int End) InvokeFindLastWordBounds(string text, int caretPos)
    {
        MethodInfo method = typeof(UIAutomationTargetAdapter)
            .GetMethod("FindLastWordBounds", BindingFlags.NonPublic | BindingFlags.Static)!;

        object result = method.Invoke(null, new object[] { text, caretPos })!;
        return ((int Start, int End))result;
    }

    private static bool InvokeTryBuildReplacementValue(
        string currentValue,
        string cachedValue,
        string original,
        int cachedStart,
        int cachedEnd,
        string replacement,
        out string newValue,
        out int targetCaretIndex)
    {
        MethodInfo method = typeof(UIAutomationTargetAdapter)
            .GetMethod("TryBuildReplacementValue", BindingFlags.NonPublic | BindingFlags.Static)!;

        object?[] args =
        {
            currentValue,
            cachedValue,
            original,
            cachedStart,
            cachedEnd,
            replacement,
            null,
            null
        };

        bool ok = (bool)method.Invoke(null, args)!;
        newValue = (string?)args[6] ?? string.Empty;
        targetCaretIndex = (int?)args[7] ?? -1;
        return ok;
    }

    private static NativeMethods.INPUT[] InvokeBuildCaretRestoreInputs(int textLength, int targetCaretIndex)
    {
        MethodInfo method = typeof(UIAutomationTargetAdapter)
            .GetMethod("BuildCaretRestoreInputs", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (NativeMethods.INPUT[])method.Invoke(null, new object[] { textLength, targetCaretIndex })!;
    }
}

public class NativeEditTargetAdapterRegressionTests
{
    [Fact]
    public void CanHandle_WindowsFormsEditClass_ReturnsFull()
    {
        var adapter = new NativeEditTargetAdapter();
        var context = new ForegroundContext(
            new IntPtr(1),
            new IntPtr(2),
            "Switcher.TestTarget",
            123,
            "WindowsForms10.Window.8.app.0.378734a_r3_ad1",
            "WindowsForms10.EDIT.app.0.378734a_r3_ad1");

        Assert.Equal(TargetSupport.Full, adapter.CanHandle(context));
    }

    [Fact]
    public void TryGetCurrentSentence_WindowsFormsEditWithoutHandle_ReturnsNull()
    {
        var adapter = new NativeEditTargetAdapter();
        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "Switcher.TestTarget",
            123,
            "WindowsForms10.Window.8.app.0.378734a_r3_ad1",
            "WindowsForms10.EDIT.app.0.378734a_r3_ad1");

        Assert.Null(adapter.TryGetCurrentSentence(context));
    }
}

public class GlobalHotkeyManagerRegressionTests
{
    [Fact]
    public void RegisterAllForTesting_WhenBothHotkeysRegister_SetsRegisteredState()
    {
        var platform = new FakeHotkeyPlatform();
        var manager = new GlobalHotkeyManager(
            new HotkeyDescriptor(6, 0x4B),
            new HotkeyDescriptor(6, 0x4C),
            platform);

        manager.RegisterAllForTesting(new IntPtr(101));

        Assert.True(manager.IsRegistered);
        Assert.Null(manager.LastRegistrationError);
        Assert.Collection(platform.RegisterCalls,
            call =>
            {
                Assert.Equal(1, call.Id);
                Assert.Equal(6u | NativeMethods.MOD_NOREPEAT, call.Modifiers);
                Assert.Equal(0x4Bu, call.VirtualKey);
            },
            call =>
            {
                Assert.Equal(2, call.Id);
                Assert.Equal(6u | NativeMethods.MOD_NOREPEAT, call.Modifiers);
                Assert.Equal(0x4Cu, call.VirtualKey);
            });
    }

    [Fact]
    public void RegisterAllForTesting_WhenSecondRegistrationFails_UnregistersPartialSuccessAndStoresError()
    {
        var platform = new FakeHotkeyPlatform
        {
            RegisterResults = new Queue<(bool Success, int Error)>(new[]
            {
                (true, 0),
                (false, 1409),
            })
        };
        var manager = new GlobalHotkeyManager(
            new HotkeyDescriptor(6, 0x4B),
            new HotkeyDescriptor(6, 0x4C),
            platform);

        manager.RegisterAllForTesting(new IntPtr(202));

        Assert.False(manager.IsRegistered);
        Assert.Contains("Ctrl+Shift+K=OK", manager.LastRegistrationError);
        Assert.Contains("Ctrl+Shift+L=error 1409", manager.LastRegistrationError);
        Assert.Single(platform.UnregisterCalls);
        Assert.Equal(1, platform.UnregisterCalls[0].Id);
    }

    [Fact]
    public void UpdateHotkeys_ThenReload_ReRegistersWithNewDescriptors()
    {
        var platform = new FakeHotkeyPlatform();
        var manager = new GlobalHotkeyManager(
            new HotkeyDescriptor(6, 0x4B),
            new HotkeyDescriptor(6, 0x4C),
            platform);

        manager.RegisterAllForTesting(new IntPtr(303));
        manager.UpdateHotkeys(new HotkeyDescriptor(2, 0x48), new HotkeyDescriptor(2, 0x4A));

        Assert.Single(platform.PostThreadMessages);
        Assert.Equal(unchecked((int)GlobalHotkeyManager.ReloadMessageForTesting), platform.PostThreadMessages[0].Message);

        platform.ClearRecordedCalls();
        manager.ProcessControlMessageForTesting(GlobalHotkeyManager.ReloadMessageForTesting);

        Assert.Equal(2, platform.UnregisterCalls.Count);
        Assert.Collection(platform.RegisterCalls,
            call =>
            {
                Assert.Equal(1, call.Id);
                Assert.Equal(2u | NativeMethods.MOD_NOREPEAT, call.Modifiers);
                Assert.Equal(0x48u, call.VirtualKey);
            },
            call =>
            {
                Assert.Equal(2, call.Id);
                Assert.Equal(2u | NativeMethods.MOD_NOREPEAT, call.Modifiers);
                Assert.Equal(0x4Au, call.VirtualKey);
            });
    }

    [Fact]
    public void ProcessControlMessageForTesting_Exit_UnregistersBothHotkeys()
    {
        var platform = new FakeHotkeyPlatform();
        var manager = new GlobalHotkeyManager(
            new HotkeyDescriptor(6, 0x4B),
            new HotkeyDescriptor(6, 0x4C),
            platform);

        manager.RegisterAllForTesting(new IntPtr(404));
        platform.ClearRecordedCalls();

        manager.ProcessControlMessageForTesting(GlobalHotkeyManager.ExitMessageForTesting);

        Assert.False(manager.IsRegistered);
        Assert.Equal(2, platform.UnregisterCalls.Count);
        Assert.Equal(new[] { 1, 2 }, platform.UnregisterCalls.Select(c => c.Id).ToArray());
    }

    private sealed class FakeHotkeyPlatform : IHotkeyPlatform
    {
        public Queue<(bool Success, int Error)> RegisterResults { get; set; } = new();
        public List<RegisterCall> RegisterCalls { get; } = [];
        public List<UnregisterCall> UnregisterCalls { get; } = [];
        public List<PostThreadMessageCall> PostThreadMessages { get; } = [];
        private int _lastError;

        public bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey)
        {
            RegisterCalls.Add(new RegisterCall(hwnd, id, modifiers, virtualKey));

            if (RegisterResults.Count == 0)
            {
                _lastError = 0;
                return true;
            }

            var result = RegisterResults.Dequeue();
            _lastError = result.Error;
            return result.Success;
        }

        public bool UnregisterHotKey(IntPtr hwnd, int id)
        {
            UnregisterCalls.Add(new UnregisterCall(hwnd, id));
            return true;
        }

        public bool PostThreadMessage(uint threadId, int msg, IntPtr wParam, IntPtr lParam)
        {
            PostThreadMessages.Add(new PostThreadMessageCall(threadId, msg, wParam, lParam));
            return true;
        }

        public int GetLastWin32Error() => _lastError;

        public void ClearRecordedCalls()
        {
            RegisterCalls.Clear();
            UnregisterCalls.Clear();
            PostThreadMessages.Clear();
        }
    }

    private readonly record struct RegisterCall(IntPtr Hwnd, int Id, uint Modifiers, uint VirtualKey);
    private readonly record struct UnregisterCall(IntPtr Hwnd, int Id);
    private readonly record struct PostThreadMessageCall(uint ThreadId, int Message, IntPtr WParam, IntPtr LParam);
}

public class AutoModeHandlerPunctuationRegressionTests
{
    [Theory]
    [InlineData(",")]
    [InlineData(".")]
    [InlineData(";")]
    [InlineData("!")]
    [InlineData("?")]
    public void ApplyVisibleTrailingPunctuation_AppendsLiteralTrailingPunctuation_WhenCandidateDroppedIt(string suffix)
    {
        var candidate = new CorrectionCandidate(
            "руддщ",
            "hello",
            CorrectionDirection.UaToEn,
            0.95,
            "test");

        CorrectionCandidate normalized = InvokeApplyVisibleTrailingPunctuation(candidate, suffix);

        Assert.Equal("руддщ" + suffix, normalized.OriginalText);
        Assert.Equal("hello" + suffix, normalized.ConvertedText);
    }

    [Fact]
    public void ApplyVisibleTrailingPunctuation_ReplacesMappedCommaLetter_WithLiteralComma()
    {
        var candidate = new CorrectionCandidate(
            "ghbdsn,",
            "привітб",
            CorrectionDirection.EnToUa,
            0.95,
            "test");

        CorrectionCandidate normalized = InvokeApplyVisibleTrailingPunctuation(candidate, ",");

        Assert.Equal("ghbdsn,", normalized.OriginalText);
        Assert.Equal("привіт,", normalized.ConvertedText);
    }

    private static CorrectionCandidate InvokeApplyVisibleTrailingPunctuation(CorrectionCandidate candidate, string visibleSuffix)
    {
        var method = typeof(AutoModeHandler)
            .GetMethod("ApplyVisibleTrailingPunctuation", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (CorrectionCandidate)method.Invoke(null, [candidate, visibleSuffix])!;
    }

    [Fact]
    public void BuildAutoReplacementInputs_ReleasesModifiersBeforeReplacingWord()
    {
        NativeMethods.INPUT[] inputs = InvokeBuildAutoReplacementInputs("ghbdsn", "привіт", string.Empty);
        NativeMethods.INPUT[] release = NativeMethods.BuildModifierReleaseInputs();

        Assert.True(inputs.Length > release.Length);
        for (int i = 0; i < release.Length; i++)
            Assert.Equal(release[i].U.ki.wVk, inputs[i].U.ki.wVk);
        Assert.Equal(NativeMethods.VK_BACK, inputs[release.Length].U.ki.wVk);
    }

    [Fact]
    public void BuildAutoReplacementInputs_WithTrailingPunctuation_MovesBeforeSuffixAndReturnsAfterTyping()
    {
        NativeMethods.INPUT[] inputs = InvokeBuildAutoReplacementInputs("ghbdsn", "привіт", "?");
        int releaseCount = NativeMethods.BuildModifierReleaseInputs().Length;

        Assert.Equal(NativeMethods.VK_LEFT, inputs[releaseCount].U.ki.wVk);
        Assert.Equal(NativeMethods.KEYEVENTF_EXTENDEDKEY, inputs[releaseCount].U.ki.dwFlags);
        Assert.Equal(NativeMethods.VK_LEFT, inputs[releaseCount + 1].U.ki.wVk);
        Assert.Equal(NativeMethods.KEYEVENTF_EXTENDEDKEY | NativeMethods.KEYEVENTF_KEYUP, inputs[releaseCount + 1].U.ki.dwFlags);

        NativeMethods.INPUT[] suffixInputs = inputs[^2..];
        Assert.Equal(NativeMethods.VK_RIGHT, suffixInputs[0].U.ki.wVk);
        Assert.Equal(NativeMethods.KEYEVENTF_EXTENDEDKEY, suffixInputs[0].U.ki.dwFlags);
        Assert.Equal(NativeMethods.VK_RIGHT, suffixInputs[1].U.ki.wVk);
        Assert.Equal(NativeMethods.KEYEVENTF_EXTENDEDKEY | NativeMethods.KEYEVENTF_KEYUP, suffixInputs[1].U.ki.dwFlags);
    }

    [Theory]
    [InlineData("ghbdsn", "?", "ghbdsn")]
    [InlineData("ghbdsn.", ".", "ghbdsn")]
    [InlineData("привітб", ",", "привіт")]
    public void StripVisibleSuffixFromInterpretation_RemovesOnlyMatchingVisibleOrMappedSuffix(string text, string suffix, string expected)
    {
        string actual = InvokeStripVisibleSuffixFromInterpretation(text, suffix);
        Assert.Equal(expected, actual);
    }

    private static NativeMethods.INPUT[] InvokeBuildAutoReplacementInputs(string originalCore, string replacementCore, string suffix)
    {
        var method = typeof(AutoModeHandler)
            .GetMethod("BuildAutoReplacementInputs", BindingFlags.NonPublic | BindingFlags.Static)!;

        // eraseCountOverride parameter is optional (int?), provide null for default behavior
        return (NativeMethods.INPUT[])method.Invoke(null, [originalCore, replacementCore, suffix, null])!;
    }

    private static string InvokeStripVisibleSuffixFromInterpretation(string text, string suffix)
    {
        var method = typeof(AutoModeHandler)
            .GetMethod("StripVisibleSuffixFromInterpretation", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (string)method.Invoke(null, [text, suffix])!;
    }
}

public class SafeModeHandlerRegressionTests
{
    [Fact]
    public void FixLastWord_FallsBackToSecondFullAdapter_WhenFirstFindsNoWord()
    {
        var diagnostics = new DiagnosticsLogger();
        diagnostics.Configure(true);
        var settings = new SettingsManager();
        var exclusions = new ExclusionManager(settings);
        var context = new ForegroundContext(IntPtr.Zero, IntPtr.Zero, "codex", 999, "Chrome_WidgetWin_1", "Chrome_RenderWidgetHostHWND");

        var first = new SafeModeStubAdapter("UIA", TargetSupport.Full) { LastWord = null };
        var second = new SafeModeStubAdapter("SendInput", TargetSupport.Full) { LastWord = "ghbdsn", ReplaceLastWordResult = true };
        var coordinator = new TextTargetCoordinator(new ITextTargetAdapter[] { first, second });
        var handler = new SafeModeHandler(() => context, coordinator, exclusions, diagnostics, settings);

        handler.FixLastWord();

        Assert.True(second.TryReplaceLastWordCalled);
        var entry = Assert.Single(diagnostics.GetEntries());
        Assert.Equal("SendInput", entry.AdapterName);
        Assert.Equal(DiagnosticResult.Replaced, entry.Result);
        Assert.Equal("привіт", entry.ConvertedText);
    }

    private sealed class SafeModeStubAdapter(string name, TargetSupport support) : ITextTargetAdapter
    {
        public string AdapterName => name;
        public string? LastWord { get; set; }
        public string? SelectedText { get; set; }
        public string? CurrentSentence { get; set; }
        public bool ReplaceLastWordResult { get; set; }
        public bool ReplaceSelectionResult { get; set; }
        public bool ReplaceCurrentSentenceResult { get; set; }
        public bool TryReplaceLastWordCalled { get; private set; }

        public TargetSupport CanHandle(ForegroundContext context) => support;
        public string DescribeSupport(ForegroundContext context) => support.ToString();
        public string? TryGetLastWord(ForegroundContext context) => LastWord;
        public string? TryGetSelectedText(ForegroundContext context) => SelectedText;
        public string? TryGetCurrentSentence(ForegroundContext context) => CurrentSentence;
        public bool TryReplaceLastWord(ForegroundContext context, string replacement)
        {
            TryReplaceLastWordCalled = true;
            return ReplaceLastWordResult;
        }
        public bool TryReplaceSelection(ForegroundContext context, string replacement) => ReplaceSelectionResult;
        public bool TryReplaceCurrentSentence(ForegroundContext context, string replacement) => ReplaceCurrentSentenceResult;
    }
}

public class AutoModeHandlerRegressionTests
{
    [Fact]
    public void BuildUndoInputs_ReleasesModifiers_BackspacesReplacementAndRetypesOriginal()
    {
        var inputs = InvokeBuildUndoInputs("привіт", "ghbdsn");

        int modifierCount = NativeMethods.BuildModifierReleaseInputs().Length;
        int expectedCount = modifierCount + (("привіт".Length + 1) * 2) + ("ghbdsn".Length * 2);

        Assert.Equal(expectedCount, inputs.Length);
        Assert.Equal(NativeMethods.VK_SHIFT, inputs[0].U.ki.wVk);
        Assert.Equal(NativeMethods.KEYEVENTF_KEYUP, inputs[0].U.ki.dwFlags);

        Assert.Equal(NativeMethods.VK_BACK, inputs[modifierCount].U.ki.wVk);
        Assert.Equal(0u, inputs[modifierCount].U.ki.dwFlags);
        Assert.Equal(NativeMethods.VK_BACK, inputs[modifierCount + 1].U.ki.wVk);
        Assert.Equal(NativeMethods.KEYEVENTF_KEYUP, inputs[modifierCount + 1].U.ki.dwFlags);

        int unicodeStart = modifierCount + (("привіт".Length + 1) * 2);
        Assert.Equal('g', (char)inputs[unicodeStart].U.ki.wScan);
        Assert.Equal(NativeMethods.KEYEVENTF_UNICODE, inputs[unicodeStart].U.ki.dwFlags);
    }

    private static NativeMethods.INPUT[] InvokeBuildUndoInputs(string replacementText, string restoreText)
    {
        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("BuildUndoInputs", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (NativeMethods.INPUT[])method.Invoke(null, new object[] { replacementText, restoreText })!;
    }
}

public class SendInputAdapterRegressionTests
{
    [Fact]
    public void BuildWordSelectionReplaceInputs_UsesExactShiftLeftSelection_NotCtrlWordJump()
    {
        var inputs = InvokeBuildWordSelectionReplaceInputs(3, "привіт");

        int modifierCount = NativeMethods.BuildModifierReleaseInputs().Length;
        Assert.Equal(NativeMethods.VK_SHIFT, inputs[modifierCount].U.ki.wVk);
        Assert.Equal(0u, inputs[modifierCount].U.ki.dwFlags);

        for (int i = 0; i < 3; i++)
        {
            int offset = modifierCount + 1 + (i * 2);
            Assert.Equal(NativeMethods.VK_LEFT, inputs[offset].U.ki.wVk);
            Assert.Equal(NativeMethods.KEYEVENTF_EXTENDEDKEY, inputs[offset].U.ki.dwFlags);
            Assert.Equal(NativeMethods.VK_LEFT, inputs[offset + 1].U.ki.wVk);
            Assert.Equal(NativeMethods.KEYEVENTF_EXTENDEDKEY | NativeMethods.KEYEVENTF_KEYUP, inputs[offset + 1].U.ki.dwFlags);
        }

        int shiftUpIndex = modifierCount + 1 + (3 * 2);
        Assert.Equal(NativeMethods.VK_SHIFT, inputs[shiftUpIndex].U.ki.wVk);
        Assert.Equal(NativeMethods.KEYEVENTF_KEYUP, inputs[shiftUpIndex].U.ki.dwFlags);

        Assert.DoesNotContain(inputs, input => input.U.ki.wVk == NativeMethods.VK_CONTROL && input.U.ki.dwFlags == 0u);
    }

    private static NativeMethods.INPUT[] InvokeBuildWordSelectionReplaceInputs(int selectionLength, string replacement)
    {
        MethodInfo method = typeof(SendInputAdapter)
            .GetMethod("BuildWordSelectionReplaceInputs", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (NativeMethods.INPUT[])method.Invoke(null, new object[] { selectionLength, replacement })!;
    }
}
