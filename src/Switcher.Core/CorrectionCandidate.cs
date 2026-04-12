namespace Switcher.Core;

public enum CorrectionDirection { None, EnToUa, UaToEn }
public enum CorrectionMode { Safe, Auto }

public record CorrectionCandidate(
    string OriginalText,
    string ConvertedText,
    CorrectionDirection Direction,
    double Confidence,
    string Reason
);
