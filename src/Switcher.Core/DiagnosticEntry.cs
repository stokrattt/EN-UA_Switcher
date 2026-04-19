namespace Switcher.Core;

public enum OperationType { AutoMode, SafeLastWord, SafeSelection }
public enum DiagnosticResult { Replaced, Skipped, Unsupported, Error }

public record DiagnosticEntry(
    DateTime Timestamp,
    string ProcessName,
    string WindowClass,
    string AdapterName,
    bool TargetSupported,
    OperationType Operation,
    string OriginalText,
    string? ConvertedText,
    DiagnosticResult Result,
    string Reason
);
