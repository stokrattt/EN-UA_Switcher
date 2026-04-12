using Switcher.Core;

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
    private bool _enabled;

    public event Action<DiagnosticEntry>? EntryAdded;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value) CloseFile();
        }
    }

    public void Configure(bool enabled, bool toFile = false)
    {
        _enabled = enabled;
        if (toFile && enabled)
            OpenFile();
        else
            CloseFile();
    }

    public void Log(DiagnosticEntry entry)
    {
        if (!_enabled) return;

        lock (_lock)
        {
            if (_buffer.Count >= RingBufferSize)
                _buffer.Dequeue();
            _buffer.Enqueue(entry);
            _fileWriter?.WriteLine(FormatEntry(entry));
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

    private void OpenFile()
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Switcher");
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
}
