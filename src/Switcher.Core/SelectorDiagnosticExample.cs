namespace Switcher.Core;

public record SelectorDiagnosticExample(
    DateTime TimestampUtc,
    string ProcessName,
    string WindowClass,
    string OriginalText,
    string ConvertedText,
    string Direction,
    string Stage,
    bool Accepted,
    double Probability,
    IReadOnlyDictionary<string, double> Features
);
