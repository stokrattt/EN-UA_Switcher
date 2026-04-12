using System.Collections.ObjectModel;
using System.Windows;
using Switcher.Core;
using Switcher.Engine;

namespace Switcher.App;

public partial class DiagnosticsWindow : Window
{
    private readonly SwitcherEngine _engine;
    private readonly ObservableCollection<DiagnosticRow> _rows = new();

    public DiagnosticsWindow(SwitcherEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        LogList.ItemsSource = _rows;

        // Load existing entries
        foreach (var entry in engine.Diagnostics.GetEntries())
            _rows.Add(new DiagnosticRow(entry));

        UpdateCount();

        // Subscribe to new entries
        engine.Diagnostics.EntryAdded += OnEntryAdded;
        Closed += (_, _) => engine.Diagnostics.EntryAdded -= OnEntryAdded;
    }

    private void OnEntryAdded(DiagnosticEntry entry)
    {
        Dispatcher.Invoke(() =>
        {
            _rows.Add(new DiagnosticRow(entry));
            UpdateCount();
            if (ChkAutoScroll.IsChecked == true && _rows.Count > 0)
                LogList.ScrollIntoView(_rows[^1]);
        });
    }

    private void UpdateCount() =>
        TxtEntryCount.Text = $"{_rows.Count} entries";

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        _engine.Diagnostics.Clear();
        _rows.Clear();
        UpdateCount();
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        var lines = LogList.SelectedItems.Cast<DiagnosticRow>()
            .Select(r => $"{r.TimestampStr} [{r.Operation}] {r.ProcessName} | {r.ShortAdapter} | " +
                         $"{r.OriginalText} → {r.ConvertedText} | {r.Result} | {r.Reason}");
        string text = string.Join(Environment.NewLine, lines);
        if (!string.IsNullOrEmpty(text))
            System.Windows.Clipboard.SetText(text);
    }
}

public class DiagnosticRow
{
    public string TimestampStr  { get; }
    public string ProcessName   { get; }
    public string ShortAdapter  { get; }
    public string Operation     { get; }
    public string OriginalText  { get; }
    public string ConvertedText { get; }
    public string Result        { get; }
    public string Reason        { get; }

    public DiagnosticRow(DiagnosticEntry e)
    {
        TimestampStr  = e.Timestamp.ToString("HH:mm:ss.fff");
        ProcessName   = e.ProcessName;
        ShortAdapter  = e.AdapterName.Replace("TargetAdapter", "").Replace("Adapter", "");
        Operation     = e.Operation.ToString();
        OriginalText  = e.OriginalText;
        ConvertedText = e.ConvertedText ?? "-";
        Result        = e.Result.ToString();
        Reason        = e.Reason;
    }
}
