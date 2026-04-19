using Switcher.Core;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Switcher.Engine;

/// <summary>
/// Diagnostics logger with in-memory ring buffer (500 entries) and optional file output.
/// Thread-safe. Fires <see cref="EntryAdded"/> for UI listeners.
/// </summary>
public class DiagnosticsLogger
{
    private const int RingBufferSize = 500;
    private readonly Queue<DiagnosticEntry> _buffer = new();
    private readonly object _lock = new();
    private StreamWriter? _fileWriter;
    private StreamWriter? _selectorExportWriter;
    private bool _enabled;
    private bool _selectorExportEnabled;

    public event Action<DiagnosticEntry>? EntryAdded;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value)
                CloseFile();
        }
    }

    public void Configure(bool enabled, bool toFile = false, bool selectorExport = false)
    {
        _enabled = enabled;
        _selectorExportEnabled = selectorExport;
        if (toFile && enabled)
            OpenFile();
        else
            CloseFile();

        if (selectorExport)
            OpenSelectorExport();
        else
            CloseSelectorExport();
    }

    public void Log(DiagnosticEntry entry)
    {
        if (!_enabled) return;

        lock (_lock)
        {
            if (_buffer.Count >= RingBufferSize)
                _buffer.Dequeue();
            _buffer.Enqueue(entry);
            _fileWriter?.WriteLine(FormatFileEntry(entry));
            _fileWriter?.Flush();
        }

        EntryAdded?.Invoke(entry);
    }

    public void Log(
        string processName, string windowClass, string adapterName, bool targetSupported,
        OperationType operation, string originalText, string? convertedText,
        DiagnosticResult result, string reason)
    {
        Log(new DiagnosticEntry(
            DateTime.Now, processName, windowClass, adapterName, targetSupported,
            operation, originalText, convertedText, result, reason));
    }

    public void LogSelectorExample(SelectorDiagnosticExample example)
    {
        if (!_selectorExportEnabled)
            return;

        lock (_lock)
        {
            if (_selectorExportWriter == null)
                return;

            _selectorExportWriter.WriteLine(JsonSerializer.Serialize(example));
            _selectorExportWriter.Flush();
        }
    }

    public IReadOnlyList<DiagnosticEntry> GetEntries()
    {
        lock (_lock) return _buffer.ToList();
    }

    public void Clear()
    {
        lock (_lock) _buffer.Clear();
    }

    private static string FormatEntry(DiagnosticEntry e) =>
        $"{e.Timestamp:HH:mm:ss.fff} [{e.Operation}] proc={e.ProcessName} adapter={e.AdapterName} " +
        $"orig={e.OriginalText} conv={e.ConvertedText ?? "-"} result={e.Result} reason={e.Reason}";

    private static string FormatFileEntry(DiagnosticEntry e) =>
        $"{e.Timestamp:HH:mm:ss.fff} [{e.Operation}] proc={e.ProcessName} adapter={e.AdapterName} " +
        $"orig={RedactText(e.OriginalText)} conv={RedactText(e.ConvertedText)} result={e.Result} reason={e.Reason}";

    private static string RedactText(string? text)
    {
        if (string.IsNullOrEmpty(text) || text == "-")
            return "-";

        string normalized = KeyboardLayoutMap.NormalizeWordToken(text);
        string script = normalized.Length == 0
            ? "OTHER"
            : KeyboardLayoutMap.ClassifyScript(normalized).ToString().ToUpperInvariant();
        string hash = ComputeShortHash(text);
        return $"[redacted len={text.Length} norm={normalized.Length} script={script} hash={hash}]";
    }

    private static string ComputeShortHash(string value)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes[..4]);
    }

    private void OpenFile()
    {
        try
        {
            string dir = GetDiagnosticsDirectory();
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"switcher-{DateTime.Now:yyyy-MM-dd}.log");
            _fileWriter = new StreamWriter(path, append: true) { AutoFlush = false };
        }
        catch { }
    }

    private void CloseFile()
    {
        try { _fileWriter?.Dispose(); } catch { }
        _fileWriter = null;
    }

    private void OpenSelectorExport()
    {
        try
        {
            string dir = GetDiagnosticsDirectory();
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"selector-examples-{DateTime.Now:yyyy-MM-dd}.jsonl");
            _selectorExportWriter = new StreamWriter(path, append: true) { AutoFlush = false };
        }
        catch { }
    }

    private void CloseSelectorExport()
    {
        try { _selectorExportWriter?.Dispose(); } catch { }
        _selectorExportWriter = null;
    }

    private static string GetDiagnosticsDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Switcher");
}
