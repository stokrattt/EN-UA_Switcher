using Switcher.Core;
using Switcher.Infrastructure;

namespace Switcher.Engine;

internal enum ReplacementSafetyProfile
{
    NativeSafe,
    BrowserValuePatternSafe,
    BrowserBestEffort,
    ElectronUiaSafe,
    UnsafeSkip
}

internal enum ReplacementExecutionPath
{
    NativeSelectionTransaction,
    BrowserAddressBarBufferedBackspace,
    BrowserAddressBarLiveTokenBackspace,
    BrowserValuePattern,
    ClipboardAssistedSelection,
    ElectronUiaBackspaceReplace,
    UnsafeSkip
}

internal enum CandidateSource
{
    None,
    PrimaryHeuristics,
    VkFallback,
    LiveRuntimeRead
}

internal sealed record BufferQualitySnapshot(
    int ApproxWordLength,
    int RecoveryCount,
    int DroppedKeyCount,
    int SequentialScanCount)
{
    public bool HasChromeLikeGarbage => SequentialScanCount >= 3;
    public bool NeedsLiveDiscovery =>
        HasChromeLikeGarbage
        || RecoveryCount > 0
        || DroppedKeyCount > 0;
}

internal sealed record WordSnapshot(
    DateTime CapturedAtUtc,
    ForegroundContext Context,
    ITextTargetAdapter? ReadAdapter,
    string ProcessName,
    string ControlClass,
    string WindowClass,
    string CurrentSentence,
    string LayoutTag,
    uint DelimiterVk,
    string LiveWord,
    string VisibleWord,
    string VisibleTrailingSuffix,
    string WordEN,
    string WordUA,
    string VkWordEN,
    string VkWordUA,
    string AnalysisWordEN,
    string AnalysisWordUA,
    string AnalysisVkEN,
    string AnalysisVkUA,
    string RawDebug,
    string OriginalDisplay,
    string TechDetail,
    BufferQualitySnapshot BufferQuality)
{
    public string ExpectedOriginalWord =>
        !string.IsNullOrWhiteSpace(LiveWord)
            ? LiveWord
            : (!string.IsNullOrWhiteSpace(LayoutTag) && LayoutTag.Equals("UA", StringComparison.OrdinalIgnoreCase)
                ? (string.IsNullOrWhiteSpace(WordUA)
                    ? (!string.IsNullOrWhiteSpace(VisibleWord) ? VisibleWord : WordEN)
                    : WordUA)
                : (string.IsNullOrWhiteSpace(WordEN)
                    ? (!string.IsNullOrWhiteSpace(VisibleWord) ? VisibleWord : WordUA)
                    : WordEN));
}

internal sealed record CandidateDecision(
    CorrectionCandidate? Candidate,
    CandidateSource Source,
    bool RequiresLiveRuntimeRead,
    SelectorFeatureVector? SelectorFeatures,
    LearnedSelectorDecision? LearnedDecision,
    string Reason)
{
    public bool ShouldReplace => Candidate is not null;
}

internal sealed record ReplacementPlan(
    WordSnapshot Snapshot,
    CandidateDecision Decision,
    ReplacementSafetyProfile SafetyProfile,
    ReplacementExecutionPath ExecutionPath,
    string AdapterName,
    string Reason
);
