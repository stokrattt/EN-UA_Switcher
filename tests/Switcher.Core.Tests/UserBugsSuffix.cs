using Xunit;
using Xunit.Abstractions;
using Switcher.Core;

public class UserBugsSuffixTests {
    private readonly ITestOutputHelper _output;
    public UserBugsSuffixTests(ITestOutputHelper output) { _output = output; }

    private static string ExtractLiteralTrailingPunctuation(string text) {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        int end = text.Length;
        while (end > 0 && !char.IsLetterOrDigit(text[end - 1]) && !KeyboardLayoutMap.IsLayoutLetterChar(text[end - 1]) && !KeyboardLayoutMap.IsWordConnector(text[end - 1]))
            end--;
        return text[end..];
    }

    [Fact]
    public void TestSuffix() {
        var r1 = ExtractLiteralTrailingPunctuation("дштлю");
        var r2 = ExtractLiteralTrailingPunctuation("link/");
        var r3 = ExtractLiteralTrailingPunctuation("дштл.");
        _output.WriteLine($"run for дштлю = '{r1}'");
        _output.WriteLine($"run for link/ = '{r2}'");
        _output.WriteLine($"run for дштл. = '{r3}'");
    }
}
