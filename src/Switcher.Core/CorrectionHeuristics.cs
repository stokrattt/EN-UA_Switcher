namespace Switcher.Core;

/// <summary>
/// Conservative heuristics for determining whether a word was typed in the wrong layout.
///
/// Core principle:
///   False negative (missed correction) is acceptable.
///   False positive (corrupting correct text) is NOT acceptable.
///
/// Rules:
///   1. Mixed-script words → never convert.
///   2. Short words (&lt;= 2 letters) → skip in Auto mode; allow in Safe mode only with dictionary hit.
///   3. Latin word that looks plausible as English (or other Latin language) → do NOT convert.
///   4. Cyrillic word that looks plausible as Ukrainian → do NOT convert.
///   5. Only convert when the CONVERTED result has higher plausibility than the ORIGINAL.
/// </summary>
public static class CorrectionHeuristics
{
    private const double AutoThreshold = 0.75;
    private const double SafeThreshold = 0.50;

    private static readonly HashSet<string> EnDictionary = WordList.LoadEn();
    private static readonly HashSet<string> UaDictionary = WordList.LoadUa();

    // Common English bigrams (top ~60) for plausibility scoring
    private static readonly HashSet<string> EnBigrams = new(StringComparer.Ordinal)
    {
        "th","he","in","er","an","re","on","en","at","es",
        "or","nt","ea","ti","st","to","ar","nd","ng","is",
        "it","al","as","ha","ou","ed","hi","te","se","of",
        "le","no","me","de","co","ri","li","ne","ve","io",
        "ra","el","ta","la","ma","si","ca","ge","ic","be",
        "ce","ch","ho","ll","ly","pe","ur","wa","wh","ab"
    };

    // Common Ukrainian bigrams (~80) for plausibility scoring
    private static readonly HashSet<string> UaBigrams = new(StringComparer.Ordinal)
    {
        "на","но","не","ні","ня","ну","та","то","те","ти",
        "ту","ть","по","пр","пе","пі","па","ра","ро","ри",
        "ре","рі","ру","ко","ка","ки","ку","кі","кн","ст",
        "ся","си","со","сі","сь","сл","ві","во","ва","ве",
        "ді","до","де","да","ні","ни","ли","ле","ла","ло",
        "лю","лі","ій","їх","ін","ів","іс","іт","ік","ор",
        "ом","ол","он","ос","от","об","ов","од","ен","ем",
        "ел","ер","ан","ак","ар","ат","ав","ал","аз","ма",
        "ми","мо","му","ме","за","зн","зі","із","бо","бу",
        "чн","чи","чк","ше","ші","що","що","юч","яв","як"
    };

    // Characters that should NEVER appear in a plausible Ukrainian word typed via ЙЦУКЕН
    private static readonly HashSet<char> LatinOnlyChars = new()
    { 'q','w','e','r','t','y','u','i','o','p','a','s','d','f','g','h',
      'j','k','l','z','x','c','v','b','n','m',
      'Q','W','E','R','T','Y','U','I','O','P','A','S','D','F','G','H',
      'J','K','L','Z','X','C','V','B','N','M' };

    private static readonly HashSet<char> CyrillicOnlyChars = new()
    { 'й','ц','у','к','е','н','г','ш','щ','з','х','ї','ф','і','в','а','п','р','о','л','д','ж','є',
      'я','ч','с','м','и','т','ь','б','ю','ё','ъ','э',
      'Й','Ц','У','К','Е','Н','Г','Ш','Щ','З','Х','Ї','Ф','І','В','А','П','Р','О','Л','Д','Ж','Є',
      'Я','Ч','С','М','И','Т','Ь','Б','Ю' };

    /// <summary>
    /// Evaluates whether a word should be converted and returns a candidate, or null if it should not be changed.
    /// </summary>
    public static CorrectionCandidate? Evaluate(string word, CorrectionMode mode)
    {
        if (string.IsNullOrWhiteSpace(word)) return null;

        // Strip leading/trailing punctuation for analysis, preserve for replacement
        string stripped = StripPunctuation(word, out string prefix, out string suffix);

        if (stripped.Length == 0) return null;

        var script = KeyboardLayoutMap.ClassifyScript(stripped);

        // Mixed script: never convert
        if (script == ScriptType.Mixed || script == ScriptType.Other)
            return null;

        int letterCount = stripped.Count(char.IsLetter);
        double threshold = mode == CorrectionMode.Auto ? AutoThreshold : SafeThreshold;

        if (script == ScriptType.Latin)
            return EvaluateLatin(stripped, prefix, suffix, letterCount, mode, threshold);

        if (script == ScriptType.Cyrillic)
            return EvaluateCyrillic(stripped, prefix, suffix, letterCount, mode, threshold);

        return null;
    }

    /// <summary>
    /// Checks whether a converted text exists in the target language dictionary.
    /// Used by AutoModeHandler's fallback path to require high-quality matches.
    /// </summary>
    public static bool IsInDictionary(string convertedText, CorrectionDirection direction)
    {
        string lower = convertedText.ToLowerInvariant();
        return direction == CorrectionDirection.EnToUa
            ? UaDictionary.Contains(lower)
            : EnDictionary.Contains(lower);
    }

    // ─── Latin word → try EN→UA ─────────────────────────────────────────────

    private static CorrectionCandidate? EvaluateLatin(
        string word, string prefix, string suffix, int letterCount,
        CorrectionMode mode, double threshold)
    {
        // Very short words: skip unless converted result is a known dictionary word
        if (letterCount <= 2)
        {
            if (mode == CorrectionMode.Auto)
            {
                // Allow 2-letter words only if converted result is a known UA word
                // and original is NOT a known EN word (e.g. pf→за, ys→ні, yt→не)
                string? probe = KeyboardLayoutMap.ConvertEnToUa(word, strict: true);
                if (probe == null || !UaDictionary.Contains(probe.ToLowerInvariant()))
                    return null;
                if (EnDictionary.Contains(word.ToLowerInvariant()))
                    return null;
            }
            // Safe mode: allow all 2-letter words through to full evaluation
        }

        string lower = word.ToLowerInvariant();

        // If the word is a known English word → very likely correct layout → do NOT convert
        // Also check with trailing keyboard-mapped punctuation stripped (e.g., "hello." → "hello")
        string lowerTrimmed = lower.TrimEnd('.', ',', ';', ':', '<', '>');
        if (EnDictionary.Contains(lower) || EnDictionary.Contains(lowerTrimmed))
            return null;

        // Try to convert EN→UA
        string? converted = KeyboardLayoutMap.ConvertEnToUa(word, strict: true);
        if (converted == null) return null;

        // Reject if converted result contains non-letter characters (e.g. brackets, punctuation)
        // This prevents false positives like typing [] which maps to хї
        if (converted.Any(c => !char.IsLetter(c)))
            return null;

        string convertedLower = converted.ToLowerInvariant();

        // Score the converted result as Ukrainian
        double uaScore = ScoreCyrillic(convertedLower);
        // Score the original as "accidental English" (i.e., how implausible is it as English text)
        double enImplausibility = 1.0 - ScoreEnglish(lower);

        // Combined confidence: converted must be plausible UA, and original must not be good EN
        double confidence = (uaScore * 0.6) + (enImplausibility * 0.4);

        // Boost if converted word is in UA dictionary
        if (UaDictionary.Contains(convertedLower))
            confidence = Math.Min(1.0, confidence + 0.25);

        if (confidence < threshold)
            return null;

        string full = prefix + converted + suffix;
        string fullWord = prefix + word + suffix;
        return new CorrectionCandidate(fullWord, full, CorrectionDirection.EnToUa, confidence,
            $"Latin→UA score={uaScore:F2} enImplaus={enImplausibility:F2} conf={confidence:F2}");
    }

    // ─── Cyrillic word → try UA→EN ──────────────────────────────────────────

    private static CorrectionCandidate? EvaluateCyrillic(
        string word, string prefix, string suffix, int letterCount,
        CorrectionMode mode, double threshold)
    {
        if (letterCount <= 2)
        {
            if (mode == CorrectionMode.Auto)
            {
                // Allow 2-letter words only if converted result is a known EN word
                string? probe = KeyboardLayoutMap.ConvertUaToEn(word, strict: true);
                if (probe == null || !EnDictionary.Contains(probe.ToLowerInvariant()))
                    return null;
                if (UaDictionary.Contains(word.ToLowerInvariant()))
                    return null;
            }
        }

        string lower = word.ToLowerInvariant();

        // If the word is a known Ukrainian word → correct layout → do NOT convert
        string lowerTrimmed = lower.TrimEnd('.', ',', ';', ':', '<', '>');
        if (UaDictionary.Contains(lower) || UaDictionary.Contains(lowerTrimmed))
            return null;

        // Try to convert UA→EN
        string? converted = KeyboardLayoutMap.ConvertUaToEn(word, strict: true);
        if (converted == null) return null;

        // Reject if converted result contains non-letter characters
        // This prevents false positives like хїфі → []as
        if (converted.Any(c => !char.IsLetter(c)))
            return null;

        string convertedLower = converted.ToLowerInvariant();

        // Score the converted result as English
        double enScore = ScoreEnglish(convertedLower);
        // Score the original as "accidental Ukrainian"
        double uaImplausibility = 1.0 - ScoreCyrillic(lower);

        double confidence = (enScore * 0.6) + (uaImplausibility * 0.4);

        // Boost if converted word is in EN dictionary
        if (EnDictionary.Contains(convertedLower))
            confidence = Math.Min(1.0, confidence + 0.25);

        if (confidence < threshold)
            return null;

        string full = prefix + converted + suffix;
        string fullWord = prefix + word + suffix;
        return new CorrectionCandidate(fullWord, full, CorrectionDirection.UaToEn, confidence,
            $"Cyrillic→EN score={enScore:F2} uaImplaus={uaImplausibility:F2} conf={confidence:F2}");
    }

    // ─── Scoring functions ───────────────────────────────────────────────────

    /// <summary>Scores how plausible a lowercase string is as English text (0.0–1.0).</summary>
    private static double ScoreEnglish(string lower)
    {
        if (lower.Length == 0) return 0;
        if (EnDictionary.Contains(lower)) return 1.0;

        int letters = lower.Count(char.IsLetter);
        if (letters == 0) return 0;

        // Check for non-Latin characters
        if (lower.Any(c => char.IsLetter(c) && c >= 128)) return 0.0;

        // Bigram scoring
        int bigramCount = 0, bigramHits = 0;
        for (int i = 0; i < lower.Length - 1; i++)
        {
            if (char.IsLetter(lower[i]) && char.IsLetter(lower[i + 1]))
            {
                bigramCount++;
                string bg = lower.Substring(i, 2);
                if (EnBigrams.Contains(bg)) bigramHits++;
            }
        }
        double bigramScore = bigramCount > 0 ? (double)bigramHits / bigramCount : 0;

        // Consonant/vowel ratio check (English: ~40% vowels)
        double cvScore = ConsonantVowelScore(lower, isEnglish: true);

        return (bigramScore * 0.5) + (cvScore * 0.5);
    }

    /// <summary>Scores how plausible a lowercase string is as Ukrainian text (0.0–1.0).</summary>
    private static double ScoreCyrillic(string lower)
    {
        if (lower.Length == 0) return 0;
        if (UaDictionary.Contains(lower)) return 1.0;

        int letters = lower.Count(char.IsLetter);
        if (letters == 0) return 0;

        // Check for non-Cyrillic characters
        if (lower.Any(c => char.IsLetter(c) && (c < '\u0400' || c > '\u04FF'))) return 0.0;

        // Bigram scoring
        int bigramCount = 0, bigramHits = 0;
        for (int i = 0; i < lower.Length - 1; i++)
        {
            if (char.IsLetter(lower[i]) && char.IsLetter(lower[i + 1]))
            {
                bigramCount++;
                string bg = lower.Substring(i, 2);
                if (UaBigrams.Contains(bg)) bigramHits++;
            }
        }
        double bigramScore = bigramCount > 0 ? (double)bigramHits / bigramCount : 0;

        // Consonant/vowel ratio check (Ukrainian: ~40-50% vowels)
        double cvScore = ConsonantVowelScore(lower, isEnglish: false);

        return (bigramScore * 0.5) + (cvScore * 0.5);
    }

    private static readonly HashSet<char> EnVowels = new() { 'a','e','i','o','u' };
    private static readonly HashSet<char> UaVowels = new()
    { 'а','е','є','и','і','ї','о','у','ю','я' };

    private static double ConsonantVowelScore(string lower, bool isEnglish)
    {
        var vowels = isEnglish ? EnVowels : UaVowels;
        int letters = 0, vowelCount = 0;
        foreach (char c in lower)
        {
            if (!char.IsLetter(c)) continue;
            letters++;
            if (vowels.Contains(c)) vowelCount++;
        }
        if (letters == 0) return 0;

        double vowelRatio = (double)vowelCount / letters;
        // Ideal range: 0.25 – 0.60. Outside this → penalty.
        if (vowelRatio < 0.10 || vowelRatio > 0.75) return 0.0;
        if (vowelRatio < 0.20 || vowelRatio > 0.65) return 0.4;
        if (vowelRatio < 0.25 || vowelRatio > 0.60) return 0.6;
        return 1.0;
    }

    // ─── Punctuation stripping ───────────────────────────────────────────────

    // Characters that look like punctuation but ARE keyboard keys mapping to UA letters:
    //   [  → х,  ] → ї,  \ → ї,  { → Х,  } → Ї,  | → Ї
    //   .  → ю,  ,  → б,  ;  → ж,  :  → Ж,  <  → Б,  >  → Ю
    // These must NOT be stripped as trailing/leading punctuation, otherwise words like
    // "ndj]" (typed in EN while meaning "твої") lose their last letter,
    // or "cd'nj." loses the trailing "ю" (свєтою → свєто).
    private static readonly HashSet<char> KeyboardLetterChars = new()
        { '[', ']', '{', '}', '\\', '|', '.', ',', ';', ':', '<', '>' };

    private static string StripPunctuation(string word, out string prefix, out string suffix)
    {
        int start = 0, end = word.Length;
        while (start < end && !char.IsLetterOrDigit(word[start]) && !KeyboardLetterChars.Contains(word[start]))
            start++;
        while (end > start && !char.IsLetterOrDigit(word[end - 1]) && !KeyboardLetterChars.Contains(word[end - 1]))
            end--;
        prefix = word[..start];
        suffix = word[end..];
        return word[start..end];
    }
}
