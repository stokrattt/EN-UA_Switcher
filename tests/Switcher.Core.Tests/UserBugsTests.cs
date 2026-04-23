using System.Collections.Generic;
using System.Reflection;
using Switcher.Core;
using Xunit;
using Xunit.Abstractions;

public class UserBugsTests
{
    private readonly ITestOutputHelper _output;

    public UserBugsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Run()
    {
        const string w1 = "дштлю";
        const string w2 = "ьйее";
        const string w3 = "фіва";
        const string w4 = "ролд";

        var c1 = CorrectionHeuristics.Evaluate(w1, CorrectionMode.Auto);
        var c2 = CorrectionHeuristics.Evaluate(w2, CorrectionMode.Auto);
        var c3 = CorrectionHeuristics.Evaluate(w3, CorrectionMode.Auto);
        var c4 = CorrectionHeuristics.Evaluate(w4, CorrectionMode.Auto);

        double srcScore = InvokeRequiredDoubleMethod("ScoreCyrillic", w1);
        double mqttScore = InvokeRequiredDoubleMethod("ScoreEnglish", "mqtt");
        _output.WriteLine($"{w1} -> {c1?.ConvertedText ?? "NULL"} (src: {srcScore:F3}, dst mqtt: {mqttScore:F3})");

        Dictionary<string, double> enFreq = GetRequiredBigramFreq("EnBigramFreq");
        Dictionary<string, double> uaFreq = GetRequiredBigramFreq("UaBigramFreq");

        double zUa = InvokeRequiredZeroBigramRatio(w2, uaFreq);
        double zEn = InvokeRequiredZeroBigramRatio("mqtt", enFreq);

        _output.WriteLine($"ьйее (mqtt) -> zeroUA: {zUa:F3}, zeroEN: {zEn:F3}");
        _output.WriteLine($"{w2} -> {c2?.ConvertedText ?? "NULL"}");
        _output.WriteLine($"{w3} -> {c3?.ConvertedText ?? "NULL"}");
        _output.WriteLine($"{w4} -> {c4?.ConvertedText ?? "NULL"}");

        var c5 = CorrectionHeuristics.Evaluate("дштл", CorrectionMode.Auto);
        _output.WriteLine($"дштл -> {c5?.ConvertedText ?? "NULL"}");
    }

    private static double InvokeRequiredDoubleMethod(string methodName, string value)
    {
        MethodInfo method = GetRequiredMethod(methodName);
        object? result = method.Invoke(null, [value]);
        return result is double score
            ? score
            : throw new InvalidOperationException($"{methodName} returned unexpected result.");
    }

    private static double InvokeRequiredZeroBigramRatio(string value, Dictionary<string, double> freq)
    {
        MethodInfo method = GetRequiredMethod("ZeroBigramRatio");
        object? result = method.Invoke(null, [value, freq]);
        return result is double ratio
            ? ratio
            : throw new InvalidOperationException("ZeroBigramRatio returned unexpected result.");
    }

    private static Dictionary<string, double> GetRequiredBigramFreq(string fieldName)
    {
        FieldInfo field = typeof(CorrectionHeuristics).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");

        return field.GetValue(null) as Dictionary<string, double>
            ?? throw new InvalidOperationException($"Field {fieldName} did not contain the expected dictionary.");
    }

    private static MethodInfo GetRequiredMethod(string methodName) =>
        typeof(CorrectionHeuristics).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Method {methodName} was not found.");
}
