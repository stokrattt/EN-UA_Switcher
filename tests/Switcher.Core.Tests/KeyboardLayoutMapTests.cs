using Switcher.Core;
using Xunit;

namespace Switcher.Core.Tests;

public class KeyboardLayoutMapTests
{
    // ─── ConvertEnToUa ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ghbdsn", "привіт")]   // typical mis-typed Ukrainian word
    [InlineData("руддщ",  null)]        // Cyrillic input — not EN, strict returns null
    [InlineData("",       "")]
    public void ConvertEnToUa_StrictMode_ReturnsNullOnUnmappedInput(string input, string? expected)
    {
        var result = KeyboardLayoutMap.ConvertEnToUa(input, strict: true);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("q", "й")]
    [InlineData("w", "ц")]
    [InlineData("e", "у")]
    [InlineData("r", "к")]
    [InlineData("t", "е")]
    [InlineData("y", "н")]
    [InlineData("u", "г")]
    [InlineData("i", "ш")]
    [InlineData("o", "щ")]
    [InlineData("p", "з")]
    [InlineData("a", "ф")]
    [InlineData("s", "і")]
    [InlineData("d", "в")]
    [InlineData("f", "а")]
    [InlineData("g", "п")]
    [InlineData("h", "р")]
    [InlineData("j", "о")]
    [InlineData("k", "л")]
    [InlineData("l", "д")]
    [InlineData("z", "я")]
    [InlineData("x", "ч")]
    [InlineData("c", "с")]
    [InlineData("v", "м")]
    [InlineData("b", "и")]
    [InlineData("n", "т")]
    [InlineData("m", "ь")]
    public void ConvertEnToUa_EveryLowercaseLetter_MapsCorrectly(string en, string ua)
    {
        var result = KeyboardLayoutMap.ConvertEnToUa(en, strict: true);
        Assert.Equal(ua, result);
    }

    [Theory]
    [InlineData("Q", "Й")]
    [InlineData("W", "Ц")]
    [InlineData("E", "У")]
    [InlineData("A", "Ф")]
    [InlineData("S", "І")]
    [InlineData("D", "В")]
    [InlineData("Z", "Я")]
    [InlineData("X", "Ч")]
    public void ConvertEnToUa_UppercaseLetters_PreservesCase(string en, string ua)
    {
        var result = KeyboardLayoutMap.ConvertEnToUa(en, strict: true);
        Assert.Equal(ua, result);
    }

    [Fact]
    public void ConvertEnToUa_MixedCase_ConvertsAllLetters()
    {
        // "Ghbdsn" = Г+р+и+в+і+т = "Привіт"
        var result = KeyboardLayoutMap.ConvertEnToUa("Ghbdsn", strict: true);
        Assert.Equal("Привіт", result);
    }

    // ─── ConvertUaToEn ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("руддщ", "hello")]   // typical mis-typed English word
    [InlineData("hello", null)]       // Latin input — not UA, strict returns null
    [InlineData("",      "")]
    public void ConvertUaToEn_StrictMode_ReturnsNullOnUnmappedInput(string input, string? expected)
    {
        var result = KeyboardLayoutMap.ConvertUaToEn(input, strict: true);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("й", "q")]
    [InlineData("ц", "w")]
    [InlineData("у", "e")]
    [InlineData("к", "r")]
    [InlineData("е", "t")]
    [InlineData("н", "y")]
    [InlineData("г", "u")]
    [InlineData("ш", "i")]
    [InlineData("щ", "o")]
    [InlineData("з", "p")]
    [InlineData("ф", "a")]
    [InlineData("і", "s")]
    [InlineData("в", "d")]
    [InlineData("а", "f")]
    [InlineData("п", "g")]
    [InlineData("р", "h")]
    [InlineData("о", "j")]
    [InlineData("л", "k")]
    [InlineData("д", "l")]
    [InlineData("я", "z")]
    [InlineData("ч", "x")]
    [InlineData("с", "c")]
    [InlineData("м", "v")]
    [InlineData("и", "b")]
    [InlineData("т", "n")]
    [InlineData("ь", "m")]
    public void ConvertUaToEn_EveryLowercaseLetter_MapsCorrectly(string ua, string en)
    {
        var result = KeyboardLayoutMap.ConvertUaToEn(ua, strict: true);
        Assert.Equal(en, result);
    }

    [Theory]
    [InlineData("Й", "Q")]
    [InlineData("Ц", "W")]
    [InlineData("У", "E")]
    [InlineData("Ф", "A")]
    [InlineData("І", "S")]
    [InlineData("В", "D")]
    [InlineData("Я", "Z")]
    [InlineData("Ч", "X")]
    public void ConvertUaToEn_UppercaseLetters_PreservesCase(string ua, string en)
    {
        var result = KeyboardLayoutMap.ConvertUaToEn(ua, strict: true);
        Assert.Equal(en, result);
    }

    [Fact]
    public void ConvertUaToEn_MixedCase_ConvertsAllLetters()
    {
        // "Руддщ" = R+u+l+l+o = "Rullo"… wait, map: Р=H, у=e, д=l, д=l, щ=o → "Hello"
        var result = KeyboardLayoutMap.ConvertUaToEn("Руддщ", strict: true);
        Assert.Equal("Hello", result);
    }

    // ─── Round-trip ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("привіт")]
    [InlineData("Привіт")]
    [InlineData("текст")]
    [InlineData("можна")]
    public void RoundTrip_UaToEnToUa_PreservesOriginal(string original)
    {
        var toEn = KeyboardLayoutMap.ConvertUaToEn(original, strict: true);
        Assert.NotNull(toEn);
        var backToUa = KeyboardLayoutMap.ConvertEnToUa(toEn!, strict: true);
        Assert.Equal(original, backToUa);
    }

    [Theory]
    [InlineData("ghbdsn")]
    [InlineData("руддщ")]
    [InlineData("ntrcn")]  // текст in EN keys
    public void RoundTrip_EnToUaToEn_PreservesOriginal(string original)
    {
        var toUa = KeyboardLayoutMap.ConvertEnToUa(original, strict: true);
        if (toUa is null) return;  // original has unmapped chars, skip
        var backToEn = KeyboardLayoutMap.ConvertUaToEn(toUa, strict: true);
        Assert.Equal(original, backToEn);
    }

    // ─── ClassifyScript ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("hello",   ScriptType.Latin)]
    [InlineData("HELLO",   ScriptType.Latin)]
    [InlineData("test123", ScriptType.Latin)]
    public void ClassifyScript_PureLatinInput_ReturnsLatin(string input, ScriptType expected)
    {
        Assert.Equal(expected, KeyboardLayoutMap.ClassifyScript(input));
    }

    [Theory]
    [InlineData("привіт",  ScriptType.Cyrillic)]
    [InlineData("ТЕКСТ",   ScriptType.Cyrillic)]
    [InlineData("дякую",   ScriptType.Cyrillic)]
    public void ClassifyScript_PureCyrillicInput_ReturnsCyrillic(string input, ScriptType expected)
    {
        Assert.Equal(expected, KeyboardLayoutMap.ClassifyScript(input));
    }

    [Theory]
    [InlineData("helloПривіт", ScriptType.Mixed)]
    [InlineData("aб",          ScriptType.Mixed)]
    public void ClassifyScript_MixedInput_ReturnsMixed(string input, ScriptType expected)
    {
        Assert.Equal(expected, KeyboardLayoutMap.ClassifyScript(input));
    }

    [Theory]
    [InlineData("",     ScriptType.Other)]
    [InlineData("123",  ScriptType.Other)]
    [InlineData("!@#",  ScriptType.Other)]
    public void ClassifyScript_NoLetters_ReturnsOther(string input, ScriptType expected)
    {
        Assert.Equal(expected, KeyboardLayoutMap.ClassifyScript(input));
    }

    // ─── Punctuation keys mapping to UA letters ──────────────────────────────

    [Theory]
    [InlineData(".", "ю")]   // period → ю
    [InlineData(",", "б")]   // comma → б
    [InlineData(";", "ж")]   // semicolon → ж
    [InlineData(":", "Ж")]   // colon → Ж (uppercase)
    [InlineData("<", "Б")]   // less-than → Б
    [InlineData(">", "Ю")]   // greater-than → Ю
    public void ConvertEnToUa_PunctuationMapsToUaLetters(string en, string ua)
    {
        var result = KeyboardLayoutMap.ConvertEnToUa(en, strict: true);
        Assert.Equal(ua, result);
    }
}
