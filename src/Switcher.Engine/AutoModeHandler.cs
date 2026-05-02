using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Windows.Automation;
using Switcher.Core;
using Switcher.Infrastructure;

namespace Switcher.Engine;

public class AutoModeHandler
{
    private const int BrowserValuePatternStartDelayMs = 70;
    private const int ElectronUiaStartDelayMs = 45;
    private const int ClipboardAssistedStartDelayMs = 150;
    private const int AddressBarClipboardSelectDelayMs = 35;
    private const int AddressBarClipboardCopyInitialDelayMs = 55;
    private const int AddressBarClipboardCopyRetryDelayMs = 35;
    private const int AddressBarClipboardCopyAttempts = 4;
    private static readonly TimeSpan RecentAutoSwitchGuardWindow = TimeSpan.FromSeconds(20);

    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "brave", "opera", "vivaldi",
        "codex", "element", "slack", "discord", "teams",
        "claude"
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
        "windsurf",
        "claude"
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
        "search google or type a url",
        "search with google or enter address",
        "search google or enter address",
        "search or type a url",
        "search or type web address",
        "location bar",
        "url bar",
        "адресний рядок",
        "пошук google або введіть url-адресу",
        "шукайте в google або введіть url-адресу",
        "адресная строка",
        "рядок адреси",
        "строка поиска или адреса",
        "адрес и поиск"
    ];

    // Google Docs / Sheets / Slides render text into a <canvas>, not a textarea.
    // UIA ValuePattern.SetValue and naive Backspace+Unicode inject don't work there,
    // but Docs DOES handle Ctrl+C / Ctrl+V through its hidden texteventtarget iframe.
    // We therefore force the clipboard-assisted selection path for these surfaces.
    // Detection is done via the foreground window title, which Chrome/Edge/Brave etc.
    // format as "Document Name - Google Docs - Google Chrome" (locale-dependent suffix).
    private static readonly string[] GoogleDocsTitleMarkers =
    [
        "google docs",
        "google sheets",
        "google slides",
        "google документи",   // uk
        "google таблиці",     // uk
        "google презентації", // uk
        "google документы",   // ru
        "google таблицы",     // ru
        "google презентации"  // ru
    ];

    private static readonly string[] YouTubeTitleMarkers =
    [
        "youtube",
        "youtu.be"
    ];

    private static readonly string[] OneNoteTitleMarkers =
    [
        "onenote",
        "one note"
    ];

    private readonly ForegroundContextProvider _contextProvider;
    private readonly TextTargetCoordinator _coordinator;
    private readonly ExclusionManager _exclusions;
    private readonly DiagnosticsLogger _diagnostics;
    private readonly SettingsManager _settings;
    private readonly KeyboardObserver _observer;

    private record UndoState(string OriginalText, string ReplacementText, CorrectionDirection Direction, uint DelimiterVk);
    private sealed record AutoSwitchState(
        CorrectionDirection Direction,
        DateTime AtUtc,
        uint ProcessId,
        IntPtr Hwnd,
        IntPtr FocusedControlHwnd);

    private sealed record BrowserSurfaceSnapshot(
        bool HasFocusedElement,
        bool HasWritableValuePattern,
        bool HasTextPattern,
        string ControlType,
        string LocalizedControlType,
        string ClassName,
        string AutomationId,
        string ElementName,
        string AriaProperties,
        bool UnsafeCustomEditorLike,
        string Summary);

    private sealed record AddressBarLiveTokenCandidate(
        string OriginalToken,
        CorrectionCandidate Candidate,
        string FullText);

    private UndoState? _lastCorrection;
    private AutoSwitchState? _lastAutoSwitch;
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

        if (_exclusions.IsExcluded(snapshot.ProcessName))
        {
            LogSkip(snapshot, "AutoMode", $"Process is excluded {snapshot.TechDetail}");
            return;
        }

        if (TryHandleRecentSwitchSingleLetter(snapshot, approxWordLength))
            return;

        if (approxWordLength < 2 || (snapshot.AnalysisWordEN.Length < 2 && snapshot.AnalysisWordUA.Length < 2))
        {
            if (!snapshot.BufferQuality.NeedsLiveDiscovery || approxWordLength < 3)
            {
                LogSkip(snapshot, "AutoMode", $"Too short {snapshot.TechDetail} [{snapshot.RawDebug}]");
                return;
            }
        }

        // Chromium-based surfaces (Chrome/Edge/Brave/etc. AND Electron apps built on them,
        // e.g. VS Code, Telegram, Claude, Slack, Discord) sometimes re-dispatch keystrokes
        // with fake scan codes and/or fake virtual keys through the low-level hook.
        // In that case `ExpectedOriginalWord` is garbage (e.g. "45678" for "руддщ" typed
        // in Chrome's omnibox, or a nonsense first-word burst in Claude.exe / VS Code),
        // and AutoContextGuards would reject it as short-token / technical-mixed-token
        // based on that garbage.
        //
        // The address-bar live-token path and the Electron UIA path read the actual text
        // via UIA (ValuePattern / TextPattern). The Google Docs path reads the actual text
        // via the system clipboard (Shift+Left×N → Ctrl+C). Neither depends on the hook
        // buffer, so we can safely bypass the buffer-derived context guard in those cases.
        BrowserSurfaceSnapshot? guardSurface = null;
        BrowserSurfaceSnapshot GetGuardSurface() => guardSurface ??= InspectBrowserSurface();

        bool bypassContextGuardForLiveRead =
            snapshot.BufferQuality.NeedsLiveDiscovery
            && IsBrowserLikeContext(snapshot.Context)
            && (ShouldUseElectronUiaPath(snapshot.Context.ProcessName)
                || IsBrowserAddressBarSurface(snapshot.Context, GetGuardSurface())
                || IsGoogleDocsSurface(snapshot.Context)
                || IsYouTubeBrowserPageSurface(snapshot.Context, GetGuardSurface()));

        if (!bypassContextGuardForLiveRead)
        {
            string? unsafeContextReason = AutoContextGuards.GetUnsafeAutoCorrectionReason(
                snapshot.ExpectedOriginalWord,
                snapshot.CurrentSentence,
                snapshot.ProcessName);
            if (unsafeContextReason is not null)
            {
                LogSkip(snapshot, "ContextGuard", $"{unsafeContextReason} {snapshot.TechDetail}");
                return;
            }
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

        if (ShouldExecuteImmediately(plan))
        {
            _ = ExecuteReplacementPlan(plan);
            return;
        }

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

        if (candidate is null && snapshot.BufferQuality.NeedsLiveDiscovery)
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

        if (ShouldUseOneNoteNativeBackspaceRoute(snapshot, decision))
        {
            return new ReplacementPlan(
                snapshot,
                decision,
                ReplacementSafetyProfile.NativeSafe,
                ReplacementExecutionPath.NativeSelectionTransaction,
                "OneNoteBackspace",
                $"profile={ReplacementSafetyProfile.NativeSafe} path={ReplacementExecutionPath.NativeSelectionTransaction} surface=onenote-backspace");
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

        // Google Docs / Sheets / Slides: the canvas surface does not respond to ValuePattern.SetValue
        // or raw Backspace+Unicode injection, but it does honor system clipboard shortcuts through its
        // hidden texteventtarget iframe. Force the clipboard-assisted selection path.
        if (IsGoogleDocsSurface(snapshot.Context))
        {
            return new ReplacementPlan(
                snapshot,
                decision,
                ReplacementSafetyProfile.BrowserBestEffort,
                ReplacementExecutionPath.ClipboardAssistedSelection,
                "ClipboardSelectionTransaction",
                $"profile={ReplacementSafetyProfile.BrowserBestEffort} path={ReplacementExecutionPath.ClipboardAssistedSelection} surface=google-docs");
        }

        BrowserSurfaceSnapshot surface = InspectBrowserSurface();
        bool exactAddressBarSurface = IsBrowserAddressBarSurface(snapshot.Context, surface);
        bool browserWordTokenSurface = ShouldUseBrowserWordTokenRoute(snapshot, decision, surface);
        if (browserWordTokenSurface)
        {
            var (addressBarProfile, path, adapterName, reason) = BuildAddressBarRoute(
                decision,
                surface.HasWritableValuePattern,
                exactAddressBarSurface);
            return new ReplacementPlan(snapshot, decision, addressBarProfile, path, adapterName, reason);
        }

        if (ShouldUseYouTubeLiveBackspaceRoute(snapshot, decision, surface))
        {
            return new ReplacementPlan(
                snapshot,
                decision,
                ReplacementSafetyProfile.NativeSafe,
                ReplacementExecutionPath.BrowserPageLiveBackspace,
                "YouTubeLiveBackspace",
                $"profile={ReplacementSafetyProfile.NativeSafe} path={ReplacementExecutionPath.BrowserPageLiveBackspace} surface=youtube-live-backspace {surface.Summary}");
        }

        if (ShouldUseBrowserPageClipboardFallback(snapshot, decision, surface))
        {
            return new ReplacementPlan(
                snapshot,
                BuildBrowserPageClipboardDecision(decision),
                ReplacementSafetyProfile.BrowserBestEffort,
                ReplacementExecutionPath.ClipboardAssistedSelection,
                "ClipboardSelectionTransaction",
                $"profile={ReplacementSafetyProfile.BrowserBestEffort} path={ReplacementExecutionPath.ClipboardAssistedSelection} surface=browser-page-exact-clipboard {surface.Summary}");
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
                    : $"profile=UnsafeSkip reason=custom browser/editor surface without exact-slice verification wordTokenRoute=false exactAddress={exactAddressBarSurface} reqLive={decision.RequiresLiveRuntimeRead} hasCandidate={decision.Candidate is not null} needsLive={snapshot.BufferQuality.NeedsLiveDiscovery} process={snapshot.ProcessName} focusClass={snapshot.Context.FocusedControlClass} windowClass={snapshot.Context.WindowClass} surface={surface.Summary}")
        };
    }

    private async Task ExecuteReplacementPlan(ReplacementPlan plan)
    {
        try
        {
            switch (plan.ExecutionPath)
            {
                case ReplacementExecutionPath.NativeSelectionTransaction:
                case ReplacementExecutionPath.BrowserAddressBarBufferedBackspace:
                    ExecuteNativeReplacementTransaction(plan);
                    break;
                case ReplacementExecutionPath.BrowserAddressBarLiveTokenBackspace:
                    ExecuteBrowserAddressBarLiveTokenReplacement(plan);
                    break;
                case ReplacementExecutionPath.BrowserPageLiveBackspace:
                    ExecuteBrowserPageLiveBackspaceReplacement(plan);
                    break;
                case ReplacementExecutionPath.BrowserValuePattern:
                    await ExecuteBrowserValuePatternReplacement(plan);
                    break;
                case ReplacementExecutionPath.ClipboardAssistedSelection:
                    if (IsBrowserPageExactClipboardPlan(plan))
                        ExecuteBrowserPageClipboardReplacement(plan);
                    else
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

        string expectedOriginal = ShouldVerifyNativeVisibleWord(plan)
            ? candidate.OriginalText
            : string.Empty;
        bool requireVisibleExpectedWord = plan.ExecutionPath == ReplacementExecutionPath.BrowserAddressBarBufferedBackspace;
        if (TryAbortPreconditions(
                snapshot,
                expectedOriginal,
                "Native transaction",
                out string abortReason,
                allowPostDelimiterInteraction: false,
                requireVisibleExpectedWord: requireVisibleExpectedWord))
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

    private void ExecuteBrowserAddressBarLiveTokenReplacement(ReplacementPlan plan)
    {
        var snapshot = plan.Snapshot;
        if (TryAbortPreconditions(
                snapshot,
                string.Empty,
                "Browser address bar live token",
                out string abortReason,
                allowPostDelimiterInteraction: false,
                requireVisibleExpectedWord: false,
                allowAddressBarFocusDrift: true))
        {
            LogSkip(snapshot, plan.AdapterName, $"Browser address bar live token aborted: {abortReason}");
            TryReinjectDelimiter(plan);
            return;
        }

        string? addressText = TryReadFocusedValuePatternText();
        if (string.IsNullOrWhiteSpace(addressText))
        {
            LogSkip(snapshot, plan.AdapterName, "Browser address bar live token skipped: live UIA text unavailable");
            TryReinjectDelimiter(plan);
            return;
        }

        AddressBarLiveTokenCandidate? live = TryBuildAddressBarLiveTokenCandidate(addressText, snapshot.Context.ProcessName);
        if (live is null)
        {
            LogSkip(snapshot, plan.AdapterName, "Browser address bar live token skipped: live UIA token has no safe conversion");
            TryReinjectDelimiter(plan);
            return;
        }

        var candidate = live.Candidate;
        var inputs = BuildAutoReplacementInputs(
            candidate.OriginalText,
            candidate.ConvertedText,
            trailingSuffix: string.Empty,
            eraseCountOverride: live.OriginalToken.Length);
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
                ? $"{plan.Reason}; live-token; {candidate.Reason}"
                : $"{plan.Reason}; live-token SendInput returned {sent}/{inputs.Length}");

        if (success)
            FinalizeSuccessfulReplacement(snapshot, candidate);

        Thread.Sleep(30);
        TryReinjectDelimiter(plan);
    }

    private void ExecuteBrowserPageLiveBackspaceReplacement(ReplacementPlan plan)
    {
        var snapshot = plan.Snapshot;
        if (TryAbortPreconditions(
                snapshot,
                string.Empty,
                "Browser page live backspace",
                out string abortReason,
                allowPostDelimiterInteraction: false,
                requireVisibleExpectedWord: false))
        {
            LogSkip(snapshot, plan.AdapterName, $"Browser page live backspace aborted: {abortReason}");
            TryReinjectDelimiter(plan);
            return;
        }

        var adapter = snapshot.ReadAdapter as UIAutomationTargetAdapter ?? new UIAutomationTargetAdapter();
        string actualWord = snapshot.LiveWord;
        if (string.IsNullOrWhiteSpace(actualWord) || actualWord.Length < 2)
            actualWord = adapter.TryGetLastWord(snapshot.Context) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(actualWord) || actualWord.Length < 2)
        {
            LogSkip(snapshot, plan.AdapterName, "Browser page live backspace skipped: live word unavailable");
            TryReinjectDelimiter(plan);
            return;
        }

        CandidateDecision liveDecision = plan.Decision.Candidate is not null
                                         && ExactSliceMatchesExpected(actualWord, plan.Decision.Candidate.OriginalText)
            ? plan.Decision
            : BuildLiveRuntimeCandidateDecision(snapshot, actualWord);
        CorrectionCandidate? candidate = liveDecision.Candidate;
        if (candidate is null)
        {
            LogSkip(snapshot, plan.AdapterName, $"Browser page live backspace skipped: {liveDecision.Reason}", actualWord);
            TryReinjectDelimiter(plan);
            return;
        }

        var inputs = BuildAutoReplacementInputs(actualWord, candidate.ConvertedText, string.Empty, actualWord.Length);
        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        bool success = sent == (uint)inputs.Length;

        _diagnostics.Log(
            snapshot.ProcessName,
            snapshot.ControlClass,
            plan.AdapterName,
            true,
            OperationType.AutoMode,
            actualWord,
            candidate.ConvertedText,
            success ? DiagnosticResult.Replaced : DiagnosticResult.Error,
            success
                ? $"{plan.Reason}; {liveDecision.Reason}"
                : $"{plan.Reason}; SendInput returned {sent}/{inputs.Length}");

        if (success)
            FinalizeSuccessfulReplacement(snapshot, candidate);

        Thread.Sleep(30);
        TryReinjectDelimiter(plan);
    }

    private void ExecuteBrowserAddressBarClipboardTokenReplacement(ReplacementPlan plan, string fallbackReason)
    {
        var snapshot = plan.Snapshot;
        int selectionLength = DetermineAddressBarTokenSelectionLength(snapshot, plan.Decision);
        if (selectionLength < 2)
        {
            LogSkip(snapshot, plan.AdapterName, $"Browser address bar token fallback skipped: selection length too short after {fallbackReason}");
            return;
        }

        string? savedClipboard = NativeMethods.GetClipboardText();
        NativeMethods.SetClipboardText(null);

        try
        {
            NativeMethods.INPUT[] selectionInputs = BuildSelectionInputs(selectionLength);
            uint selected = NativeMethods.SendInput(
                (uint)selectionInputs.Length,
                selectionInputs,
                Marshal.SizeOf<NativeMethods.INPUT>());

            if (selected != (uint)selectionInputs.Length)
            {
                CollapseSelectionRight();
                LogSkip(snapshot, plan.AdapterName, $"Browser address bar token fallback aborted: selection SendInput returned {selected}/{selectionInputs.Length}");
                return;
            }

            Thread.Sleep(AddressBarClipboardSelectDelayMs);

            NativeMethods.INPUT[] copyInputs = BuildCopyInputs();
            NativeMethods.SendInput((uint)copyInputs.Length, copyInputs, Marshal.SizeOf<NativeMethods.INPUT>());

            string? selectedText = null;
            for (int attempt = 0; attempt < AddressBarClipboardCopyAttempts; attempt++)
            {
                Thread.Sleep(attempt == 0 ? AddressBarClipboardCopyInitialDelayMs : AddressBarClipboardCopyRetryDelayMs);
                selectedText = NativeMethods.GetClipboardText();
                if (!string.IsNullOrEmpty(selectedText))
                    break;
            }

            if (string.IsNullOrWhiteSpace(selectedText))
            {
                CollapseSelectionRight();
                LogSkip(snapshot, plan.AdapterName, $"Browser address bar token fallback aborted: clipboard read is empty after {fallbackReason}");
                return;
            }

            if (!TryGetLastAddressBarToken(selectedText, out string liveToken))
            {
                CollapseSelectionRight();
                LogSkip(snapshot, plan.AdapterName, $"Browser address bar token fallback skipped: copied slice has no word token [{selectedText.Trim()}]");
                return;
            }

            var liveDecision = BuildLiveRuntimeCandidateDecision(snapshot, liveToken);
            CorrectionCandidate? candidate = liveDecision.Candidate;
            if (candidate is null)
            {
                CollapseSelectionRight();
                LogSkip(snapshot, plan.AdapterName, $"Browser address bar token fallback skipped: {liveDecision.Reason}", liveToken);
                return;
            }

            if (!IsExactSelectedAddressBarToken(selectedText, liveToken))
            {
                CollapseSelectionRight();
                NativeMethods.INPUT[] reselectInputs = BuildSelectionInputs(liveToken.Length);
                uint reselected = NativeMethods.SendInput(
                    (uint)reselectInputs.Length,
                    reselectInputs,
                    Marshal.SizeOf<NativeMethods.INPUT>());

                if (reselected != (uint)reselectInputs.Length)
                {
                    CollapseSelectionRight();
                    LogSkip(snapshot, plan.AdapterName, $"Browser address bar token fallback aborted: exact token reselect returned {reselected}/{reselectInputs.Length}");
                    return;
                }

                Thread.Sleep(AddressBarClipboardSelectDelayMs);
            }

            NativeMethods.INPUT[] typeInputs = BuildUnicodeInputs(candidate.ConvertedText);
            uint sent = NativeMethods.SendInput((uint)typeInputs.Length, typeInputs, Marshal.SizeOf<NativeMethods.INPUT>());
            bool success = sent == (uint)typeInputs.Length;

            _diagnostics.Log(
                snapshot.ProcessName,
                snapshot.ControlClass,
                plan.AdapterName,
                true,
                OperationType.AutoMode,
                liveToken,
                candidate.ConvertedText,
                success ? DiagnosticResult.Replaced : DiagnosticResult.Error,
                success
                    ? $"{plan.Reason}; clipboard-token fallback after {fallbackReason}; {liveDecision.Reason}"
                    : $"{plan.Reason}; clipboard-token fallback SendInput returned {sent}/{typeInputs.Length}");

            if (success)
                FinalizeSuccessfulReplacement(snapshot, candidate);
        }
        finally
        {
            NativeMethods.SetClipboardText(savedClipboard);
        }
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
        await Task.Delay(GetClipboardAssistedStartDelayMs(plan));
        if (_observer.UserKeyDownCounter != startCount)
        {
            LogSkip(snapshot, plan.AdapterName, "Browser best-effort aborted: user kept typing before async replace started");
            TryReinjectDelimiter(plan);
            return;
        }

        string expectedWord = GetClipboardPreconditionExpectedWord(plan);
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

            if (candidate is null)
            {
                LogSkip(snapshot, plan.AdapterName, "Browser best-effort skipped: no conversion candidate", liveWord);
                TryReinjectDelimiter(plan);
                return;
            }

            string beforeReplaceExpected = GetClipboardBeforeReplaceExpectedWord(plan, candidate);
            if (TryAbortPreconditions(snapshot, beforeReplaceExpected, "Browser best-effort before replace", out abortReason))
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

    private void ExecuteBrowserPageClipboardReplacement(ReplacementPlan plan)
    {
        var snapshot = plan.Snapshot;
        string expectedWord = GetClipboardPreconditionExpectedWord(plan);
        if (TryAbortPreconditions(snapshot, expectedWord, "Browser page clipboard preflight", out string abortReason))
        {
            LogSkip(snapshot, plan.AdapterName, $"Browser page clipboard aborted: {abortReason}");
            TryReinjectDelimiter(plan);
            return;
        }

        int selectionLength = DetermineSelectionLength(snapshot, plan.Decision);
        if (selectionLength < 2)
        {
            LogSkip(snapshot, plan.AdapterName, "Browser page clipboard aborted: selection length is too short");
            TryReinjectDelimiter(plan);
            return;
        }

        string? savedClipboard = NativeMethods.GetClipboardText();
        NativeMethods.SetClipboardText(null);

        try
        {
            NativeMethods.INPUT[] selectionInputs = BuildSelectionInputs(selectionLength);
            NativeMethods.SendInput((uint)selectionInputs.Length, selectionInputs, Marshal.SizeOf<NativeMethods.INPUT>());
            Thread.Sleep(45);

            NativeMethods.INPUT[] copyInputs = BuildCopyInputs();
            NativeMethods.SendInput((uint)copyInputs.Length, copyInputs, Marshal.SizeOf<NativeMethods.INPUT>());

            string? selectedText = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                Thread.Sleep(attempt == 0 ? 55 : 65);
                if (TryAbortPreconditions(snapshot, expectedWord, "Browser page clipboard wait", out abortReason))
                {
                    LogSkip(snapshot, plan.AdapterName, $"Browser page clipboard aborted: {abortReason}");
                    CollapseSelectionRight();
                    TryReinjectDelimiter(plan);
                    return;
                }

                selectedText = NativeMethods.GetClipboardText();
                if (!string.IsNullOrEmpty(selectedText))
                    break;
            }

            CollapseSelectionRight();

            if (string.IsNullOrWhiteSpace(selectedText))
            {
                LogSkip(snapshot, plan.AdapterName, "Browser page clipboard aborted: clipboard read is stale or empty");
                TryReinjectDelimiter(plan);
                return;
            }

            string liveWord = selectedText.Trim();
            if (string.IsNullOrWhiteSpace(liveWord) || liveWord.Length < 2 || liveWord.Any(char.IsWhiteSpace))
            {
                LogSkip(snapshot, plan.AdapterName, $"Browser page clipboard aborted: copied slice is not one word [{liveWord}]");
                TryReinjectDelimiter(plan);
                return;
            }

            var liveDecision = BuildLiveRuntimeCandidateDecision(snapshot, liveWord);
            CorrectionCandidate? candidate = liveDecision.Candidate;
            if (candidate is null)
            {
                LogSkip(snapshot, plan.AdapterName, $"Browser page clipboard skipped: {liveDecision.Reason}", liveWord);
                TryReinjectDelimiter(plan);
                return;
            }

            if (TryAbortPreconditions(snapshot, string.Empty, "Browser page clipboard before replace", out abortReason))
            {
                LogSkip(snapshot, plan.AdapterName, $"Browser page clipboard aborted: {abortReason}");
                TryReinjectDelimiter(plan);
                return;
            }

            NativeMethods.INPUT[] reselectInputs = BuildSelectionInputs(liveWord.Length);
            NativeMethods.SendInput((uint)reselectInputs.Length, reselectInputs, Marshal.SizeOf<NativeMethods.INPUT>());
            Thread.Sleep(25);

            NativeMethods.INPUT[] typeInputs = BuildUnicodeInputs(candidate.ConvertedText);
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
                success
                    ? $"{plan.Reason}; browser-page-sync; {liveDecision.Reason}"
                    : $"{plan.Reason}; browser-page-sync SendInput returned {sent}/{typeInputs.Length}");

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

        CorrectionCandidate? candidate = TryEvaluateVisibleTokenCandidate(actualWord);

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

    private bool TryHandleRecentSwitchSingleLetter(WordSnapshot snapshot, int approxWordLength)
    {
        AutoSwitchState? recentSwitch = _lastAutoSwitch;
        if (recentSwitch is null)
            return false;

        DateTime nowUtc = DateTime.UtcNow;
        CorrectionCandidate? candidate = TryBuildRecentSwitchSingleLetterCandidate(
            snapshot,
            approxWordLength,
            recentSwitch,
            nowUtc);

        if (candidate is null)
        {
            if (ShouldConsumeRecentAutoSwitch(snapshot, recentSwitch, nowUtc))
                _lastAutoSwitch = null;

            return false;
        }

        BrowserSurfaceSnapshot surface = InspectBrowserSurface();
        if (!IsRecentSwitchSingleLetterSurface(snapshot.Context, surface))
        {
            _lastAutoSwitch = null;
            return false;
        }

        var decision = new CandidateDecision(
            candidate,
            CandidateSource.PrimaryHeuristics,
            RequiresLiveRuntimeRead: false,
            SelectorFeatures: null,
            LearnedDecision: null,
            Reason: candidate.Reason);
        var plan = new ReplacementPlan(
            snapshot,
            decision,
            ReplacementSafetyProfile.NativeSafe,
            ReplacementExecutionPath.NativeSelectionTransaction,
            "RecentSwitchGuard",
            $"profile={ReplacementSafetyProfile.NativeSafe} path={ReplacementExecutionPath.NativeSelectionTransaction} reason={candidate.Reason}");

        if (!TryBeginAutoOperation(snapshot, plan))
            return true;

        _observer.SuppressCurrentDelimiter();
        _ = ExecuteReplacementPlan(plan);
        return true;
    }

    private static CorrectionCandidate? TryBuildRecentSwitchSingleLetterCandidate(
        WordSnapshot snapshot,
        int approxWordLength,
        AutoSwitchState? recentSwitch,
        DateTime nowUtc)
    {
        if (recentSwitch is null)
            return null;

        if (recentSwitch.Direction != CorrectionDirection.UaToEn)
            return null;

        if (!IsRecentAutoSwitchContext(snapshot, recentSwitch, nowUtc))
            return null;

        if (!string.Equals(snapshot.LayoutTag, "EN", StringComparison.OrdinalIgnoreCase))
            return null;

        if (approxWordLength != 1 || snapshot.BufferQuality.ApproxWordLength != 1)
            return null;

        string original = FirstNonEmpty(snapshot.AnalysisWordEN, snapshot.WordEN, snapshot.VkWordEN);
        string converted = FirstNonEmpty(snapshot.AnalysisWordUA, snapshot.WordUA, snapshot.VkWordUA);
        if (string.Equals(original, "f", StringComparison.Ordinal)
            && string.Equals(converted, "а", StringComparison.Ordinal))
        {
            return new CorrectionCandidate(
                "f",
                "а",
                CorrectionDirection.EnToUa,
                0.99,
                "recent UaToEn switch single-letter guard");
        }

        if (string.Equals(original, "d", StringComparison.Ordinal)
            && string.Equals(converted, "в", StringComparison.Ordinal))
        {
            return new CorrectionCandidate(
                "d",
                "в",
                CorrectionDirection.EnToUa,
                0.99,
                "recent UaToEn switch single-letter guard");
        }

        if (string.Equals(original, "F", StringComparison.Ordinal)
            && string.Equals(converted, "А", StringComparison.Ordinal))
        {
            return new CorrectionCandidate(
                "F",
                "А",
                CorrectionDirection.EnToUa,
                0.99,
                "recent UaToEn switch single-letter guard");
        }

        if (string.Equals(original, "D", StringComparison.Ordinal)
            && string.Equals(converted, "В", StringComparison.Ordinal))
        {
            return new CorrectionCandidate(
                "D",
                "В",
                CorrectionDirection.EnToUa,
                0.99,
                "recent UaToEn switch single-letter guard");
        }

        return null;
    }

    private static bool ShouldConsumeRecentAutoSwitch(
        WordSnapshot snapshot,
        AutoSwitchState recentSwitch,
        DateTime nowUtc) =>
        recentSwitch.Direction == CorrectionDirection.UaToEn
        && IsRecentAutoSwitchContext(snapshot, recentSwitch, nowUtc);

    private static bool IsRecentAutoSwitchContext(
        WordSnapshot snapshot,
        AutoSwitchState recentSwitch,
        DateTime nowUtc)
    {
        if (nowUtc - recentSwitch.AtUtc > RecentAutoSwitchGuardWindow)
            return false;

        return snapshot.Context.ProcessId == recentSwitch.ProcessId
               && snapshot.Context.Hwnd == recentSwitch.Hwnd;
    }

    private static bool IsRecentSwitchSingleLetterSurface(
        ForegroundContext context,
        BrowserSurfaceSnapshot surface) =>
        IsBrowserPageFocusedControl(context)
        && !IsBrowserAddressBarSurface(context, surface);

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }

    private bool TryBeginAutoOperation(WordSnapshot snapshot, ReplacementPlan plan)
    {
        if (Interlocked.CompareExchange(ref _autoOperationInFlight, 1, 0) == 0)
        {
            string preflightOriginal = plan.ExecutionPath is ReplacementExecutionPath.NativeSelectionTransaction
                    or ReplacementExecutionPath.BrowserAddressBarBufferedBackspace
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
        _lastAutoSwitch = candidate.Direction == CorrectionDirection.None
            ? null
            : new AutoSwitchState(
                candidate.Direction,
                DateTime.UtcNow,
                snapshot.Context.ProcessId,
                snapshot.Context.Hwnd,
                snapshot.Context.FocusedControlHwnd);
    }

    private bool TryAbortPreconditions(WordSnapshot snapshot, string expectedOriginal, string stage, out string reason)
        => TryAbortPreconditions(
            snapshot,
            expectedOriginal,
            stage,
            out reason,
            allowPostDelimiterInteraction: false,
            requireVisibleExpectedWord: false);

    private bool TryAbortPreconditions(
        WordSnapshot snapshot,
        string expectedOriginal,
        string stage,
        out string reason,
        bool allowPostDelimiterInteraction,
        bool requireVisibleExpectedWord = false,
        bool allowAddressBarFocusDrift = false)
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

        bool processMatches = current.ProcessId == snapshot.Context.ProcessId;
        bool topLevelHwndMatches = current.Hwnd == snapshot.Context.Hwnd;
        bool focusedControlMatches = current.FocusedControlHwnd == snapshot.Context.FocusedControlHwnd;

        if (!processMatches || !topLevelHwndMatches || !focusedControlMatches)
        {
            // Chrome/Edge omnibox shuffles its internal focused-control HWND while typing
            // (the suggestion popup briefly steals focus). The address bar live-token path
            // does NOT depend on the captured FocusedControlHwnd — UIA reads the value
            // directly from the current foreground at execution time. So when callers opt
            // in via allowAddressBarFocusDrift we accept a drifted focused-control HWND
            // as long as the same process and top-level window are still in front and the
            // foreground is still recognised as a browser address bar surface.
            bool acceptDrift =
                allowAddressBarFocusDrift
                && processMatches
                && topLevelHwndMatches
                && IsBrowserAddressBarSurface(current, InspectBrowserSurface());

            if (!acceptDrift)
            {
                reason = $"{stage}: focus changed";
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(expectedOriginal))
        {
            string visibleNearCaret = _observer.GetVisibleWordNearCaret(current);
            if (requireVisibleExpectedWord && string.IsNullOrWhiteSpace(visibleNearCaret))
            {
                reason = $"{stage}: visible word is unavailable for verified address bar replace";
                return true;
            }

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

    private static bool ShouldVerifyNativeVisibleWord(ReplacementPlan plan) =>
        true;

    private static bool CanUseElectronBufferedFallback(CandidateDecision decision) =>
        decision.Candidate is not null
        && !decision.RequiresLiveRuntimeRead;

    private static bool CanUseBufferedBrowserBackspace(CandidateDecision decision) =>
        decision.Candidate is not null
        && !decision.RequiresLiveRuntimeRead;

    private static bool ShouldExecuteImmediately(ReplacementPlan plan) =>
        plan.ExecutionPath is ReplacementExecutionPath.NativeSelectionTransaction
            or ReplacementExecutionPath.BrowserAddressBarBufferedBackspace
            or ReplacementExecutionPath.BrowserAddressBarLiveTokenBackspace
            or ReplacementExecutionPath.BrowserPageLiveBackspace;

    private static bool ShouldSuppressDelimiter(ReplacementPlan plan) =>
        !(plan.ExecutionPath == ReplacementExecutionPath.BrowserValuePattern
          && plan.Snapshot.DelimiterVk == NativeMethods.VK_SPACE);

    private static bool IsBrowserPageExactClipboardPlan(ReplacementPlan plan) =>
        plan.ExecutionPath == ReplacementExecutionPath.ClipboardAssistedSelection
        && plan.Reason.Contains("surface=browser-page-exact-clipboard", StringComparison.Ordinal);

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

    private static int DetermineAddressBarTokenSelectionLength(WordSnapshot snapshot, CandidateDecision decision)
    {
        int bufferedLength = snapshot.BufferQuality.ApproxWordLength;
        int candidateLength = decision.Candidate?.OriginalText.Length ?? 0;
        int expectedLength = snapshot.ExpectedOriginalWord.Length;

        return Math.Max(bufferedLength, Math.Max(candidateLength, expectedLength));
    }

    private static string GetClipboardPreconditionExpectedWord(ReplacementPlan plan) =>
        plan.Decision.RequiresLiveRuntimeRead
            ? string.Empty
            : plan.Decision.Candidate?.OriginalText ?? plan.Snapshot.ExpectedOriginalWord;

    private static string GetClipboardBeforeReplaceExpectedWord(ReplacementPlan plan, CorrectionCandidate candidate) =>
        plan.Decision.RequiresLiveRuntimeRead ? string.Empty : candidate.OriginalText;

    private static AddressBarLiveTokenCandidate? TryBuildAddressBarLiveTokenCandidate(string text, string? processName)
    {
        if (!TryGetLastAddressBarToken(text, out string token))
            return null;

        string? guardReason = AutoContextGuards.GetUnsafeAutoCorrectionReason(token, text, processName);
        if (guardReason is not null)
            return null;

        CorrectionCandidate? candidate = TryEvaluateVisibleTokenCandidate(token);
        if (candidate is null || string.Equals(candidate.ConvertedText, token, StringComparison.Ordinal))
            return null;

        return new AddressBarLiveTokenCandidate(token, candidate, text);
    }

    private static bool TryGetLastAddressBarToken(string text, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        int end = text.Length;
        while (end > 0 && char.IsWhiteSpace(text[end - 1]))
            end--;

        if (end <= 0)
            return false;

        int start = end;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
            start--;

        token = text[start..end];
        return token.Length >= 2;
    }

    private static bool IsExactSelectedAddressBarToken(string selectedText, string liveToken) =>
        string.Equals(selectedText, liveToken, StringComparison.Ordinal);

    private static CorrectionCandidate? TryEvaluateVisibleTokenCandidate(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 2)
            return null;

        string toggledToken = KeyboardLayoutMap.ToggleLayoutText(token, out _);
        string visibleSuffix = ResolveVisibleTrailingPunctuation(token, toggledToken);
        string analysis = TrimTrailingChars(token, visibleSuffix.Length);
        return TryEvaluateCandidate(analysis, visibleSuffix);
    }

    private static string? TryReadFocusedValuePatternText()
    {
        try
        {
            AutomationElement? element = AutomationElement.FocusedElement;
            if (element is null)
                return null;

            return element.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp)
                ? ((ValuePattern)vp).Current.Value
                : null;
        }
        catch
        {
            return null;
        }
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
            string ariaProperties = TryReadAutomationStringProperty(element, "AriaPropertiesProperty");
            string merged = $"{controlType} {localizedControlType} {className} {automationId} {elementName} {ariaProperties}".ToLowerInvariant();
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
                AriaProperties: ariaProperties,
                UnsafeCustomEditorLike: unsafeCustom,
                Summary: $"uia=value={hasWritableValuePattern} text={hasTextPattern} type={controlType}/{localizedControlType} class={className} id={automationId} name={elementName} aria={ariaProperties}");
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
                string.Empty,
                false,
                $"uia=error:{ex.GetType().Name}");
        }
    }

    private static string TryReadAutomationStringProperty(AutomationElement element, string propertyName)
    {
        try
        {
            AutomationProperty? property = typeof(AutomationElementIdentifiers)
                .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null) as AutomationProperty
                ?? typeof(AutomationElementIdentifiers)
                    .GetField(propertyName, BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null) as AutomationProperty;

            if (property is null)
                return string.Empty;

            object value = element.GetCurrentPropertyValue(property, ignoreDefaultValue: true);
            return ReferenceEquals(value, AutomationElement.NotSupported)
                ? string.Empty
                : value?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
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

    private static bool ShouldUseBrowserPageClipboardFallback(
        WordSnapshot snapshot,
        CandidateDecision decision,
        BrowserSurfaceSnapshot surface)
    {
        return false;
    }

    private static int GetClipboardAssistedStartDelayMs(ReplacementPlan plan) =>
        plan.Reason.Contains("surface=browser-page-exact-clipboard", StringComparison.Ordinal)
            ? 20
            : ClipboardAssistedStartDelayMs;

    private static CandidateDecision BuildBrowserPageClipboardDecision(CandidateDecision decision)
    {
        if (decision.RequiresLiveRuntimeRead || decision.Candidate is null)
            return decision;

        return decision with
        {
            RequiresLiveRuntimeRead = true,
            Reason = $"{decision.Reason}; browser page clipboard verifies live slice"
        };
    }

    private static bool IsBrowserPageFocusedControl(ForegroundContext context) =>
        string.Equals(context.FocusedControlClass, "Chrome_RenderWidgetHostHWND", StringComparison.OrdinalIgnoreCase)
        || string.Equals(context.WindowClass, "MozillaWindowClass", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldUseBrowserWordTokenRoute(
        WordSnapshot snapshot,
        CandidateDecision decision,
        BrowserSurfaceSnapshot surface)
    {
        if (!IsBrowserAddressBarProcess(snapshot.ProcessName))
            return false;

        return IsBrowserAddressBarSurface(snapshot.Context, surface);
    }

    private static bool LooksLikeTopChromeFocus(ForegroundContext context, BrowserSurfaceSnapshot surface)
    {
        if (!IsBrowserAddressBarProcess(context.ProcessName))
            return false;

        if (context.FocusedControlClass.Contains("Omnibox", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(context.WindowClass, "Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase)
            && string.Equals(context.FocusedControlClass, context.WindowClass, StringComparison.OrdinalIgnoreCase)
            && !surface.UnsafeCustomEditorLike)
        {
            return true;
        }

        return surface.HasWritableValuePattern
               && !string.Equals(context.FocusedControlClass, "Chrome_RenderWidgetHostHWND", StringComparison.OrdinalIgnoreCase)
               && !surface.UnsafeCustomEditorLike;
    }

    private static bool IsBrowserAddressBarSurface(ForegroundContext context, BrowserSurfaceSnapshot surface) =>
        IsBrowserAddressBarSurface(
            context.ProcessName,
            surface.ControlType,
            surface.LocalizedControlType,
            surface.ClassName,
            surface.AutomationId,
            surface.ElementName,
            surface.AriaProperties,
            context.FocusedControlClass,
            context.WindowClass);

    private static bool IsBrowserAddressBarSurface(
        string? processName,
        string controlType,
        string localizedControlType,
        string className,
        string automationId,
        string elementName) =>
        IsBrowserAddressBarSurface(
            processName,
            controlType,
            localizedControlType,
            className,
            automationId,
            elementName,
            string.Empty,
            string.Empty,
            string.Empty);

    private static bool IsBrowserAddressBarSurface(
        string? processName,
        string controlType,
        string localizedControlType,
        string className,
        string automationId,
        string elementName,
        string ariaProperties,
        string focusedControlClass,
        string windowClass)
    {
        if (!IsBrowserAddressBarProcess(processName))
        {
            return false;
        }

        string merged = $"{controlType} {localizedControlType} {className} {automationId} {elementName} {ariaProperties}".ToLowerInvariant();
        if (merged.Contains("omnibox", StringComparison.Ordinal)
            || focusedControlClass.Contains("Omnibox", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (HasUrlTypeAttribute(ariaProperties)
            && IsBrowserChromeFocusedControl(focusedControlClass, windowClass))
        {
            return true;
        }

        string normalizedName = (elementName ?? string.Empty).Trim().ToLowerInvariant();
        return BrowserAddressBarNameMarkers.Any(marker => string.Equals(normalizedName, marker, StringComparison.Ordinal));
    }

    private static bool HasUrlTypeAttribute(string attributes)
    {
        if (string.IsNullOrWhiteSpace(attributes))
            return false;

        string normalized = attributes.ToLowerInvariant();
        return normalized.Contains("type=url", StringComparison.Ordinal)
               || normalized.Contains("type=\"url\"", StringComparison.Ordinal)
               || normalized.Contains("type='url'", StringComparison.Ordinal);
    }

    private static bool IsBrowserChromeFocusedControl(string focusedControlClass, string windowClass)
    {
        if (string.Equals(focusedControlClass, "Chrome_RenderWidgetHostHWND", StringComparison.OrdinalIgnoreCase))
            return false;

        return focusedControlClass.Contains("Omnibox", StringComparison.OrdinalIgnoreCase)
               || string.Equals(focusedControlClass, "Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGoogleDocsSurface(ForegroundContext context) =>
        IsGoogleDocsTitle(context.ProcessName, TryReadWindowTitle(context.Hwnd));

    private static bool IsYouTubeBrowserPageSurface(ForegroundContext context, BrowserSurfaceSnapshot surface)
    {
        if (!IsBrowserAddressBarProcess(context.ProcessName))
            return false;

        if (!IsBrowserPageFocusedControl(context))
            return false;

        if (IsBrowserAddressBarSurface(context, surface))
            return false;

        return IsYouTubeTitle(context.ProcessName, TryReadWindowTitle(context.Hwnd));
    }

    private static bool ShouldUseYouTubeLiveBackspaceRoute(
        WordSnapshot snapshot,
        CandidateDecision decision,
        BrowserSurfaceSnapshot surface)
    {
        if (!IsYouTubeBrowserPageSurface(snapshot.Context, surface))
            return false;

        if (!surface.HasTextPattern && string.IsNullOrWhiteSpace(snapshot.LiveWord))
            return false;

        return decision.Candidate is not null
               || decision.RequiresLiveRuntimeRead
               || !string.IsNullOrWhiteSpace(snapshot.LiveWord);
    }

    private static bool ShouldUseOneNoteNativeBackspaceRoute(WordSnapshot snapshot, CandidateDecision decision) =>
        decision.Candidate is not null
        && !decision.RequiresLiveRuntimeRead
        && IsOneNoteSurface(snapshot.Context);

    private static bool IsOneNoteSurface(ForegroundContext context) =>
        IsOneNoteTitle(context.ProcessName, TryReadWindowTitle(context.Hwnd));

    /// <summary>
    /// Pure predicate: given a browser process name and a foreground window title,
    /// returns true if the title indicates a Google Docs / Sheets / Slides editor.
    /// Exposed as internal for unit tests.
    /// </summary>
    internal static bool IsGoogleDocsTitle(string? processName, string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(processName) || string.IsNullOrWhiteSpace(windowTitle))
            return false;

        // Docs in a Chromium browser tab — same process list as address bar detection.
        if (!IsBrowserAddressBarProcess(processName))
            return false;

        string lowered = windowTitle.ToLowerInvariant();
        return GoogleDocsTitleMarkers.Any(marker => lowered.Contains(marker, StringComparison.Ordinal));
    }

    internal static bool IsYouTubeTitle(string? processName, string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(processName) || string.IsNullOrWhiteSpace(windowTitle))
            return false;

        if (!IsBrowserAddressBarProcess(processName))
            return false;

        string lowered = windowTitle.ToLowerInvariant();
        return YouTubeTitleMarkers.Any(marker => lowered.Contains(marker, StringComparison.Ordinal));
    }

    internal static bool IsOneNoteTitle(string? processName, string? windowTitle)
    {
        if (!string.IsNullOrWhiteSpace(processName)
            && processName.Contains("onenote", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsBrowserAddressBarProcess(processName))
            return false;

        if (string.IsNullOrWhiteSpace(windowTitle))
            return false;

        string lowered = windowTitle.ToLowerInvariant();
        return OneNoteTitleMarkers.Any(marker => lowered.Contains(marker, StringComparison.Ordinal));
    }

    private static bool IsBrowserAddressBarProcess(string? processName) =>
        !string.IsNullOrWhiteSpace(processName)
        && BrowserAddressBarProcesses.Contains(processName.Trim());

    private static string TryReadWindowTitle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return string.Empty;
        try
        {
            var sb = new StringBuilder(512);
            int written = NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            return written > 0 ? sb.ToString() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static (ReplacementSafetyProfile Profile, ReplacementExecutionPath Path, string AdapterName, string Reason) BuildAddressBarRoute(
        CandidateDecision decision,
        bool hasWritableValuePattern,
        bool exactAddressBarSurface)
    {
        string detail = exactAddressBarSurface
            ? "exact-address-bar"
            : hasWritableValuePattern
            ? "live-uia-first"
            : decision.Candidate is not null && !decision.RequiresLiveRuntimeRead
                ? "buffered-candidate-with-clipboard-verification"
                : "optimistic-after-uia-snapshot-miss";
        return (
            ReplacementSafetyProfile.NativeSafe,
            ReplacementExecutionPath.BrowserAddressBarLiveTokenBackspace,
            "AddressBarLiveToken",
            $"profile={ReplacementSafetyProfile.NativeSafe} path={ReplacementExecutionPath.BrowserAddressBarLiveTokenBackspace} surface=browser-address-bar-word-token {detail}");
    }

    private static ReplacementSafetyProfile ClassifyReplacementSafetyProfile(
        bool isBrowserLikeContext,
        bool hasWritableValuePattern,
        bool safeOnlyMode,
        bool unsafeCustomSurface)
    {
        if (!isBrowserLikeContext)
            return ReplacementSafetyProfile.NativeSafe;

        if (hasWritableValuePattern)
            return ReplacementSafetyProfile.BrowserValuePatternSafe;

        return ReplacementSafetyProfile.UnsafeSkip;
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
        if (changedCount > 0
            && toggledSuffix.Any(char.IsLetter)
            && FullTokenPrefersLayoutLetterSuffix(suffix, toggledSuffix, interpretations))
        {
            return false;
        }

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

    private static bool FullTokenPrefersLayoutLetterSuffix(
        string suffix,
        string toggledSuffix,
        params string[] interpretations)
    {
        foreach (string interpretation in interpretations)
        {
            if (string.IsNullOrWhiteSpace(interpretation)
                || interpretation.Length <= suffix.Length
                || !interpretation.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            string core = interpretation[..^suffix.Length];
            CorrectionCandidate? fullCandidate = CorrectionHeuristics.Evaluate(interpretation, CorrectionMode.Auto);
            if (fullCandidate is null)
                continue;

            CorrectionCandidate? coreCandidate = string.IsNullOrWhiteSpace(core)
                ? null
                : CorrectionHeuristics.Evaluate(core, CorrectionMode.Auto);

            bool fullLooksCorrect = CorrectionHeuristics.LooksCorrectAsTyped(fullCandidate.ConvertedText)
                                    || CorrectionHeuristics.HasStrongAsTypedSignal(fullCandidate.ConvertedText);
            bool coreLooksCorrect = coreCandidate is not null
                                    && (CorrectionHeuristics.LooksCorrectAsTyped(coreCandidate.ConvertedText)
                                        || CorrectionHeuristics.HasStrongAsTypedSignal(coreCandidate.ConvertedText));

            if (fullLooksCorrect && !coreLooksCorrect)
                return true;

            if (fullLooksCorrect
                && coreLooksCorrect
                && fullCandidate.ConvertedText.Length > coreCandidate!.ConvertedText.Length
                && fullCandidate.Confidence >= coreCandidate.Confidence)
            {
                return true;
            }

            if (!coreLooksCorrect
                && coreCandidate is not null
                && fullCandidate.ConvertedText.Length > coreCandidate.ConvertedText.Length
                && fullCandidate.Confidence + 0.06 >= coreCandidate.Confidence)
            {
                return true;
            }

            if (coreCandidate is null
                && fullCandidate.ConvertedText.Length > core.Length
                && fullCandidate.Confidence >= 0.74)
            {
                return true;
            }
        }

        foreach (string interpretation in interpretations)
        {
            if (string.IsNullOrWhiteSpace(interpretation)
                || interpretation.Length <= toggledSuffix.Length
                || !interpretation.EndsWith(toggledSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            string core = interpretation[..^toggledSuffix.Length];
            if (CorrectionHeuristics.LooksCorrectAsTyped(interpretation)
                && !CorrectionHeuristics.LooksCorrectAsTyped(core))
            {
                return true;
            }
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
