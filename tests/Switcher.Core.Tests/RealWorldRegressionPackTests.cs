using Switcher.Core;
using Xunit;

namespace Switcher.Core.Tests;

public class RealWorldHeuristicRegressionPackTests
{
    private static CorrectionCandidate? Evaluate(string word) =>
        CorrectionHeuristics.Evaluate(word, CorrectionMode.Auto);

    private static string Mistype(string word)
    {
        string typed = KeyboardLayoutMap.ToggleLayoutText(word, out int changedCount);
        Assert.True(changedCount > 0);
        return typed;
    }

    [Theory]
    [InlineData("askmv", "фільм", CorrectionDirection.EnToUa)]
    [InlineData("lhjy", "дрон", CorrectionDirection.EnToUa)]
    [InlineData("lhjyb", "дрони", CorrectionDirection.EnToUa)]
    [InlineData("дшлу", "like", CorrectionDirection.UaToEn)]
    [InlineData("тмшвшф", "nvidia", CorrectionDirection.UaToEn)]
    [InlineData("фьв", "amd", CorrectionDirection.UaToEn)]
    [InlineData("ізщешан", "spotify", CorrectionDirection.UaToEn)]
    [InlineData("цшт11", "win11", CorrectionDirection.UaToEn)]
    [InlineData("кеч4050", "rtx4050", CorrectionDirection.UaToEn)]
    public void Evaluate_ManualQaWords_AutoMode_Converts(string typed, string expected, CorrectionDirection direction)
    {
        var result = Evaluate(typed);

        Assert.NotNull(result);
        Assert.Equal(direction, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }

    [Theory]
    [InlineData("nvidia")]
    [InlineData("amd")]
    [InlineData("spotify")]
    [InlineData("telegram")]
    [InlineData("discord")]
    [InlineData("win11")]
    [InlineData("rtx4050")]
    public void Evaluate_ManualQaReferenceWords_AutoMode_DoesNotConvert(string word)
    {
        var result = Evaluate(word);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("docker")]
    [InlineData("build")]
    [InlineData("issue")]
    [InlineData("tools")]
    [InlineData("driver")]
    [InlineData("update")]
    public void Evaluate_TechChatHotspotWordTypedInWrongLayout_AutoMode_Converts(string expected)
    {
        var result = Evaluate(Mistype(expected));

        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }
}

public class RealWorldGuardRegressionPackTests
{
    [Theory]
    [InlineData("oj", "chrome")]
    [InlineData("nb", "chrome")]
    [InlineData("шт", "telegram")]
    [InlineData("ьн", "telegram")]
    [InlineData("цшт11", "telegram")]
    [InlineData("кеч4050", "telegram")]
    [InlineData("екч4050", "telegram")]
    public void GetUnsafeAutoCorrectionReason_ManualQaChatTokens_ReturnsNull(string token, string processName)
    {
        string? reason = AutoContextGuards.GetUnsafeAutoCorrectionReason(token, null, processName);
        Assert.Null(reason);
    }

    [Fact]
    public void GetUnsafeAutoCorrectionReason_ManualQaTechTokenInCode_ReturnsReason()
    {
        string? reason = AutoContextGuards.GetUnsafeAutoCorrectionReason("цшт11", null, "code");
        Assert.NotNull(reason);
    }

    [Fact]
    public void GetUnsafeAutoCorrectionReason_ManualQaPlainEnglishShortToken_ReturnsReason()
    {
        string? reason = AutoContextGuards.GetUnsafeAutoCorrectionReason("hi", null, "telegram");
        Assert.NotNull(reason);
    }
}