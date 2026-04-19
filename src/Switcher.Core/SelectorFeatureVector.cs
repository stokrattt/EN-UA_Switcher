namespace Switcher.Core;

public sealed record SelectorFeatureVector(
    CorrectionDirection Direction,
    string Original,
    string Converted,
    double SourceScore,
    double TargetScore,
    double Confidence,
    double Delta,
    double LengthNorm,
    double OriginalDistinctRatio,
    double ConvertedDistinctRatio,
    double OriginalVowelRatio,
    double ConvertedVowelRatio,
    double DigitRatio,
    double BoundaryAffinity,
    double RepeatPenalty,
    double SourceDictionarySignal,
    double TargetDictionarySignal,
    double TechnicalMarkerSignal)
{
    public static IReadOnlyList<string> FeatureNames { get; } = new[]
    {
        "source_score",
        "target_score",
        "confidence",
        "delta",
        "length_norm",
        "original_distinct_ratio",
        "converted_distinct_ratio",
        "original_vowel_ratio",
        "converted_vowel_ratio",
        "digit_ratio",
        "boundary_affinity",
        "repeat_penalty",
        "source_dictionary_signal",
        "target_dictionary_signal",
        "technical_marker_signal"
    };

    public IReadOnlyDictionary<string, double> ToFeatureMap() =>
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [FeatureNames[0]] = SourceScore,
            [FeatureNames[1]] = TargetScore,
            [FeatureNames[2]] = Confidence,
            [FeatureNames[3]] = Delta,
            [FeatureNames[4]] = LengthNorm,
            [FeatureNames[5]] = OriginalDistinctRatio,
            [FeatureNames[6]] = ConvertedDistinctRatio,
            [FeatureNames[7]] = OriginalVowelRatio,
            [FeatureNames[8]] = ConvertedVowelRatio,
            [FeatureNames[9]] = DigitRatio,
            [FeatureNames[10]] = BoundaryAffinity,
            [FeatureNames[11]] = RepeatPenalty,
            [FeatureNames[12]] = SourceDictionarySignal,
            [FeatureNames[13]] = TargetDictionarySignal,
            [FeatureNames[14]] = TechnicalMarkerSignal
        };
}
