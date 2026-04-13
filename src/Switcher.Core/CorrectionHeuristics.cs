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
///   2. Short tokens are treated more strictly in Auto mode, but still evaluated if the language signal is strong enough.
///   3. Latin word that looks plausible as English (or other Latin language) → do NOT convert.
///   4. Cyrillic word that looks plausible as Ukrainian → do NOT convert.
///   5. Only convert when the CONVERTED result has higher plausibility than the ORIGINAL.
/// </summary>
public static class CorrectionHeuristics
{
    private const double AutoThreshold = 0.40;
    private const double SafeThreshold = 0.44;
    private const double AutoDeltaThreshold = 0.15;
    private const double SafeDeltaThreshold = 0.08;
    private const double AutoTargetFloor = 0.34;
    private const double SafeTargetFloor = 0.24;

    private static readonly HashSet<string> EnDictionary = WordList.LoadEn();
    private static readonly HashSet<string> UaDictionary = WordList.LoadUa();
    private static readonly NgramProfile EnProfile = NgramProfile.Build(EnDictionary);
    private static readonly NgramProfile UaProfile = NgramProfile.Build(UaDictionary);

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

        int tokenLength = CountTokenChars(stripped);
        double threshold = mode == CorrectionMode.Auto ? AutoThreshold : SafeThreshold;

        if (script == ScriptType.Latin)
            return EvaluateLatin(stripped, prefix, suffix, tokenLength, mode, threshold);

        if (script == ScriptType.Cyrillic)
            return EvaluateCyrillic(stripped, prefix, suffix, tokenLength, mode, threshold);

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
        string word, string prefix, string suffix, int tokenLength,
        CorrectionMode mode, double threshold)
    {
        string lower = word.ToLowerInvariant();
        bool sourceHasLayoutSymbols = lower.Any(KeyboardLayoutMap.IsLayoutLetterChar);

        // If the word is a known English word → very likely correct layout → do NOT convert
        // Also check with trailing keyboard-mapped punctuation stripped (e.g., "hello." → "hello")
        string lowerTrimmed = lower.TrimEnd('.', ',', ';', ':', '<', '>');
        bool sourceDictionaryHit = EnDictionary.Contains(lower) || EnDictionary.Contains(lowerTrimmed);
        if (sourceDictionaryHit)
            return null;

        // Try to convert EN→UA
        string? converted = KeyboardLayoutMap.ConvertEnToUa(word, strict: true);
        if (converted == null) return null;

        // Reject if converted result contains non-letter characters (e.g. brackets, punctuation)
        // This prevents false positives like typing [] which maps to хї
        if (converted.Any(c => !char.IsLetter(c) && !KeyboardLayoutMap.IsWordConnector(c)))
            return null;

        string convertedLower = converted.ToLowerInvariant();
        bool targetDictionaryHit = UaDictionary.Contains(convertedLower);

        // Score the converted result as Ukrainian
        double sourceScore = ScoreEnglish(lower);
        if (!string.Equals(lower, lowerTrimmed, StringComparison.Ordinal))
            sourceScore = Math.Max(sourceScore, ScoreEnglish(lowerTrimmed));
        double targetScore = ScoreCyrillic(convertedLower);

        if (mode == CorrectionMode.Auto)
        {
            if (tokenLength <= 1)
                return null;

            if (tokenLength == 2 && !ShouldConvertShortToken(sourceScore, targetScore, sourceDictionaryHit, targetDictionaryHit))
                return null;
        }

        double confidence = ComputeConfidence(
            originalScore: sourceScore,
            targetScore: targetScore,
            targetDictionaryHit: targetDictionaryHit,
            mode: mode);

        if (!ShouldConvert(sourceScore, targetScore, confidence, threshold, mode, tokenLength, sourceDictionaryHit, targetDictionaryHit, sourceHasLayoutSymbols))
            return null;

        string full = prefix + converted + suffix;
        string fullWord = prefix + word + suffix;
        return new CorrectionCandidate(fullWord, full, CorrectionDirection.EnToUa, confidence,
            $"Latin→UA src={sourceScore:F2} dst={targetScore:F2} conf={confidence:F2}");
    }

    // ─── Cyrillic word → try UA→EN ──────────────────────────────────────────

    private static CorrectionCandidate? EvaluateCyrillic(
        string word, string prefix, string suffix, int tokenLength,
        CorrectionMode mode, double threshold)
    {
        string lower = word.ToLowerInvariant();
        bool sourceHasLayoutSymbols = false;

        // If the word is a known Ukrainian word → correct layout → do NOT convert
        string lowerTrimmed = lower.TrimEnd('.', ',', ';', ':', '<', '>');
        bool sourceDictionaryHit = UaDictionary.Contains(lower) || UaDictionary.Contains(lowerTrimmed);
        if (sourceDictionaryHit)
            return null;

        // Try to convert UA→EN
        string? converted = KeyboardLayoutMap.ConvertUaToEn(word, strict: true);
        if (converted == null) return null;

        // Reject if converted result contains non-letter characters
        // This prevents false positives like хїфі → []as
        if (converted.Any(c => !char.IsLetter(c) && !KeyboardLayoutMap.IsWordConnector(c)))
            return null;

        string convertedLower = converted.ToLowerInvariant();
        bool targetDictionaryHit = EnDictionary.Contains(convertedLower);

        double sourceScore = ScoreCyrillic(lower);
        if (!string.Equals(lower, lowerTrimmed, StringComparison.Ordinal))
            sourceScore = Math.Max(sourceScore, ScoreCyrillic(lowerTrimmed));
        double targetScore = ScoreEnglish(convertedLower);

        if (mode == CorrectionMode.Auto)
        {
            if (tokenLength <= 1)
                return null;

            if (tokenLength == 2 && !ShouldConvertShortToken(sourceScore, targetScore, sourceDictionaryHit, targetDictionaryHit))
                return null;
        }

        double confidence = ComputeConfidence(
            originalScore: sourceScore,
            targetScore: targetScore,
            targetDictionaryHit: targetDictionaryHit,
            mode: mode);

        if (!ShouldConvert(sourceScore, targetScore, confidence, threshold, mode, tokenLength, sourceDictionaryHit, targetDictionaryHit, sourceHasLayoutSymbols))
            return null;

        string full = prefix + converted + suffix;
        string fullWord = prefix + word + suffix;
        return new CorrectionCandidate(fullWord, full, CorrectionDirection.UaToEn, confidence,
            $"Cyrillic→EN src={sourceScore:F2} dst={targetScore:F2} conf={confidence:F2}");
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

        double ngramScore = EnProfile.Score(lower);

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
        double score = Clamp01((ngramScore * 0.58) + (bigramScore * 0.18) + (cvScore * 0.24));

        if (lower.Any(KeyboardLayoutMap.IsLayoutLetterChar))
            score *= 0.42;

        return Clamp01(score);
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

        double ngramScore = UaProfile.Score(lower);

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

        return Clamp01((ngramScore * 0.58) + (bigramScore * 0.18) + (cvScore * 0.24));
    }

    private static bool ShouldConvert(
        double originalScore,
        double targetScore,
        double confidence,
        double threshold,
        CorrectionMode mode,
        int tokenLength,
        bool sourceDictionaryHit,
        bool targetDictionaryHit,
        bool sourceHasLayoutSymbols)
    {
        double delta = targetScore - originalScore;
        double deltaThreshold = mode == CorrectionMode.Auto ? AutoDeltaThreshold : SafeDeltaThreshold;
        double targetFloor = mode == CorrectionMode.Auto ? AutoTargetFloor : SafeTargetFloor;

        if (targetScore >= targetFloor
            && delta >= deltaThreshold
            && confidence >= threshold)
            return true;

        if (mode != CorrectionMode.Auto)
            return false;

        if (targetDictionaryHit && !sourceDictionaryHit && targetScore >= 0.32 && delta >= 0.03 && confidence >= 0.28)
            return true;

        if (tokenLength >= 4 && !sourceDictionaryHit && targetScore >= 0.37 && delta >= 0.04 && confidence >= 0.30)
            return true;

        if (tokenLength >= 3
            && sourceHasLayoutSymbols
            && !sourceDictionaryHit
            && targetScore >= 0.18
            && delta >= 0.08
            && confidence >= 0.18)
            return true;

        return false;
    }

    private static bool ShouldConvertShortToken(
        double originalScore,
        double targetScore,
        bool sourceDictionaryHit,
        bool targetDictionaryHit)
    {
        if (sourceDictionaryHit)
            return false;

        if (targetDictionaryHit)
            return true;

        return targetScore >= 0.43 && originalScore <= 0.14;
    }

    private static double ComputeConfidence(
        double originalScore,
        double targetScore,
        bool targetDictionaryHit,
        CorrectionMode mode)
    {
        double delta = Math.Max(0.0, targetScore - originalScore);
        double confidence = (targetScore * 0.52)
            + ((1.0 - originalScore) * 0.18)
            + (delta * 0.30);

        if (targetDictionaryHit)
            confidence += mode == CorrectionMode.Auto ? 0.08 : 0.12;

        return Clamp01(confidence);
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

    private static double Clamp01(double value)
    {
        if (value < 0) return 0;
        if (value > 1) return 1;
        return value;
    }

    private static int CountTokenChars(string text) =>
        text.Count(c => char.IsLetter(c) || KeyboardLayoutMap.IsLayoutLetterChar(c));

    // ─── Punctuation stripping ───────────────────────────────────────────────

    // Characters that look like punctuation but ARE keyboard keys mapping to UA letters:
    //   [  → х,  ] → ї,  \ → ї,  { → Х,  } → Ї,  | → Ї
    //   .  → ю,  ,  → б,  ;  → ж,  :  → Ж,  <  → Б,  >  → Ю
    // These must NOT be stripped as trailing/leading punctuation, otherwise words like
    // "ndj]" (typed in EN while meaning "твої") lose their last letter,
    // or "cd'nj." loses the trailing "ю" (свєтою → свєто).
    private static string StripPunctuation(string word, out string prefix, out string suffix)
    {
        int start = 0, end = word.Length;
        while (start < end
               && !char.IsLetterOrDigit(word[start])
               && !KeyboardLayoutMap.IsLayoutLetterChar(word[start])
               && !KeyboardLayoutMap.IsWordConnector(word[start]))
            start++;
        while (end > start
               && !char.IsLetterOrDigit(word[end - 1])
               && !KeyboardLayoutMap.IsLayoutLetterChar(word[end - 1])
               && !KeyboardLayoutMap.IsWordConnector(word[end - 1]))
            end--;
        prefix = word[..start];
        suffix = word[end..];
        return word[start..end];
    }

    private sealed class NgramProfile
    {
        private readonly Dictionary<string, int> _unigrams;
        private readonly Dictionary<string, int> _bigrams;
        private readonly Dictionary<string, int> _trigrams;
        private readonly double _maxUnigram;
        private readonly double _maxBigram;
        private readonly double _maxTrigram;

        private NgramProfile(
            Dictionary<string, int> unigrams,
            Dictionary<string, int> bigrams,
            Dictionary<string, int> trigrams,
            double maxUnigram,
            double maxBigram,
            double maxTrigram)
        {
            _unigrams = unigrams;
            _bigrams = bigrams;
            _trigrams = trigrams;
            _maxUnigram = maxUnigram;
            _maxBigram = maxBigram;
            _maxTrigram = maxTrigram;
        }

        public static NgramProfile Build(IEnumerable<string> words)
        {
            var unigrams = new Dictionary<string, int>(StringComparer.Ordinal);
            var bigrams = new Dictionary<string, int>(StringComparer.Ordinal);
            var trigrams = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (string word in words)
            {
                string normalized = Normalize(word);
                if (normalized.Length == 0)
                    continue;

                CountNgrams(normalized, 1, unigrams);

                if (normalized.Length < 2)
                    continue;

                string padded = $"^{normalized}$";
                CountNgrams(padded, 2, bigrams);
                CountNgrams(padded, 3, trigrams);
            }

            double maxUnigram = unigrams.Count == 0 ? 1.0 : unigrams.Values.Max();
            double maxBigram = bigrams.Count == 0 ? 1.0 : bigrams.Values.Max();
            double maxTrigram = trigrams.Count == 0 ? 1.0 : trigrams.Values.Max();
            return new NgramProfile(unigrams, bigrams, trigrams, maxUnigram, maxBigram, maxTrigram);
        }

        public double Score(string word)
        {
            string normalized = Normalize(word);
            if (normalized.Length == 0)
                return 0.0;

            double unigramScore = AverageMatchScore(normalized, 1, _unigrams, _maxUnigram);
            if (normalized.Length == 1)
                return unigramScore;

            string padded = $"^{normalized}$";
            double bigramScore = AverageMatchScore(padded, 2, _bigrams, _maxBigram);
            double trigramScore = AverageMatchScore(padded, 3, _trigrams, _maxTrigram);

            if (normalized.Length == 2)
                return (unigramScore * 0.40) + (bigramScore * 0.60);

            if (normalized.Length == 3)
                return (unigramScore * 0.24) + (bigramScore * 0.40) + (trigramScore * 0.36);

            return (unigramScore * 0.14) + (bigramScore * 0.32) + (trigramScore * 0.54);
        }

    private static string Normalize(string word) =>
        new string(word
                .ToLowerInvariant()
                .Where(c => char.IsLetter(c) || KeyboardLayoutMap.IsWordConnector(c))
                .ToArray());

        private static void CountNgrams(string text, int size, Dictionary<string, int> counts)
        {
            for (int i = 0; i <= text.Length - size; i++)
            {
                string gram = text.Substring(i, size);
                counts.TryGetValue(gram, out int current);
                counts[gram] = current + 1;
            }
        }

        private static double AverageMatchScore(string text, int size, Dictionary<string, int> counts, double maxCount)
        {
            if (text.Length < size || counts.Count == 0)
                return 0.0;

            double total = 0.0;
            int grams = 0;

            for (int i = 0; i <= text.Length - size; i++)
            {
                string gram = text.Substring(i, size);
                counts.TryGetValue(gram, out int count);
                total += count <= 0 ? 0.0 : Math.Sqrt(count / maxCount);
                grams++;
            }

            return grams == 0 ? 0.0 : total / grams;
        }
    }
}
