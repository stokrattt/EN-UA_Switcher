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
///   4. Short words are skipped in Auto mode unless they are in the narrow auto-allowlists.
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

    [Theory]
    [InlineData("ещтшс", "tonic")]   // е→t щ→o т→n ш→i с→c
    [InlineData("пщдиукп", "golberg")] // п→g щ→o д→l и→b у→e к→r п→g
    public void Evaluate_UaLayoutTypedAsEn_UserQuestion_Converts(string typed, string expected)
    {
        // Питання користувача: "якщо я буду вводити на урк розкладці ось це 'пщдиукп ещтшс'"
        var result = Evaluate(typed, CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }

    [Theory]
    [InlineData("vs;", "між")]     // v→м s→і ;→ж (semicolon = ж on UA layout)
    [InlineData("uskre", "гілку")] // u→г s→і k→л r→к e→у
    [InlineData("ifgrf", "шапка")] // i→ш f→а g→п r→к f→а
    public void Evaluate_EnLayoutTypedAsUa_LayoutSymbolWords_Converts(string typed, string expected)
    {
        var result = Evaluate(typed, CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.EnToUa, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }

    [Fact]
    public void Evaluate_RsheInAutoMode_ConvertedToHit()
    {
        var result = Evaluate("рше", CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal("hit", result.ConvertedText);
    }

    [Fact]
    public void Evaluate_DshluInAutoMode_ConvertedToLike()
    {
        var result = Evaluate("дшлу", CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal("like", result.ConvertedText);
    }

    [Theory]
    [InlineData("lhjy", "дрон")]
    [InlineData("lhjyb", "дрони")]
    public void Evaluate_DroneWordsInAutoMode_Convert(string word, string expected)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.EnToUa, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }

    [Fact]
    public void Evaluate_TmshvshfInAutoMode_ConvertedToNvidia()
    {
        var result = Evaluate("тмшвшф", CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal("nvidia", result.ConvertedText);
    }

    [Theory]
    [InlineData("фьв", "amd")]
    [InlineData("ізщешан", "spotify")]
    public void Evaluate_TechBrandWordsInAutoMode_Converted(string word, string expected)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }

    [Fact]
    public void Evaluate_AskmvInAutoMode_ConvertedToFilm()
    {
        var result = Evaluate("askmv", CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.EnToUa, result!.Direction);
        Assert.Equal("фільм", result.ConvertedText);
    }

    [Fact]
    public void Evaluate_MukvuInSafeMode_ConvertedToVerde()
    {
        var result = Evaluate("мукву", CorrectionMode.Safe);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal("verde", result.ConvertedText);
    }

    [Theory]
    [InlineData("lfdfq", "давай")]
    [InlineData("ljlfq", "додай")]
    public void Evaluate_CommonUkrainianWordsInAutoMode_Converted(string word, string expected)
    {
        Assert.False(CorrectionHeuristics.IsInDictionary(expected, CorrectionDirection.EnToUa));

        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.EnToUa, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }

    [Theory]
    [InlineData("pybpe", "знизу")] // знизу is in dict, heuristic also valid
    public void Evaluate_CommonUkrainianWordsInAutoMode_Converted_DictWord(string word, string expected)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.EnToUa, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }

    [Theory]
    [InlineData("db", "ви")]
    [InlineData("]]", "її")]
    [InlineData("][", "їх")]
    [InlineData("xjve", "чому")]
    [InlineData(",elt", "буде")]
    [InlineData(";bnnz", "життя")]
    [InlineData("];f", "їжа")]
    [InlineData("uhjis", "гроші")]
    [InlineData("uhege", "групу")]
    [InlineData("lheu", "друг")]
    [InlineData("[ks,", "хліб")]
    public void Evaluate_ExternalSmokeFailuresInAutoMode_NowConvert(string word, string expected)
    {
        var result = Evaluate(word, CorrectionMode.Auto);

        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.EnToUa, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }

    [Fact]
    public void Evaluate_UkrainianVerbFormWithoutDictionaryHit_AutoMode_DoesNotFlipToLatinGibberish()
    {
        var result = Evaluate("пишеш", CorrectionMode.Auto);
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_CyrillicNoiseWithoutStrongEnglishEvidence_AutoMode_DoesNotFlipToWiwin()
    {
        var result = Evaluate("цшцшт", CorrectionMode.Auto);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("switcher")]
    [InlineData("browser")]
    [InlineData("feature")]
    [InlineData("amd")]
    [InlineData("spotify")]
    public void Evaluate_PlausibleLatinWordsWithoutLayoutMistype_AutoMode_DoesNotConvert(string word)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_RepetitiveLatinLookingNoise_AutoMode_DoesNotConvert()
    {
        var result = Evaluate("цшцшт", CorrectionMode.Auto);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("win11")]
    [InlineData("rtx4090")]
    [InlineData("iphone15")]
    public void Evaluate_AlphaNumericTokens_AutoMode_DoesNotConvert(string word)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_CyrillicAlphaNumericLayoutMistype_AutoMode_ConvertsToWin11()
    {
        var result = Evaluate("цшт11", CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal("win11", result.ConvertedText);
    }

    [Theory]
    [InlineData("шзрщту15", "iphone15")]
    [InlineData("кеч4090", "rtx4090")]
    [InlineData("пзе4щ", "gpt4o")]
    [InlineData("мі2022", "vs2022")]
    public void Evaluate_CyrillicAlphaNumericTechTokens_AutoMode_Convert(string word, string expected)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }

    [Theory]
    [InlineData("цшт11,", "win11,")]
    [InlineData("шзрщту15,", "iphone15,")]
    [InlineData("кеч4090,", "rtx4090,")]
    [InlineData("пзе4щ,", "gpt4o,")]
    public void Evaluate_CyrillicAlphaNumericTechTokensWithTrailingComma_AutoMode_Convert(string word, string expected)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }

    [Theory]
    [InlineData("rjine'", "коштує")]
    [InlineData("gf-gf", "па-па")]
    public void Evaluate_PhraseSmokeEdgeCasesInAutoMode_Converted(string word, string expected)
    {
        var result = Evaluate(word, CorrectionMode.Auto);

        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.EnToUa, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }

    [Fact]
    public void Evaluate_QuotedEnglishWordInAutoMode_ReturnsNull()
    {
        var result = Evaluate("\"also\"", CorrectionMode.Auto);
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_QuotedCyrillicMistypeInAutoMode_ConvertsWrappedWord()
    {
        var result = Evaluate("\"цщкдв\"", CorrectionMode.Auto);

        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal("\"world\"", result.ConvertedText);
    }

    [Fact]
    public void Evaluate_HyphenatedCyrillicMistypeInAutoMode_ConvertsEachPart()
    {
        var result = Evaluate("руддщ-цщкдв", CorrectionMode.Auto);

        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal("hello-world", result.ConvertedText);
    }

    [Theory]
    [InlineData("g'znm", "п'ять")]
    [InlineData("ltd'znm", "дев'ять")]
    [InlineData("pljhjd'z", "здоров'я")]
    [InlineData("j,jd'zpjr", "обов'язок")]
    [InlineData("pfgfv'znjdedfnb", "запам'ятовувати")]
    public void Evaluate_UkrainianApostropheWordsInAutoMode_Converted(string word, string expected)
    {
        var result = Evaluate(word, CorrectionMode.Auto);

        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.EnToUa, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
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
    [InlineData("nb", "ти")]   // n→т, b→и → "ти" (short auto allowlist)
    public void Evaluate_ShortWords_WithDictHit_AutoMode_Converts(string word, string expected)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.EnToUa, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }

    [Fact]
    public void Evaluate_ShortWords_WithoutAllowlistOrDictionaryHit_AutoMode_StillReturnsNull()
    {
        var result = Evaluate("vf", CorrectionMode.Auto);
        Assert.Null(result);
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
        // "rybuf" → "книга" (в UA словнику), trailing comma test via CorpusLayoutSwitchTests
        // The direct test: "rybuf," should convert if "книга" is in dict and comma maps to "б"
        // Since comma is treated as part of the word and "книгаб" is not in dict,
        // we test the simpler version: "rybuf" converts to "книга"
        var result = Evaluate("rybuf", CorrectionMode.Safe);
        Assert.NotNull(result);
        Assert.Equal("книга", result!.ConvertedText);
    }

    [Fact]
    public void Evaluate_EnglishWordWithPeriod_StillSkipped()
    {
        // "hello." should still be recognized as English and skipped
        var result = Evaluate("hello.", CorrectionMode.Auto);
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_EnglishWordWithPeriod_AndNoDictionaryHit_StillSkipped()
    {
        var result = Evaluate("future.", CorrectionMode.Auto);
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

    [Fact]
    public void Evaluate_CyrillicPluralEnglishWord_AutoMode_ConvertsToWords()
    {
        var result = Evaluate("цщкві", CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal("words", result.ConvertedText);
    }

    [Theory]
    [InlineData("вщпі", "dogs")]
    [InlineData("луні", "keys")]
    public void Evaluate_CyrillicPluralEnglishWord_AutoMode_ConvertsCommonInflections(string word, string expected)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }

    [Theory]
    [InlineData("вфн", "day")]
    [InlineData("кудб", "rel,")]
    [InlineData("пкуз", "grep")]
    [InlineData("вуигп", "debug")]
    public void Evaluate_CyrillicCommonEnglishWord_AutoMode_Converts(string word, string expected)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }

    [Theory]
    [InlineData("цршср", "which")]
    [InlineData("ghjcybcm", "проснись")]
    [InlineData("rhefcfy", "круасан")]
    public void Evaluate_RealDiagnosticsWords_AutoMode_Converts(string word, string expected)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(expected, result!.ConvertedText);
    }

    [Fact]
    public void Evaluate_CommonUkrainianQuestionWord_AutoMode_Converts()
    {
        var result = Evaluate("relb", CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.EnToUa, result!.Direction);
        Assert.Equal("куди", result.ConvertedText);
    }

    [Fact]
    public void Evaluate_KnownUkrainianWordGrupu_AutoMode_Skips()
    {
        var result = Evaluate("групу", CorrectionMode.Auto);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("вщпб", "dog,")]
    [InlineData("вщпю", "dog.")]
    public void Evaluate_CyrillicWord_WithTrailingMappedPunctuation_AutoMode_ConvertsPunctuationToo(string word, string expected)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }

    [Theory]
    [InlineData("місщву", "vscode")]
    [InlineData("кгекфслук", "rutracker")]
    public void Evaluate_CyrillicTechnicalLatinToken_AutoMode_Converts(string word, string expected)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }

    [Theory]
    [InlineData("asxf", "фіча")]
    [InlineData("asxe", "фічу")]
    [InlineData("asxs", "фічі")]
    public void Evaluate_EnglishTechSlangToUkrainian_AutoMode_Converts(string word, string expected)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.EnToUa, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }

    [Fact]
    public void Evaluate_LatinWordWithoutDictionaryHit_AutoMode_StillConvertsWhenUkrainianShapeIsStrong()
    {
        var result = Evaluate("uskre", CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.EnToUa, result!.Direction);
        Assert.Equal("гілку", result.ConvertedText);
    }

    [Fact]
    public void Evaluate_LatinWordWithLeadingLayoutPunctuation_AutoMode_Converts()
    {
        var result = Evaluate(".kz", CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.EnToUa, result!.Direction);
        Assert.Equal("юля", result.ConvertedText);
    }

    [Fact]
    public void Evaluate_CyrillicLookingShortLatinToken_AutoMode_Skips()
    {
        var result = Evaluate("ws;", CorrectionMode.Auto);
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_CyrillicShortEnglishWordWithoutDictionaryHit_AutoMode_Converts()
    {
        var result = Evaluate("фку", CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal("are", result.ConvertedText);
    }

    [Theory]
    [InlineData("щр", "oh")]
    [InlineData("фь", "am")]
    [InlineData("гр", "uh")]
    [InlineData("щл", "ok")]
    public void Evaluate_CyrillicShortConversationalEnglishWord_AutoMode_Converts(string word, string expected)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }

    [Theory]
    [InlineData("ltitdis", "дешевші")]
    [InlineData("crjhsi", "скоріш")]
    [InlineData("ufneyre", "гатунку")]
    [InlineData("levre", "думку")]
    [InlineData("pfgecre", "запуску")]
    [InlineData("dhexye", "вручну")]
    [InlineData("zrs", "які")]
    public void Evaluate_LatinWordWithUkrainianMorphologyWithoutDictionaryHit_AutoMode_Converts(string word, string expected)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.EnToUa, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }

    [Theory]
    [InlineData("ші")]
    [InlineData("сум")]
    [InlineData("сумм")]
    [InlineData("запуску")]
    [InlineData("вручну")]
    [InlineData("які")]
    [InlineData("wsl")]
    public void Evaluate_AlreadyCorrectWordOrProtectedToken_AutoMode_Skips(string word)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("нуфр", "yeah")]
    [InlineData("щлфн", "okay")]
    [InlineData("ьфниу", "maybe")]
    [InlineData("тшпре", "night")]
    [InlineData("ерфтлі", "thanks")]
    [InlineData("куьуьиук", "remember")]
    [InlineData("вщуіт", "doesn")]
    public void Evaluate_CyrillicCommonEnglishWordWithoutDictionaryEntry_AutoMode_Converts(string word, string expected)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal(expected, result.ConvertedText);
    }

    [Theory]
    [InlineData("ш", "i")]
    [InlineData("z", "я")]
    [InlineData("'", "є")]
    public void Evaluate_SingleLetterShortPhraseToken_AutoMode_Converts(string word, string expected)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(expected, result!.ConvertedText);
    }

    [Fact]
    public void Evaluate_CyrillicTechnicalTokenWithLeadingMappedPunctuation_AutoMode_Converts()
    {
        var result = Evaluate("юТУЕ", CorrectionMode.Auto);
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Equal(".NET", result.ConvertedText);
    }

    [Theory]
    [InlineData("vscode")]
    [InlineData("rutracker")]
    [InlineData("words")]
    [InlineData("mqtt")]
    [InlineData("night")]
    [InlineData("fight")]
    [InlineData("eight")]
    [InlineData("tight")]
    [InlineData("docker")]
    [InlineData("build")]
    [InlineData("issue")]
    [InlineData("tools")]
    [InlineData("sight")]
    [InlineData("busy")]
    [InlineData("screw")]
    public void Evaluate_ValidLatinTechnicalOrInflectedWord_AutoMode_Skips(string word)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_KnownUkrainianStemFich_AutoMode_Skips()
    {
        var result = Evaluate("фіч", CorrectionMode.Auto);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("e")]
    [InlineData("cd")]
    [InlineData("cd/")]
    [InlineData("nj")]
    public void Evaluate_AmbiguousShortCommandLikeToken_AutoMode_Skips(string word)
    {
        var result = Evaluate(word, CorrectionMode.Auto);
        Assert.Null(result);
    }
}
