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
            currentCaretPos: 12,
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
            currentCaretPos: "старе руддщ нове руддщ".Length,
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
            currentCaretPos: "старе hit нове".Length,
            out _,
            out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryBuildReplacementValue_PreservesTrailingSpaceCaret_WhenDelimiterAlreadyExists()
    {
        bool ok = InvokeTryBuildReplacementValue(
            currentValue: "привіт руддщ ",
            cachedValue: "привіт руддщ",
            original: "руддщ",
            cachedStart: 7,
            cachedEnd: 12,
            replacement: "hello",
            currentCaretPos: "привіт руддщ ".Length,
            out string newValue,
            out int targetCaretIndex);

        Assert.True(ok);
        Assert.Equal("привіт hello ", newValue);
        Assert.Equal("привіт hello ".Length, targetCaretIndex);
    }

    [Fact]
    public void TryBuildReplacementValue_PreservesTypedTailAfterSuppressedBoundary()
    {
        const string currentValue = "hello руддщ world";

        bool ok = InvokeTryBuildReplacementValue(
            currentValue: currentValue,
            cachedValue: "hello руддщ",
            original: "руддщ",
            cachedStart: 6,
            cachedEnd: 11,
            replacement: "hello",
            currentCaretPos: currentValue.Length,
            out string newValue,
            out int targetCaretIndex);

        Assert.True(ok);
        Assert.Equal("hello hello world", newValue);
        Assert.Equal("hello hello world".Length, targetCaretIndex);
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
    [InlineData(12, 12, 4)]
    [InlineData(12, 9, 10)]
    public void BuildCaretRestoreInputs_UsesCtrlEndAndExpectedLeftMoves(int textLength, int targetCaretIndex, int expectedInputCount)
    {
        var inputs = InvokeBuildCaretRestoreInputs(textLength, targetCaretIndex);
        int modCount = NativeMethods.BuildModifierReleaseInputs().Length;

        Assert.Equal(expectedInputCount + modCount, inputs.Length);
        Assert.Equal(NativeMethods.VK_CONTROL, inputs[modCount].U.ki.wVk);
        Assert.Equal(NativeMethods.VK_END, inputs[modCount + 1].U.ki.wVk);
    }

    [Fact]
    public void BuildModifierReleaseInputs_ReleasesCommonModifiers()
    {
        var inputs = NativeMethods.BuildModifierReleaseInputs();
        if (inputs.Length == 0) return;

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
        int currentCaretPos,
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
            currentCaretPos,
            null,
            null
        };

        bool ok = (bool)method.Invoke(null, args)!;
        newValue = (string?)args[7] ?? string.Empty;
        targetCaretIndex = (int?)args[8] ?? -1;
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

    [Fact]
    public void ResolveVisibleTrailingPunctuation_DoesNotTreatPeriodAsLiteral_WhenItCompletesLayoutWord()
    {
        string suffix = InvokeResolveVisibleTrailingPunctuation("hjpevs.", "розумію");

        Assert.Equal(string.Empty, suffix);
    }

    [Fact]
    public void ResolveVisibleTrailingPunctuation_TreatsPeriodAsLiteral_WhenCoreWordIsBetter()
    {
        string suffix = InvokeResolveVisibleTrailingPunctuation("ghbdsn.", "привітю");

        Assert.Equal(".", suffix);
    }

    [Fact]
    public void TryEvaluateVisibleTokenCandidate_ConvertsLayoutPeriodAsLetter()
    {
        CorrectionCandidate candidate = InvokeTryEvaluateVisibleTokenCandidate("hjpevs.");

        Assert.Equal("hjpevs.", candidate.OriginalText);
        Assert.Equal("розумію", candidate.ConvertedText);
    }

    [Fact]
    public void TryEvaluateVisibleTokenCandidate_PreservesLiteralPeriod_WhenCoreWordIsBetter()
    {
        CorrectionCandidate candidate = InvokeTryEvaluateVisibleTokenCandidate("ghbdsn.");

        Assert.Equal("ghbdsn.", candidate.OriginalText);
        Assert.Equal("привіт.", candidate.ConvertedText);
    }

    [Fact]
    public void TryEvaluateVisibleTokenCandidate_PreservesLiteralComma_WhenCoreWordIsBetter()
    {
        CorrectionCandidate candidate = InvokeTryEvaluateVisibleTokenCandidate("ghbdsn,");

        Assert.Equal("ghbdsn,", candidate.OriginalText);
        Assert.Equal("привіт,", candidate.ConvertedText);
    }

    [Theory]
    [InlineData("юля,")]
    [InlineData("god.")]
    [InlineData("ти?")]
    public void TryEvaluateVisibleTokenCandidate_SkipsAlreadyCorrectWordsWithLiteralPunctuation(string token)
    {
        Assert.Null(InvokeTryEvaluateVisibleTokenCandidateOrNull(token));
    }

    private static CorrectionCandidate InvokeApplyVisibleTrailingPunctuation(CorrectionCandidate candidate, string visibleSuffix)
    {
        var method = typeof(AutoModeHandler)
            .GetMethod("ApplyVisibleTrailingPunctuation", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (CorrectionCandidate)method.Invoke(null, [candidate, visibleSuffix])!;
    }

    private static string InvokeResolveVisibleTrailingPunctuation(params string[] visibleWords)
    {
        var method = typeof(AutoModeHandler)
            .GetMethod("ResolveVisibleTrailingPunctuation", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (string)method.Invoke(null, [visibleWords])!;
    }

    private static CorrectionCandidate InvokeTryEvaluateVisibleTokenCandidate(string token)
    {
        var candidate = InvokeTryEvaluateVisibleTokenCandidateOrNull(token);
        Assert.NotNull(candidate);
        return candidate!;
    }

    private static CorrectionCandidate? InvokeTryEvaluateVisibleTokenCandidateOrNull(string token)
    {
        var method = typeof(AutoModeHandler)
            .GetMethod("TryEvaluateVisibleTokenCandidate", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (CorrectionCandidate?)method.Invoke(null, [token]);
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
        Assert.Equal(0u, inputs[release.Length].U.ki.dwFlags);
    }

    [Fact]
    public void BuildAutoReplacementInputs_UsesBackspaceUnicode_NotShiftSelection()
    {
        NativeMethods.INPUT[] inputs = InvokeBuildAutoReplacementInputs("ghbdsn", "привіт", string.Empty);

        int backspaceDownCount = inputs.Count(input =>
            input.U.ki.wVk == NativeMethods.VK_BACK
            && input.U.ki.dwFlags == 0u);
        bool hasShiftDown = inputs.Any(input =>
            input.U.ki.wVk == NativeMethods.VK_SHIFT
            && (input.U.ki.dwFlags & NativeMethods.KEYEVENTF_KEYUP) == 0);

        Assert.Equal("ghbdsn".Length, backspaceDownCount);
        Assert.False(hasShiftDown);
        Assert.Contains(inputs, input =>
            input.U.ki.wScan == 'п'
            && (input.U.ki.dwFlags & NativeMethods.KEYEVENTF_UNICODE) != 0);
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

    // ─── eraseCountOverride ──────────────────────────────────────────────────

    [Fact]
    public void BuildAutoReplacementInputs_WithEraseCountOverride_UsesOverrideForSelectionCount()
    {
        // "Win" is 3 chars, but the word on screen was "Win11" (5 chars typed).
        // eraseCountOverride=5 must produce 5 Shift+Left presses, not 3.
        int releaseCount = NativeMethods.BuildModifierReleaseInputs().Length;
        NativeMethods.INPUT[] inputsDefault = InvokeBuildAutoReplacementInputs("Win", "Він", string.Empty);
        NativeMethods.INPUT[] inputsOverride = InvokeBuildAutoReplacementInputs("Win", "Він", string.Empty, eraseCountOverride: 5);

        // Default: releaseCount + ShiftDown + 3×(Left×2) + ShiftUp + 3×(Unicode×2)
        // Override: releaseCount + ShiftDown + 5×(Left×2) + ShiftUp + 3×(Unicode×2)
        Assert.Equal(inputsDefault.Length + 4, inputsOverride.Length); // 4 = (5-3)*2
    }

    [Fact]
    public void BuildAutoReplacementInputs_WithEraseCountOverride_EqualToDefault_SameLengthAsNoOverride()
    {
        NativeMethods.INPUT[] inputsDefault = InvokeBuildAutoReplacementInputs("ghbdsn", "привіт", string.Empty);
        NativeMethods.INPUT[] inputsOverride = InvokeBuildAutoReplacementInputs("ghbdsn", "привіт", string.Empty, eraseCountOverride: 6);

        Assert.Equal(inputsDefault.Length, inputsOverride.Length);
    }

    // ─── Trailing suffix variety ─────────────────────────────────────────────

    [Theory]
    [InlineData(",")]
    [InlineData(".")]
    [InlineData("!")]
    [InlineData("?")]
    [InlineData(";")]
    [InlineData(":")]
    public void BuildAutoReplacementInputs_WithSingleCharSuffix_ProducesCorrectLeftRightWrap(string suffix)
    {
        int releaseCount = NativeMethods.BuildModifierReleaseInputs().Length;
        NativeMethods.INPUT[] inputs = InvokeBuildAutoReplacementInputs("ghbdsn", "привіт", suffix);

        // First non-release input after modifiers must be Left (move before suffix)
        Assert.Equal(NativeMethods.VK_LEFT, inputs[releaseCount].U.ki.wVk);
        // Last two inputs must be Right (move back after suffix)
        Assert.Equal(NativeMethods.VK_RIGHT, inputs[^2].U.ki.wVk);
        Assert.Equal(NativeMethods.VK_RIGHT, inputs[^1].U.ki.wVk);
    }

    [Fact]
    public void BuildAutoReplacementInputs_WithMultiCharSuffix_ProducesCorrectNumberOfArrows()
    {
        // suffix "[]" = 2 chars → 2 Left at start, 2 Right at end
        int releaseCount = NativeMethods.BuildModifierReleaseInputs().Length;
        NativeMethods.INPUT[] inputs = InvokeBuildAutoReplacementInputs("ghbdsn", "привіт", "[]");

        // 2 Left down+up pairs before Shift
        Assert.Equal(NativeMethods.VK_LEFT, inputs[releaseCount].U.ki.wVk);
        Assert.Equal(NativeMethods.VK_LEFT, inputs[releaseCount + 2].U.ki.wVk);
        // 2 Right down+up pairs at end
        Assert.Equal(NativeMethods.VK_RIGHT, inputs[^4].U.ki.wVk);
        Assert.Equal(NativeMethods.VK_RIGHT, inputs[^2].U.ki.wVk);
    }

    [Fact]
    public void BuildAutoReplacementInputs_NoSuffix_NoLeadingArrows()
    {
        int releaseCount = NativeMethods.BuildModifierReleaseInputs().Length;
        NativeMethods.INPUT[] inputs = InvokeBuildAutoReplacementInputs("ghbdsn", "привіт", string.Empty);

        // First input after modifier release must be Backspace (no Left arrows without suffix)
        Assert.Equal(NativeMethods.VK_BACK, inputs[releaseCount].U.ki.wVk);
        Assert.Equal(0u, inputs[releaseCount].U.ki.dwFlags); // key-down, not key-up
    }

    private static NativeMethods.INPUT[] InvokeBuildAutoReplacementInputs(string originalCore, string replacementCore, string suffix, int? eraseCountOverride = null)
    {
        var method = typeof(AutoModeHandler)
            .GetMethod("BuildAutoReplacementInputs", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (NativeMethods.INPUT[])method.Invoke(null, [originalCore, replacementCore, suffix, eraseCountOverride])!;
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
        var context = new ForegroundContext(IntPtr.Zero, IntPtr.Zero, "notepad", 999, "Notepad", "Edit");

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

    [Fact]
    public void FixLastWord_CaretSensitiveContext_UsesSendInputInsteadOfUiaSetValue()
    {
        var diagnostics = new DiagnosticsLogger();
        diagnostics.Configure(true);
        var settings = new SettingsManager();
        var exclusions = new ExclusionManager(settings);
        var context = new ForegroundContext(IntPtr.Zero, IntPtr.Zero, "element", 999, "Chrome_WidgetWin_1", "Chrome_RenderWidgetHostHWND");

        var uia = new SafeModeStubAdapter("UIAutomationTargetAdapter", TargetSupport.Full) { LastWord = "ghbdsn", ReplaceLastWordResult = true };
        var sendInput = new SafeModeStubAdapter("SendInputAdapter", TargetSupport.Full) { LastWord = "ghbdsn", ReplaceLastWordResult = true };
        var coordinator = new TextTargetCoordinator(new ITextTargetAdapter[] { uia, sendInput });
        var handler = new SafeModeHandler(() => context, coordinator, exclusions, diagnostics, settings);

        handler.FixLastWord();

        Assert.False(uia.TryGetLastWordCalled);
        Assert.False(uia.TryReplaceLastWordCalled);
        Assert.True(sendInput.TryReplaceLastWordCalled);
        var entry = Assert.Single(diagnostics.GetEntries());
        Assert.Equal("SendInputAdapter", entry.AdapterName);
        Assert.Equal(DiagnosticResult.Replaced, entry.Result);
        Assert.Equal("привіт", entry.ConvertedText);
    }

    [Fact]
    public void FixLastWord_SkipsCorrectUkrainianWord()
    {
        var diagnostics = new DiagnosticsLogger();
        diagnostics.Configure(true);
        var settings = new SettingsManager();
        var exclusions = new ExclusionManager(settings);
        var context = new ForegroundContext(IntPtr.Zero, IntPtr.Zero, "notepad", 999, "Notepad", "Edit");

        var adapter = new SafeModeStubAdapter("NativeEdit", TargetSupport.Full) { LastWord = "ти", ReplaceLastWordResult = true };
        var coordinator = new TextTargetCoordinator(new ITextTargetAdapter[] { adapter });
        var handler = new SafeModeHandler(() => context, coordinator, exclusions, diagnostics, settings);

        handler.FixLastWord();

        Assert.True(adapter.TryGetLastWordCalled);
        Assert.False(adapter.TryReplaceLastWordCalled);
        var entry = Assert.Single(diagnostics.GetEntries());
        Assert.Equal(DiagnosticResult.Skipped, entry.Result);
        Assert.Equal("ти", entry.OriginalText);
        Assert.Contains("already looks correct", entry.Reason);
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
        public bool TryGetLastWordCalled { get; private set; }
        public bool TryReplaceLastWordCalled { get; private set; }

        public TargetSupport CanHandle(ForegroundContext context) => support;
        public string DescribeSupport(ForegroundContext context) => support.ToString();
        public string? TryGetLastWord(ForegroundContext context)
        {
            TryGetLastWordCalled = true;
            return LastWord;
        }
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
    public void BuildCandidateDecision_NoBufferedCandidateInBrowserContext_RequestsLiveRuntimeRead()
    {
        var settings = new SettingsManager();
        var handler = new AutoModeHandler(
            new ForegroundContextProvider(),
            new TextTargetCoordinator(Array.Empty<ITextTargetAdapter>()),
            new ExclusionManager(settings),
            new DiagnosticsLogger(),
            settings,
            new KeyboardObserver(settings));

        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_RenderWidgetHostHWND");

        object snapshot = CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_RenderWidgetHostHWND",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "UA",
            liveWord: "",
            visibleWord: "",
            wordEn: "fiii",
            wordUa: "ащшу",
            analysisWordEn: "fiii",
            analysisWordUa: "ащшу",
            rawDebug: "[/(s29/vC0) z/z(s2C/vBC) j/j(s24/vBE) /(s35/vBF)",
            originalDisplay: "ащшу",
            techDetail: "keys=4 rec=0 drop=0 seq=2 lay=UA",
            approxWordLength: 4,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 2);

        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("BuildCandidateDecision", BindingFlags.NonPublic | BindingFlags.Instance)!;

        object decision = method.Invoke(handler, new[] { snapshot })!;
        bool requiresLiveRuntimeRead = (bool)decision.GetType().GetProperty("RequiresLiveRuntimeRead")!.GetValue(decision)!;

        Assert.True(requiresLiveRuntimeRead);
    }

    [Fact]
    public void BuildCandidateDecision_ChromeSequentialScanNoise_UsesVkFallbackCandidate()
    {
        var settings = new SettingsManager();
        var handler = new AutoModeHandler(
            new ForegroundContextProvider(),
            new TextTargetCoordinator(Array.Empty<ITextTargetAdapter>()),
            new ExclusionManager(settings),
            new DiagnosticsLogger(),
            settings,
            new KeyboardObserver(settings));

        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_RenderWidgetHostHWND");

        object snapshot = CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_RenderWidgetHostHWND",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "EN",
            liveWord: "",
            visibleWord: "ghbdsn",
            wordEn: "123456",
            wordUa: "123456",
            analysisWordEn: "123456",
            analysisWordUa: "123456",
            rawDebug: "1/g(s02/v47) 2/h(s03/v48) 3/b(s04/v42) 4/d(s05/v44) 5/s(s06/v53) 6/n(s07/v4E)",
            originalDisplay: "123456",
            techDetail: "keys=6 rec=0 drop=0 seq=5 lay=EN vkEn=ghbdsn vkUa=привіт",
            approxWordLength: 6,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 5,
            vkWordEn: "ghbdsn",
            vkWordUa: "привіт");

        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("BuildCandidateDecision", BindingFlags.NonPublic | BindingFlags.Instance)!;

        CandidateDecision decision = (CandidateDecision)method.Invoke(handler, [snapshot])!;

        Assert.NotNull(decision.Candidate);
        Assert.Equal(CandidateSource.VkFallback, decision.Source);
        Assert.False(decision.RequiresLiveRuntimeRead);
        Assert.Equal("ghbdsn", decision.Candidate!.OriginalText);
        Assert.Equal("привіт", decision.Candidate.ConvertedText);
    }

    [Fact]
    public void BuildCandidateDecision_ChromeSequentialScanNoiseWithPunctuation_UsesVkFallbackCandidate()
    {
        var settings = new SettingsManager();
        var handler = new AutoModeHandler(
            new ForegroundContextProvider(),
            new TextTargetCoordinator(Array.Empty<ITextTargetAdapter>()),
            new ExclusionManager(settings),
            new DiagnosticsLogger(),
            settings,
            new KeyboardObserver(settings));

        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_RenderWidgetHostHWND");

        object snapshot = CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_RenderWidgetHostHWND",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "EN",
            liveWord: "",
            visibleWord: "ghbdsn;",
            wordEn: "123456;",
            wordUa: "123456;",
            analysisWordEn: "123456",
            analysisWordUa: "123456",
            rawDebug: "1/g(s02/v47) 2/h(s03/v48) 3/b(s04/v42) 4/d(s05/v44) 5/s(s06/v53) 6/n(s07/v4E) ;/;(s27/vBA)",
            originalDisplay: "123456;",
            techDetail: "keys=7 rec=0 drop=0 seq=5 lay=EN vkEn=ghbdsn; vkUa=привіт;",
            approxWordLength: 7,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 5,
            vkWordEn: "ghbdsn;",
            vkWordUa: "привіт;",
            visibleTrailingSuffix: ";");

        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("BuildCandidateDecision", BindingFlags.NonPublic | BindingFlags.Instance)!;

        CandidateDecision decision = (CandidateDecision)method.Invoke(handler, [snapshot])!;

        Assert.NotNull(decision.Candidate);
        Assert.Equal(CandidateSource.VkFallback, decision.Source);
        Assert.Equal("ghbdsn;", decision.Candidate!.OriginalText);
        Assert.Equal("привіт;", decision.Candidate.ConvertedText);
    }

    [Fact]
    public void ResolveCandidateDecision_NativeContextWithoutCandidate_UsesLiveWordFallback()
    {
        var settings = new SettingsManager();
        var handler = new AutoModeHandler(
            new ForegroundContextProvider(),
            new TextTargetCoordinator(Array.Empty<ITextTargetAdapter>()),
            new ExclusionManager(settings),
            new DiagnosticsLogger(),
            settings,
            new KeyboardObserver(settings));

        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "notepad",
            123,
            "Edit",
            "Notepad");

        object snapshot = CreateWordSnapshot(
            context,
            processName: "notepad",
            controlClass: "Edit",
            windowClass: "Notepad",
            layoutTag: "EN",
            liveWord: "ghbdsn",
            visibleWord: "ghbdsn",
            wordEn: "ghbdsn",
            wordUa: "привіт",
            analysisWordEn: "zzzzzz",
            analysisWordUa: "яяяяяя",
            rawDebug: "test",
            originalDisplay: "ghbdsn",
            techDetail: "keys=6 rec=1 drop=0 seq=0 lay=EN",
            approxWordLength: 6,
            recoveryCount: 1,
            droppedKeyCount: 0,
            sequentialScanCount: 0);

        var initialDecision = new CandidateDecision(
            Candidate: null,
            Source: CandidateSource.None,
            RequiresLiveRuntimeRead: true,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "buffer fallback needed");

        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("ResolveCandidateDecision", BindingFlags.NonPublic | BindingFlags.Instance)!;

        CandidateDecision resolved = (CandidateDecision)method.Invoke(handler, [snapshot, initialDecision])!;

        Assert.NotNull(resolved.Candidate);
        Assert.False(resolved.RequiresLiveRuntimeRead);
        Assert.Equal("ghbdsn", resolved.Candidate!.OriginalText);
        Assert.Equal("привіт", resolved.Candidate.ConvertedText);
        Assert.Contains("native live read fallback", resolved.Reason);
    }

    [Fact]
    public void ResolveCandidateDecision_NativeContextWithoutCandidateAndWithoutLiveWord_DisablesAsyncFallback()
    {
        var settings = new SettingsManager();
        var handler = new AutoModeHandler(
            new ForegroundContextProvider(),
            new TextTargetCoordinator(Array.Empty<ITextTargetAdapter>()),
            new ExclusionManager(settings),
            new DiagnosticsLogger(),
            settings,
            new KeyboardObserver(settings));

        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "notepad",
            123,
            "Edit",
            "Notepad");

        object snapshot = CreateWordSnapshot(
            context,
            processName: "notepad",
            controlClass: "Edit",
            windowClass: "Notepad",
            layoutTag: "UA",
            liveWord: "",
            visibleWord: "кубік",
            wordEn: "re,br",
            wordUa: "кубік",
            analysisWordEn: "re,br",
            analysisWordUa: "кубік",
            rawDebug: "test",
            originalDisplay: "кубік",
            techDetail: "keys=5 rec=1 drop=0 seq=0 lay=UA",
            approxWordLength: 5,
            recoveryCount: 1,
            droppedKeyCount: 0,
            sequentialScanCount: 0);

        var initialDecision = new CandidateDecision(
            Candidate: null,
            Source: CandidateSource.None,
            RequiresLiveRuntimeRead: true,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "buffer fallback needed");

        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("ResolveCandidateDecision", BindingFlags.NonPublic | BindingFlags.Instance)!;

        CandidateDecision resolved = (CandidateDecision)method.Invoke(handler, [snapshot, initialDecision])!;

        Assert.Null(resolved.Candidate);
        Assert.False(resolved.RequiresLiveRuntimeRead);
        Assert.Contains("native live read unavailable", resolved.Reason);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("code - insiders")]
    [InlineData("codex")]
    [InlineData("cursor")]
    [InlineData("element")]
    [InlineData("element-desktop")]
    [InlineData("vscodium")]
    [InlineData("windsurf")]
    public void ShouldUseElectronUiaPath_DefaultsToTrueForElement(string processName)
    {
        var settings = new SettingsManager
        {
            Current =
            {
                ElectronUiaPathEnabled = false
            }
        };

        var handler = new AutoModeHandler(
            new ForegroundContextProvider(),
            new TextTargetCoordinator(Array.Empty<ITextTargetAdapter>()),
            new ExclusionManager(settings),
            new DiagnosticsLogger(),
            settings,
            new KeyboardObserver(settings));

        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("ShouldUseElectronUiaPath", BindingFlags.NonPublic | BindingFlags.Instance)!;

        bool shouldUse = (bool)method.Invoke(handler, [processName])!;

        Assert.True(shouldUse);
    }

    [Theory]
    [InlineData("slack")]
    [InlineData("discord")]
    public void ShouldUseElectronUiaPath_RemainsOptInForOtherElectronApps(string processName)
    {
        var settings = new SettingsManager
        {
            Current =
            {
                ElectronUiaPathEnabled = false
            }
        };

        var handler = new AutoModeHandler(
            new ForegroundContextProvider(),
            new TextTargetCoordinator(Array.Empty<ITextTargetAdapter>()),
            new ExclusionManager(settings),
            new DiagnosticsLogger(),
            settings,
            new KeyboardObserver(settings));

        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("ShouldUseElectronUiaPath", BindingFlags.NonPublic | BindingFlags.Instance)!;

        bool shouldUse = (bool)method.Invoke(handler, [processName])!;

        Assert.False(shouldUse);
    }

    [Fact]
    public void CanUseElectronBufferedFallback_AllowsResolvedBufferedCandidate()
    {
        var candidate = new CorrectionCandidate(
            "руддщ",
            "hello",
            CorrectionDirection.UaToEn,
            0.95,
            "test");
        var decision = new CandidateDecision(
            candidate,
            CandidateSource.PrimaryHeuristics,
            RequiresLiveRuntimeRead: false,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "buffer candidate");

        bool canFallback = InvokeCanUseElectronBufferedFallback(decision);

        Assert.True(canFallback);
    }

    [Fact]
    public void CanUseElectronBufferedFallback_BlocksLiveRuntimeOnlyDecision()
    {
        var candidate = new CorrectionCandidate(
            "руддщ",
            "hello",
            CorrectionDirection.UaToEn,
            0.95,
            "test");
        var decision = new CandidateDecision(
            candidate,
            CandidateSource.PrimaryHeuristics,
            RequiresLiveRuntimeRead: true,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "needs live read");

        bool canFallback = InvokeCanUseElectronBufferedFallback(decision);

        Assert.False(canFallback);
    }

    [Fact]
    public void CanUseElectronBufferedFallback_BlocksMissingCandidate()
    {
        var decision = new CandidateDecision(
            Candidate: null,
            Source: CandidateSource.None,
            RequiresLiveRuntimeRead: false,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "no candidate");

        bool canFallback = InvokeCanUseElectronBufferedFallback(decision);

        Assert.False(canFallback);
    }

    [Fact]
    public void CanUseBufferedBrowserBackspace_AllowsBufferedCandidateForGoogleDocsLikeSurface()
    {
        var candidate = new CorrectionCandidate(
            "руддщ",
            "hello",
            CorrectionDirection.UaToEn,
            0.95,
            "test");
        var decision = new CandidateDecision(
            candidate,
            CandidateSource.PrimaryHeuristics,
            RequiresLiveRuntimeRead: false,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "buffer candidate");

        Assert.True(InvokeCanUseBufferedBrowserBackspace(decision));
    }

    [Fact]
    public void CanUseBufferedBrowserBackspace_BlocksLiveOnlyDecision()
    {
        var decision = new CandidateDecision(
            Candidate: null,
            Source: CandidateSource.None,
            RequiresLiveRuntimeRead: true,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "needs live read");

        Assert.False(InvokeCanUseBufferedBrowserBackspace(decision));
    }

    [Theory]
    [InlineData("chrome", "ControlType.Edit", "edit", "Chrome_OmniboxView", "", "")]
    [InlineData("msedge", "ControlType.Edit", "edit", "", "", "Address and search bar")]
    [InlineData("brave", "ControlType.Edit", "edit", "", "", "Search Google or type a URL")]
    [InlineData("chrome", "ControlType.Edit", "edit", "", "", "Адресний рядок")]
    [InlineData("chrome", "ControlType.Edit", "edit", "", "", "Пошук Google або введіть URL-адресу")]
    [InlineData("chrome", "ControlType.Edit", "edit", "", "", "Шукайте в Google або введіть URL-адресу")]
    public void IsBrowserAddressBarSurface_RecognizesBrowserOmnibox(
        string processName,
        string controlType,
        string localizedControlType,
        string className,
        string automationId,
        string elementName)
    {
        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("IsBrowserAddressBarSurface", BindingFlags.NonPublic | BindingFlags.Static, [typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string)])!;

        bool isAddressBar = (bool)method.Invoke(null, [processName, controlType, localizedControlType, className, automationId, elementName])!;

        Assert.True(isAddressBar);
    }

    [Theory]
    [InlineData("notepad", "ControlType.Edit", "edit", "Chrome_OmniboxView", "", "")]
    [InlineData("chrome", "ControlType.Edit", "edit", "", "", "Address")]
    [InlineData("chrome", "ControlType.Edit", "edit", "", "", "URL")]
    [InlineData("chrome", "ControlType.Edit", "edit", "", "", "Search the web")]
    [InlineData("chrome", "ControlType.Edit", "edit", "", "", "Street address")]
    [InlineData("chrome", "ControlType.Edit", "edit", "", "", "Address and search bar for project field")]
    [InlineData("chrome", "ControlType.Edit", "edit", "", "", "Search or enter address here")]
    public void IsBrowserAddressBarSurface_DoesNotTreatNormalInputsAsOmnibox(
        string processName,
        string controlType,
        string localizedControlType,
        string className,
        string automationId,
        string elementName)
    {
        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("IsBrowserAddressBarSurface", BindingFlags.NonPublic | BindingFlags.Static, [typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string)])!;

        bool isAddressBar = (bool)method.Invoke(null, [processName, controlType, localizedControlType, className, automationId, elementName])!;

        Assert.False(isAddressBar);
    }

    [Fact]
    public void ShouldUseBrowserPageClipboardFallback_BlocksChromePageWithoutWritableValuePattern()
    {
        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_RenderWidgetHostHWND");
        var snapshot = (WordSnapshot)CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_RenderWidgetHostHWND",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "UA",
            liveWord: "",
            visibleWord: "руддщ",
            wordEn: "hello",
            wordUa: "руддщ",
            analysisWordEn: "hello",
            analysisWordUa: "руддщ",
            rawDebug: "page",
            originalDisplay: "руддщ",
            techDetail: "keys=5 rec=0 drop=0 seq=0 lay=UA",
            approxWordLength: 5,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 0);
        var decision = new CandidateDecision(
            new CorrectionCandidate("руддщ", "hello", CorrectionDirection.UaToEn, 0.95, "buffer"),
            CandidateSource.PrimaryHeuristics,
            RequiresLiveRuntimeRead: false,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "buffer candidate");
        object surface = CreateBrowserSurfaceSnapshot(
            hasWritableValuePattern: false,
            className: "Chrome_RenderWidgetHostHWND",
            elementName: "YouTube comment",
            unsafeCustomEditorLike: true);

        Assert.False(InvokeShouldUseBrowserPageClipboardFallback(snapshot, decision, surface));
    }

    [Fact]
    public void ShouldUseBrowserPageClipboardFallback_BlocksWritableBrowserPageBeforeValuePattern()
    {
        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_RenderWidgetHostHWND");
        var snapshot = (WordSnapshot)CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_RenderWidgetHostHWND",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "UA",
            liveWord: "",
            visibleWord: "руддщ",
            wordEn: "hello",
            wordUa: "руддщ",
            analysisWordEn: "hello",
            analysisWordUa: "руддщ",
            rawDebug: "page",
            originalDisplay: "руддщ",
            techDetail: "keys=5 rec=0 drop=0 seq=0 lay=UA",
            approxWordLength: 5,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 0);
        var decision = new CandidateDecision(
            new CorrectionCandidate("руддщ", "hello", CorrectionDirection.UaToEn, 0.95, "buffer"),
            CandidateSource.PrimaryHeuristics,
            RequiresLiveRuntimeRead: false,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "buffer candidate");
        object writablePage = CreateBrowserSurfaceSnapshot(
            hasWritableValuePattern: true,
            className: "Chrome_RenderWidgetHostHWND",
            elementName: "Comment",
            unsafeCustomEditorLike: false);

        Assert.False(InvokeShouldUseBrowserPageClipboardFallback(snapshot, decision, writablePage));
    }

    [Fact]
    public void ShouldUseBrowserPageClipboardFallback_DoesNotProbeEveryCorrectBrowserWord()
    {
        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_RenderWidgetHostHWND");
        var snapshot = (WordSnapshot)CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_RenderWidgetHostHWND",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "UA",
            liveWord: "",
            visibleWord: "сьогодні",
            wordEn: "c]ujlys",
            wordUa: "сьогодні",
            analysisWordEn: "c]ujlys",
            analysisWordUa: "сьогодні",
            rawDebug: "page",
            originalDisplay: "сьогодні",
            techDetail: "keys=8 rec=0 drop=0 seq=0 lay=UA",
            approxWordLength: 8,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 0);
        var decision = new CandidateDecision(
            Candidate: null,
            Source: CandidateSource.None,
            RequiresLiveRuntimeRead: true,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "no buffered conversion");
        object page = CreateBrowserSurfaceSnapshot(
            hasWritableValuePattern: true,
            className: "Chrome_RenderWidgetHostHWND",
            elementName: "Comment",
            unsafeCustomEditorLike: false);

        Assert.False(InvokeShouldUseBrowserPageClipboardFallback(snapshot, decision, page));
    }

    [Fact]
    public void ShouldUseBrowserPageClipboardFallback_BlocksWhenChromeBufferNeedsLiveDiscovery()
    {
        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_RenderWidgetHostHWND");
        var snapshot = (WordSnapshot)CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_RenderWidgetHostHWND",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "UA",
            liveWord: "",
            visibleWord: "",
            wordEn: "45678",
            wordUa: "45678",
            analysisWordEn: "45678",
            analysisWordUa: "45678",
            rawDebug: "chrome scan noise",
            originalDisplay: "45678",
            techDetail: "keys=5 rec=0 drop=0 seq=4 lay=UA",
            approxWordLength: 5,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 4);
        var decision = new CandidateDecision(
            Candidate: null,
            Source: CandidateSource.None,
            RequiresLiveRuntimeRead: true,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "needs live read");
        object page = CreateBrowserSurfaceSnapshot(
            hasWritableValuePattern: true,
            className: "Chrome_RenderWidgetHostHWND",
            elementName: "Comment",
            unsafeCustomEditorLike: false);

        Assert.False(InvokeShouldUseBrowserPageClipboardFallback(snapshot, decision, page));
    }

    [Fact]
    public void ShouldUseBrowserPageClipboardFallback_BlocksAddressBar()
    {
        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_RenderWidgetHostHWND");
        var snapshot = (WordSnapshot)CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_RenderWidgetHostHWND",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "UA",
            liveWord: "",
            visibleWord: "руддщ",
            wordEn: "hello",
            wordUa: "руддщ",
            analysisWordEn: "hello",
            analysisWordUa: "руддщ",
            rawDebug: "page",
            originalDisplay: "руддщ",
            techDetail: "keys=5 rec=0 drop=0 seq=0 lay=UA",
            approxWordLength: 5,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 0);
        var decision = new CandidateDecision(
            new CorrectionCandidate("руддщ", "hello", CorrectionDirection.UaToEn, 0.95, "buffer"),
            CandidateSource.PrimaryHeuristics,
            RequiresLiveRuntimeRead: false,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "buffer candidate");
        object addressBar = CreateBrowserSurfaceSnapshot(
            hasWritableValuePattern: false,
            className: "Chrome_OmniboxView",
            elementName: "Address and search bar",
            unsafeCustomEditorLike: false);

        Assert.False(InvokeShouldUseBrowserPageClipboardFallback(snapshot, decision, addressBar));
    }

    [Fact]
    public void BuildAddressBarRoute_UsesLiveTokenBackspace_WhenLiveReadIsRequiredWithWritableValuePattern()
    {
        var decision = new CandidateDecision(
            Candidate: null,
            Source: CandidateSource.None,
            RequiresLiveRuntimeRead: true,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "needs live read");

        object route = InvokeBuildAddressBarRoute(decision, hasWritableValuePattern: true, exactAddressBarSurface: true);
        var values = ((System.Runtime.CompilerServices.ITuple)route);

        Assert.Equal(ReplacementSafetyProfile.NativeSafe, values[0]);
        Assert.Equal(ReplacementExecutionPath.BrowserAddressBarLiveTokenBackspace, values[1]);
        Assert.Equal("AddressBarLiveToken", values[2]);
        Assert.Contains("browser-address-bar-word-token", Assert.IsType<string>(values[3]));
    }

    [Fact]
    public void BuildAddressBarRoute_UsesLiveTokenPath_WhenBufferedCandidateIsAvailable()
    {
        var candidate = new CorrectionCandidate(
            "ghbdsn",
            "привіт",
            CorrectionDirection.EnToUa,
            0.95,
            "test");
        var decision = new CandidateDecision(
            candidate,
            CandidateSource.PrimaryHeuristics,
            RequiresLiveRuntimeRead: false,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "buffer candidate");

        object route = InvokeBuildAddressBarRoute(decision, hasWritableValuePattern: false, exactAddressBarSurface: false);
        var values = ((System.Runtime.CompilerServices.ITuple)route);

        Assert.Equal(ReplacementSafetyProfile.NativeSafe, values[0]);
        Assert.Equal(ReplacementExecutionPath.BrowserAddressBarLiveTokenBackspace, values[1]);
        Assert.Equal("AddressBarLiveToken", values[2]);
        Assert.NotEqual(ReplacementExecutionPath.ClipboardAssistedSelection, values[1]);
        Assert.Contains("buffered-candidate-with-clipboard-verification", Assert.IsType<string>(values[3]));
    }

    [Fact]
    public void BuildAddressBarRoute_UsesLiveToken_WhenValuePatternIsAvailableEvenWithBufferedCandidate()
    {
        var candidate = new CorrectionCandidate(
            "vj]",
            "мої",
            CorrectionDirection.EnToUa,
            0.95,
            "test");
        var decision = new CandidateDecision(
            candidate,
            CandidateSource.PrimaryHeuristics,
            RequiresLiveRuntimeRead: false,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "buffer candidate");

        object route = InvokeBuildAddressBarRoute(decision, hasWritableValuePattern: true, exactAddressBarSurface: false);
        var values = ((System.Runtime.CompilerServices.ITuple)route);

        Assert.Equal(ReplacementSafetyProfile.NativeSafe, values[0]);
        Assert.Equal(ReplacementExecutionPath.BrowserAddressBarLiveTokenBackspace, values[1]);
        Assert.Equal("AddressBarLiveToken", values[2]);
        Assert.Contains("browser-address-bar-word-token", Assert.IsType<string>(values[3]));
    }

    [Fact]
    public void BuildAddressBarRoute_AttemptsLiveToken_WhenSurfaceSnapshotMissesWritableValuePattern()
    {
        var decision = new CandidateDecision(
            Candidate: null,
            Source: CandidateSource.None,
            RequiresLiveRuntimeRead: true,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "needs live read");

        object route = InvokeBuildAddressBarRoute(decision, hasWritableValuePattern: false, exactAddressBarSurface: false);
        var values = ((System.Runtime.CompilerServices.ITuple)route);

        Assert.Equal(ReplacementSafetyProfile.NativeSafe, values[0]);
        Assert.Equal(ReplacementExecutionPath.BrowserAddressBarLiveTokenBackspace, values[1]);
        Assert.Equal("AddressBarLiveToken", values[2]);
        Assert.Contains("optimistic-after-uia-snapshot-miss", Assert.IsType<string>(values[3]));
    }

    [Fact]
    public void ShouldUseBrowserWordTokenRoute_ChromeLiveReadNoise_DoesNotRoutePageSurface()
    {
        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_RenderWidgetHostHWND");
        var snapshot = (WordSnapshot)CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_RenderWidgetHostHWND",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "UA",
            liveWord: "",
            visibleWord: "",
            wordEn: "kengu",
            wordUa: "лутпг",
            analysisWordEn: "kengu",
            analysisWordUa: "лутпг",
            rawDebug: "chrome-scan-noise",
            originalDisplay: "лутпг",
            techDetail: "keys=5 rec=0 drop=0 seq=4 lay=UA",
            approxWordLength: 5,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 4);
        var decision = new CandidateDecision(
            Candidate: null,
            Source: CandidateSource.None,
            RequiresLiveRuntimeRead: true,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "needs live read");
        object surface = CreateBrowserSurfaceSnapshot(
            hasWritableValuePattern: false,
            className: "Chrome_RenderWidgetHostHWND",
            elementName: "page document",
            unsafeCustomEditorLike: true);

        Assert.False(InvokeShouldUseBrowserWordTokenRoute(snapshot, decision, surface));
    }

    [Fact]
    public void ShouldUseBrowserWordTokenRoute_ChromeWithoutBufferedCandidate_DoesNotRoutePageSurface()
    {
        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_RenderWidgetHostHWND");
        var snapshot = (WordSnapshot)CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_RenderWidgetHostHWND",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "UA",
            liveWord: "",
            visibleWord: "",
            wordEn: "kengu",
            wordUa: "лутпг",
            analysisWordEn: "kengu",
            analysisWordUa: "лутпг",
            rawDebug: "chrome-no-buffered-candidate",
            originalDisplay: "лутпг",
            techDetail: "keys=5 rec=0 drop=0 seq=0 lay=UA",
            approxWordLength: 5,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 0);
        var decision = new CandidateDecision(
            Candidate: null,
            Source: CandidateSource.None,
            RequiresLiveRuntimeRead: false,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "no buffered candidate");
        object surface = CreateBrowserSurfaceSnapshot(
            hasWritableValuePattern: false,
            className: "Chrome_RenderWidgetHostHWND",
            elementName: "page document",
            unsafeCustomEditorLike: true);

        Assert.False(InvokeShouldUseBrowserWordTokenRoute(snapshot, decision, surface));
    }

    [Fact]
    public void ShouldUseBrowserWordTokenRoute_NonBrowser_DoesNotRouteUnknownSurface()
    {
        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "notepad",
            123,
            "Edit",
            "Edit");
        var snapshot = (WordSnapshot)CreateWordSnapshot(
            context,
            processName: "notepad",
            controlClass: "Edit",
            windowClass: "Edit",
            layoutTag: "UA",
            liveWord: "",
            visibleWord: "",
            wordEn: "kengu",
            wordUa: "лутпг",
            analysisWordEn: "kengu",
            analysisWordUa: "лутпг",
            rawDebug: "native-noise",
            originalDisplay: "лутпг",
            techDetail: "keys=5 rec=0 drop=0 seq=4 lay=UA",
            approxWordLength: 5,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 4);
        var decision = new CandidateDecision(
            Candidate: null,
            Source: CandidateSource.None,
            RequiresLiveRuntimeRead: true,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "needs live read");
        object surface = CreateBrowserSurfaceSnapshot(
            hasWritableValuePattern: false,
            className: "Edit",
            elementName: "edit",
            unsafeCustomEditorLike: false);

        Assert.False(InvokeShouldUseBrowserWordTokenRoute(snapshot, decision, surface));
    }

    [Fact]
    public void ShouldUseBrowserWordTokenRoute_TopChromeFocus_DoesNotRouteWithoutStrongAddressBarSignal()
    {
        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_WidgetWin_1");
        var snapshot = (WordSnapshot)CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_WidgetWin_1",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "UA",
            liveWord: "",
            visibleWord: "руддщ",
            wordEn: "hello",
            wordUa: "руддщ",
            analysisWordEn: "hello",
            analysisWordUa: "руддщ",
            rawDebug: "top-chrome",
            originalDisplay: "руддщ",
            techDetail: "keys=5 rec=0 drop=0 seq=0 lay=UA",
            approxWordLength: 5,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 0);
        var decision = new CandidateDecision(
            new CorrectionCandidate("руддщ", "hello", CorrectionDirection.UaToEn, 0.95, "buffer"),
            CandidateSource.PrimaryHeuristics,
            RequiresLiveRuntimeRead: false,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "buffer candidate");
        object surface = CreateBrowserSurfaceSnapshot(
            hasWritableValuePattern: false,
            className: "Chrome_WidgetWin_1",
            elementName: "",
            unsafeCustomEditorLike: false);

        Assert.False(InvokeShouldUseBrowserWordTokenRoute(snapshot, decision, surface));
    }

    [Fact]
    public void ShouldUseBrowserWordTokenRoute_AddressBarName_Routes()
    {
        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_OmniboxView");
        var snapshot = (WordSnapshot)CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_OmniboxView",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "UA",
            liveWord: "",
            visibleWord: "руддщ",
            wordEn: "hello",
            wordUa: "руддщ",
            analysisWordEn: "hello",
            analysisWordUa: "руддщ",
            rawDebug: "omnibox",
            originalDisplay: "руддщ",
            techDetail: "keys=5 rec=0 drop=0 seq=0 lay=UA",
            approxWordLength: 5,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 0);
        var decision = new CandidateDecision(
            new CorrectionCandidate("руддщ", "hello", CorrectionDirection.UaToEn, 0.95, "buffer"),
            CandidateSource.PrimaryHeuristics,
            RequiresLiveRuntimeRead: false,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "buffer candidate");
        object surface = CreateBrowserSurfaceSnapshot(
            hasWritableValuePattern: true,
            className: "Chrome_OmniboxView",
            elementName: "Address and search bar",
            unsafeCustomEditorLike: false);

        Assert.True(InvokeShouldUseBrowserWordTokenRoute(snapshot, decision, surface));
    }

    [Fact]
    public void ShouldUseBrowserWordTokenRoute_TypeUrlInBrowserChrome_Routes()
    {
        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_WidgetWin_1");
        var snapshot = (WordSnapshot)CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_WidgetWin_1",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "UA",
            liveWord: "",
            visibleWord: "руддщ",
            wordEn: "hello",
            wordUa: "руддщ",
            analysisWordEn: "hello",
            analysisWordUa: "руддщ",
            rawDebug: "type-url",
            originalDisplay: "руддщ",
            techDetail: "keys=5 rec=0 drop=0 seq=0 lay=UA",
            approxWordLength: 5,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 0);
        var decision = new CandidateDecision(
            new CorrectionCandidate("руддщ", "hello", CorrectionDirection.UaToEn, 0.95, "buffer"),
            CandidateSource.PrimaryHeuristics,
            RequiresLiveRuntimeRead: false,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "buffer candidate");
        object surface = CreateBrowserSurfaceSnapshot(
            hasWritableValuePattern: true,
            className: string.Empty,
            elementName: string.Empty,
            unsafeCustomEditorLike: false,
            ariaProperties: "type=url");

        Assert.True(InvokeShouldUseBrowserWordTokenRoute(snapshot, decision, surface));
    }

    [Fact]
    public void ShouldUseBrowserWordTokenRoute_TypeUrlInPageField_DoesNotRoute()
    {
        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_RenderWidgetHostHWND");
        var snapshot = (WordSnapshot)CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_RenderWidgetHostHWND",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "UA",
            liveWord: "",
            visibleWord: "",
            wordEn: "kengu",
            wordUa: "лутпг",
            analysisWordEn: "kengu",
            analysisWordUa: "лутпг",
            rawDebug: "page-type-url",
            originalDisplay: "лутпг",
            techDetail: "keys=5 rec=0 drop=0 seq=0 lay=UA",
            approxWordLength: 5,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 0);
        var decision = new CandidateDecision(
            Candidate: null,
            Source: CandidateSource.None,
            RequiresLiveRuntimeRead: true,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "needs live read");
        object surface = CreateBrowserSurfaceSnapshot(
            hasWritableValuePattern: true,
            className: "Chrome_RenderWidgetHostHWND",
            elementName: "Website URL",
            unsafeCustomEditorLike: false,
            ariaProperties: "type=url");

        Assert.False(InvokeShouldUseBrowserWordTokenRoute(snapshot, decision, surface));
    }

    [Fact]
    public void TryBuildAddressBarLiveTokenCandidate_ConvertsLastMistypedWord()
    {
        object live = InvokeTryBuildAddressBarLiveTokenCandidate("ghbdsn ghbdsn", "chrome");
        string original = Assert.IsType<string>(live.GetType().GetProperty("OriginalToken")!.GetValue(live));
        var candidate = Assert.IsType<CorrectionCandidate>(live.GetType().GetProperty("Candidate")!.GetValue(live));

        Assert.Equal("ghbdsn", original);
        Assert.Equal("ghbdsn", candidate.OriginalText);
        Assert.Equal("привіт", candidate.ConvertedText);
    }

    [Fact]
    public void TryBuildAddressBarLiveTokenCandidate_OnlyUsesLastWordInPhrase()
    {
        object live = InvokeTryBuildAddressBarLiveTokenCandidate("ghbdsn руддщ", "chrome");
        string original = Assert.IsType<string>(live.GetType().GetProperty("OriginalToken")!.GetValue(live));
        var candidate = Assert.IsType<CorrectionCandidate>(live.GetType().GetProperty("Candidate")!.GetValue(live));

        Assert.Equal("руддщ", original);
        Assert.Equal("hello", candidate.ConvertedText);
    }

    [Fact]
    public void TryBuildAddressBarLiveTokenCandidate_ConvertsCyrillicMistypedWord()
    {
        object live = InvokeTryBuildAddressBarLiveTokenCandidate("руддщ", "chrome");
        string original = Assert.IsType<string>(live.GetType().GetProperty("OriginalToken")!.GetValue(live));
        var candidate = Assert.IsType<CorrectionCandidate>(live.GetType().GetProperty("Candidate")!.GetValue(live));

        Assert.Equal("руддщ", original);
        Assert.Equal("hello", candidate.ConvertedText);
    }

    [Fact]
    public void TryBuildAddressBarLiveTokenCandidate_UsesPeriodAsLayoutLetter_WhenItCompletesWord()
    {
        object live = InvokeTryBuildAddressBarLiveTokenCandidate("hjpevs.", "chrome");
        var candidate = Assert.IsType<CorrectionCandidate>(live.GetType().GetProperty("Candidate")!.GetValue(live));

        Assert.Equal("hjpevs.", candidate.OriginalText);
        Assert.Equal("розумію", candidate.ConvertedText);
    }

    [Fact]
    public void TryBuildAddressBarLiveTokenCandidate_SkipsCorrectUkrainianLastWord()
    {
        Assert.Null(InvokeTryBuildAddressBarLiveTokenCandidateOrNull("ghbdsn справи", "chrome"));
    }

    [Theory]
    [InlineData("ghbdsn", "ghbdsn", true)]
    [InlineData(" ghbdsn ", "ghbdsn", false)]
    [InlineData("foo ghbdsn", "ghbdsn", false)]
    [InlineData("foo/ghbdsn", "ghbdsn", false)]
    public void IsExactSelectedAddressBarToken_DetectsWhenSelectionWouldEatPrefix(
        string selectedText,
        string liveToken,
        bool expected)
    {
        Assert.Equal(expected, InvokeIsExactSelectedAddressBarToken(selectedText, liveToken));
    }

    [Theory]
    [InlineData("f", "а")]
    [InlineData("F", "А")]
    [InlineData("d", "в")]
    [InlineData("D", "В")]
    public void TryBuildRecentSwitchSingleLetterCandidate_ConvertsAAfterUaToEnSwitch(
        string original,
        string converted)
    {
        var context = new ForegroundContext(
            new IntPtr(101),
            new IntPtr(202),
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_RenderWidgetHostHWND");
        var snapshot = (WordSnapshot)CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_RenderWidgetHostHWND",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "EN",
            liveWord: "",
            visibleWord: original,
            wordEn: original,
            wordUa: converted,
            analysisWordEn: original,
            analysisWordUa: converted,
            rawDebug: "test",
            originalDisplay: original,
            techDetail: "keys=1 rec=0 drop=0 seq=0 lay=EN",
            approxWordLength: 1,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 0);
        object recentSwitch = CreateAutoSwitchState(
            CorrectionDirection.UaToEn,
            DateTime.UtcNow.AddSeconds(-1),
            context);

        CorrectionCandidate? candidate = InvokeTryBuildRecentSwitchSingleLetterCandidate(
            snapshot,
            approxWordLength: 1,
            recentSwitch,
            DateTime.UtcNow);

        Assert.NotNull(candidate);
        Assert.Equal(original, candidate!.OriginalText);
        Assert.Equal(converted, candidate.ConvertedText);
        Assert.Equal(CorrectionDirection.EnToUa, candidate.Direction);
    }

    [Fact]
    public void TryBuildRecentSwitchSingleLetterCandidate_RequiresRecentUaToEnSwitch()
    {
        var context = new ForegroundContext(
            new IntPtr(101),
            new IntPtr(202),
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_RenderWidgetHostHWND");
        var snapshot = (WordSnapshot)CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_RenderWidgetHostHWND",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "EN",
            liveWord: "",
            visibleWord: "f",
            wordEn: "f",
            wordUa: "а",
            analysisWordEn: "f",
            analysisWordUa: "а",
            rawDebug: "test",
            originalDisplay: "f",
            techDetail: "keys=1 rec=0 drop=0 seq=0 lay=EN",
            approxWordLength: 1,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 0);
        object wrongDirection = CreateAutoSwitchState(
            CorrectionDirection.EnToUa,
            DateTime.UtcNow.AddSeconds(-1),
            context);
        object stale = CreateAutoSwitchState(
            CorrectionDirection.UaToEn,
            DateTime.UtcNow.AddMinutes(-1),
            context);

        Assert.Null(InvokeTryBuildRecentSwitchSingleLetterCandidate(snapshot, 1, wrongDirection, DateTime.UtcNow));
        Assert.Null(InvokeTryBuildRecentSwitchSingleLetterCandidate(snapshot, 1, stale, DateTime.UtcNow));
    }

    [Fact]
    public void TryBuildRecentSwitchSingleLetterCandidate_DoesNotEnableGeneralOneLetterCorrection()
    {
        var context = new ForegroundContext(
            new IntPtr(101),
            new IntPtr(202),
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_RenderWidgetHostHWND");
        var snapshot = (WordSnapshot)CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_RenderWidgetHostHWND",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "EN",
            liveWord: "",
            visibleWord: "g",
            wordEn: "g",
            wordUa: "п",
            analysisWordEn: "g",
            analysisWordUa: "п",
            rawDebug: "test",
            originalDisplay: "d",
            techDetail: "keys=1 rec=0 drop=0 seq=0 lay=EN",
            approxWordLength: 1,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 0);
        object recentSwitch = CreateAutoSwitchState(
            CorrectionDirection.UaToEn,
            DateTime.UtcNow.AddSeconds(-1),
            context);

        Assert.Null(InvokeTryBuildRecentSwitchSingleLetterCandidate(snapshot, 1, recentSwitch, DateTime.UtcNow));
        Assert.Null(InvokeTryBuildRecentSwitchSingleLetterCandidate(snapshot, 2, recentSwitch, DateTime.UtcNow));
    }

    [Fact]
    public void RecentSwitchSingleLetterSurface_AllowsBrowserPageButBlocksAddressBar()
    {
        var pageContext = new ForegroundContext(
            new IntPtr(101),
            new IntPtr(202),
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_RenderWidgetHostHWND");
        var addressContext = new ForegroundContext(
            new IntPtr(101),
            new IntPtr(202),
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_OmniboxView");
        object pageSurface = CreateBrowserSurfaceSnapshot(
            hasWritableValuePattern: true,
            className: "",
            elementName: "Comment",
            unsafeCustomEditorLike: false);
        object addressSurface = CreateBrowserSurfaceSnapshot(
            hasWritableValuePattern: true,
            className: "Chrome_OmniboxView",
            elementName: "Address and search bar",
            unsafeCustomEditorLike: false,
            ariaProperties: "type=url");

        Assert.True(InvokeIsRecentSwitchSingleLetterSurface(pageContext, pageSurface));
        Assert.False(InvokeIsRecentSwitchSingleLetterSurface(addressContext, addressSurface));
    }

    [Fact]
    public void ClipboardLiveRead_DoesNotCompareCorruptedBufferBeforeReplace()
    {
        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_RenderWidgetHostHWND");
        var snapshot = (WordSnapshot)CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_RenderWidgetHostHWND",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "EN",
            liveWord: "",
            visibleWord: "ghbdsn",
            wordEn: "yuiop[",
            wordUa: "нгшщзх",
            analysisWordEn: "yuiop[",
            analysisWordUa: "нгшщзх",
            rawDebug: "y/y(s15/v59) u/u(s16/v55) i/i(s17/v49) o/o(s18/v4F) p/p(s19/v50) [/[...]",
            originalDisplay: "yuiop[",
            techDetail: "keys=6 rec=0 drop=0 seq=3 lay=EN",
            approxWordLength: 6,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 3);
        var decision = new CandidateDecision(
            Candidate: null,
            Source: CandidateSource.None,
            RequiresLiveRuntimeRead: true,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "needs live read");
        var plan = new ReplacementPlan(
            snapshot,
            decision,
            ReplacementSafetyProfile.BrowserBestEffort,
            ReplacementExecutionPath.ClipboardAssistedSelection,
            "ClipboardSelectionTransaction",
            "browser-address-bar-live-clipboard-fallback");
        var liveCandidate = new CorrectionCandidate(
            "ghbdsn",
            "привіт",
            CorrectionDirection.EnToUa,
            0.95,
            "live clipboard");

        Assert.Equal(string.Empty, InvokeGetClipboardPreconditionExpectedWord(plan));
        Assert.Equal(string.Empty, InvokeGetClipboardBeforeReplaceExpectedWord(plan, liveCandidate));
    }

    [Fact]
    public void ClipboardBufferedCandidate_StillComparesExpectedOriginalBeforeReplace()
    {
        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_RenderWidgetHostHWND");
        var snapshot = (WordSnapshot)CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_RenderWidgetHostHWND",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "EN",
            liveWord: "",
            visibleWord: "ghbdsn",
            wordEn: "ghbdsn",
            wordUa: "привіт",
            analysisWordEn: "ghbdsn",
            analysisWordUa: "привіт",
            rawDebug: "test",
            originalDisplay: "ghbdsn",
            techDetail: "keys=6 rec=0 drop=0 seq=0 lay=EN",
            approxWordLength: 6,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 0);
        var candidate = new CorrectionCandidate(
            "ghbdsn",
            "привіт",
            CorrectionDirection.EnToUa,
            0.95,
            "buffer");
        var decision = new CandidateDecision(
            candidate,
            CandidateSource.PrimaryHeuristics,
            RequiresLiveRuntimeRead: false,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "buffer candidate");
        var plan = new ReplacementPlan(
            snapshot,
            decision,
            ReplacementSafetyProfile.BrowserBestEffort,
            ReplacementExecutionPath.ClipboardAssistedSelection,
            "ClipboardSelectionTransaction",
            "browser fallback");

        Assert.Equal("ghbdsn", InvokeGetClipboardPreconditionExpectedWord(plan));
        Assert.Equal("ghbdsn", InvokeGetClipboardBeforeReplaceExpectedWord(plan, candidate));
    }

    [Fact]
    public void AddressBarBufferedBackspace_RequiresVisibleWordVerification()
    {
        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_OmniboxView");
        var snapshot = (WordSnapshot)CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_OmniboxView",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "EN",
            liveWord: "",
            visibleWord: "ghbdsn",
            wordEn: "ghbdsn",
            wordUa: "привіт",
            analysisWordEn: "ghbdsn",
            analysisWordUa: "привіт",
            rawDebug: "test",
            originalDisplay: "ghbdsn",
            techDetail: "keys=6 rec=0 drop=0 seq=0 lay=EN",
            approxWordLength: 6,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 0);
        var candidate = new CorrectionCandidate(
            "ghbdsn",
            "привіт",
            CorrectionDirection.EnToUa,
            0.95,
            "buffer");
        var decision = new CandidateDecision(
            candidate,
            CandidateSource.PrimaryHeuristics,
            RequiresLiveRuntimeRead: false,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "buffer candidate");
        var plan = new ReplacementPlan(
            snapshot,
            decision,
            ReplacementSafetyProfile.NativeSafe,
            ReplacementExecutionPath.BrowserAddressBarBufferedBackspace,
            "SendInput",
            "browser address bar buffered");

        Assert.True(InvokeShouldVerifyNativeVisibleWord(plan));
    }

    [Theory]
    [InlineData(nameof(ReplacementExecutionPath.NativeSelectionTransaction))]
    [InlineData(nameof(ReplacementExecutionPath.BrowserAddressBarBufferedBackspace))]
    [InlineData(nameof(ReplacementExecutionPath.BrowserAddressBarLiveTokenBackspace))]
    [InlineData(nameof(ReplacementExecutionPath.BrowserPageLiveBackspace))]
    public void ShouldExecuteImmediately_BackspaceSendInputPaths_DoNotWaitForThreadPool(string pathName)
    {
        var path = Enum.Parse<ReplacementExecutionPath>(pathName);
        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_RenderWidgetHostHWND");
        var snapshot = (WordSnapshot)CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_RenderWidgetHostHWND",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "EN",
            liveWord: "",
            visibleWord: "руддщ",
            wordEn: "hello",
            wordUa: "руддщ",
            analysisWordEn: "hello",
            analysisWordUa: "руддщ",
            rawDebug: "test",
            originalDisplay: "руддщ",
            techDetail: "keys=5 rec=0 drop=0 seq=0 lay=UA",
            approxWordLength: 5,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 0);
        var candidate = new CorrectionCandidate(
            "руддщ",
            "hello",
            CorrectionDirection.UaToEn,
            0.95,
            "buffer");
        var decision = new CandidateDecision(
            candidate,
            CandidateSource.PrimaryHeuristics,
            RequiresLiveRuntimeRead: false,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "buffer candidate");
        var plan = new ReplacementPlan(
            snapshot,
            decision,
            ReplacementSafetyProfile.NativeSafe,
            path,
            "SendInput",
            "immediate path");

        Assert.True(InvokeShouldExecuteImmediately(plan));
    }

    [Fact]
    public void ShouldExecuteImmediately_ClipboardFallback_RemainsAsyncIfReenabled()
    {
        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_RenderWidgetHostHWND");
        var snapshot = (WordSnapshot)CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_RenderWidgetHostHWND",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "EN",
            liveWord: "",
            visibleWord: "",
            wordEn: "",
            wordUa: "",
            analysisWordEn: "",
            analysisWordUa: "",
            rawDebug: "test",
            originalDisplay: "",
            techDetail: "keys=0 rec=0 drop=0 seq=0 lay=EN",
            approxWordLength: 0,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 0);
        var decision = new CandidateDecision(
            Candidate: null,
            Source: CandidateSource.None,
            RequiresLiveRuntimeRead: true,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "needs live read");
        var plan = new ReplacementPlan(
            snapshot,
            decision,
            ReplacementSafetyProfile.BrowserBestEffort,
            ReplacementExecutionPath.ClipboardAssistedSelection,
            "ClipboardSelectionTransaction",
            "legacy clipboard fallback");

        Assert.False(InvokeShouldExecuteImmediately(plan));
    }

    [Fact]
    public void ShouldExecuteImmediately_BrowserPageExactClipboard_DoesNotRunInline()
    {
        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_RenderWidgetHostHWND");
        var snapshot = (WordSnapshot)CreateWordSnapshot(
            context,
            processName: "chrome",
            controlClass: "Chrome_RenderWidgetHostHWND",
            windowClass: "Chrome_WidgetWin_1",
            layoutTag: "UA",
            liveWord: "",
            visibleWord: "руддщ",
            wordEn: "hello",
            wordUa: "руддщ",
            analysisWordEn: "hello",
            analysisWordUa: "руддщ",
            rawDebug: "test",
            originalDisplay: "руддщ",
            techDetail: "keys=5 rec=0 drop=0 seq=0 lay=UA",
            approxWordLength: 5,
            recoveryCount: 0,
            droppedKeyCount: 0,
            sequentialScanCount: 0);
        var decision = new CandidateDecision(
            new CorrectionCandidate("руддщ", "hello", CorrectionDirection.UaToEn, 0.95, "buffer"),
            CandidateSource.PrimaryHeuristics,
            RequiresLiveRuntimeRead: true,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: "browser page clipboard verifies live slice");
        var plan = new ReplacementPlan(
            snapshot,
            decision,
            ReplacementSafetyProfile.BrowserBestEffort,
            ReplacementExecutionPath.ClipboardAssistedSelection,
            "ClipboardSelectionTransaction",
            "profile=BrowserBestEffort path=ClipboardAssistedSelection surface=browser-page-exact-clipboard");

        Assert.False(InvokeShouldExecuteImmediately(plan));
    }

    [Fact]
    public void ResolveOriginalDisplay_PrefersLiveWord_WhenAvailable()
    {
        string original = InvokeResolveOriginalDisplay(
            liveWord: "привіт",
            visibleWord: "ghbdsn",
            wordEn: "ghbdsn",
            wordUa: "привіт",
            layoutTag: "UA");

        Assert.Equal("привіт", original);
    }

    [Fact]
    public void ResolveOriginalDisplay_PrefersCurrentLayoutWord_OverVisibleWord()
    {
        string original = InvokeResolveOriginalDisplay(
            liveWord: string.Empty,
            visibleWord: "sdfghj",
            wordEn: "ghbdsn",
            wordUa: "привіт",
            layoutTag: "EN");

        Assert.Equal("ghbdsn", original);
    }

    [Fact]
    public void ResolveOriginalDisplay_FallsBackToVisibleWord_WhenLayoutWordIsMissing()
    {
        string original = InvokeResolveOriginalDisplay(
            liveWord: string.Empty,
            visibleWord: "actual-visible",
            wordEn: string.Empty,
            wordUa: "привіт",
            layoutTag: "EN");

        Assert.Equal("actual-visible", original);
    }

    [Fact]
    public void ClassifyReplacementSafetyProfile_UnsafeBrowserEditorSurface_SkipsClipboardFallback()
    {
        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("ClassifyReplacementSafetyProfile", BindingFlags.NonPublic | BindingFlags.Static)!;

        object profile = method.Invoke(
            null,
            [true, false, false, true])!;

        Assert.Equal(ReplacementSafetyProfile.UnsafeSkip, profile);
    }

    [Fact]
    public void ClassifyReplacementSafetyProfile_UnsafeBrowserEditorSurface_SkipsInSafeOnlyMode()
    {
        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("ClassifyReplacementSafetyProfile", BindingFlags.NonPublic | BindingFlags.Static)!;

        object profile = method.Invoke(
            null,
            [true, false, true, true])!;

        Assert.Equal(ReplacementSafetyProfile.UnsafeSkip, profile);
    }

    [Fact]
    public void ClassifyReplacementSafetyProfile_BrowserWithoutWritableValuePattern_SkipsClipboardFallback()
    {
        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("ClassifyReplacementSafetyProfile", BindingFlags.NonPublic | BindingFlags.Static)!;

        object profile = method.Invoke(
            null,
            [true, false, false, false])!;

        Assert.Equal(ReplacementSafetyProfile.UnsafeSkip, profile);
    }

    [Fact]
    public void BuildUndoInputs_ReleasesModifiers_BackspacesReplacementAndRetypesOriginal()
    {
        var inputs = InvokeBuildUndoInputs("привіт", "ghbdsn");

        int modifierCount = NativeMethods.BuildModifierReleaseInputs().Length;
        int expectedCount = modifierCount + (("привіт".Length + 1) * 2) + ("ghbdsn".Length * 2);

        Assert.Equal(expectedCount, inputs.Length);
        if (modifierCount > 0)
        {
            Assert.Equal(NativeMethods.VK_SHIFT, inputs[0].U.ki.wVk);
            Assert.Equal(NativeMethods.KEYEVENTF_KEYUP, inputs[0].U.ki.dwFlags);
        }

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

    private static object InvokeBuildAddressBarRoute(
        CandidateDecision decision,
        bool hasWritableValuePattern,
        bool exactAddressBarSurface)
    {
        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("BuildAddressBarRoute", BindingFlags.NonPublic | BindingFlags.Static)!;

        return method.Invoke(null, [decision, hasWritableValuePattern, exactAddressBarSurface])!;
    }

    private static bool InvokeShouldUseBrowserWordTokenRoute(
        WordSnapshot snapshot,
        CandidateDecision decision,
        object surface)
    {
        Type surfaceType = typeof(AutoModeHandler).GetNestedType("BrowserSurfaceSnapshot", BindingFlags.NonPublic)!;
        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod(
                "ShouldUseBrowserWordTokenRoute",
                BindingFlags.NonPublic | BindingFlags.Static,
                [typeof(WordSnapshot), typeof(CandidateDecision), surfaceType])!;

        return (bool)method.Invoke(null, [snapshot, decision, surface])!;
    }

    private static object CreateBrowserSurfaceSnapshot(
        bool hasWritableValuePattern,
        string className,
        string elementName,
        bool unsafeCustomEditorLike,
        string ariaProperties = "",
        bool hasTextPattern = false)
    {
        Type surfaceType = typeof(AutoModeHandler).GetNestedType("BrowserSurfaceSnapshot", BindingFlags.NonPublic)!;
        return Activator.CreateInstance(
            surfaceType,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args:
            [
                true,
                hasWritableValuePattern,
                hasTextPattern,
                "ControlType.Edit",
                "edit",
                className,
                string.Empty,
                elementName,
                ariaProperties,
                unsafeCustomEditorLike,
                $"test-surface class={className} name={elementName} aria={ariaProperties}"
            ],
            culture: null)!;
    }

    private static bool InvokeShouldUseBrowserPageClipboardFallback(
        WordSnapshot snapshot,
        CandidateDecision decision,
        object surface)
    {
        Type surfaceType = typeof(AutoModeHandler).GetNestedType("BrowserSurfaceSnapshot", BindingFlags.NonPublic)!;
        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod(
                "ShouldUseBrowserPageClipboardFallback",
                BindingFlags.NonPublic | BindingFlags.Static,
                [typeof(WordSnapshot), typeof(CandidateDecision), surfaceType])!;

        return (bool)method.Invoke(null, [snapshot, decision, surface])!;
    }

    private static string InvokeResolveOriginalDisplay(
        string liveWord,
        string visibleWord,
        string wordEn,
        string wordUa,
        string layoutTag)
    {
        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("ResolveOriginalDisplay", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (string)method.Invoke(null, [liveWord, visibleWord, wordEn, wordUa, layoutTag])!;
    }

    private static bool InvokeCanUseElectronBufferedFallback(CandidateDecision decision)
    {
        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("CanUseElectronBufferedFallback", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (bool)method.Invoke(null, [decision])!;
    }

    private static bool InvokeCanUseBufferedBrowserBackspace(CandidateDecision decision)
    {
        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("CanUseBufferedBrowserBackspace", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (bool)method.Invoke(null, [decision])!;
    }

    private static bool InvokeShouldExecuteImmediately(ReplacementPlan plan)
    {
        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("ShouldExecuteImmediately", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (bool)method.Invoke(null, [plan])!;
    }

    private static string InvokeGetClipboardPreconditionExpectedWord(ReplacementPlan plan)
    {
        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("GetClipboardPreconditionExpectedWord", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (string)method.Invoke(null, [plan])!;
    }

    private static string InvokeGetClipboardBeforeReplaceExpectedWord(ReplacementPlan plan, CorrectionCandidate candidate)
    {
        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("GetClipboardBeforeReplaceExpectedWord", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (string)method.Invoke(null, [plan, candidate])!;
    }

    private static bool InvokeShouldVerifyNativeVisibleWord(ReplacementPlan plan)
    {
        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("ShouldVerifyNativeVisibleWord", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (bool)method.Invoke(null, [plan])!;
    }

    private static object InvokeTryBuildAddressBarLiveTokenCandidate(string text, string? processName)
    {
        object? result = InvokeTryBuildAddressBarLiveTokenCandidateOrNull(text, processName);
        Assert.NotNull(result);
        return result;
    }

    private static object? InvokeTryBuildAddressBarLiveTokenCandidateOrNull(string text, string? processName)
    {
        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("TryBuildAddressBarLiveTokenCandidate", BindingFlags.NonPublic | BindingFlags.Static)!;

        return method.Invoke(null, [text, processName]);
    }

    private static bool InvokeIsExactSelectedAddressBarToken(string selectedText, string liveToken)
    {
        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("IsExactSelectedAddressBarToken", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (bool)method.Invoke(null, [selectedText, liveToken])!;
    }

    private static CorrectionCandidate? InvokeTryBuildRecentSwitchSingleLetterCandidate(
        WordSnapshot snapshot,
        int approxWordLength,
        object recentSwitch,
        DateTime nowUtc)
    {
        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod("TryBuildRecentSwitchSingleLetterCandidate", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (CorrectionCandidate?)method.Invoke(null, [snapshot, approxWordLength, recentSwitch, nowUtc]);
    }

    private static bool InvokeIsRecentSwitchSingleLetterSurface(ForegroundContext context, object surface)
    {
        Type surfaceType = typeof(AutoModeHandler).GetNestedType("BrowserSurfaceSnapshot", BindingFlags.NonPublic)!;
        MethodInfo method = typeof(AutoModeHandler)
            .GetMethod(
                "IsRecentSwitchSingleLetterSurface",
                BindingFlags.NonPublic | BindingFlags.Static,
                [typeof(ForegroundContext), surfaceType])!;

        return (bool)method.Invoke(null, [context, surface])!;
    }

    private static object CreateAutoSwitchState(
        CorrectionDirection direction,
        DateTime atUtc,
        ForegroundContext context)
    {
        Type autoSwitchType = typeof(AutoModeHandler).GetNestedType("AutoSwitchState", BindingFlags.NonPublic)!;
        return Activator.CreateInstance(
            autoSwitchType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                direction,
                atUtc,
                context.ProcessId,
                context.Hwnd,
                context.FocusedControlHwnd
            ],
            culture: null)!;
    }

    private static object CreateWordSnapshot(
        ForegroundContext context,
        string processName,
        string controlClass,
        string windowClass,
        string layoutTag,
        string liveWord,
        string visibleWord,
        string wordEn,
        string wordUa,
        string analysisWordEn,
        string analysisWordUa,
        string rawDebug,
        string originalDisplay,
        string techDetail,
        int approxWordLength,
        int recoveryCount,
        int droppedKeyCount,
        int sequentialScanCount,
        string? vkWordEn = null,
        string? vkWordUa = null,
        string visibleTrailingSuffix = "")
    {
        var engineAssembly = typeof(AutoModeHandler).Assembly;
        Type bufferQualityType = engineAssembly.GetType("Switcher.Engine.BufferQualitySnapshot", throwOnError: true)!;
        object bufferQuality = Activator.CreateInstance(
            bufferQualityType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { approxWordLength, recoveryCount, droppedKeyCount, sequentialScanCount },
            culture: null)!;

        Type wordSnapshotType = engineAssembly.GetType("Switcher.Engine.WordSnapshot", throwOnError: true)!;
        return Activator.CreateInstance(
            wordSnapshotType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                DateTime.UtcNow,
                context,
                null,
                processName,
                controlClass,
                windowClass,
                string.Empty,
                layoutTag,
                (uint)0,
                liveWord,
                visibleWord,
                visibleTrailingSuffix,
                wordEn,
                wordUa,
                vkWordEn ?? wordEn,
                vkWordUa ?? wordUa,
                analysisWordEn,
                analysisWordUa,
                vkWordEn ?? analysisWordEn,
                vkWordUa ?? analysisWordUa,
                rawDebug,
                originalDisplay,
                techDetail,
                bufferQuality
            ],
            culture: null)!;
    }
}

public class SendInputAdapterRegressionTests
{
    [Fact]
    public void ShouldUseWordSelectionReplace_DisabledForBrowserAddressBar()
    {
        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "chrome",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_OmniboxView");

        Assert.False(InvokeShouldUseWordSelectionReplace(context));
    }

    [Fact]
    public void ShouldUseWordSelectionReplace_DisabledForElectronApps()
    {
        var context = new ForegroundContext(
            IntPtr.Zero,
            IntPtr.Zero,
            "element",
            123,
            "Chrome_WidgetWin_1",
            "Chrome_RenderWidgetHostHWND");

        Assert.False(InvokeShouldUseWordSelectionReplace(context));
    }

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

    private static bool InvokeShouldUseWordSelectionReplace(ForegroundContext context)
    {
        MethodInfo method = typeof(SendInputAdapter)
            .GetMethod("ShouldUseWordSelectionReplace", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (bool)method.Invoke(null, [context])!;
    }
}

public class UIAutomationTargetAdapterRegressionTests
{
    [Theory]
    [InlineData("chrome", "Chrome_OmniboxView", "", "")]
    [InlineData("msedge", "", "", "Address and search bar")]
    [InlineData("brave", "", "", "Адресний рядок")]
    public void LooksLikeBrowserAddressBar_RecognizesBrowserAddressBars(
        string processName,
        string className,
        string automationId,
        string elementName)
    {
        Assert.True(InvokeLooksLikeBrowserAddressBar(processName, className, automationId, elementName));
    }

    [Theory]
    [InlineData("chrome", "", "", "Comment")]
    [InlineData("notepad", "Chrome_OmniboxView", "", "")]
    public void LooksLikeBrowserAddressBar_DoesNotMatchNormalInputs(
        string processName,
        string className,
        string automationId,
        string elementName)
    {
        Assert.False(InvokeLooksLikeBrowserAddressBar(processName, className, automationId, elementName));
    }

    private static bool InvokeLooksLikeBrowserAddressBar(
        string? processName,
        string className,
        string automationId,
        string elementName)
    {
        MethodInfo method = typeof(UIAutomationTargetAdapter)
            .GetMethod("LooksLikeBrowserAddressBar", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (bool)method.Invoke(null, [processName, className, automationId, elementName])!;
    }
}

public class AutoModeHandlerGoogleDocsRegressionTests
{
    // ─── Positive: browser + Google Docs / Sheets / Slides title ────────────────

    [Theory]
    [InlineData("chrome",   "My doc - Google Docs - Google Chrome")]
    [InlineData("msedge",   "Q4 numbers - Google Sheets - Microsoft\u00a0Edge")]
    [InlineData("brave",    "All hands - Google Slides - Brave")]
    [InlineData("opera",    "Something - Google Docs - Opera")]
    [InlineData("vivaldi",  "Untitled document - Google Docs")]
    [InlineData("CHROME",   "Document - Google Docs - Google Chrome")] // case-insensitive process
    public void IsGoogleDocsTitle_RecognizesGoogleEditorsInChromiumBrowsers(string processName, string title)
    {
        Assert.True(AutoModeHandler.IsGoogleDocsTitle(processName, title),
            $"Expected Google Docs surface for process='{processName}', title='{title}'.");
    }

    // ─── Positive: localized title markers ──────────────────────────────────────

    [Theory]
    [InlineData("chrome", "Мій документ - Google Документи - Google Chrome")]
    [InlineData("chrome", "Звіт - Google Таблиці - Google Chrome")]
    [InlineData("chrome", "Презентация - Google Презентации - Google Chrome")]
    [InlineData("msedge", "Бюджет - Google Таблицы - Microsoft Edge")]
    public void IsGoogleDocsTitle_RecognizesLocalizedTitles(string processName, string title)
    {
        Assert.True(AutoModeHandler.IsGoogleDocsTitle(processName, title),
            $"Expected localized Google Docs surface for '{title}'.");
    }

    // ─── Negative: non-Google browser tab ───────────────────────────────────────

    [Theory]
    [InlineData("chrome", "Stack Overflow - How to fix bug - Google Chrome")]
    [InlineData("chrome", "YouTube - Google Chrome")]
    [InlineData("chrome", "GitHub - username/repo - Google Chrome")]
    [InlineData("chrome", "google search results - Google Chrome")]
    [InlineData("msedge", "Bing - Microsoft Edge")]
    public void IsGoogleDocsTitle_DoesNotMatchNonGoogleTabs(string processName, string title)
    {
        Assert.False(AutoModeHandler.IsGoogleDocsTitle(processName, title),
            $"Did not expect Google Docs surface for '{title}'.");
    }

    // ─── Negative: non-browser process with Docs-like title ─────────────────────

    [Theory]
    [InlineData("notepad",      "Google Docs — user notes")]
    [InlineData("winword",      "Google Docs comparison.docx - Word")]
    [InlineData("code",         "readme.md - Google Docs integration - Visual Studio Code")]
    [InlineData("claude",       "Chat about Google Docs - Claude")]
    public void IsGoogleDocsTitle_DoesNotMatchNonBrowserProcesses(string processName, string title)
    {
        Assert.False(AutoModeHandler.IsGoogleDocsTitle(processName, title),
            $"Only Chromium browsers should route to Google Docs path. Got process='{processName}'.");
    }

    // ─── Negative: edge cases ───────────────────────────────────────────────────

    [Theory]
    [InlineData(null,    "Document - Google Docs")]
    [InlineData("",      "Document - Google Docs")]
    [InlineData("  ",    "Document - Google Docs")]
    [InlineData("chrome", null)]
    [InlineData("chrome", "")]
    [InlineData("chrome", "   ")]
    public void IsGoogleDocsTitle_HandlesNullAndEmptyInputsSafely(string? processName, string? title)
    {
        Assert.False(AutoModeHandler.IsGoogleDocsTitle(processName, title));
    }
}

public class AutoModeHandlerTargetedSurfaceRegressionTests
{
    [Theory]
    [InlineData("chrome", "Some video - YouTube - Google Chrome")]
    [InlineData("msedge", "Watch later - YouTube - Microsoft Edge")]
    [InlineData("brave", "youtube.com/watch?v=abc - Brave")]
    public void IsYouTubeTitle_RecognizesChromiumYouTubeTabs(string processName, string title)
    {
        Assert.True(AutoModeHandler.IsYouTubeTitle(processName, title));
    }

    [Theory]
    [InlineData("notepad", "Some video - YouTube")]
    [InlineData("chrome", "GitHub - Google Chrome")]
    [InlineData("code", "YouTube notes.md - Visual Studio Code")]
    [InlineData(null, "YouTube - Google Chrome")]
    [InlineData("chrome", null)]
    public void IsYouTubeTitle_DoesNotMatchOutsideBrowserYouTubeTabs(string? processName, string? title)
    {
        Assert.False(AutoModeHandler.IsYouTubeTitle(processName, title));
    }

    [Theory]
    [InlineData("onenote", "Work Log - OneNote")]
    [InlineData("ONENOTE", "Notes")]
    [InlineData("ApplicationFrameHost", "Work Log 2025 - 2026 - OneNote")]
    public void IsOneNoteTitle_RecognizesDesktopAndStoreOneNote(string processName, string title)
    {
        Assert.True(AutoModeHandler.IsOneNoteTitle(processName, title));
    }

    [Theory]
    [InlineData("chrome", "OneNote web clipper - Google Chrome")]
    [InlineData("notepad", "meeting notes.txt")]
    [InlineData(null, null)]
    public void IsOneNoteTitle_DoesNotMatchUnrelatedWindows(string? processName, string? title)
    {
        Assert.False(AutoModeHandler.IsOneNoteTitle(processName, title));
    }
}
