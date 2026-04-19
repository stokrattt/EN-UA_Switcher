using Xunit;
using Xunit.Abstractions;
using Switcher.Core;

public class UserBugsTests {
    private readonly ITestOutputHelper _output;
    public UserBugsTests(ITestOutputHelper output) { _output = output; }

    [Fact]
    public void Run() {
        var w1 = "дштлю";
        var w2 = "ьйее";
        var w3 = "фіва";
        var w4 = "ролд";
        
        var c1 = CorrectionHeuristics.Evaluate(w1, CorrectionMode.Auto);
        var c2 = CorrectionHeuristics.Evaluate(w2, CorrectionMode.Auto);
        var c3 = CorrectionHeuristics.Evaluate(w3, CorrectionMode.Auto);
        var c4 = CorrectionHeuristics.Evaluate(w4, CorrectionMode.Auto);

        _output.WriteLine($"{w1} -> {c1?.ConvertedText ?? "NULL"} (src: {((double)typeof(CorrectionHeuristics).GetMethod("ScoreCyrillic", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, new object[] { w1 })):F3}, dst mqtt: {((double)typeof(CorrectionHeuristics).GetMethod("ScoreEnglish", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, new object[] { "mqtt" })):F3})");
        
        var enFreq = (System.Collections.Generic.Dictionary<string, double>)typeof(CorrectionHeuristics).GetField("EnBigramFreq", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
        var uaFreq = (System.Collections.Generic.Dictionary<string, double>)typeof(CorrectionHeuristics).GetField("UaBigramFreq", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
        
        var zbrMethod = typeof(CorrectionHeuristics).GetMethod("ZeroBigramRatio", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        double zUa = (double)zbrMethod.Invoke(null, new object[] { w2, uaFreq });
        double zEn = (double)zbrMethod.Invoke(null, new object[] { "mqtt", enFreq });
        
        _output.WriteLine($"ьйее (mqtt) -> zeroUA: {zUa:F3}, zeroEN: {zEn:F3}");
        _output.WriteLine($"{w2} -> {c2?.ConvertedText ?? "NULL"}");
        _output.WriteLine($"{w3} -> {c3?.ConvertedText ?? "NULL"}");
        var c5 = CorrectionHeuristics.Evaluate("дштл", CorrectionMode.Auto);
        _output.WriteLine($"дштл -> {c5?.ConvertedText ?? "NULL"}");
    }
}