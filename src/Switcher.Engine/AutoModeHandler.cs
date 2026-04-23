using System.Runtime.InteropServices;
using System.Windows.Automation;
using Switcher.Core;
using Switcher.Infrastructure;

namespace Switcher.Engine;

public class AutoModeHandler
{
    private const int BrowserValuePatternStartDelayMs = 70;
    private const int ElectronUiaStartDelayMs = 45;
    private const int ClipboardAssistedStartDelayMs = 150;

    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "brave", "opera", "vivaldi",
        "codex", "element", "slack", "discord", "teams"
    };

    private static readonly HashSet<string> BrowserAddressBarProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "brave", "opera", "vivaldi"
    };

    private static readonly HashSet<string> DefaultElectronUiaProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "code",
        "code - insiders",
        "codex",
        "cursor",
        "element",
        "element-desktop",
        "vscodium",
        "windsurf"
    };

    private static readonly HashSet<string> BrowserLikeWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Chrome_WidgetWin_1", "MozillaWindowClass", "Chrome_RenderWidgetHostHWND",
        "ApplicationFrameWindow", "Windows.UI.Core.CoreWindow"
    };

    private static readonly string[] UnsafeBrowserMarkers =
    [
        "contenteditable",
        "monaco",
        "codemirror",
        "ace_editor",
        "prosemirror",
        "lexical",
        "tox-edit-area",
        "editor"
    ];

    private static readonly string[] BrowserAddressBarNameMarkers =
    [
        "address and search bar",
        "address bar",
        "search or enter address",
        "search with google or enter address",
        "адресний рядок",
        "адресная строка"
    ];

    private readonly ForegroundContextProvider _contextProvider;
    private readonly TextTargetCoordinator _coordinator;
    private readonly ExclusionManager _exclusions;
    private readonly DiagnosticsLogger _diagnostics;
    private readonly SettingsManager _settings;
    private readonly KeyboardObserver _observer;

    private record UndoState(string OriginalText, string ReplacementText, CorrectionDirection Direction, uint DelimiterVk);

    private sealed record BrowserSurfaceSnapshot(
        bool HasFocusedElement,
        bool HasWritableValuePattern,
        bool HasTextPattern,
        string ControlType,
        string LocalizedControlType,
        string ClassName,
        string AutomationId,
        string ElementName,
        bool UnsafeCustomEditorLike,
        string Summary);

    private UndoState? _lastCorrection;
    private int _autoOperationInFlight;

    public AutoModeHandler(
        ForegroundContextProvider contextProvider,
        TextTargetCoordinator coordinator,
        ExclusionManager exclusions,
        DiagnosticsLogger diagnostics,
        SettingsManager settings,
        KeyboardObserver observer)
    {
        _contextProvider = contextProvider;
        _coordinator = coordinator;
        _exclusions = exclusions;
        _diagnostics = diagnostics;
        _settings = settings;
        _observer = observer;
    }

    public void OnWordBoundary(int approxWordLength)
    {
        if (!_settings.Current.AutoModeEnabled)
            return;

        _lastCorrection = null;

        WordSnapshot? snapshot = CaptureWordSnapshot(approxWordLength);
        if (snapshot is null)
            return;

        if (approxWordLength < 2 || (snapshot.AnalysisWordEN.Length < 2 && snapshot.AnalysisWordUA.Length < 2))
        {
            if (!snapshot.BufferQuality.NeedsLiveDiscovery || approxWordLength < 3)
            {
                LogSkip(snapshot, "AutoMode", $"Too short {snapshot.TechDetail} [{snapshot.RawDebug}]");
                return;
            }
        }

        if (_exclusions.IsExcluded(snapshot.ProcessName))
        {
            LogSkip(snapshot, "AutoMode", $"Process is excluded {snapshot.TechDetail}");
            return;
        }

        string? unsafeContextReason = AutoContextGuards.GetUnsafeAutoCorrectionReason(
            snapshot.ExpectedOriginalWord,
            snapshot.CurrentSentence,
            snapshot.ProcessName);
        if (unsafeContextReason is not null)
        {
            LogSkip(snapshot, "ContextGuard", $"{unsafeContextReason} {snapshot.TechDetail}");
            return;
        }

        if (IsExcludedAutoWord(
                snapshot.WordEN,
                snapshot.WordUA,
                snapshot.VkWordEN,
                snapshot.VkWordUA,
                snapshot.AnalysisWordEN,
                snapshot.AnalysisWordUA,
                snapshot.AnalysisVkEN,
                snapshot.AnalysisVkUA))
        {
            LogSkip(snapshot, "AutoMode", $"Word is excluded from Auto Mode {snapshot.TechDetail}");
            return;
        }

        CandidateDecision decision = ResolveCandidateDecision(snapshot, BuildCandidateDecision(snapshot));
        if (!decision.ShouldReplace && !decision.RequiresLiveRuntimeRead)
        {
            LogSkip(snapshot, "CandidateDecision", decision.Reason);
            return;
        }

        ReplacementPlan plan = BuildReplacementPlan(snapshot, decision);
        if (plan.SafetyProfile == ReplacementSafetyProfile.UnsafeSkip)
        {
            LogSkip(snapshot, plan.AdapterName, plan.Reason);
            return;
        }

        if (!TryBeginAutoOperation(snapshot, plan))
            return;

        if (ShouldSuppressDelimiter(plan))
            _observer.SuppressCurrentDelimiter();

        _ = Task.Run(() => ExecuteReplacementPlan(plan));
    }

    public void OnUndoRequested()
    {
        if (!_settings.Current.UndoOnBackspace)
            return;

        var undo = _lastCorrection;
        if (undo is null)
            return;

        _lastCorrection = null;
        _observer.SuppressCurrentBackspace();

        var context = _contextProvider.GetCurrent();
        Task.Run(() =>
        {
            try
            {
                var inputs = BuildUndoInputs(undo.ReplacementText, undo.OriginalText);
                uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());

                if (context != null)
                {
                    NativeMethods.SwitchInputLanguage(
                        context.Hwnd,
                        context.FocusedControlHwnd,
                        toUkrainian: undo.Direction != CorrectionDirection.EnToUa);
                }

                Thread.Sleep(30);
                ReinjectDelimiter(undo.DelimiterVk);

                _diagnostics.Log(
                    context?.ProcessName ?? "?",
                    context?.FocusedControlClass ?? "?",
                    "SendInput",
                    true,
                    OperationType.AutoMode,
                    undo.ReplacementText,
                    undo.OriginalText,
                    sent == (uint)inputs.Length ? DiagnosticResult.Replaced : DiagnosticResult.Error,
                    "Undo via Backspace");
            }
            catch
            {
                // Undo remains best-effort.
            }
        });
    }

    private WordSnapshot? CaptureWordSnapshot(int approxWordLength)
    {
        ForegroundContext? context = _contextProvider.GetCurrent();
        if (context is null)
        {
            _diagnostics.Log("?", "?", "AutoMode", false, OperationType.AutoMode,
                _observer.CurrentWordEN + " | " + _observer.CurrentWordUA, null,
                DiagnosticResult.Error, "Context is null");
            return null;
        }

        string wordEN = _observer.CurrentWordEN;
        string wordUA = _observer.CurrentWordUA;
        string rawDebug = _observer.LastVkDebug;
        int recCount = _observer.LastScanRecoveryCount;
        int droppedKeys = _observer.LastDroppedKeyCount;
        int seqScans = _observer.LastSequentialScanCount;
        string vkEN = _observer.CurrentWordEN_VkOnly;
        string vkUA = _observer.CurrentWordUA_VkOnly;
        string visibleWord = _observer.CurrentVisibleWord;
        var (adapter, support) = _coordinator.Resolve(context);
        string liveWord = support == TargetSupport.Unsupported || adapter is null
            ? string.Empty
            : (adapter.TryGetLastWord(context) ?? string.Empty);
        string currentSentence = support == TargetSupport.Unsupported || adapter is null
            ? string.Empty
            : (adapter.TryGetCurrentSentence(context) ?? string.Empty);
        string layoutTag = _observer.LastLayoutWasUkrainian ? "UA" : "EN";
        string visibleTrailingSuffix = ResolveVisibleTrailingPunctuation(
            liveWord,
            visibleWord,
            layoutTag == "UA" ? wordUA : wordEN,
            layoutTag == "UA" ? wordEN : wordUA);

        string analysisWordEN = StripVisibleSuffixFromInterpretation(wordEN, visibleTrailingSuffix);
        string analysisWordUA = StripVisibleSuffixFromInterpretation(wordUA, visibleTrailingSuffix);
        string analysisVkEN = StripVisibleSuffixFromInterpretation(vkEN, visibleTrailingSuffix);
        string analysisVkUA = StripVisibleSuffixFromInterpretation(vkUA, visibleTrailingSuffix);

        string originalDisplay = ResolveOriginalDisplay(liveWord, visibleWord, wordEN, wordUA, layoutTag);
        string techDetail = $"keys={approxWordLength} rec={recCount} drop={droppedKeys} seq={seqScans} lay={layoutTag}"
                            + (vkEN != wordEN ? $" vkEn={vkEN}" : "")
                            + (vkUA != wordUA ? $" vkUa={vkUA}" : "");

        return new WordSnapshot(
            DateTime.UtcNow,
            context,
            adapter,
            context.ProcessName,
            context.FocusedControlClass,
            context.WindowClass,
            currentSentence,
            layoutTag,
            _observer.LastDelimiterVk,
            liveWord,
            visibleWord,
            visibleTrailingSuffix,
            wordEN,
            wordUA,
            vkEN,
            vkUA,
            analysisWordEN,
            analysisWordUA,
            analysisVkEN,
            analysisVkUA,
            rawDebug,
            originalDisplay,
            techDetail,
            new BufferQualitySnapshot(approxWordLength, recCount, droppedKeys, seqScans));
    }

    private CandidateDecision BuildCandidateDecision(WordSnapshot snapshot)
    {
        CorrectionCandidate? candidate = TryEvaluateCandidate(snapshot.AnalysisWordEN, snapshot.VisibleTrailingSuffix);
        CandidateSource source = candidate is null ? CandidateSource.None : CandidateSource.PrimaryHeuristics;

        if (candidate is null)
        {
            candidate = TryEvaluateCandidate(snapshot.AnalysisWordUA, snapshot.VisibleTrailingSuffix);
            source = candidate is null ? CandidateSource.None : CandidateSource.PrimaryHeuristics;
        }

        if (candidate is null && !snapshot.BufferQuality.HasChromeLikeGarbage
            && (snapshot.BufferQuality.RecoveryCount > 0 || snapshot.BufferQuality.DroppedKeyCount > 0))
        {
            if (!string.Equals(snapshot.AnalysisVkEN, snapshot.AnalysisWordEN, StringComparison.Ordinal))
                candidate = TryEvaluateCandidate(snapshot.AnalysisVkEN, snapshot.VisibleTrailingSuffix);

            if (candidate is null && !string.Equals(snapshot.AnalysisVkUA, snapshot.AnalysisWordUA, StringComparison.Ordinal))
                candidate = TryEvaluateCandidate(snapshot.AnalysisVkUA, snapshot.VisibleTrailingSuffix);

            if (candidate is not null)
                source = CandidateSource.VkFallback;
        }

        if (candidate is null)
        {
            bool shouldTryLiveRuntimeRead = snapshot.BufferQuality.NeedsLiveDiscovery || IsBrowserLikeContext(snapshot.Context);
            return snapshot.BufferQuality.NeedsLiveDiscovery
                ? new CandidateDecision(
                    Candidate: null,
                    Source: CandidateSource.None,
                    RequiresLiveRuntimeRead: true,
                    SelectorFeatures: null,
                    LearnedDecision: null,
                    Reason: $"No reliable buffered candidate -> live runtime read {snapshot.TechDetail} [{snapshot.RawDebug}]")
                : new CandidateDecision(
                    Candidate: null,
                    Source: CandidateSource.None,
                    RequiresLiveRuntimeRead: shouldTryLiveRuntimeRead,
                    SelectorFeatures: null,
                    LearnedDecision: null,
                    Reason: shouldTryLiveRuntimeRead
                        ? $"No buffered conversion -> live runtime read {snapshot.TechDetail} [{snapshot.RawDebug}]"
                        : $"No conversion {snapshot.TechDetail} [{snapshot.RawDebug}]");
        }

        return ApplyLearnedSelector(snapshot, candidate, source, candidate.Reason);
    }

    private CandidateDecision ResolveCandidateDecision(WordSnapshot snapshot, CandidateDecision decision)
    {
        if (decision.Candidate is not null || IsBrowserLikeContext(snapshot.Context))
            return decision;

        if (!decision.RequiresLiveRuntimeRead)
            return decision;

        if (string.IsNullOrWhiteSpace(snapshot.LiveWord) || snapshot.LiveWord.Length < 2)
        {
            return decision with
            {
                RequiresLiveRuntimeRead = false,
                Reason = $"{decision.Reason}; native live read unavailable"
            };
        }

        CandidateDecision liveDecision = BuildLiveRuntimeCandidateDecision(snapshot, snapshot.LiveWord);
        return liveDecision with
        {
            Reason = $"{liveDecision.Reason}; native live read fallback"
        };
    }

    private CandidateDecision ApplyLearnedSelector(
        WordSnapshot snapshot,
        CorrectionCandidate candidate,
        CandidateSource source,
        string baseReason)
    {
        SelectorFeatureVector? features = CorrectionHeuristics.BuildSelectorFeatures(
            candidate.OriginalText,
            candidate.ConvertedText,
            candidate.Direction);

        if (!ShouldScoreWithLearnedSelector(candidate, features))
        {
            return new CandidateDecision(
                candidate,
                source,
                RequiresLiveRuntimeRead: false,
                SelectorFeatures: features,
                LearnedDecision: null,
                Reason: $"{baseReason} {snapshot.TechDetail} [{snapshot.RawDebug}]");
        }

        LearnedSelectorDecision? learnedDecision = features is null ? null : LearnedSelectorRuntime.Evaluate(features);
        if (features is not null && learnedDecision is not null)
        {
            _diagnostics.LogSelectorExample(new SelectorDiagnosticExample(
                DateTime.UtcNow,
                snapshot.ProcessName,
                snapshot.ControlClass,
                candidate.OriginalText,
                candidate.ConvertedText,
                candidate.Direction.ToString(),
                _settings.Current.LearnedSelectorGateEnabled ? "Gate" : "Shadow",
                learnedDecision.Accept,
                learnedDecision.Probability,
                features.ToFeatureMap()));
        }

        if (learnedDecision is null)
        {
            return new CandidateDecision(
                candidate,
                source,
                RequiresLiveRuntimeRead: false,
                SelectorFeatures: features,
                LearnedDecision: null,
                Reason: $"{baseReason} {snapshot.TechDetail} [{snapshot.RawDebug}]");
        }

        string selectorReason = $"{baseReason} {learnedDecision.Reason}";
        if (_settings.Current.LearnedSelectorGateEnabled && !learnedDecision.Accept)
        {
            return new CandidateDecision(
                Candidate: null,
                Source: source,
                RequiresLiveRuntimeRead: false,
                SelectorFeatures: features,
                LearnedDecision: learnedDecision,
                Reason: $"{selectorReason} gate=reject {snapshot.TechDetail} [{snapshot.RawDebug}]");
        }

        string stage = _settings.Current.LearnedSelectorGateEnabled ? "gate=pass" : "shadow";
        return new CandidateDecision(
            candidate,
            source,
            RequiresLiveRuntimeRead: false,
            SelectorFeatures: features,
            LearnedDecision: learnedDecision,
            Reason: $"{selectorReason} {stage} {snapshot.TechDetail} [{snapshot.RawDebug}]");
    }

    private ReplacementPlan BuildReplacementPlan(WordSnapshot snapshot, CandidateDecision decision)
    {
        bool browserLike = IsBrowserLikeContext(snapshot.Context);
        if (!browserLike)
        {
            return new ReplacementPlan(
                snapshot,
                decision,
                ReplacementSafetyProfile.NativeSafe,
                ReplacementExecutionPath.NativeSelectionTransaction,
                "SendInput",
                $"profile={ReplacementSafetyProfile.NativeSafe} path={ReplacementExecutionPath.NativeSelectionTransaction}");
        }

        // Electron-specific path: UIA read + Backspace+Unicode replace (no Shift+Left selection, no race).
        // Gated behind a user opt-in setting so it can be tested without affecting default behaviour.
        if (ShouldUseElectronUiaPath(snapshot.Context.ProcessName))
        {
            return new ReplacementPlan(
                snapshot,
                decision,
                ReplacementSafetyProfile.ElectronUiaSafe,
                ReplacementExecutionPath.ElectronUiaBackspaceReplace,
                "ElectronUia",
                $"profile={ReplacementSafetyProfile.ElectronUiaSafe} path={ReplacementExecutionPath.ElectronUiaBackspaceReplace} process={snapshot.ProcessName}");
        }

        BrowserSurfaceSnapshot surface = InspectBrowserSurface();
        if (IsBrowserAddressBarSurface(snapshot.Context, surface))
        {
            var (addressBarProfile, path, adapterName, reason) = BuildAddressBarRoute(decision, surface.HasWritableValuePattern);
            return new ReplacementPlan(snapshot, decision, addressBarProfile, path, adapterName, reason);
        }

        ReplacementSafetyProfile profile = ClassifyReplacementSafetyProfile(
            isBrowserLikeContext: true,
            hasWritableValuePattern: surface.HasWritableValuePattern,
            safeOnlyMode: _settings.Current.SafeOnlyAutoMode,
            unsafeCustomSurface: surface.UnsafeCustomEditorLike);

        return profile switch
        {
            ReplacementSafetyProfile.NativeSafe => new ReplacementPlan(
                snapshot,
                decision,
                ReplacementSafetyProfile.NativeSafe,
                ReplacementExecutionPath.NativeSelectionTransaction,
                "SendInput",
                $"profile={ReplacementSafetyProfile.NativeSafe} path={ReplacementExecutionPath.NativeSelectionTransaction} surface={surface.Summary}"),
            ReplacementSafetyProfile.BrowserValuePatternSafe => new ReplacementPlan(
                snapshot,
                decision,
                profile,
                ReplacementExecutionPath.BrowserValuePattern,
                "UIAutomationTargetAdapter",
                $"profile={profile} path={ReplacementExecutionPath.BrowserValuePattern} surface={surface.Summary}"),
            ReplacementSafetyProfile.BrowserBestEffort => new ReplacementPlan(
                snapshot,
                decision,
                profile,
                ReplacementExecutionPath.ClipboardAssistedSelection,
                "ClipboardSelectionTransaction",
                $"profile={profile} path={ReplacementExecutionPath.ClipboardAssistedSelection} surface={surface.Summary}"),
            _ => new ReplacementPlan(
                snapshot,
                decision,
                ReplacementSafetyProfile.UnsafeSkip,
                ReplacementExecutionPath.UnsafeSkip,
                "AutoMode",
                _settings.Current.SafeOnlyAutoMode
                    ? "profile=UnsafeSkip reason=safe-only toggle forced browser fallback off"
                    : $"profile=UnsafeSkip reason=custom browser/editor surface without exact-slice verification surface={surface.Summary}")
        };
    }

    private async Task ExecuteReplacementPlan(ReplacementPlan plan)
    {
        try
        {
            switch (plan.ExecutionPath)
            {
                case ReplacementExecutionPath.NativeSelectionTransaction:
                    ExecuteNativeReplacementTransaction(plan);
                    break;
                case ReplacementExecutionPath.BrowserValuePattern:
                    await ExecuteBrowserValuePatternReplacement(plan);
                    break;
                case ReplacementExecutionPath.ClipboardAssistedSelection:
                    await ExecuteClipboardAssistedReplacement(plan);
                    break;
                case ReplacementExecutionPath.ElectronUiaBackspaceReplace:
                    await ExecuteElectronUiaReplacement(plan);
                    break;
                default:
                    LogSkip(plan.Snapshot, plan.AdapterName, plan.Reason);
                    break;
            }
        }
        finally
        {
            EndAutoOperation();
        }
    }

    private void ExecuteNativeReplacementTransaction(ReplacementPlan plan)
    {
        var snapshot = plan.Snapshot;
        var candidate = plan.Decision.Candidate;
        if (candidate is null)
        {
            LogSkip(snapshot, plan.AdapterName, "Native transaction skipped: no candidate");
            TryReinjectDelimiter(plan);
            return;
        }

        if (TryAbortPreconditions(snapshot, candidate.OriginalText, "Native transaction", out string abortReason))
        {
            LogSkip(snapshot, plan.AdapterName, $"Native transaction aborted: {abortReason}");
            TryReinjectDelimiter(plan);
            return;
        }

        string replacementCore = TrimLiteralTrailingSuffix(candidate.ConvertedText, snapshot.VisibleTrailingSuffix);
        string originalCore = TrimLiteralTrailingSuffix(candidate.OriginalText, snapshot.VisibleTrailingSuffix);
        int eraseOverride = Math.Max(snapshot.BufferQuality.ApproxWordLength - snapshot.VisibleTrailingSuffix.Length, originalCore.Length);
        var inputs = BuildAutoReplacementInputs(originalCore, replacementCore, snapshot.VisibleTrailingSuffix, eraseOverride);
        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        bool success = sent == (uint)inputs.Length;

        _diagnostics.Log(
            snapshot.ProcessName,
            snapshot.ControlClass,
            plan.AdapterName,
            true,
            OperationType.AutoMode,
            candidate.OriginalText,
            candidate.ConvertedText,
            success ? DiagnosticResult.Replaced : DiagnosticResult.Error,
            success
                ? $"{plan.Reason}; {plan.Decision.Reason}"
                : $"{plan.Reason}; SendInput returned {sent}/{inputs.Length}");

        if (success)
            FinalizeSuccessfulReplacement(snapshot, candidate);

        Thread.Sleep(30);
        TryReinjectDelimiter(plan);
    }

    private async Task ExecuteBrowserValuePatternReplacement(ReplacementPlan plan)
    {
        long startCount = _observer.UserKeyDownCounter;
        try
        {
            var snapshot = plan.Snapshot;
            bool useCachedExactSlice = CanUseCachedBrowserValuePatternReplace(plan);
            await Task.Delay(BrowserValuePatternStartDelayMs);
            if (!useCachedExactSlice && _observer.UserKeyDownCounter != startCount)
            {
                LogSkip(snapshot, plan.AdapterName, "Browser ValuePattern aborted: user kept typing before async replace started");
                return;
            }

            if (TryAbortPreconditions(
                    snapshot,
                    string.Empty,
                    "Browser ValuePattern",
                    out string abortReason,
                    allowPostDelimiterInteraction: useCachedExactSlice))
            {
                LogSkip(snapshot, plan.AdapterName, $"Browser ValuePattern aborted: {abortReason}");
                return;
            }

            var adapter = snapshot.ReadAdapter as UIAutomationTargetAdapter ?? new UIAutomationTargetAdapter();
            if (adapter.CanHandle(snapshot.Context) != TargetSupport.Full)
            {
                LogSkip(snapshot, plan.AdapterName, "Browser ValuePattern aborted: ValuePattern is unavailable");
                return;
            }

            string? actualWord = useCachedExactSlice
                ? snapshot.LiveWord
                : adapter.TryGetLastWord(snapshot.Context);

            if (!useCachedExactSlice && _observer.UserKeyDownCounter != startCount)
            {
                LogSkip(snapshot, plan.AdapterName, "Browser ValuePattern aborted: user kept typing after live word read");
                return;
            }

            if (string.IsNullOrWhiteSpace(actualWord) || actualWord.Length < 2)
            {
                if (IsAddressBarBrowserValuePatternPlan(plan))
                {
                    LogSkip(snapshot, plan.AdapterName, "Browser ValuePattern aborted: unable to read current address bar word");
                    return;
                }

                _diagnostics.Log(snapshot.ProcessName, snapshot.ControlClass, plan.AdapterName, true, OperationType.AutoMode,
                    snapshot.OriginalDisplay, null, DiagnosticResult.Skipped, "Browser ValuePattern aborted: unable to read current word -> Fallback to NativeSafe");
                
                ExecuteNativeReplacementTransaction(new ReplacementPlan(
                    snapshot, plan.Decision, ReplacementSafetyProfile.NativeSafe, ReplacementExecutionPath.NativeSelectionTransaction, "SendInput", "Fallback from BrowserValuePatternSafe"));
                return;
            }

            CorrectionCandidate? candidate;
            if (plan.Decision.RequiresLiveRuntimeRead
                || !ExactSliceMatchesExpected(actualWord, plan.Decision.Candidate?.OriginalText ?? snapshot.ExpectedOriginalWord))
            {
                var liveDecision = BuildLiveRuntimeCandidateDecision(snapshot, actualWord);
                candidate = liveDecision.Candidate;
                if (candidate is null)
                {
                    LogSkip(snapshot, plan.AdapterName, $"Browser ValuePattern skipped: {liveDecision.Reason}", actualWord);
                    return;
                }
            }
            else
            {
                candidate = plan.Decision.Candidate;
            }

            bool success = adapter.TryReplaceLastWord(snapshot.Context, candidate!.ConvertedText);
            _diagnostics.Log(
                snapshot.ProcessName,
                snapshot.ControlClass,
                plan.AdapterName,
                true,
                OperationType.AutoMode,
                actualWord,
                candidate.ConvertedText,
                success ? DiagnosticResult.Replaced : DiagnosticResult.Error,
                success ? $"{plan.Reason}; {plan.Decision.Reason}" : $"{plan.Reason}; exact-slice replace failed");

            if (success)
                FinalizeSuccessfulReplacement(snapshot, candidate);

            Thread.Sleep(30);
        }
        finally
        {
            TryReinjectDelimiter(plan);
        }
    }

    private async Task ExecuteElectronUiaReplacement(ReplacementPlan plan)
    {
        long startCount = _observer.UserKeyDownCounter;
        var snapshot = plan.Snapshot;

        await Task.Delay(ElectronUiaStartDelayMs);
        if (_observer.UserKeyDownCounter != startCount)
        {
            LogSkip(snapshot, plan.AdapterName, "Electron UIA aborted: user kept typing before async replace started");
            TryReinjectDelimiter(plan);
            return;
        }

        if (TryAbortPreconditions(snapshot, string.Empty, "Electron UIA", out string abortReason))
        {
            LogSkip(snapshot, plan.AdapterName, $"Electron UIA aborted: {abortReason}");
            TryReinjectDelimiter(plan);
            return;
        }

        // 1. Read live word via UIA (TextPattern or ValuePattern)
        var adapter = new UIAutomationTargetAdapter();
        string? actualWord = adapter.TryGetLastWord(snapshot.Context);
        CorrectionCandidate? candidate = null;
        bool usedBufferedFallback = false;

        bool hasReliableLiveWord = !string.IsNullOrWhiteSpace(actualWord) && actualWord.Length >= 2;
        if (!hasReliableLiveWord)
        {
            if (!CanUseElectronBufferedFallback(plan.Decision))
            {
                LogSkip(snapshot, plan.AdapterName,
                    "Electron: UIA unavailable or word too short - auto-correction skipped to avoid selection race");
                TryReinjectDelimiter(plan);
                return;
            }

            candidate = plan.Decision.Candidate!;
            actualWord = candidate.OriginalText;
            usedBufferedFallback = true;
        }

        string currentWord = actualWord!;

        if (_observer.UserKeyDownCounter != startCount)
        {
            LogSkip(snapshot, plan.AdapterName, "Electron UIA aborted: user kept typing after live word read");
            TryReinjectDelimiter(plan);
            return;
        }

        // 2. Evaluate candidate from live word, or use the buffered candidate when UIA is unavailable.
        if (!usedBufferedFallback && (plan.Decision.RequiresLiveRuntimeRead
            || !ExactSliceMatchesExpected(currentWord, plan.Decision.Candidate?.OriginalText ?? snapshot.ExpectedOriginalWord)))
        {
            var liveDecision = BuildLiveRuntimeCandidateDecision(snapshot, currentWord);
            candidate = liveDecision.Candidate;
            if (candidate is null)
            {
                LogSkip(snapshot, plan.AdapterName, $"Electron UIA skipped: {liveDecision.Reason}", currentWord);
                TryReinjectDelimiter(plan);
                return;
            }
        }
        else
        {
            candidate ??= plan.Decision.Candidate;
        }

        if (candidate is null)
        {
            LogSkip(snapshot, plan.AdapterName, "Electron UIA skipped: no conversion candidate", currentWord);
            TryReinjectDelimiter(plan);
            return;
        }

        // 3. Final abort check
        if (TryAbortPreconditions(snapshot, candidate.OriginalText, "Electron UIA before replace", out abortReason))
        {
            LogSkip(snapshot, plan.AdapterName, $"Electron UIA aborted: {abortReason}");
            TryReinjectDelimiter(plan);
            return;
        }

        // 4. Build Backspace+Unicode inputs (no Shift+Left → no selection → no race condition)
        string replacementCore = TrimLiteralTrailingSuffix(candidate.ConvertedText, snapshot.VisibleTrailingSuffix);
        string originalCore = TrimLiteralTrailingSuffix(candidate.OriginalText, snapshot.VisibleTrailingSuffix);
        int suffixLen = snapshot.VisibleTrailingSuffix.Length;
        // Subtract suffix: suffix chars count in ApproxWordLength but are handled by separate Left/Right moves.
        int eraseCount = Math.Max(originalCore.Length, snapshot.BufferQuality.ApproxWordLength - suffixLen);

        var modRelease = NativeMethods.BuildModifierReleaseInputs();
        int totalInputs = modRelease.Length
            + (suffixLen * 2)       // Left past suffix
            + (eraseCount * 2)      // Backspace×N
            + (replacementCore.Length * 2)  // Unicode chars
            + (suffixLen * 2);      // Right back past suffix
        var inputs = new NativeMethods.INPUT[totalInputs];
        int idx = 0;

        foreach (var inp in modRelease) inputs[idx++] = inp;

        for (int i = 0; i < suffixLen; i++)
        {
            inputs[idx++] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_LEFT, keyUp: false);
            inputs[idx++] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_LEFT, keyUp: true);
        }

        for (int i = 0; i < eraseCount; i++)
        {
            inputs[idx++] = NativeMethods.MakeKeyInput(NativeMethods.VK_BACK, keyUp: false);
            inputs[idx++] = NativeMethods.MakeKeyInput(NativeMethods.VK_BACK, keyUp: true);
        }

        foreach (char c in replacementCore)
        {
            inputs[idx++] = NativeMethods.MakeUnicodeInput(c, keyUp: false);
            inputs[idx++] = NativeMethods.MakeUnicodeInput(c, keyUp: true);
        }

        for (int i = 0; i < suffixLen; i++)
        {
            inputs[idx++] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_RIGHT, keyUp: false);
            inputs[idx++] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_RIGHT, keyUp: true);
        }

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        bool success = sent == (uint)inputs.Length;

        _diagnostics.Log(
            snapshot.ProcessName,
            snapshot.ControlClass,
            plan.AdapterName,
            true,
            OperationType.AutoMode,
            currentWord,
            candidate.ConvertedText,
            success ? DiagnosticResult.Replaced : DiagnosticResult.Error,
            success
                ? $"{plan.Reason}; {(usedBufferedFallback ? "buffer fallback after UIA unavailable; " : string.Empty)}{plan.Decision.Reason}"
                : $"{plan.Reason}; SendInput returned {sent}/{inputs.Length}");

        if (success)
            FinalizeSuccessfulReplacement(snapshot, candidate);

        Thread.Sleep(30);
        TryReinjectDelimiter(plan);
    }

    private async Task ExecuteClipboardAssistedReplacement(ReplacementPlan plan)
    {
        long startCount = _observer.UserKeyDownCounter;
        var snapshot = plan.Snapshot;
        await Task.Delay(ClipboardAssistedStartDelayMs);
        if (_observer.UserKeyDownCounter != startCount)
        {
            LogSkip(snapshot, plan.AdapterName, "Browser best-effort aborted: user kept typing before async replace started");
            return;
        }

        string expectedWord = plan.Decision.Candidate?.OriginalText ?? snapshot.ExpectedOriginalWord;
        if (TryAbortPreconditions(snapshot, expectedWord, "Browser best-effort preflight", out string abortReason))
        {
            LogSkip(snapshot, plan.AdapterName, $"Browser best-effort aborted: {abortReason}");
            TryReinjectDelimiter(plan);
            return;
        }

        int selectionLength = DetermineSelectionLength(snapshot, plan.Decision);
        if (selectionLength < 2)
        {
            LogSkip(snapshot, plan.AdapterName, "Browser best-effort aborted: selection length is too short");
            TryReinjectDelimiter(plan);
            return;
        }

        string? savedClipboard = NativeMethods.GetClipboardText();
        NativeMethods.SetClipboardText(null);

        try
        {
            NativeMethods.INPUT[] selectionInputs = BuildSelectionInputs(selectionLength);
            NativeMethods.SendInput((uint)selectionInputs.Length, selectionInputs, Marshal.SizeOf<NativeMethods.INPUT>());
            await Task.Delay(90);

            NativeMethods.INPUT[] copyInputs = BuildCopyInputs();
            NativeMethods.SendInput((uint)copyInputs.Length, copyInputs, Marshal.SizeOf<NativeMethods.INPUT>());

            string? selectedText = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                await Task.Delay(attempt == 0 ? 120 : 160);
                if (TryAbortPreconditions(snapshot, expectedWord, "Browser best-effort clipboard wait", out abortReason))
                {
                    LogSkip(snapshot, plan.AdapterName, $"Browser best-effort aborted: {abortReason}");
                    CollapseSelectionRight();
                    TryReinjectDelimiter(plan);
                    return;
                }

                selectedText = NativeMethods.GetClipboardText();
                if (!string.IsNullOrEmpty(selectedText))
                    break;
            }

            CollapseSelectionRight();

            if (string.IsNullOrEmpty(selectedText))
            {
                LogSkip(snapshot, plan.AdapterName, "Browser best-effort aborted: clipboard read is stale or empty");
                TryReinjectDelimiter(plan);
                return;
            }

            string liveWord = selectedText.Trim();
            if (string.IsNullOrWhiteSpace(liveWord) || liveWord.Length < 2)
            {
                LogSkip(snapshot, plan.AdapterName, "Browser best-effort aborted: copied slice is too short");
                TryReinjectDelimiter(plan);
                return;
            }

            CorrectionCandidate? candidate;
            if (plan.Decision.RequiresLiveRuntimeRead)
            {
                var liveDecision = BuildLiveRuntimeCandidateDecision(snapshot, liveWord);
                candidate = liveDecision.Candidate;
                if (candidate is null)
                {
                    LogSkip(snapshot, plan.AdapterName, $"Browser best-effort skipped: {liveDecision.Reason}", liveWord);
                    TryReinjectDelimiter(plan);
                    return;
                }
            }
            else
            {
                candidate = plan.Decision.Candidate;
                if (!ExactSliceMatchesExpected(liveWord, candidate?.OriginalText ?? expectedWord))
                {
                    LogSkip(snapshot, plan.AdapterName, "Browser best-effort aborted: exact-slice mismatch skip");
                    TryReinjectDelimiter(plan);
                    return;
                }
            }

            if (TryAbortPreconditions(snapshot, candidate!.OriginalText, "Browser best-effort before replace", out abortReason))
            {
                LogSkip(snapshot, plan.AdapterName, $"Browser best-effort aborted: {abortReason}");
                TryReinjectDelimiter(plan);
                return;
            }

            NativeMethods.INPUT[] reselectInputs = BuildSelectionInputs(liveWord.Length);
            NativeMethods.SendInput((uint)reselectInputs.Length, reselectInputs, Marshal.SizeOf<NativeMethods.INPUT>());
            await Task.Delay(50);

            var typeInputs = BuildUnicodeInputs(candidate.ConvertedText);
            uint sent = NativeMethods.SendInput((uint)typeInputs.Length, typeInputs, Marshal.SizeOf<NativeMethods.INPUT>());
            bool success = sent == (uint)typeInputs.Length;

            _diagnostics.Log(
                snapshot.ProcessName,
                snapshot.ControlClass,
                plan.AdapterName,
                true,
                OperationType.AutoMode,
                liveWord,
                candidate.ConvertedText,
                success ? DiagnosticResult.Replaced : DiagnosticResult.Error,
                success ? $"{plan.Reason}; {plan.Decision.Reason}" : $"{plan.Reason}; SendInput returned {sent}/{typeInputs.Length}");

            if (success)
                FinalizeSuccessfulReplacement(snapshot, candidate);

            Thread.Sleep(30);
            TryReinjectDelimiter(plan);
        }
        finally
        {
            NativeMethods.SetClipboardText(savedClipboard);
        }
    }

    private CandidateDecision BuildLiveRuntimeCandidateDecision(WordSnapshot snapshot, string actualWord)
    {
        if (_exclusions.IsWordExcluded(actualWord))
        {
            return new CandidateDecision(
                Candidate: null,
                Source: CandidateSource.LiveRuntimeRead,
                RequiresLiveRuntimeRead: false,
                SelectorFeatures: null,
                LearnedDecision: null,
                Reason: $"Live runtime word is excluded from Auto Mode [{actualWord}]");
        }

        string toggledWord = KeyboardLayoutMap.ToggleLayoutText(actualWord, out _);
        string visibleSuffix = ResolveVisibleTrailingPunctuation(actualWord, toggledWord);
        string analysis = TrimTrailingChars(actualWord, visibleSuffix.Length);
        CorrectionCandidate? candidate = string.IsNullOrWhiteSpace(analysis)
            ? null
            : CorrectionHeuristics.Evaluate(analysis, CorrectionMode.Auto);

        if (candidate is not null)
            candidate = ApplyVisibleTrailingPunctuation(candidate, visibleSuffix);

        if (candidate is null)
        {
            return new CandidateDecision(
                Candidate: null,
                Source: CandidateSource.LiveRuntimeRead,
                RequiresLiveRuntimeRead: false,
                SelectorFeatures: null,
                LearnedDecision: null,
                Reason: $"Live runtime read found no conversion [{actualWord}]");
        }

        return ApplyLearnedSelector(snapshot, candidate, CandidateSource.LiveRuntimeRead, candidate.Reason);
    }

    private bool TryBeginAutoOperation(WordSnapshot snapshot, ReplacementPlan plan)
    {
        if (Interlocked.CompareExchange(ref _autoOperationInFlight, 1, 0) == 0)
        {
            string preflightOriginal = plan.ExecutionPath == ReplacementExecutionPath.NativeSelectionTransaction
                ? snapshot.OriginalDisplay
                : string.Empty;
            _diagnostics.Log(
                snapshot.ProcessName,
                snapshot.ControlClass,
                plan.AdapterName,
                true,
                OperationType.AutoMode,
                preflightOriginal,
                null,
                DiagnosticResult.Skipped,
                $"Chosen {plan.Reason}");
            return true;
        }

        LogSkip(snapshot, plan.AdapterName, $"Skipped auto replacement: previous operation still in progress; {plan.Reason}");
        return false;
    }

    private void EndAutoOperation() => Volatile.Write(ref _autoOperationInFlight, 0);

    private void FinalizeSuccessfulReplacement(WordSnapshot snapshot, CorrectionCandidate candidate)
    {
        _observer.ClearBuffer();
        NativeMethods.SwitchInputLanguage(
            snapshot.Context.Hwnd,
            snapshot.Context.FocusedControlHwnd,
            toUkrainian: candidate.Direction == CorrectionDirection.EnToUa);
        _lastCorrection = new UndoState(candidate.OriginalText, candidate.ConvertedText, candidate.Direction, snapshot.DelimiterVk);
    }

    private bool TryAbortPreconditions(WordSnapshot snapshot, string expectedOriginal, string stage, out string reason)
        => TryAbortPreconditions(snapshot, expectedOriginal, stage, out reason, allowPostDelimiterInteraction: false);

    private bool TryAbortPreconditions(
        WordSnapshot snapshot,
        string expectedOriginal,
        string stage,
        out string reason,
        bool allowPostDelimiterInteraction)
    {
        if (!allowPostDelimiterInteraction && _observer.HasInteractionSinceLastDelimiter)
        {
            reason = $"{stage}: post-delimiter interaction happened before async replace executes";
            return true;
        }

        if ((DateTime.UtcNow - snapshot.CapturedAtUtc) > TimeSpan.FromMilliseconds(1200))
        {
            reason = $"{stage}: snapshot is stale";
            return true;
        }

        ForegroundContext? current = _contextProvider.GetCurrent();
        if (current is null)
        {
            reason = $"{stage}: focus changed";
            return true;
        }

        if (current.ProcessId != snapshot.Context.ProcessId
            || current.Hwnd != snapshot.Context.Hwnd
            || current.FocusedControlHwnd != snapshot.Context.FocusedControlHwnd)
        {
            reason = $"{stage}: focus changed";
            return true;
        }

        if (!string.IsNullOrWhiteSpace(expectedOriginal))
        {
            string visibleNearCaret = _observer.GetVisibleWordNearCaret(current);
            if (!string.IsNullOrWhiteSpace(visibleNearCaret)
                && !ExactSliceMatchesExpected(visibleNearCaret, expectedOriginal))
            {
                reason = $"{stage}: visible word no longer matches expected original";
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }

    private void TryReinjectDelimiter(ReplacementPlan plan)
    {
        if (!ShouldSuppressDelimiter(plan))
            return;

        ForegroundContext? current = _contextProvider.GetCurrent();
        if (current is null
            || current.ProcessId != plan.Snapshot.Context.ProcessId
            || current.Hwnd != plan.Snapshot.Context.Hwnd
            || current.FocusedControlHwnd != plan.Snapshot.Context.FocusedControlHwnd)
        {
            return;
        }

        ReinjectDelimiter(plan.Snapshot.DelimiterVk);
    }

    private static bool CanUseCachedBrowserValuePatternReplace(ReplacementPlan plan) =>
        plan.ExecutionPath == ReplacementExecutionPath.BrowserValuePattern
        && plan.Decision.Candidate is not null
        && !plan.Decision.RequiresLiveRuntimeRead
        && plan.Snapshot.ReadAdapter is UIAutomationTargetAdapter
        && !string.IsNullOrWhiteSpace(plan.Snapshot.LiveWord);

    private static string ResolveOriginalDisplay(
        string liveWord,
        string visibleWord,
        string wordEN,
        string wordUA,
        string layoutTag)
    {
        if (!string.IsNullOrWhiteSpace(liveWord))
            return liveWord;

        bool useUkrainianLayout = layoutTag.Equals("UA", StringComparison.OrdinalIgnoreCase);
        string preferredLayoutWord = useUkrainianLayout ? wordUA : wordEN;
        if (!string.IsNullOrWhiteSpace(preferredLayoutWord))
            return preferredLayoutWord;

        if (!string.IsNullOrWhiteSpace(visibleWord))
            return visibleWord;

        return useUkrainianLayout ? wordEN : wordUA;
    }

    private static bool IsAddressBarBrowserValuePatternPlan(ReplacementPlan plan) =>
        plan.ExecutionPath == ReplacementExecutionPath.BrowserValuePattern
        && plan.Reason.Contains("surface=browser-address-bar", StringComparison.Ordinal);

    private static bool CanUseElectronBufferedFallback(CandidateDecision decision) =>
        decision.Candidate is not null
        && !decision.RequiresLiveRuntimeRead;

    private static bool ShouldSuppressDelimiter(ReplacementPlan plan) =>
        !(plan.ExecutionPath == ReplacementExecutionPath.BrowserValuePattern
          && plan.Snapshot.DelimiterVk == NativeMethods.VK_SPACE);

    private static CorrectionCandidate? TryEvaluateCandidate(string text, string visibleTrailingSuffix)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
            return null;

        CorrectionCandidate? candidate = CorrectionHeuristics.Evaluate(text, CorrectionMode.Auto);
        return candidate is null
            ? null
            : ApplyVisibleTrailingPunctuation(candidate, visibleTrailingSuffix);
    }

    private static bool ShouldScoreWithLearnedSelector(CorrectionCandidate candidate, SelectorFeatureVector? features)
    {
        if (features is null)
            return false;

        if (candidate.Confidence < 0.74)
            return true;

        return candidate.Direction == CorrectionDirection.UaToEn
               || features.TargetDictionarySignal < 1.0
               || features.Delta < 0.34;
    }

    private static int DetermineSelectionLength(WordSnapshot snapshot, CandidateDecision decision)
    {
        string preferred = decision.Candidate?.OriginalText ?? snapshot.ExpectedOriginalWord;
        return preferred.Length >= 2 ? preferred.Length : snapshot.BufferQuality.ApproxWordLength;
    }

    private static NativeMethods.INPUT[] BuildSelectionInputs(int length)
    {
        var inputs = new NativeMethods.INPUT[2 + (length * 2)];
        int idx = 0;
        inputs[idx++] = NativeMethods.MakeKeyInput(NativeMethods.VK_SHIFT, keyUp: false);
        for (int i = 0; i < length; i++)
        {
            inputs[idx++] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_LEFT, keyUp: false);
            inputs[idx++] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_LEFT, keyUp: true);
        }
        inputs[idx++] = NativeMethods.MakeKeyInput(NativeMethods.VK_SHIFT, keyUp: true);
        return inputs;
    }

    private static NativeMethods.INPUT[] BuildCopyInputs()
    {
        var inputs = new NativeMethods.INPUT[4];
        inputs[0] = NativeMethods.MakeKeyInput(NativeMethods.VK_CONTROL, keyUp: false);
        inputs[1] = NativeMethods.MakeKeyInput(0x43, keyUp: false);
        inputs[2] = NativeMethods.MakeKeyInput(0x43, keyUp: true);
        inputs[3] = NativeMethods.MakeKeyInput(NativeMethods.VK_CONTROL, keyUp: true);
        return inputs;
    }

    private static NativeMethods.INPUT[] BuildUnicodeInputs(string text)
    {
        var inputs = new NativeMethods.INPUT[text.Length * 2];
        int idx = 0;
        foreach (char c in text)
        {
            inputs[idx++] = NativeMethods.MakeUnicodeInput(c, keyUp: false);
            inputs[idx++] = NativeMethods.MakeUnicodeInput(c, keyUp: true);
        }

        return inputs;
    }

    private static void CollapseSelectionRight()
    {
        var inputs = new NativeMethods.INPUT[2];
        inputs[0] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_RIGHT, keyUp: false);
        inputs[1] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_RIGHT, keyUp: true);
        NativeMethods.SendInput(2, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static BrowserSurfaceSnapshot InspectBrowserSurface()
    {
        try
        {
            AutomationElement? element = AutomationElement.FocusedElement;
            if (element is null)
            {
                return new BrowserSurfaceSnapshot(
                    false,
                    false,
                    false,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    false,
                    "uia=none");
            }

            bool hasWritableValuePattern = false;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp))
                hasWritableValuePattern = !((ValuePattern)vp).Current.IsReadOnly;

            bool hasTextPattern = element.TryGetCurrentPattern(TextPattern.Pattern, out _);
            string controlType = element.Current.ControlType?.ProgrammaticName ?? string.Empty;
            string localizedControlType = element.Current.LocalizedControlType ?? string.Empty;
            string className = element.Current.ClassName ?? string.Empty;
            string automationId = element.Current.AutomationId ?? string.Empty;
            string elementName = element.Current.Name ?? string.Empty;
            string merged = $"{controlType} {localizedControlType} {className} {automationId} {elementName}".ToLowerInvariant();
            bool customType = element.Current.ControlType == ControlType.Document
                              || element.Current.ControlType == ControlType.Custom
                              || element.Current.ControlType == ControlType.Pane;
            bool unsafeCustom = UnsafeBrowserMarkers.Any(marker => merged.Contains(marker, StringComparison.Ordinal))
                                || (!hasWritableValuePattern && customType);

            return new BrowserSurfaceSnapshot(
                HasFocusedElement: true,
                HasWritableValuePattern: hasWritableValuePattern,
                HasTextPattern: hasTextPattern,
                ControlType: controlType,
                LocalizedControlType: localizedControlType,
                ClassName: className,
                AutomationId: automationId,
                ElementName: elementName,
                UnsafeCustomEditorLike: unsafeCustom,
                Summary: $"uia=value={hasWritableValuePattern} text={hasTextPattern} type={controlType}/{localizedControlType} class={className} id={automationId} name={elementName}");
        }
        catch (Exception ex)
        {
            return new BrowserSurfaceSnapshot(
                false,
                false,
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                false,
                $"uia=error:{ex.GetType().Name}");
        }
    }

    private bool IsBrowserLikeContext(ForegroundContext context) =>
        BrowserProcesses.Contains(context.ProcessName)
        || ShouldUseElectronUiaPath(context.ProcessName)
        || BrowserLikeWindowClasses.Contains(context.FocusedControlClass)
        || BrowserLikeWindowClasses.Contains(context.WindowClass);

    private bool ShouldUseElectronUiaPath(string? processName) =>
        !string.IsNullOrWhiteSpace(processName)
        && ElectronProcessCatalog.IsElectronProcess(processName)
        && (_settings.Current.ElectronUiaPathEnabled
            || DefaultElectronUiaProcesses.Contains(processName));

    private static bool IsBrowserAddressBarSurface(ForegroundContext context, BrowserSurfaceSnapshot surface) =>
        IsBrowserAddressBarSurface(
            context.ProcessName,
            surface.ControlType,
            surface.LocalizedControlType,
            surface.ClassName,
            surface.AutomationId,
            surface.ElementName);

    private static bool IsBrowserAddressBarSurface(
        string? processName,
        string controlType,
        string localizedControlType,
        string className,
        string automationId,
        string elementName)
    {
        if (string.IsNullOrWhiteSpace(processName)
            || !BrowserAddressBarProcesses.Contains(processName.Trim()))
        {
            return false;
        }

        string merged = $"{controlType} {localizedControlType} {className} {automationId} {elementName}".ToLowerInvariant();
        if (merged.Contains("omnibox", StringComparison.Ordinal))
            return true;

        string normalizedName = (elementName ?? string.Empty).Trim().ToLowerInvariant();
        return BrowserAddressBarNameMarkers.Any(marker => string.Equals(normalizedName, marker, StringComparison.Ordinal));
    }

    private static (ReplacementSafetyProfile Profile, ReplacementExecutionPath Path, string AdapterName, string Reason) BuildAddressBarRoute(
        CandidateDecision decision,
        bool hasWritableValuePattern)
    {
        if (hasWritableValuePattern)
        {
            return (
                ReplacementSafetyProfile.BrowserValuePatternSafe,
                ReplacementExecutionPath.BrowserValuePattern,
                "UIAutomationTargetAdapter",
                $"profile={ReplacementSafetyProfile.BrowserValuePatternSafe} path={ReplacementExecutionPath.BrowserValuePattern} surface=browser-address-bar-value-pattern");
        }

        if (decision.Candidate is not null && !decision.RequiresLiveRuntimeRead)
        {
            return (
                ReplacementSafetyProfile.NativeSafe,
                ReplacementExecutionPath.NativeSelectionTransaction,
                "SendInput",
                $"profile={ReplacementSafetyProfile.NativeSafe} path={ReplacementExecutionPath.NativeSelectionTransaction} surface=browser-address-bar-no-clipboard");
        }

        return (
            ReplacementSafetyProfile.BrowserBestEffort,
            ReplacementExecutionPath.ClipboardAssistedSelection,
            "ClipboardSelectionTransaction",
            $"profile={ReplacementSafetyProfile.BrowserBestEffort} path={ReplacementExecutionPath.ClipboardAssistedSelection} surface=browser-address-bar-clipboard-fallback");
    }

    private static ReplacementSafetyProfile ClassifyReplacementSafetyProfile(
        bool isBrowserLikeContext,
        bool hasWritableValuePattern,
        bool safeOnlyMode,
        bool unsafeCustomSurface)
    {
        if (!isBrowserLikeContext)
            return ReplacementSafetyProfile.NativeSafe;

        if (unsafeCustomSurface)
            return ReplacementSafetyProfile.UnsafeSkip;

        if (hasWritableValuePattern)
            return ReplacementSafetyProfile.BrowserValuePatternSafe;

        if (safeOnlyMode)
            return ReplacementSafetyProfile.UnsafeSkip;

        return ReplacementSafetyProfile.BrowserBestEffort;
    }

    private static bool ExactSliceMatchesExpected(string selectedText, string expectedOriginal)
    {
        string actual = selectedText.Trim();
        string expected = expectedOriginal.Trim();
        if (string.Equals(actual, expected, StringComparison.Ordinal))
            return true;

        string actualCore = TrimBoundaryLiteralPunctuation(actual);
        if (!string.Equals(actualCore, actual, StringComparison.Ordinal)
            && string.Equals(actualCore, expected, StringComparison.Ordinal))
            return true;

        string expectedCore = TrimBoundaryLiteralPunctuation(expected);
        return !string.Equals(expectedCore, expected, StringComparison.Ordinal)
            && string.Equals(actual, expectedCore, StringComparison.Ordinal);
    }

    private static bool ShouldReinjectDelimiter(bool hasInteractionSinceDelimiter) =>
        !hasInteractionSinceDelimiter;

    private void LogSkip(WordSnapshot snapshot, string adapterName, string reason, string? originalOverride = null) =>
        _diagnostics.Log(
            snapshot.ProcessName,
            snapshot.ControlClass,
            adapterName,
            true,
            OperationType.AutoMode,
            string.IsNullOrWhiteSpace(originalOverride) ? snapshot.OriginalDisplay : originalOverride,
            null,
            DiagnosticResult.Skipped,
            reason);

    private static void ReinjectDelimiter(uint delimVk)
    {
        var inputs = new NativeMethods.INPUT[2];
        inputs[0] = NativeMethods.MakeKeyInput(delimVk, keyUp: false);
        inputs[1] = NativeMethods.MakeKeyInput(delimVk, keyUp: true);
        NativeMethods.SendInput(2, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT[] BuildUndoInputs(string replacementText, string restoreText)
    {
        int eraseCount = replacementText.Length + 1;
        var modifierRelease = NativeMethods.BuildModifierReleaseInputs();
        var inputs = new NativeMethods.INPUT[modifierRelease.Length + eraseCount * 2 + restoreText.Length * 2];
        int idx = 0;

        foreach (var input in modifierRelease)
            inputs[idx++] = input;

        for (int i = 0; i < eraseCount; i++)
        {
            inputs[idx++] = NativeMethods.MakeKeyInput(NativeMethods.VK_BACK, keyUp: false);
            inputs[idx++] = NativeMethods.MakeKeyInput(NativeMethods.VK_BACK, keyUp: true);
        }

        foreach (char c in restoreText)
        {
            inputs[idx++] = NativeMethods.MakeUnicodeInput(c, keyUp: false);
            inputs[idx++] = NativeMethods.MakeUnicodeInput(c, keyUp: true);
        }

        return inputs;
    }

    private bool IsExcludedAutoWord(params string?[] words)
    {
        foreach (string? word in words)
        {
            if (string.IsNullOrWhiteSpace(word))
                continue;

            if (_exclusions.IsWordExcluded(word))
                return true;
        }

        return false;
    }

    private static CorrectionCandidate ApplyVisibleTrailingPunctuation(CorrectionCandidate candidate, string visibleSuffix)
    {
        if (string.IsNullOrEmpty(visibleSuffix))
            return candidate;

        string original = candidate.OriginalText;
        if (!original.EndsWith(visibleSuffix, StringComparison.Ordinal))
            original += visibleSuffix;

        string converted = candidate.ConvertedText;
        if (!converted.EndsWith(visibleSuffix, StringComparison.Ordinal))
        {
            string toggledSuffix = KeyboardLayoutMap.ToggleLayoutText(visibleSuffix, out int changedCount);
            if (changedCount > 0 && converted.EndsWith(toggledSuffix, StringComparison.Ordinal))
                converted = converted[..^toggledSuffix.Length] + visibleSuffix;
            else
                converted += visibleSuffix;
        }

        if (original == candidate.OriginalText && converted == candidate.ConvertedText)
            return candidate;

        return candidate with
        {
            OriginalText = original,
            ConvertedText = converted,
            Reason = $"{candidate.Reason} + preserved trailing punctuation"
        };
    }

    private static string ExtractLiteralTrailingPunctuation(string visibleWord)
    {
        if (string.IsNullOrEmpty(visibleWord) || visibleWord.Length < 2)
            return string.Empty;

        int start = visibleWord.Length;
        while (start > 0 && IsLiteralTrailingPunctuation(visibleWord[start - 1]))
            start--;

        while (start < visibleWord.Length
               && IsWrappingQuote(visibleWord[start])
               && start > 0
               && (char.IsLetterOrDigit(visibleWord[start - 1])
                   || KeyboardLayoutMap.IsLayoutLetterChar(visibleWord[start - 1])
                   || KeyboardLayoutMap.IsWordConnector(visibleWord[start - 1])))
        {
            start++;
        }

        if (start == visibleWord.Length || start == 0)
            return string.Empty;

        return char.IsLetterOrDigit(visibleWord[start - 1])
            ? visibleWord[start..]
            : string.Empty;
    }

    private static string ResolveVisibleTrailingPunctuation(params string[] visibleWords)
    {
        foreach (string visibleWord in visibleWords)
        {
            string run = ExtractLiteralTrailingPunctuation(visibleWord);
            if (string.IsNullOrEmpty(run))
                continue;

            for (int offset = run.Length - 1; offset >= 0; offset--)
            {
                string suffix = run[offset..];
                if (ShouldTreatTrailingSuffixAsLiteral(suffix, visibleWords))
                    return suffix;
            }
        }

        return string.Empty;
    }

    private static bool ShouldTreatTrailingSuffixAsLiteral(string suffix, params string[] interpretations)
    {
        string toggledSuffix = KeyboardLayoutMap.ToggleLayoutText(suffix, out int changedCount);
        if (changedCount > 0 && toggledSuffix.Any(char.IsLetter))
        {
            foreach (string interpretation in interpretations)
            {
                if (!string.IsNullOrWhiteSpace(interpretation)
                    && interpretation.EndsWith(toggledSuffix, StringComparison.Ordinal)
                    && interpretation.Length > toggledSuffix.Length)
                {
                    string core = interpretation[..^toggledSuffix.Length];

                    if (CorrectionHeuristics.LooksCorrectAsTyped(interpretation)
                        && !CorrectionHeuristics.HasStrongAsTypedSignal(core))
                    {
                        return false;
                    }

                    var fullCandidate = CorrectionHeuristics.Evaluate(interpretation, CorrectionMode.Auto);
                    var coreCandidate = CorrectionHeuristics.Evaluate(core, CorrectionMode.Auto);

                    if (fullCandidate is not null
                        && (coreCandidate is null
                            || (fullCandidate.Confidence >= coreCandidate.Confidence
                                && fullCandidate.ConvertedText.Length > coreCandidate.ConvertedText.Length)))
                    {
                        return false;
                    }
                }
            }
        }

        foreach (string interpretation in interpretations)
        {
            if (string.IsNullOrWhiteSpace(interpretation)
                || !interpretation.EndsWith(suffix, StringComparison.Ordinal)
                || interpretation.Length <= suffix.Length)
                continue;

            string core = interpretation[..^suffix.Length];
            if (string.IsNullOrWhiteSpace(core))
                continue;

            if (changedCount > 0 && toggledSuffix.Any(char.IsLetter))
            {
                var fullCandidate = CorrectionHeuristics.Evaluate(interpretation, CorrectionMode.Auto);
                var coreCandidate = CorrectionHeuristics.Evaluate(core, CorrectionMode.Auto);
                if (fullCandidate is not null
                    && coreCandidate is not null
                    && fullCandidate.Confidence >= coreCandidate.Confidence
                    && fullCandidate.ConvertedText.Length > coreCandidate.ConvertedText.Length)
                {
                    return false;
                }
            }

            bool interpretationStable = CorrectionHeuristics.HasStrongAsTypedSignal(interpretation);
            bool coreStable = CorrectionHeuristics.HasStrongAsTypedSignal(core);
            bool coreConvertible = CorrectionHeuristics.Evaluate(core, CorrectionMode.Auto) is not null;

            if ((interpretationStable && coreStable) || (!interpretationStable && coreConvertible))
                return true;
        }

        return false;
    }

    private static string TrimTrailingChars(string text, int count)
    {
        if (count <= 0 || string.IsNullOrEmpty(text))
            return text;

        return text.Length > count ? text[..^count] : string.Empty;
    }

    private static string TrimLiteralTrailingSuffix(string text, string suffix)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(suffix))
            return text;

        return text.EndsWith(suffix, StringComparison.Ordinal)
            ? text[..^suffix.Length]
            : text;
    }

    private static string TrimBoundaryLiteralPunctuation(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        int start = 0;
        int end = text.Length;

        while (start < end && IsLiteralTrailingPunctuation(text[start]))
            start++;

        while (end > start && IsLiteralTrailingPunctuation(text[end - 1]))
            end--;

        return end > start ? text[start..end] : string.Empty;
    }

    private static string StripVisibleSuffixFromInterpretation(string text, string visibleSuffix)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(visibleSuffix))
            return text;

        if (text.EndsWith(visibleSuffix, StringComparison.Ordinal))
            return text[..^visibleSuffix.Length];

        string toggledSuffix = KeyboardLayoutMap.ToggleLayoutText(visibleSuffix, out int changedCount);
        if (changedCount > 0 && text.EndsWith(toggledSuffix, StringComparison.Ordinal))
            return text[..^toggledSuffix.Length];

        return text;
    }

    private static NativeMethods.INPUT[] BuildAutoReplacementInputs(
        string originalCore,
        string replacementCore,
        string trailingSuffix,
        int? eraseCountOverride = null)
    {
        var modifierRelease = NativeMethods.BuildModifierReleaseInputs();
        int suffixLen = trailingSuffix.Length;
        int eraseCount = eraseCountOverride ?? originalCore.Length;
        int totalInputs = modifierRelease.Length
            + (suffixLen * 2)
            + (eraseCount * 2)
            + (replacementCore.Length * 2)
            + (suffixLen * 2);
        var inputs = new NativeMethods.INPUT[totalInputs];
        int idx = 0;

        foreach (var input in modifierRelease)
            inputs[idx++] = input;

        for (int i = 0; i < suffixLen; i++)
        {
            inputs[idx++] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_LEFT, keyUp: false);
            inputs[idx++] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_LEFT, keyUp: true);
        }

        for (int i = 0; i < eraseCount; i++)
        {
            inputs[idx++] = NativeMethods.MakeKeyInput(NativeMethods.VK_BACK, keyUp: false);
            inputs[idx++] = NativeMethods.MakeKeyInput(NativeMethods.VK_BACK, keyUp: true);
        }

        foreach (char c in replacementCore)
        {
            inputs[idx++] = NativeMethods.MakeUnicodeInput(c, keyUp: false);
            inputs[idx++] = NativeMethods.MakeUnicodeInput(c, keyUp: true);
        }

        for (int i = 0; i < suffixLen; i++)
        {
            inputs[idx++] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_RIGHT, keyUp: false);
            inputs[idx++] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_RIGHT, keyUp: true);
        }

        return inputs;
    }

    private static bool IsLiteralTrailingPunctuation(char c) =>
        char.IsPunctuation(c) && !KeyboardLayoutMap.IsWordConnector(c);

    private static bool IsWrappingQuote(char c) =>
        c is '"' or '«' or '»' or '“' or '”' or '„';
}
