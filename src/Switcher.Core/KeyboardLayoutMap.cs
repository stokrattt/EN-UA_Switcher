using System.Text;

namespace Switcher.Core;

/// <summary>
/// Static EN (QWERTY) ↔ UA (ЙЦУКЕН) keyboard layout mapping.
/// Maps physical key positions, not semantic character equivalents.
/// </summary>
public static class KeyboardLayoutMap
{
    // ─── EN → UA ────────────────────────────────────────────────────────────
    private static readonly Dictionary<char, char> EnToUaMap = new()
    {
        // Row 1: numbers row - same digits, but shift chars differ
        ['`'] = '\'', ['~'] = '\'',
        // Row 2: QWERTY
        ['q'] = 'й', ['w'] = 'ц', ['e'] = 'у', ['r'] = 'к', ['t'] = 'е',
        ['y'] = 'н', ['u'] = 'г', ['i'] = 'ш', ['o'] = 'щ', ['p'] = 'з',
        ['['] = 'х', [']'] = 'ї', ['\\'] = 'ї',
        // Row 3: ASDF
        ['a'] = 'ф', ['s'] = 'і', ['d'] = 'в', ['f'] = 'а', ['g'] = 'п',
        ['h'] = 'р', ['j'] = 'о', ['k'] = 'л', ['l'] = 'д',
        [';'] = 'ж', ['\''] = 'є',
        // Row 4: ZXCV
        ['z'] = 'я', ['x'] = 'ч', ['c'] = 'с', ['v'] = 'м', ['b'] = 'и',
        ['n'] = 'т', ['m'] = 'ь', [','] = 'б', ['.'] = 'ю', ['/'] = '.',
        // Uppercase
        ['Q'] = 'Й', ['W'] = 'Ц', ['E'] = 'У', ['R'] = 'К', ['T'] = 'Е',
        ['Y'] = 'Н', ['U'] = 'Г', ['I'] = 'Ш', ['O'] = 'Щ', ['P'] = 'З',
        ['{'] = 'Х', ['}'] = 'Ї', ['|'] = 'Ї',
        ['A'] = 'Ф', ['S'] = 'І', ['D'] = 'В', ['F'] = 'А', ['G'] = 'П',
        ['H'] = 'Р', ['J'] = 'О', ['K'] = 'Л', ['L'] = 'Д',
        [':'] = 'Ж', ['"'] = 'Є',
        ['Z'] = 'Я', ['X'] = 'Ч', ['C'] = 'С', ['V'] = 'М', ['B'] = 'И',
        ['N'] = 'Т', ['M'] = 'Ь', ['<'] = 'Б', ['>'] = 'Ю', ['?'] = ',',
    };

    // ─── UA → EN ────────────────────────────────────────────────────────────
    private static readonly Dictionary<char, char> UaToEnMap = new()
    {
        // Lowercase
        ['й'] = 'q', ['ц'] = 'w', ['у'] = 'e', ['к'] = 'r', ['е'] = 't',
        ['н'] = 'y', ['г'] = 'u', ['ш'] = 'i', ['щ'] = 'o', ['з'] = 'p',
        ['х'] = '[', ['ї'] = ']',
        ['ф'] = 'a', ['і'] = 's', ['в'] = 'd', ['а'] = 'f', ['п'] = 'g',
        ['р'] = 'h', ['о'] = 'j', ['л'] = 'k', ['д'] = 'l',
        ['ж'] = ';', ['є'] = '\'',
        ['я'] = 'z', ['ч'] = 'x', ['с'] = 'c', ['м'] = 'v', ['и'] = 'b',
        ['т'] = 'n', ['ь'] = 'm', ['б'] = ',', ['ю'] = '.',
        // Uppercase
        ['Й'] = 'Q', ['Ц'] = 'W', ['У'] = 'E', ['К'] = 'R', ['Е'] = 'T',
        ['Н'] = 'Y', ['Г'] = 'U', ['Ш'] = 'I', ['Щ'] = 'O', ['З'] = 'P',
        ['Х'] = '{', ['Ї'] = '}',
        ['Ф'] = 'A', ['І'] = 'S', ['В'] = 'D', ['А'] = 'F', ['П'] = 'G',
        ['Р'] = 'H', ['О'] = 'J', ['Л'] = 'K', ['Д'] = 'L',
        ['Ж'] = ':', ['Є'] = '"',
        ['Я'] = 'Z', ['Ч'] = 'X', ['С'] = 'C', ['М'] = 'V', ['И'] = 'B',
        ['Т'] = 'N', ['Ь'] = 'M', ['Б'] = '<', ['Ю'] = '>',
    };

    /// <summary>
    /// Converts a string typed in English QWERTY layout as if it were Ukrainian ЙЦУКЕН.
    /// Returns null if any character cannot be mapped and <paramref name="strict"/> is true.
    /// </summary>
    public static string? ConvertEnToUa(string input, bool strict = false)
    {
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (EnToUaMap.TryGetValue(c, out char mapped))
                sb.Append(mapped);
            else if (IsWordConnector(c))
                sb.Append(c);
            else if (!strict)
                sb.Append(c);   // pass through unmapped (digits, spaces, etc.)
            else
                return null;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Converts a string typed in Ukrainian ЙЦУКЕН layout as if it were English QWERTY.
    /// Returns null if any character cannot be mapped and <paramref name="strict"/> is true.
    /// </summary>
    public static string? ConvertUaToEn(string input, bool strict = false)
    {
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (UaToEnMap.TryGetValue(c, out char mapped))
                sb.Append(mapped);
            else if (IsWordConnector(c))
                sb.Append(c);
            else if (!strict)
                sb.Append(c);
            else
                return null;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Toggles every mappable character between EN and UA keyboard layouts.
    /// Latin chars become Ukrainian, Ukrainian chars become Latin, everything else stays as-is.
    /// </summary>
    public static string ToggleLayoutText(string input, out int changedCount)
    {
        changedCount = 0;
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (EnToUaMap.TryGetValue(c, out char uaChar))
            {
                sb.Append(uaChar);
                changedCount++;
            }
            else if (UaToEnMap.TryGetValue(c, out char enChar))
            {
                sb.Append(enChar);
                changedCount++;
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns true when the character is a non-letter keyboard key that maps directly to a UA letter.
    /// Examples: ], [, ;, ,, ., '.
    /// </summary>
    public static bool IsLayoutLetterChar(char c) =>
        EnToUaMap.ContainsKey(c) && !char.IsLetter(c);

    public static bool IsWordConnector(char c) =>
        c is '-' or '’' or 'ʼ' or '\'';

    /// <summary>
    /// Trims surrounding punctuation but keeps keyboard symbols that represent letters in the opposite layout.
    /// </summary>
    public static string NormalizeWordToken(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string value = text.Trim();
        int start = 0;
        int end = value.Length;

        while (start < end && !char.IsLetterOrDigit(value[start]) && !IsLayoutLetterChar(value[start]) && !IsWordConnector(value[start]))
            start++;

        while (end > start && !char.IsLetterOrDigit(value[end - 1]) && !IsLayoutLetterChar(value[end - 1]) && !IsWordConnector(value[end - 1]))
            end--;

        if (end <= start)
            return string.Empty;

        return value[start..end].ToLowerInvariant();
    }

    /// <summary>
    /// Returns all normalized layout-equivalent forms for the same token.
    /// If the input is added as either "привіт" or "ghbdsn", both forms are considered equivalent.
    /// </summary>
    public static IReadOnlyCollection<string> GetEquivalentLayoutForms(string? text)
    {
        string normalized = NormalizeWordToken(text);
        if (string.IsNullOrEmpty(normalized))
            return Array.Empty<string>();

        var forms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            normalized
        };

        string toggled = ToggleLayoutText(normalized, out int changedCount).ToLowerInvariant();
        if (changedCount > 0 && !string.Equals(normalized, toggled, StringComparison.OrdinalIgnoreCase))
            forms.Add(toggled);

        return forms.ToArray();
    }

    /// <summary>Returns true if all word characters (letters) in the string are Latin.</summary>
    public static bool IsLatin(string text) =>
        text.All(c => !char.IsLetter(c) || c < 128);

    /// <summary>Returns true if all word characters (letters) in the string are Cyrillic.</summary>
    public static bool IsCyrillic(string text) =>
        text.All(c => !char.IsLetter(c) || (c >= '\u0400' && c <= '\u04FF'));

    /// <summary>
    /// Classifies the script of a string.
    /// Returns Latin, Cyrillic, or Mixed.
    /// </summary>
    public static ScriptType ClassifyScript(string text)
    {
        bool hasLatin = false, hasCyrillic = false;
        foreach (char c in text)
        {
            if (!char.IsLetter(c))
            {
                if (IsLayoutLetterChar(c))
                    hasLatin = true;
                continue;
            }
            if (c < 128) hasLatin = true;
            else if (c >= '\u0400' && c <= '\u04FF') hasCyrillic = true;
            else return ScriptType.Other;

            if (hasLatin && hasCyrillic) return ScriptType.Mixed;
        }
        if (hasLatin) return ScriptType.Latin;
        if (hasCyrillic) return ScriptType.Cyrillic;
        return ScriptType.Other; // digits/symbols only
    }

    public static IReadOnlyDictionary<char, char> GetEnToUaMap() => EnToUaMap;
    public static IReadOnlyDictionary<char, char> GetUaToEnMap() => UaToEnMap;
}

public enum ScriptType { Latin, Cyrillic, Mixed, Other }
