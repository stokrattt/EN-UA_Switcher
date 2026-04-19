using Switcher.Core;
using Xunit;

namespace Switcher.Core.Tests;

public class AutoContextGuardsTests
{
    [Fact]
    public void GetUnsafeAutoCorrectionReason_UrlSentence_ReturnsReason()
    {
        string? reason = AutoContextGuards.GetUnsafeAutoCorrectionReason(
            "docs",
            "https://example.com/docs?id=1",
            "chrome");

        Assert.NotNull(reason);
    }

    [Fact]
    public void GetUnsafeAutoCorrectionReason_CommandSentence_ReturnsReason()
    {
        string? reason = AutoContextGuards.GetUnsafeAutoCorrectionReason(
            "build",
            "dotnet build Switcher.sln",
            "cmd");

        Assert.NotNull(reason);
    }

    [Fact]
    public void GetUnsafeAutoCorrectionReason_CodeSentence_ReturnsReason()
    {
        string? reason = AutoContextGuards.GetUnsafeAutoCorrectionReason(
            "token",
            "const token = value;",
            "code");

        Assert.NotNull(reason);
    }

    [Fact]
    public void GetUnsafeAutoCorrectionReason_CommandFlag_ReturnsReason()
    {
        string? reason = AutoContextGuards.GetUnsafeAutoCorrectionReason(
            "--help",
            null,
            "pwsh");

        Assert.NotNull(reason);
    }

    [Fact]
    public void GetUnsafeAutoCorrectionReason_ShortToken_ReturnsReason()
    {
        string? reason = AutoContextGuards.GetUnsafeAutoCorrectionReason(
            "hi",
            null,
            "telegram");

        Assert.NotNull(reason);
    }

    [Fact]
    public void GetUnsafeAutoCorrectionReason_ConvertibleLayoutShortTokenWithLeadingPunctuation_ReturnsNull()
    {
        string? reason = AutoContextGuards.GetUnsafeAutoCorrectionReason(
            ".kz",
            null,
            "chrome");

        Assert.Null(reason);
    }

    [Theory]
    [InlineData("oj")]
    [InlineData("nb")]
    [InlineData("шт")]
    [InlineData("ьн")]
    public void GetUnsafeAutoCorrectionReason_ConvertibleLetterOnlyShortToken_ReturnsNull(string token)
    {
        string? reason = AutoContextGuards.GetUnsafeAutoCorrectionReason(
            token,
            null,
            "chrome");

        Assert.Null(reason);
    }

    [Theory]
    [InlineData("цшт11")]
    [InlineData("кеч4050")]
    [InlineData("екч4050")]
    public void GetUnsafeAutoCorrectionReason_ConvertibleTechnicalMixedTokenInChat_ReturnsNull(string token)
    {
        string? reason = AutoContextGuards.GetUnsafeAutoCorrectionReason(
            token,
            null,
            "telegram");

        Assert.Null(reason);
    }

    [Fact]
    public void GetUnsafeAutoCorrectionReason_ConvertibleTechnicalMixedTokenInCode_ReturnsReason()
    {
        string? reason = AutoContextGuards.GetUnsafeAutoCorrectionReason(
            "цшт11",
            null,
            "code");

        Assert.NotNull(reason);
    }

    [Fact]
    public void GetUnsafeAutoCorrectionReason_TechnicalMixedToken_ReturnsReason()
    {
        string? reason = AutoContextGuards.GetUnsafeAutoCorrectionReason(
            "win11",
            null,
            "code");

        Assert.NotNull(reason);
    }

    [Fact]
    public void GetUnsafeAutoCorrectionReason_MistypedCommandStarter_ReturnsReason()
    {
        string? reason = AutoContextGuards.GetUnsafeAutoCorrectionReason(
            "вщетуе",
            "вщетуе build Switcher.sln",
            "pwsh");

        Assert.NotNull(reason);
    }

    [Fact]
    public void GetUnsafeAutoCorrectionReason_PlainChatWord_ReturnsNull()
    {
        string? reason = AutoContextGuards.GetUnsafeAutoCorrectionReason(
            "привіт",
            "це звичайний чат привіт",
            "telegram");

        Assert.Null(reason);
    }

    [Theory]
    [InlineData("ш", "фь ш дфеу")]
    [InlineData("вщслук", "check вщслук now")]
    public void GetUnsafeAutoCorrectionReason_ChatPhraseToken_NotTreatedAsCommand(string token, string sentence)
    {
        string? reason = AutoContextGuards.GetUnsafeAutoCorrectionReason(
            token,
            sentence,
            "telegram");

        Assert.Null(reason);
    }
}
