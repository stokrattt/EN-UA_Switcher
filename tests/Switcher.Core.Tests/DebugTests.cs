using Xunit;
using Xunit.Abstractions;
using Switcher.Core;

public class DebugTests {
    private readonly ITestOutputHelper _output;

    public DebugTests(ITestOutputHelper output) {
        _output = output;
    }

    [Fact]
    public void TestWords() {
        var words = new[] { "црут", "лшдд", "рудд", "агсл" };
        foreach (var w in words) {
            var c = CorrectionHeuristics.Evaluate(w, CorrectionMode.Auto);
            _output.WriteLine($"{w} -> {(c?.ConvertedText ?? "null")} (Reason: {c?.Reason ?? "N/A"})");
        }
    }
}
