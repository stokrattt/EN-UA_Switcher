using System.Text.Json;

namespace Switcher.Core;

public static class LearnedSelectorRuntime
{
    private static readonly object Sync = new();
    private static string? _loadedPath;
    private static DateTime _loadedWriteUtc;
    private static LearnedSelectorBundle? _bundle;

    public static LearnedSelectorDecision? Evaluate(SelectorFeatureVector vector)
    {
        var bundle = LoadBundle();
        if (bundle?.Models is null || bundle.Models.Count == 0)
            return null;

        string direction = vector.Direction.ToString();
        var model = bundle.Models.FirstOrDefault(m => string.Equals(m.Direction, direction, StringComparison.OrdinalIgnoreCase));
        if (model is null)
            return null;

        var featureMap = vector.ToFeatureMap();
        double sum = model.Intercept;

        if (model.Weights is not null)
        {
            foreach (var (name, weight) in model.Weights)
            {
                if (featureMap.TryGetValue(name, out double value))
                    sum += weight * value;
            }
        }

        double probability = 1.0 / (1.0 + Math.Exp(-sum));
        double threshold = model.Threshold <= 0 ? 0.5 : model.Threshold;
        return new LearnedSelectorDecision(
            probability >= threshold,
            probability,
            $"learned={probability:F2}/{threshold:F2}");
    }

    public static string GetDefaultModelPath() =>
        Environment.GetEnvironmentVariable("SWITCHER_SELECTOR_MODEL")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Switcher",
            "selector-model.json");

    private static LearnedSelectorBundle? LoadBundle()
    {
        string path = GetDefaultModelPath();
        if (!File.Exists(path))
            return null;

        DateTime writeUtc = File.GetLastWriteTimeUtc(path);

        lock (Sync)
        {
            if (_bundle is not null
                && string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase)
                && _loadedWriteUtc == writeUtc)
                return _bundle;

            try
            {
                string json = File.ReadAllText(path);
                _bundle = JsonSerializer.Deserialize<LearnedSelectorBundle>(json, JsonOptions());
                _loadedPath = path;
                _loadedWriteUtc = writeUtc;
            }
            catch
            {
                _bundle = null;
                _loadedPath = path;
                _loadedWriteUtc = writeUtc;
            }

            return _bundle;
        }
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}

public sealed record LearnedSelectorDecision(bool Accept, double Probability, string Reason);

public sealed class LearnedSelectorBundle
{
    public int Version { get; set; } = 1;
    public List<LearnedSelectorModel> Models { get; set; } = new();
}

public sealed class LearnedSelectorModel
{
    public string Direction { get; set; } = string.Empty;
    public double Intercept { get; set; }
    public double Threshold { get; set; } = 0.5;
    public Dictionary<string, double> Weights { get; set; } = new(StringComparer.Ordinal);
}
