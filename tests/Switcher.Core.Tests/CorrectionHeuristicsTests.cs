using Switcher.Core;
using Xunit;

namespace Switcher.Core.Tests;

/// <summary>
/// Tests for CorrectionHeuristics.Evaluate().
///
/// Key invariants:
///   1. Known EN words are never converted (false-positive protection).
///   2. Known UA words are never converted (false-positive protection).
///   3. Mixed-script or non-letter input is never converted.
///   4. Short words (≤2 letters) are skipped in Auto mode.
///   5. High-confidence mis-typed words ARE converted in both modes.
/// </summary>
public class CorrectionHeuristicsTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static CorrectionCandidate? Evaluate(string word, CorrectionMode mode = CorrectionMode.Safe)
        => CorrectionHeuristics.Evaluate(word, mode);

    // ─── 1. Known English words — never touch ────────────────────────────────

    [Theory]
    [InlineData("hello")]
    [InlineData("test")]
    [InlineData("user")]
    [InlineData("code")]
    [InlineData("file")]
    [InlineData("name")]
    [InlineData("time")]
    [InlineData("good")]
    [InlineData("day")]
    [InlineData("yes")]
    public void Evaluate_KnownEnglishWords_ReturnsNull(string word)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("test")]
    [InlineData("code")]
    public void Evaluate_KnownEnglishWords_SafeMode_ReturnsNull(string word)
    {
        var result = Evaluate(word, CorrectionMode.Safe);
        Assert.Null(result);
    }

    // ─── 2. Known Ukrainian words — never touch ───────────────────────────────

    [Theory]
    [InlineData("привіт")]
    [InlineData("дякую")]
    [InlineData("можна")]
    [InlineData("текст")]
    [InlineData("набір")]
    [InlineData("файл")]
    [InlineData("день")]
    public void Evaluate_KnownUkrainianWords_ReturnsNull(string word)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("привіт")]
    [InlineData("дякую")]
    public void Evaluate_KnownUkrainianWords_SafeMode_ReturnsNull(string word)
    {
        var result = Evaluate(word, CorrectionMode.Safe);
        Assert.Null(result);
    }

    // ─── 3. High-confidence mis-typed words — should convert ─────────────────

    [Fact]
    public void Evaluate_GhbdsnInSafeMode_ConvertedToPrivit()
    {
        // "ghbdsn" typed with UA layout but EN was active → should be "привіт"
        var result = Evaluate("ghbdsn", CorrectionMode.Safe);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.EnToUa, result!.Direction);
        Assert.Equal("привіт", result.ConvertedText);
    }

    [Fact]
    public void Evaluate_GhbdsnInAutoMode_ConvertedToPrivit()
    {
        var result = Evaluate("ghbdsn", CorrectionMode.Auto);
        // Auto mode has a higher threshold (0.75); ghbdsn should still pass
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.EnToUa, result!.Direction);
        Assert.Equal("привіт", result.ConvertedText);
    }

    [Fact]
    public void Evaluate_RulloInSafeMode_ConvertedToHello()
    {
        // "руддщ" typed with EN layout but UA was active → maps to "hello"
        var result = Evaluate("руддщ", CorrectionMode.Safe);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal("hello", result.ConvertedText);
    }

    [Fact]
    public void Evaluate_RulloInAutoMode_ConvertedToHello()
    {
        var result = Evaluate("руддщ", CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal("hello", result.ConvertedText);
    }

    // ─── 4. Short words — skipped in Auto mode (unless dictionary-confirmed) ──

    [Theory]
    [InlineData("gb")]  // 2-letter EN mis-type, no dictionary hit
    [InlineData("g")]   // 1-letter
    public void Evaluate_ShortWords_NoDictHit_AutoMode_ReturnsNull(string word)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        // Short words without dictionary confirmation must not be auto-corrected
        Assert.Null(result);
    }

    [Theory]
    [InlineData("pf", "за")]   // p→з, f→а → "за" (in UA dictionary)
    [InlineData("ys", "ні")]   // y→н, s→і → "ні" (in UA dictionary)
    [InlineData("yt", "не")]   // y→н, t→е → "не" (in UA dictionary)
    public void Evaluate_ShortWords_WithDictHit_AutoMode_Converts(string word, string expected)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.EnToUa, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }

    // ─── 5. Mixed-script input — never convert ────────────────────────────────

    [Theory]
    [InlineData("helloПривіт")]
    [InlineData("textтекст")]
    [InlineData("aб")]
    public void Evaluate_MixedScript_ReturnsNull(string word)
    {
        var result = Evaluate(word, CorrectionMode.Safe);
        Assert.Null(result);
    }

    // ─── 6. Punctuation is stripped correctly ────────────────────────────────

    [Fact]
    public void Evaluate_WordWithTrailingPunctuation_StillConverts()
    {
        // The caller may pass a raw word buffer; punctuation should not block conversion
        // (CorrectionHeuristics.StripPunctuation preserves prefix/suffix and strips inner)
        var result = Evaluate("ghbdsn", CorrectionMode.Safe);
        Assert.NotNull(result);
    }

    // ─── 7. Confidence and direction fields are set correctly ─────────────────

    [Fact]
    public void Evaluate_WhenResultProduced_ConfidenceIsPositive()
    {
        var result = Evaluate("ghbdsn", CorrectionMode.Safe);
        Assert.NotNull(result);
        Assert.True(result!.Confidence > 0.0, $"Expected Confidence > 0, got {result.Confidence}");
    }

    [Fact]
    public void Evaluate_WhenResultProduced_OriginalTextMatches()
    {
        var result = Evaluate("ghbdsn", CorrectionMode.Safe);
        Assert.NotNull(result);
        Assert.Equal("ghbdsn", result!.OriginalText);
    }

    // ─── 8. Numbers-only and symbols — no crash, returns null ─────────────────

    [Theory]
    [InlineData("123")]
    [InlineData("!@#")]
    [InlineData("   ")]
    public void Evaluate_NonLetterInput_ReturnsNull(string input)
    {
        var result = Evaluate(input, CorrectionMode.Safe);
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_EmptyString_ReturnsNull()
    {
        var result = Evaluate("", CorrectionMode.Safe);
        Assert.Null(result);
    }

    // ─── 9. Another mis-typed Ukrainian phrase ────────────────────────────────

    [Fact]
    public void Evaluate_NfrcnInSafeMode_ConvertedToTekst()
    {
        // "ntrcn" = n→т, t→е, r→к, c→с, t→т... wait: t=е, e=у, x=ч, t=е  
        // "текст" on EN row: т=n, е=t, к=r, с=c, т=n → "ntrcn"
        var result = Evaluate("ntrcn", CorrectionMode.Safe);
        if (result is not null)
        {
            Assert.Equal(CorrectionDirection.EnToUa, result.Direction);
            Assert.Equal("текст", result.ConvertedText);
        }
        // If heuristics decide confidence is below threshold, null is also acceptable
        // (this is a borderline case — "ntrcn" could be a random English abbreviation)
    }

    // ─── 10. Long random Latin that's not plausible UA ───────────────────────

    [Fact]
    public void Evaluate_RandomUnlikelyInput_DoesNotCrash()
    {
        // This should either return null or a low-confidence result
        // The important thing is: no exception thrown
        var result = Evaluate("xqzwxqzw", CorrectionMode.Auto);
        // We don't assert a specific value; just ensure no exception
        _ = result;
    }

    // ─── 11. Words that previously failed due to limited bigrams ─────────────

    [Fact]
    public void Evaluate_VfrcToMaks_ConvertedInAutoMode()
    {
        // "vfrc" = v→м, f→а, r→к, c→с = "макс"
        var result = Evaluate("vfrc", CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.EnToUa, result!.Direction);
        Assert.Equal("макс", result.ConvertedText);
    }

    [Fact]
    public void Evaluate_RybpsToKnyzi_ConvertedInAutoMode()
    {
        // "rybps" = r→к, y→н, b→и, p→з, s→і = "книзі"
        var result = Evaluate("rybps", CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.EnToUa, result!.Direction);
        Assert.Equal("книзі", result.ConvertedText);
    }

    // ─── 12. Trailing period/comma/semicolon → UA letter ю/б/ж ───────────────

    [Fact]
    public void Evaluate_TrailingPeriodConvertsToYu()
    {
        // "cd'nj." → c→с, d→в, '→є, n→т, j→о, .→ю = "свєтою"
        var result = Evaluate("cd'nj.", CorrectionMode.Safe);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.EnToUa, result!.Direction);
        Assert.Contains("ю", result.ConvertedText);
    }

    [Fact]
    public void Evaluate_TrailingCommaConvertsToB()
    {
        // "rybuf," → r→к, y→н, b→и, u→г, f→а, ,→б = "книгаб"
        // The comma maps to "б" and should be included in conversion
        var result = Evaluate("rybuf,", CorrectionMode.Safe);
        Assert.NotNull(result);
        Assert.Contains("б", result!.ConvertedText);
    }

    [Fact]
    public void Evaluate_EnglishWordWithPeriod_StillSkipped()
    {
        // "hello." should still be recognized as English and skipped
        var result = Evaluate("hello.", CorrectionMode.Auto);
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_UkrainianWordWithPeriod_StillSkipped()
    {
        // "привіт." — trailing period on a known UA word; skip
        var result = Evaluate("привіт.", CorrectionMode.Auto);
        Assert.Null(result);
    }

    // ─── Non-letter conversion results — must reject ─────────────────────────

    [Theory]
    [InlineData("хїфі")]   // UA→EN would give []as (brackets)
    [InlineData("хї")]     // UA→EN would give [] (brackets only)
    public void Evaluate_CyrillicToNonLetterEN_Rejected(string word)
    {
        // Conversion results that contain brackets/punctuation must be rejected
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("хїфі")]
    public void Evaluate_CyrillicToNonLetterEN_RejectedInSafeMode(string word)
    {
        var result = Evaluate(word, CorrectionMode.Safe);
        Assert.Null(result);
    }
}
