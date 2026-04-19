using System.Text.Json;
using System.Text.RegularExpressions;
using Switcher.Core;

return await SelectorTrainerProgram.RunAsync(args);

internal static class SelectorTrainerProgram
{
    private static readonly Regex LogPattern = new(
        @"^(?<time>\d{2}:\d{2}:\d{2}\.\d{3}) \[(?<operation>[^\]]+)\] proc=(?<proc>.*?) adapter=(?<adapter>.*?) orig=(?<orig>.*?) conv=(?<conv>.*?) result=(?<result>\w+) reason=(?<reason>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        var options = ParseOptions(args.Skip(1).ToArray());

        return command switch
        {
            "extract" => await RunExtractAsync(options),
            "train" => await RunTrainAsync(options),
            _ => PrintUnknown(command)
        };
    }

    private static int PrintUnknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 1;
    }

    private static bool IsHelp(string value) =>
        value is "-h" or "--help" or "help";

    private static void PrintUsage()
    {
        Console.WriteLine("SelectorTrainer");
        Console.WriteLine("  extract --input <logFileOrDir> --output <dataset.jsonl>");
        Console.WriteLine("  train   --input <dataset.jsonl> --output <selector-model.json> [--epochs 220] [--rate 0.12]");
        Console.WriteLine();
        Console.WriteLine($"Default log directory: {GetDefaultLogDirectory()}");
        Console.WriteLine($"Default model output:  {LearnedSelectorRuntime.GetDefaultModelPath()}");
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
                continue;

            string key = arg[2..];
            string value = (i + 1) < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++i]
                : "true";
            options[key] = value;
        }

        return options;
    }

    private static async Task<int> RunExtractAsync(IReadOnlyDictionary<string, string> options)
    {
        string inputPath = GetOption(options, "input", GetDefaultLogDirectory());
        string outputPath = GetOption(
            options,
            "output",
            Path.Combine(Environment.CurrentDirectory, "selector-dataset.jsonl"));

        var files = ResolveInputFiles(inputPath);
        if (files.Count == 0)
        {
            Console.Error.WriteLine($"No log files found for input: {inputPath}");
            return 1;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        int totalLines = 0;
        int written = 0;
        await using var writer = new StreamWriter(outputPath, append: false);

        foreach (string file in files)
        {
            foreach (string line in File.ReadLines(file))
            {
                totalLines++;
                if (!TryParseLogLine(line, out var row))
                    continue;

                TrainingExample? example = BuildExample(file, row);
                if (example is null)
                    continue;

                string json = JsonSerializer.Serialize(example, JsonOptions());
                await writer.WriteLineAsync(json);
                written++;
            }
        }

        Console.WriteLine($"Extracted {written} examples from {files.Count} files ({totalLines} lines) -> {outputPath}");
        return 0;
    }

    private static async Task<int> RunTrainAsync(IReadOnlyDictionary<string, string> options)
    {
        string inputPath = GetOption(
            options,
            "input",
            Path.Combine(Environment.CurrentDirectory, "selector-dataset.jsonl"));
        string outputPath = GetOption(options, "output", LearnedSelectorRuntime.GetDefaultModelPath());
        int epochs = GetIntOption(options, "epochs", 220);
        double learningRate = GetDoubleOption(options, "rate", 0.12);

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Dataset file not found: {inputPath}");
            return 1;
        }

        var examples = new List<TrainingExample>();
        foreach (string line in File.ReadLines(inputPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var example = JsonSerializer.Deserialize<TrainingExample>(line, JsonOptions());
            if (example is not null)
                examples.Add(example);
        }

        var labeled = examples.Where(x => x.Label.HasValue).ToList();
        if (labeled.Count == 0)
        {
            Console.Error.WriteLine("Dataset has no labeled examples.");
            return 1;
        }

        var bundle = new LearnedSelectorBundle();
        foreach (var group in labeled.GroupBy(x => x.Direction, StringComparer.OrdinalIgnoreCase))
        {
            List<TrainingExample> groupExamples = group.ToList();
            int positives = groupExamples.Count(x => x.Label == true);
            int negatives = groupExamples.Count(x => x.Label == false);
            if (positives == 0 || negatives == 0)
            {
                Console.WriteLine($"Skipping {group.Key}: need both positive and negative examples (pos={positives}, neg={negatives}).");
                continue;
            }

            LearnedSelectorModel model = TrainModel(group.Key, groupExamples, epochs, learningRate);
            bundle.Models.Add(model);
            Console.WriteLine($"Trained {group.Key}: pos={positives} neg={negatives} threshold={model.Threshold:F2}");
        }

        if (bundle.Models.Count == 0)
        {
            Console.Error.WriteLine("No models were trained.");
            return 1;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        string json = JsonSerializer.Serialize(bundle, JsonOptions());
        await File.WriteAllTextAsync(outputPath, json);
        Console.WriteLine($"Wrote selector model -> {outputPath}");
        return 0;
    }

    private static TrainingExample? BuildExample(string file, DiagnosticLogRow row)
    {
        string original = KeyboardLayoutMap.NormalizeWordToken(row.Original);
        if (string.IsNullOrWhiteSpace(original))
            return null;

        string candidate = row.Converted != "-"
            ? KeyboardLayoutMap.NormalizeWordToken(row.Converted)
            : ToggleCandidate(original);

        if (string.IsNullOrWhiteSpace(candidate) || string.Equals(original, candidate, StringComparison.Ordinal))
            return null;

        CorrectionDirection direction = InferDirection(original, candidate);
        if (direction == CorrectionDirection.None)
            return null;

        SelectorFeatureVector? vector = CorrectionHeuristics.BuildSelectorFeatures(original, candidate, direction);
        if (vector is null)
            return null;

        return new TrainingExample(
            SourceFile: file,
            Timestamp: row.Time,
            Operation: row.Operation,
            Direction: direction.ToString(),
            Original: original,
            Converted: candidate,
            Result: row.Result,
            Reason: row.Reason,
            Label: InferWeakLabel(row.Result, row.Reason),
            Features: new Dictionary<string, double>(vector.ToFeatureMap(), StringComparer.Ordinal));
    }

    private static List<string> ResolveInputFiles(string inputPath)
    {
        if (File.Exists(inputPath))
            return new List<string> { Path.GetFullPath(inputPath) };

        if (!Directory.Exists(inputPath))
            return new List<string>();

        var diagnosticsFiles = Directory
            .EnumerateFiles(inputPath, "switcher-*.log", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (diagnosticsFiles.Count > 0)
            return diagnosticsFiles;

        return Directory
            .EnumerateFiles(inputPath, "*.log", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryParseLogLine(string line, out DiagnosticLogRow row)
    {
        row = default;
        Match match = LogPattern.Match(line);
        if (!match.Success)
            return false;

        row = new DiagnosticLogRow(
            match.Groups["time"].Value,
            match.Groups["operation"].Value,
            match.Groups["proc"].Value,
            match.Groups["adapter"].Value,
            match.Groups["orig"].Value,
            match.Groups["conv"].Value,
            match.Groups["result"].Value,
            match.Groups["reason"].Value);
        return true;
    }

    private static string ToggleCandidate(string original)
    {
        string toggled = KeyboardLayoutMap.ToggleLayoutText(original, out int changed);
        return changed == 0 ? string.Empty : KeyboardLayoutMap.NormalizeWordToken(toggled);
    }

    private static CorrectionDirection InferDirection(string original, string candidate)
    {
        ScriptType originalScript = KeyboardLayoutMap.ClassifyScript(original);
        ScriptType candidateScript = KeyboardLayoutMap.ClassifyScript(candidate);

        if (originalScript == ScriptType.Latin && candidateScript == ScriptType.Cyrillic)
            return CorrectionDirection.EnToUa;
        if (originalScript == ScriptType.Cyrillic && candidateScript == ScriptType.Latin)
            return CorrectionDirection.UaToEn;

        return originalScript switch
        {
            ScriptType.Latin => CorrectionDirection.EnToUa,
            ScriptType.Cyrillic => CorrectionDirection.UaToEn,
            _ => CorrectionDirection.None
        };
    }

    private static bool? InferWeakLabel(string result, string reason)
    {
        if (string.Equals(result, "Replaced", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.Equals(result, "Skipped", StringComparison.OrdinalIgnoreCase))
            return null;

        string lower = reason.ToLowerInvariant();
        if (lower.StartsWith("no conversion", StringComparison.Ordinal)
            || lower.StartsWith("too short", StringComparison.Ordinal)
            || lower.Contains("selector=", StringComparison.Ordinal)
            || lower.Contains("learned=", StringComparison.Ordinal)
            || lower.Contains("mixed script", StringComparison.Ordinal)
            || lower.Contains("latin word", StringComparison.Ordinal)
            || lower.Contains("cyrillic word", StringComparison.Ordinal))
            return false;

        return null;
    }

    private static LearnedSelectorModel TrainModel(
        string direction,
        IReadOnlyList<TrainingExample> examples,
        int epochs,
        double learningRate)
    {
        var weights = SelectorFeatureVector.FeatureNames.ToDictionary(name => name, _ => 0.0, StringComparer.Ordinal);
        int positives = examples.Count(x => x.Label == true);
        int negatives = examples.Count - positives;
        double intercept = Math.Log((positives + 1.0) / (negatives + 1.0));
        var rng = new Random(1234);
        var shuffled = examples.ToList();

        for (int epoch = 0; epoch < epochs; epoch++)
        {
            Shuffle(shuffled, rng);
            double step = learningRate / (1.0 + (epoch * 0.03));

            foreach (TrainingExample example in shuffled)
            {
                double label = example.Label == true ? 1.0 : 0.0;
                double score = intercept;
                foreach (string featureName in SelectorFeatureVector.FeatureNames)
                {
                    example.Features.TryGetValue(featureName, out double value);
                    score += weights[featureName] * value;
                }

                double probability = Sigmoid(score);
                double error = label - probability;
                intercept += step * error;

                foreach (string featureName in SelectorFeatureVector.FeatureNames)
                {
                    example.Features.TryGetValue(featureName, out double value);
                    weights[featureName] += step * ((error * value) - (0.0015 * weights[featureName]));
                }
            }
        }

        return new LearnedSelectorModel
        {
            Direction = direction,
            Intercept = intercept,
            Threshold = PickThreshold(examples, intercept, weights),
            Weights = weights
        };
    }

    private static double PickThreshold(
        IReadOnlyList<TrainingExample> examples,
        double intercept,
        IReadOnlyDictionary<string, double> weights)
    {
        double bestThreshold = 0.55;
        double bestScore = double.NegativeInfinity;

        for (int raw = 35; raw <= 80; raw += 5)
        {
            double threshold = raw / 100.0;
            int tp = 0, fp = 0, fn = 0;

            foreach (TrainingExample example in examples)
            {
                bool label = example.Label == true;
                bool predicted = Predict(example.Features, intercept, weights) >= threshold;

                if (predicted && label) tp++;
                else if (predicted) fp++;
                else if (label) fn++;
            }

            double precision = tp / (double)Math.Max(1, tp + fp);
            double recall = tp / (double)Math.Max(1, tp + fn);
            double score = FScore(precision, recall, beta: 0.5);

            if (score > bestScore)
            {
                bestScore = score;
                bestThreshold = threshold;
            }
        }

        return bestThreshold;
    }

    private static double Predict(
        IReadOnlyDictionary<string, double> features,
        double intercept,
        IReadOnlyDictionary<string, double> weights)
    {
        double score = intercept;
        foreach (string featureName in SelectorFeatureVector.FeatureNames)
        {
            features.TryGetValue(featureName, out double value);
            weights.TryGetValue(featureName, out double weight);
            score += weight * value;
        }

        return Sigmoid(score);
    }

    private static double FScore(double precision, double recall, double beta)
    {
        double beta2 = beta * beta;
        double denominator = (beta2 * precision) + recall;
        if (denominator <= 0)
            return 0.0;

        return ((1 + beta2) * precision * recall) / denominator;
    }

    private static double Sigmoid(double value) =>
        1.0 / (1.0 + Math.Exp(-value));

    private static void Shuffle<T>(IList<T> values, Random rng)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }

    private static string GetOption(
        IReadOnlyDictionary<string, string> options,
        string key,
        string fallback) =>
        options.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    private static int GetIntOption(
        IReadOnlyDictionary<string, string> options,
        string key,
        int fallback) =>
        options.TryGetValue(key, out string? value) && int.TryParse(value, out int parsed)
            ? parsed
            : fallback;

    private static double GetDoubleOption(
        IReadOnlyDictionary<string, string> options,
        string key,
        double fallback) =>
        options.TryGetValue(key, out string? value) && double.TryParse(value, out double parsed)
            ? parsed
            : fallback;

    private static string GetDefaultLogDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Switcher");

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}

internal readonly record struct DiagnosticLogRow(
    string Time,
    string Operation,
    string Process,
    string Adapter,
    string Original,
    string Converted,
    string Result,
    string Reason);

internal sealed record TrainingExample(
    string SourceFile,
    string Timestamp,
    string Operation,
    string Direction,
    string Original,
    string Converted,
    string Result,
    string Reason,
    bool? Label,
    Dictionary<string, double> Features);
