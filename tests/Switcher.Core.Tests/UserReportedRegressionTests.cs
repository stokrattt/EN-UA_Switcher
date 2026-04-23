using Switcher.Core;
using Xunit;

namespace Switcher.Core.Tests;

public class UserReportedWordRegressionTests
{
    private static CorrectionCandidate? Evaluate(string word) =>
        CorrectionHeuristics.Evaluate(word, CorrectionMode.Auto);

    [Theory]
    [InlineData("вщп", "dog")]
    [InlineData("вщпі", "dogs")]
    [InlineData("луні", "keys")]
    [InlineData("вщпб", "dog,")]
    [InlineData("vs;", "між")]
    [InlineData("uskre", "гілку")]
    [InlineData("юТУЕ", ".NET")]
    [InlineData("пщщпду", "google")]
    [InlineData("ghbdsn", "привіт")]
    [InlineData("вгкфешщт", "duration")]
    public void Evaluate_UserReportedWrongLayoutWords_AutoMode_Converts(string typed, string expected)
    {
        var result = Evaluate(typed);

        Assert.NotNull(result);
        Assert.Equal(expected, result!.ConvertedText);
    }

    [Theory]
    [InlineData("dog")]
    [InlineData("dogs")]
    [InlineData("keys")]
    [InlineData(".NET")]
    [InlineData("google")]
    [InlineData("duration")]
    [InlineData("привіт")]
    [InlineData("між")]
    [InlineData("гілку")]
    public void Evaluate_UserReportedReferenceWords_AutoMode_DoesNotConvert(string word)
    {
        var result = Evaluate(word);

        Assert.Null(result);
    }
}
