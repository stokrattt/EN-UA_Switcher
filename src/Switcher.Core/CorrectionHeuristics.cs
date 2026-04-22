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
    private const double AutoDictionaryBonus = 0.12;
    private const double SafeDictionaryBonus = 0.16;
    private const double AutoBaseInertiaMargin = 0.06;
    private const double SafeBaseInertiaMargin = 0.04;
    private const double AutoMediumInertiaMargin = 0.09;
    private const double SafeMediumInertiaMargin = 0.06;
    private const double AutoShortInertiaMargin = 0.18;
    private const double SafeShortInertiaMargin = 0.12;

    private static readonly HashSet<string> EnDictionary = WordList.LoadEn();
    private static readonly HashSet<string> UaDictionary = WordList.LoadUa();


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

        if (stripped.Contains('-'))
        {
            var compoundCandidate = EvaluateCompoundToken(stripped, prefix, suffix, mode);
            if (compoundCandidate is not null)
                return compoundCandidate;
        }

        if (mode == CorrectionMode.Auto
            && stripped.Any(char.IsLetter)
            && stripped.Any(char.IsDigit)
            && ShouldSkipAlphaNumericToken(stripped))
            return null;

        var script = KeyboardLayoutMap.ClassifyScript(stripped);

        // Mixed script: never convert
        if (script == ScriptType.Mixed || script == ScriptType.Other)
            return null;

        int tokenLength = CountTokenChars(stripped);
        if (script == ScriptType.Latin)
            return EvaluateLatin(stripped, prefix, suffix, tokenLength, mode);

        if (script == ScriptType.Cyrillic)
            return EvaluateCyrillic(stripped, prefix, suffix, tokenLength, mode);

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

    public static SelectorFeatureVector? BuildSelectorFeatures(
        string originalText,
        string convertedText,
        CorrectionDirection direction)
    {
        if (string.IsNullOrWhiteSpace(originalText) || string.IsNullOrWhiteSpace(convertedText))
            return null;

        string original = StripPunctuation(originalText, out _, out _);
        string converted = StripPunctuation(convertedText, out _, out _);
        if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(converted))
            return null;

        string originalLower = original.ToLowerInvariant();
        string convertedLower = converted.ToLowerInvariant();
        string originalLettersOnly = ExtractLettersOnly(original).ToLowerInvariant();
        string convertedLettersOnly = ExtractLettersOnly(converted).ToLowerInvariant();
        bool containsDigit = original.Any(char.IsDigit) || converted.Any(char.IsDigit);

        double sourceScore;
        double targetScore;
        bool sourceDictionaryHit;
        bool targetDictionaryHit;

        if (direction == CorrectionDirection.EnToUa)
        {
            sourceScore = ScoreEnglish(originalLower);
            targetScore = ScoreCyrillic(convertedLower);
            sourceDictionaryHit = containsDigit
                ? IsLikelyEnglishToken(originalLettersOnly)
                : IsLikelyEnglishToken(originalLower);
            targetDictionaryHit = containsDigit
                ? UaDictionary.Contains(convertedLettersOnly)
                : UaDictionary.Contains(convertedLower);
        }
        else if (direction == CorrectionDirection.UaToEn)
        {
            sourceScore = ScoreCyrillic(originalLower);
            targetScore = ScoreEnglish(convertedLower);
            sourceDictionaryHit = containsDigit
                ? UaDictionary.Contains(originalLettersOnly)
                : UaDictionary.Contains(originalLower);
            targetDictionaryHit = containsDigit
                ? IsLikelyEnglishToken(convertedLettersOnly)
                : IsLikelyEnglishToken(convertedLower);
        }
        else
        {
            return null;
        }

        double confidence = ComputeConfidence(
            originalScore: sourceScore,
            targetScore: targetScore,
            targetDictionaryHit: targetDictionaryHit,
            mode: CorrectionMode.Auto);

        return BuildSelectorFeatures(
            original,
            converted,
            direction,
            sourceScore,
            targetScore,
            confidence,
            sourceDictionaryHit,
            targetDictionaryHit);
    }

    public static bool LooksCorrectAsTyped(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        string stripped = StripPunctuation(word, out _, out _);
        if (string.IsNullOrWhiteSpace(stripped))
            return false;

        if (stripped.Contains('-'))
        {
            string[] parts = stripped.Split('-', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && parts.All(LooksCorrectAsTyped);
        }

        string lower = stripped.ToLowerInvariant();
        string lowerTrimmed = lower.TrimEnd('.', ',', ';', ':', '<', '>');
        string lettersOnly = ExtractLettersOnly(stripped).ToLowerInvariant();
        bool containsDigit = stripped.Any(char.IsDigit);
        var script = KeyboardLayoutMap.ClassifyScript(stripped);

        if (containsDigit)
        {
            return script switch
            {
                ScriptType.Latin => IsLikelyEnglishToken(lettersOnly) || LooksLikeTechnicalLatinToken(lettersOnly),
                ScriptType.Cyrillic => UaDictionary.Contains(lettersOnly),
                _ => false
            };
        }

        if (script == ScriptType.Latin)
        {
            if (IsLikelyEnglishToken(lower) || IsLikelyEnglishToken(lowerTrimmed))
                return true;

            string? converted = KeyboardLayoutMap.ConvertEnToUa(stripped, strict: true);
            double sourceScore = Math.Max(ScoreEnglish(lower), ScoreEnglish(lowerTrimmed));
            double targetScore = converted is null ? 0 : ScoreCyrillic(converted.ToLowerInvariant());
            return sourceScore >= 0.42 && sourceScore >= (targetScore + 0.05);
        }

        if (script == ScriptType.Cyrillic)
        {
            if (UaDictionary.Contains(lower) || UaDictionary.Contains(lowerTrimmed))
                return true;

            string? converted = KeyboardLayoutMap.ConvertUaToEn(stripped, strict: true);
            double sourceScore = Math.Max(ScoreCyrillic(lower), ScoreCyrillic(lowerTrimmed));
            double targetScore = converted is null ? 0 : ScoreEnglish(converted.ToLowerInvariant());
            return sourceScore >= 0.42 && sourceScore >= (targetScore + 0.05);
        }

        return false;
    }

    public static bool HasStrongAsTypedSignal(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        string stripped = StripPunctuation(word, out _, out _);
        if (string.IsNullOrWhiteSpace(stripped))
            return false;

        if (stripped.Contains('-'))
        {
            string[] parts = stripped.Split('-', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && parts.All(HasStrongAsTypedSignal);
        }

        string lower = stripped.ToLowerInvariant();
        string lowerTrimmed = lower.TrimEnd('.', ',', ';', ':', '<', '>');
        string lettersOnly = ExtractLettersOnly(stripped).ToLowerInvariant();
        bool containsDigit = stripped.Any(char.IsDigit);
        var script = KeyboardLayoutMap.ClassifyScript(stripped);

        if (containsDigit)
        {
            return script switch
            {
                ScriptType.Latin => IsLikelyEnglishToken(lettersOnly) || LooksLikeTechnicalLatinToken(lettersOnly),
                ScriptType.Cyrillic => UaDictionary.Contains(lettersOnly),
                _ => false
            };
        }

        return script switch
        {
            ScriptType.Latin => IsLikelyEnglishToken(lower) || IsLikelyEnglishToken(lowerTrimmed),
            ScriptType.Cyrillic => UaDictionary.Contains(lower) || UaDictionary.Contains(lowerTrimmed),
            _ => false
        };
    }

    // ─── Latin word → try EN→UA ─────────────────────────────────────────────

    private static CorrectionCandidate? EvaluateLatin(
        string word, string prefix, string suffix, int tokenLength,
        CorrectionMode mode)
    {
        string lower = word.ToLowerInvariant();
        bool containsDigit = word.Any(char.IsDigit);
        bool sourceHasLayoutSymbols = lower.Any(KeyboardLayoutMap.IsLayoutLetterChar);
        string lettersOnly = ExtractLettersOnly(word).ToLowerInvariant();
        bool sourceLooksLikeTechToken = containsDigit && LooksLikeTechnicalLatinToken(lettersOnly);

        // If the word is a known English word → very likely correct layout → do NOT convert
        // Also check with trailing keyboard-mapped punctuation stripped (e.g., "hello." → "hello")
        string lowerTrimmed = lower.TrimEnd('.', ',', ';', ':', '<', '>');
        bool sourceLooksLikeTechTextToken = !containsDigit
            && (LooksLikeTechnicalTextToken(lower) || LooksLikeTechnicalTextToken(lowerTrimmed));
        bool sourceProtectedShortLatinToken = !containsDigit && HasProtectedShortLatinToken(lowerTrimmed);
        bool sourceDictionaryHit = containsDigit
            ? IsLikelyEnglishToken(lettersOnly)
            : IsLikelyEnglishToken(lower) || IsLikelyEnglishToken(lowerTrimmed);
            
        if (mode != CorrectionMode.Safe
            && (sourceDictionaryHit
                || sourceLooksLikeTechToken
                || sourceLooksLikeTechTextToken
                || sourceProtectedShortLatinToken))
            return null;

        // Try to convert EN→UA
        string? converted = KeyboardLayoutMap.ConvertEnToUa(word, strict: !containsDigit);
        if (converted == null) return null;
        converted = NormalizePossibleUkrainianApostropheMistype(converted);

        // Reject if converted result contains non-letter characters (e.g. brackets, punctuation)
        // This prevents false positives like typing [] which maps to хї
        if (converted.Any(c => !char.IsLetter(c) && !KeyboardLayoutMap.IsWordConnector(c) && !char.IsDigit(c)))
            return null;

        string convertedLower = converted.ToLowerInvariant();
        string convertedLettersOnly = ExtractLettersOnly(converted).ToLowerInvariant();
        bool targetDictionaryHit = containsDigit
            ? UaDictionary.Contains(convertedLettersOnly)
            : UaDictionary.Contains(convertedLower);
        bool targetHasUkrainianMorphologySignal = HasUkrainianMorphologySignal(convertedLower);
        bool targetHasInternalSoftSignSignal = HasInternalSoftSignSignal(convertedLower);
        bool targetHasDistinctUkrainianLetter = convertedLower.Any(c => c is 'і' or 'ї' or 'є' or 'ґ');
        bool singleTokenAllowlisted = tokenLength == 1
            && IsAutoSingleAllowlisted(convertedLower, CorrectionDirection.EnToUa);
        bool shortTokenAllowlisted = tokenLength == 2
            && IsAutoShortAllowlisted(convertedLower, CorrectionDirection.EnToUa);
        bool hasBoundaryLayoutLetter = word.Length > 0
            && (KeyboardLayoutMap.IsLayoutLetterChar(word[0]) || KeyboardLayoutMap.IsLayoutLetterChar(word[^1]));
        bool hasLeadingLayoutLetter = word.Length > 0 && KeyboardLayoutMap.IsLayoutLetterChar(word[0]);

        // Score the converted result as Ukrainian
        double sourceScore = ScoreEnglish(lower);
        if (!string.Equals(lower, lowerTrimmed, StringComparison.Ordinal))
            sourceScore = Math.Max(sourceScore, ScoreEnglish(lowerTrimmed));
        double sourceZeroRatio = ZeroBigramRatio(lower, EnBigramFreq);
        double targetScore = ScoreCyrillic(convertedLower);

        // Algorithmic guard: if the converted result has many impossible UA bigrams,
        // it cannot be a real Ukrainian word — reject without needing a dictionary.
        // Short words need stricter threshold (fewer bigrams = each one matters more).
        double uaZeroThreshold = convertedLower.Length <= 4 ? 0.34 : 0.55;
        double targetZeroRatio = ZeroBigramRatio(convertedLower, UaBigramFreq);
        bool allowBoundaryShortShape = mode == CorrectionMode.Auto
            && !containsDigit
            && tokenLength == 3
            && hasLeadingLayoutLetter
            && targetZeroRatio <= 0.50;
        bool allowShortDistinctUaShape = mode == CorrectionMode.Auto
            && !containsDigit
            && tokenLength == 3
            && word.All(char.IsLetter)
            && targetHasDistinctUkrainianLetter
            && targetScore >= 0.12;
        if (!targetDictionaryHit
            && !containsDigit
            && targetZeroRatio > uaZeroThreshold
            && !allowBoundaryShortShape
            && !allowShortDistinctUaShape
            && !targetHasUkrainianMorphologySignal)
            return null;

        double effectiveTargetScore = Clamp01(targetScore + ComputeStrongShapeBonus(targetScore, targetZeroRatio, targetDictionaryHit, tokenLength));
        double targetBoundaryAffinity = ComputeBoundaryAffinity(
            convertedLower,
            CorrectionDirection.EnToUa,
            wrapped: hasBoundaryLayoutLetter || prefix.Length > 0 || suffix.Length > 0);

        if (mode == CorrectionMode.Auto && !containsDigit)
        {
            if (tokenLength == 1 && !ShouldConvertSingleLetterToken(word, sourceScore, effectiveTargetScore, sourceDictionaryHit, targetDictionaryHit, convertedLower, CorrectionDirection.EnToUa))
                return null;

            if (tokenLength == 2 && !ShouldConvertShortToken(sourceScore, effectiveTargetScore, sourceDictionaryHit, targetDictionaryHit, convertedLower, CorrectionDirection.EnToUa))
                return null;
        }

        double confidence = ComputeConfidence(
            originalScore: sourceScore,
            targetScore: effectiveTargetScore,
            targetDictionaryHit: targetDictionaryHit,
            mode: mode);

        if (mode == CorrectionMode.Auto && singleTokenAllowlisted && !sourceDictionaryHit)
        {
            string fullAllowlisted = prefix + converted + suffix;
            string fullOriginalAllowlisted = prefix + word + suffix;
            return FinalizeCandidate(
                fullOriginalAllowlisted,
                fullAllowlisted,
                word,
                converted,
                CorrectionDirection.EnToUa,
                Math.Max(confidence, 0.60),
                $"Latin→UA single-allow src={sourceScore:F2} dst={targetScore:F2}",
                mode,
                sourceScore,
                targetScore,
                sourceDictionaryHit,
                targetDictionaryHit);
        }

        if (mode == CorrectionMode.Auto && shortTokenAllowlisted && !sourceDictionaryHit)
        {
            string fullAllowlisted = prefix + converted + suffix;
            string fullOriginalAllowlisted = prefix + word + suffix;
            return FinalizeCandidate(
                fullOriginalAllowlisted,
                fullAllowlisted,
                word,
                converted,
                CorrectionDirection.EnToUa,
                Math.Max(confidence, 0.56),
                $"Latin→UA short-allow src={sourceScore:F2} dst={targetScore:F2}",
                mode,
                sourceScore,
                targetScore,
                sourceDictionaryHit,
                targetDictionaryHit);
        }

        if (mode == CorrectionMode.Auto
            && !containsDigit
            && !sourceDictionaryHit
            && !targetDictionaryHit
            && tokenLength >= 4
            && sourceScore >= 0.18
            && sourceZeroRatio <= targetZeroRatio
            && targetZeroRatio >= 0.50
            && !targetHasUkrainianMorphologySignal)
        {
            return null;
        }

        if (mode == CorrectionMode.Auto
            && !containsDigit
            && !sourceDictionaryHit
            && !targetDictionaryHit
            && tokenLength >= 4
            && sourceScore >= 0.30
            && sourceZeroRatio <= 0.10
            && effectiveTargetScore < 0.48
            && effectiveTargetScore <= sourceScore + 0.14
            && !targetHasUkrainianMorphologySignal)
        {
            return null;
        }

        if (mode == CorrectionMode.Auto
            && !containsDigit
            && !sourceDictionaryHit
            && !targetDictionaryHit
            && tokenLength >= 5
            && sourceScore >= 0.20
            && sourceZeroRatio <= 0.25
            && targetZeroRatio <= 0.25
            && targetBoundaryAffinity >= 1.00
            && effectiveTargetScore < 0.50
            && !targetHasUkrainianMorphologySignal)
        {
            return null;
        }

        if (mode == CorrectionMode.Auto
            && !sourceDictionaryHit
            && !targetDictionaryHit
            && tokenLength >= 5
            && targetHasUkrainianMorphologySignal
            && effectiveTargetScore >= 0.30
            && confidence >= 0.18
            && sourceScore <= effectiveTargetScore + 0.06)
        {
            string fullMorphology = prefix + converted + suffix;
            string fullOriginalMorphology = prefix + word + suffix;
            return FinalizeCandidate(
                fullOriginalMorphology,
                fullMorphology,
                word,
                converted,
                CorrectionDirection.EnToUa,
                Math.Max(confidence, 0.52),
                $"Latin→UA morphology src={sourceScore:F2} dst={effectiveTargetScore:F2}",
                mode,
                sourceScore,
                effectiveTargetScore,
                sourceDictionaryHit,
                targetDictionaryHit);
        }

        if (mode == CorrectionMode.Auto
            && !sourceDictionaryHit
            && !targetDictionaryHit
            && tokenLength >= 5
            && sourceZeroRatio >= 0.50
            && sourceScore <= 0.28
            && effectiveTargetScore >= sourceScore + 0.02
            && targetZeroRatio <= 0.34
            && targetBoundaryAffinity >= 0.85
            && effectiveTargetScore >= 0.28
            && confidence >= 0.20)
        {
            string fullStrongShape = prefix + converted + suffix;
            string fullOriginalStrongShape = prefix + word + suffix;
            return FinalizeCandidate(
                fullOriginalStrongShape,
                fullStrongShape,
                word,
                converted,
                CorrectionDirection.EnToUa,
                Math.Max(confidence, 0.50),
                $"Latin→UA strong-shape src={sourceScore:F2} dst={effectiveTargetScore:F2} edge={targetBoundaryAffinity:F2}",
                mode,
                sourceScore,
                effectiveTargetScore,
                sourceDictionaryHit,
                targetDictionaryHit);
        }

        if (mode == CorrectionMode.Auto
            && !containsDigit
            && !sourceDictionaryHit
            && !targetDictionaryHit
            && !sourceProtectedShortLatinToken
            && tokenLength == 3
            && word.All(char.IsLetter)
            && targetHasDistinctUkrainianLetter
            && sourceScore <= 0.30
            && effectiveTargetScore >= 0.12
            && effectiveTargetScore >= sourceScore - 0.02)
        {
            string fullShortUaShape = prefix + converted + suffix;
            string fullOriginalShortUaShape = prefix + word + suffix;
            return FinalizeCandidate(
                fullOriginalShortUaShape,
                fullShortUaShape,
                word,
                converted,
                CorrectionDirection.EnToUa,
                Math.Max(confidence, 0.48),
                $"Latin→UA short-ua-shape src={sourceScore:F2} dst={effectiveTargetScore:F2}",
                mode,
                sourceScore,
                effectiveTargetScore,
                sourceDictionaryHit,
                targetDictionaryHit);
        }

        if (mode == CorrectionMode.Auto
            && !containsDigit
            && !sourceDictionaryHit
            && !targetDictionaryHit
            && tokenLength >= 5
            && targetHasInternalSoftSignSignal
            && sourceZeroRatio >= targetZeroRatio + 0.15
            && targetZeroRatio <= 0.30
            && effectiveTargetScore >= 0.22
            && effectiveTargetScore >= sourceScore + 0.02
            && confidence >= 0.16)
        {
            string fullSoftSign = prefix + converted + suffix;
            string fullOriginalSoftSign = prefix + word + suffix;
            return FinalizeCandidate(
                fullOriginalSoftSign,
                fullSoftSign,
                word,
                converted,
                CorrectionDirection.EnToUa,
                Math.Max(confidence, 0.48),
                $"Latin→UA soft-sign src={sourceScore:F2} dst={effectiveTargetScore:F2}",
                mode,
                sourceScore,
                effectiveTargetScore,
                sourceDictionaryHit,
                targetDictionaryHit);
        }

        if (mode == CorrectionMode.Auto
            && !containsDigit
            && !sourceDictionaryHit
            && !targetDictionaryHit
            && tokenLength == 3
            && hasLeadingLayoutLetter
            && sourceScore <= 0.04
            && targetZeroRatio <= 0.50
            && targetBoundaryAffinity >= 0.30
            && effectiveTargetScore >= 0.22
            && confidence >= 0.20)
        {
            string fullBoundaryShape = prefix + converted + suffix;
            string fullOriginalBoundaryShape = prefix + word + suffix;
            return FinalizeCandidate(
                fullOriginalBoundaryShape,
                fullBoundaryShape,
                word,
                converted,
                CorrectionDirection.EnToUa,
                Math.Max(confidence, 0.46),
                $"Latin→UA boundary-shape src={sourceScore:F2} dst={effectiveTargetScore:F2} edge={targetBoundaryAffinity:F2}",
                mode,
                sourceScore,
                effectiveTargetScore,
                sourceDictionaryHit,
                targetDictionaryHit);
        }

        if (!ShouldConvert(sourceScore, effectiveTargetScore, confidence, mode, tokenLength, sourceDictionaryHit, targetDictionaryHit, sourceHasLayoutSymbols))
            return null;

        string full = prefix + converted + suffix;
        string fullWord = prefix + word + suffix;
        return FinalizeCandidate(
            fullWord,
            full,
            word,
            converted,
            CorrectionDirection.EnToUa,
            confidence,
            $"Latin→UA src={sourceScore:F2} dst={effectiveTargetScore:F2} hyb={ComputeHybridScore(effectiveTargetScore, targetDictionaryHit, mode, tokenLength):F2}>{ComputeHybridScore(sourceScore, sourceDictionaryHit, mode, tokenLength):F2} conf={confidence:F2}",
            mode,
            sourceScore,
            effectiveTargetScore,
            sourceDictionaryHit,
            targetDictionaryHit);
    }

    // ─── Cyrillic word → try UA→EN ──────────────────────────────────────────

    private static CorrectionCandidate? EvaluateCyrillic(
        string word, string prefix, string suffix, int tokenLength,
        CorrectionMode mode)
    {
        string lower = word.ToLowerInvariant();
        bool containsDigit = word.Any(char.IsDigit);
        bool sourceHasLayoutSymbols = false;
        string lettersOnly = ExtractLettersOnly(word).ToLowerInvariant();

        // If the word is a known Ukrainian word → correct layout → do NOT convert
        string lowerTrimmed = lower.TrimEnd('.', ',', ';', ':', '<', '>');
        bool sourceDictionaryHit = containsDigit
            ? UaDictionary.Contains(lettersOnly)
            : UaDictionary.Contains(lower) || UaDictionary.Contains(lowerTrimmed);

        if (mode != CorrectionMode.Safe && sourceDictionaryHit)
            return null;

        // Try to convert UA→EN
        string? converted = KeyboardLayoutMap.ConvertUaToEn(word, strict: !containsDigit);
        if (converted == null) return null;

        string convertedCore = TrimTrailingLiteralPunctuation(converted);
        string convertedCoreWithoutLeadingPunctuation = TrimLeadingLiteralPunctuation(convertedCore);
        if (string.IsNullOrEmpty(convertedCore))
            return null;

        if (convertedCoreWithoutLeadingPunctuation.Length != convertedCore.Length
            && !LooksLikeBoundaryTechnicalToken(converted, convertedCoreWithoutLeadingPunctuation))
            return null;

        convertedCore = convertedCoreWithoutLeadingPunctuation;

        // Reject if converted result contains non-letter characters
        // This prevents false positives like хїфі → []as
        if (convertedCore.Any(c => !char.IsLetter(c) && !KeyboardLayoutMap.IsWordConnector(c) && !char.IsDigit(c)))
            return null;

        string convertedLower = convertedCore.ToLowerInvariant();
        string convertedLettersOnly = ExtractLettersOnly(convertedCore).ToLowerInvariant();
        bool targetDictionaryHit = containsDigit
            ? IsLikelyEnglishToken(convertedLettersOnly)
            : IsLikelyEnglishToken(convertedLower);
        bool targetConversationalHit = !containsDigit && EnglishConversationalTokens.Contains(convertedLower);
        bool targetLooksLikeTechToken = containsDigit && LooksLikeTechnicalLatinToken(convertedLettersOnly);
        bool targetLooksLikeTechTextToken = !containsDigit && LooksLikeTechnicalTextToken(convertedLower);
        bool sourceHasUkrainianMorphologySignal = HasUkrainianMorphologySignal(lower);
        bool singleTokenAllowlisted = tokenLength == 1
            && IsAutoSingleAllowlisted(convertedLower, CorrectionDirection.UaToEn);
        bool shortTokenAllowlisted = tokenLength == 2
            && IsAutoShortAllowlisted(convertedLower, CorrectionDirection.UaToEn);
        bool hasConvertedLeadingLayoutPunctuation = converted.Length > 0 && char.IsPunctuation(converted[0]) && !KeyboardLayoutMap.IsWordConnector(converted[0]);
        if (sourceDictionaryHit && !targetLooksLikeTechToken)
            return null;

        double sourceScore = ScoreCyrillic(lower);
        if (!string.Equals(lower, lowerTrimmed, StringComparison.Ordinal))
            sourceScore = Math.Max(sourceScore, ScoreCyrillic(lowerTrimmed));
        double sourceZeroRatio = ZeroBigramRatio(lower, UaBigramFreq);
        double targetScore = ScoreEnglish(convertedLower);

        double targetBoundaryAffinity = ComputeBoundaryAffinity(
            convertedLower,
            CorrectionDirection.UaToEn,
            wrapped: hasConvertedLeadingLayoutPunctuation || prefix.Length > 0 || suffix.Length > 0);

        bool targetStrongEnglishShape = !containsDigit
            && !targetDictionaryHit
            && !sourceDictionaryHit
            && tokenLength >= 4
            && sourceScore <= 0.40
            && HasStrongEnglishShapeSignal(convertedLower);
        bool targetLooksLikeTechLatinWord = !containsDigit && LooksLikeTechnicalLatinWord(convertedLower);
        bool targetLexicalHit = targetDictionaryHit || targetStrongEnglishShape;
        bool sourceLooksPlausiblyUkrainian = sourceHasUkrainianMorphologySignal
            || lower.Any(c => c is 'і' or 'ї' or 'є' or 'ґ')
            || (tokenLength <= 4
                && sourceScore >= 0.14
                && ComputeVowelRatio(lower, englishLike: false) >= 0.25);

        // Algorithmic guard: if the converted result has many impossible EN bigrams,
        // it cannot be real English text — reject without needing a dictionary.
        double enZeroThreshold = convertedLower.Length <= 4 ? 0.34 : 0.55;
        double targetZeroRatio = ZeroBigramRatio(convertedLower, EnBigramFreq);
        bool isVowellessAcronym = !containsDigit 
            && tokenLength is >= 3 and <= 5 
            && targetLooksLikeTechLatinWord
            && sourceScore <= 0.20
            && sourceZeroRatio >= 0.66
            && !convertedLower.Any(c => EnVowels.Contains(c));

        bool allowShortTechLatinBypass = (tokenLength is >= 3 and <= 4
            && sourceScore <= 0.10
            && sourceZeroRatio >= 0.66
            && targetScore >= 0.40
            && convertedLower.Any(c => c is 'x' or 'z' or 'v' or 'k' or 't' or 'p' or 'g' or 'd' or 'm')) || isVowellessAcronym;
        if (!targetLexicalHit && !containsDigit && targetZeroRatio > enZeroThreshold && !allowShortTechLatinBypass)
            return null;

        double effectiveTargetScore = Clamp01(targetScore + ComputeStrongShapeBonus(targetScore, targetZeroRatio, targetLexicalHit, tokenLength));

        // Block repetitive noise: if both source and converted have low character diversity,
        // it's random key mashing, not a real word typed in the wrong layout.
        if (!targetDictionaryHit && !containsDigit && tokenLength >= 3)
        {
            int srcDistinct = lower.Where(char.IsLetter).Distinct().Count();
            int dstDistinct = convertedLower.Where(char.IsLetter).Distinct().Count();
            int srcTotal = lower.Count(char.IsLetter);
            int dstTotal = convertedLower.Count(char.IsLetter);
            if (srcTotal > 0 && dstTotal > 0
                && (double)srcDistinct / srcTotal < 0.70
                && (double)dstDistinct / dstTotal < 0.70)
                return null;
        }

        if (mode == CorrectionMode.Auto && !containsDigit)
        {
            if (tokenLength == 1 && !ShouldConvertSingleLetterToken(word, sourceScore, effectiveTargetScore, sourceDictionaryHit, targetDictionaryHit, convertedLower, CorrectionDirection.UaToEn))
                return null;

            if (tokenLength == 2)
            {
                if (sourceDictionaryHit)
                    return null;

                if (!shortTokenAllowlisted
                    && !ShouldConvertShortToken(sourceScore, effectiveTargetScore, sourceDictionaryHit, targetDictionaryHit, convertedLower, CorrectionDirection.UaToEn))
                    return null;
            }
        }

        double confidence = ComputeConfidence(
            originalScore: sourceScore,
            targetScore: effectiveTargetScore,
            targetDictionaryHit: targetLexicalHit,
            mode: mode);

        if (mode == CorrectionMode.Auto && singleTokenAllowlisted && !sourceDictionaryHit)
        {
            string fullOriginalAllowlisted = prefix + word + suffix;
            return FinalizeCandidate(
                fullOriginalAllowlisted,
                prefix + converted + suffix,
                word,
                converted,
                CorrectionDirection.UaToEn,
                Math.Max(confidence, 0.60),
                $"Cyrillic→EN single-allow src={sourceScore:F2} dst={targetScore:F2}",
                mode,
                sourceScore,
                targetScore,
                sourceDictionaryHit,
                targetLexicalHit);
        }

        if (mode == CorrectionMode.Auto && shortTokenAllowlisted && !sourceDictionaryHit)
        {
            string fullOriginalAllowlisted = prefix + word + suffix;
            return FinalizeCandidate(
                fullOriginalAllowlisted,
                prefix + converted + suffix,
                word,
                converted,
                CorrectionDirection.UaToEn,
                Math.Max(confidence, 0.56),
                $"Cyrillic→EN short-allow src={sourceScore:F2} dst={targetScore:F2}",
                mode,
                sourceScore,
                targetScore,
                sourceDictionaryHit,
                targetLexicalHit);
        }

        if (mode == CorrectionMode.Auto
            && !containsDigit
            && !sourceDictionaryHit
            && !targetLexicalHit
            && !targetConversationalHit
            && !targetLooksLikeTechLatinWord
            && !targetLooksLikeTechTextToken
            && tokenLength <= 4
            && sourceScore >= 0.18)
        {
            return null;
        }

        if (mode == CorrectionMode.Auto
            && !containsDigit
            && !sourceDictionaryHit
            && sourceHasUkrainianMorphologySignal
            && !targetLexicalHit
            && !targetConversationalHit
            && !targetLooksLikeTechLatinWord
            && !targetLooksLikeTechTextToken)
        {
            return null;
        }

        if (mode == CorrectionMode.Auto
            && targetConversationalHit
            && !sourceDictionaryHit
            && tokenLength >= 3
            && effectiveTargetScore >= 0.18
            && sourceScore <= 0.60)
        {
            string fullOriginalConversational = prefix + word + suffix;
            return FinalizeCandidate(
                fullOriginalConversational,
                prefix + converted + suffix,
                word,
                converted,
                CorrectionDirection.UaToEn,
                Math.Max(confidence, 0.53),
                $"Cyrillic→EN conversational src={sourceScore:F2} dst={effectiveTargetScore:F2}",
                mode,
                sourceScore,
                effectiveTargetScore,
                sourceDictionaryHit,
                targetLexicalHit);
        }

        if (mode == CorrectionMode.Auto && containsDigit && targetLooksLikeTechToken)
        {
            string fullOriginalTech = prefix + word + suffix;
            return FinalizeCandidate(
                fullOriginalTech,
                prefix + converted + suffix,
                word,
                converted,
                CorrectionDirection.UaToEn,
                Math.Max(confidence, 0.62),
                $"Cyrillic→EN tech src={sourceScore:F2} dst={effectiveTargetScore:F2} tech=1",
                mode,
                sourceScore,
                effectiveTargetScore,
                sourceDictionaryHit,
                targetLexicalHit);
        }

        if (mode == CorrectionMode.Auto
            && targetLooksLikeTechTextToken
            && !sourceDictionaryHit
            && tokenLength >= 5
            && effectiveTargetScore >= 0.24
            && sourceScore <= 0.32)
        {
            string fullOriginalTechText = prefix + word + suffix;
            return FinalizeCandidate(
                fullOriginalTechText,
                prefix + converted + suffix,
                word,
                converted,
                CorrectionDirection.UaToEn,
                Math.Max(confidence, 0.57),
                $"Cyrillic→EN tech-text src={sourceScore:F2} dst={effectiveTargetScore:F2} marker=1",
                mode,
                sourceScore,
                effectiveTargetScore,
                sourceDictionaryHit,
                targetDictionaryHit);
        }

        if (mode == CorrectionMode.Auto
            && (targetLooksLikeTechLatinWord || allowShortTechLatinBypass)
            && !sourceDictionaryHit
            && !sourceLooksPlausiblyUkrainian
            && sourceScore <= 0.34
            && (effectiveTargetScore >= 0.28 || isVowellessAcronym)
            && (confidence >= 0.20 || isVowellessAcronym)
            && (tokenLength >= 6 || allowShortTechLatinBypass || isVowellessAcronym))
        {
            string fullOriginalTechLatin = prefix + word + suffix;
            return FinalizeCandidate(
                fullOriginalTechLatin,
                prefix + converted + suffix,
                word,
                converted,
                CorrectionDirection.UaToEn,
                Math.Max(confidence, tokenLength <= 4 ? 0.54 : 0.56),
                $"Cyrillic→EN tech-shape src={sourceScore:F2} dst={effectiveTargetScore:F2} tech=1",
                mode,
                sourceScore,
                effectiveTargetScore,
                sourceDictionaryHit,
                targetLexicalHit);
        }

        if (mode == CorrectionMode.Auto
            && !containsDigit
            && !sourceDictionaryHit
            && !targetDictionaryHit
            && tokenLength >= 4
            && sourceScore <= 0.32
            && effectiveTargetScore >= sourceScore + 0.05
            && targetZeroRatio <= 0.25
            && targetBoundaryAffinity >= 0.20
            && confidence >= 0.20)
        {
            string fullOriginalStrongShape = prefix + word + suffix;
            return FinalizeCandidate(
                fullOriginalStrongShape,
                prefix + converted + suffix,
                word,
                converted,
                CorrectionDirection.UaToEn,
                Math.Max(confidence, 0.50),
                $"Cyrillic→EN strong-shape src={sourceScore:F2} dst={effectiveTargetScore:F2} edge={targetBoundaryAffinity:F2}",
                mode,
                sourceScore,
                effectiveTargetScore,
                sourceDictionaryHit,
                targetLexicalHit);
        }

            if (!ShouldConvert(sourceScore, effectiveTargetScore, confidence, mode, tokenLength, sourceDictionaryHit, targetLexicalHit, sourceHasLayoutSymbols))
            return null;

        string reasonSuffix = string.Empty;
            if (mode == CorrectionMode.Auto && !targetLexicalHit)
        {
            // Algorithmic bypass: if source has many impossible bigrams in the source language,
            // it's clearly gibberish typed in the wrong layout — skip strict selector check.
            // This is the core "Punto Switcher" approach: no dictionary needed.
            bool sourceIsGibberish = ZeroBigramRatio(lower, UaBigramFreq) > 0.60;

            if (!sourceIsGibberish)
            {
                var selectorDecision = AutoCorrectionSelector.Evaluate(
                    original: word,
                    converted: converted,
                    direction: CorrectionDirection.UaToEn,
                    sourceScore: sourceScore,
                    targetScore: effectiveTargetScore,
                    confidence: confidence);

                if (!selectorDecision.Accept)
                    return null;

                reasonSuffix = $" {selectorDecision.Reason}";
            }
            else
            {
                reasonSuffix = " selector=src-gibberish-bypass";
            }
        }

        string full = prefix + converted + suffix;
        string fullWord = prefix + word + suffix;
        return FinalizeCandidate(
            fullWord,
            full,
            word,
            converted,
            CorrectionDirection.UaToEn,
            confidence,
            $"Cyrillic→EN src={sourceScore:F2} dst={effectiveTargetScore:F2} hyb={ComputeHybridScore(effectiveTargetScore, targetDictionaryHit, mode, tokenLength):F2}>{ComputeHybridScore(sourceScore, sourceDictionaryHit, mode, tokenLength):F2} conf={confidence:F2}{reasonSuffix}",
            mode,
            sourceScore,
            effectiveTargetScore,
            sourceDictionaryHit,
            targetLexicalHit);
    }

    // ─── Scoring functions ───────────────────────────────────────────────────

    // ─── Corpus-based bigram frequency tables ──────────────────────────────
    // Derived from large corpus analysis (Peter Norvig's Google Books data for EN,
    // Ukrainian National Corpus for UA). Values are relative frequencies (0.0–1.0).
    // Using these avoids any dependency on the word-list size for scoring.

    // English bigram frequencies (top ~120 bigrams, normalized to max=1.0)
    // Source: Norvig/Mayzner corpus data, most common pairs in English text
    private static readonly Dictionary<string, double> EnBigramFreq =
        new(StringComparer.Ordinal)
    {
        {"th",1.00},{"he",0.87},{"in",0.80},{"er",0.78},{"an",0.75},{"re",0.73},
        {"on",0.71},{"en",0.67},{"at",0.65},{"es",0.63},{"ed",0.62},{"or",0.62},
        {"te",0.60},{"ti",0.59},{"st",0.59},{"ar",0.57},{"nd",0.57},{"to",0.56},
        {"it",0.55},{"is",0.54},{"ng",0.53},{"io",0.52},{"le",0.51},{"al",0.51},
        {"as",0.50},{"ha",0.50},{"ou",0.49},{"hi",0.48},{"se",0.48},{"of",0.47},
        {"nt",0.46},{"ea",0.46},{"ly",0.45},{"ne",0.45},{"ri",0.45},{"li",0.44},
        {"de",0.44},{"ve",0.43},{"no",0.43},{"me",0.42},{"ra",0.42},{"el",0.42},
        {"ta",0.41},{"la",0.41},{"ma",0.40},{"si",0.40},{"ca",0.39},{"ge",0.39},
        {"ic",0.39},{"be",0.38},{"ce",0.38},{"ch",0.38},{"ho",0.38},{"ll",0.37},
        {"pe",0.37},{"ur",0.37},{"wa",0.37},{"wh",0.36},{"ab",0.36},{"co",0.36},
        {"ro",0.35},{"us",0.35},{"tr",0.35},{"pr",0.34},{"ot",0.34},{"ss",0.34},
        {"fe",0.33},{"fo",0.33},{"fi",0.33},{"ac",0.33},{"ad",0.32},{"am",0.32},
        {"ap",0.32},{"ec",0.32},{"ew",0.31},{"ex",0.31},{"fr",0.31},{"fu",0.30},
        {"gh",0.30},{"gi",0.30},{"gr",0.30},{"gu",0.30},{"id",0.30},{"if",0.29},
        {"im",0.29},{"ip",0.29},{"ir",0.29},{"iv",0.29},{"ke",0.29},{"ki",0.29},
        {"kn",0.28},{"lo",0.28},{"lu",0.28},{"mi",0.28},{"mo",0.28},{"mu",0.28},
        {"nc",0.28},{"od",0.27},{"om",0.27},{"op",0.27},{"os",0.27},{"ow",0.27},
        {"pa",0.27},{"pi",0.27},{"pl",0.27},{"po",0.27},{"pp",0.26},{"pu",0.26},
        {"qu",0.26},{"rd",0.26},{"rn",0.26},{"rs",0.26},{"rt",0.26},{"ru",0.26},
        {"sa",0.26},{"sc",0.26},{"sh",0.25},{"sk",0.25},{"sl",0.25},{"sm",0.25},
        {"sn",0.25},{"so",0.25},{"sp",0.25},{"su",0.24},{"sw",0.24},{"sy",0.24},
        {"tu",0.24},{"tw",0.24},{"ty",0.24},{"un",0.24},{"up",0.23},
        {"ut",0.23},{"ue",0.23},{"ul",0.23},{"um",0.23},{"wi",0.23},{"wo",0.23},
        {"ye",0.22},{"yo",0.22},{"ys",0.22},{"ck",0.22},{"ct",0.22},{"cu",0.22},
        {"cy",0.22},{"da",0.22},{"di",0.21},{"do",0.21},{"dr",0.21},{"du",0.21},
        {"dy",0.21},{"eg",0.21},{"em",0.21},{"ep",0.21},{"eq",0.20},{"et",0.20},
        {"ev",0.20},{"ey",0.20},{"ee",0.20},{"ie",0.20},{"il",0.19},
        {"nf",0.19},{"nk",0.19},{"nl",0.18},{"ns",0.18},{"oc",0.18},{"oi",0.18},
        {"ol",0.18},{"oo",0.18},{"ph",0.18},{"pt",0.18},{"rb",0.17},{"rc",0.17},
        {"rf",0.17},{"rk",0.17},{"rl",0.17},{"rm",0.17},{"rr",0.17},{"rv",0.16},
        {"rw",0.16},{"ry",0.16},{"sq",0.16},{"tf",0.16},{"tl",0.16},
        {"tn",0.16},{"ts",0.15},{"tt",0.15},{"wl",0.15},{"wn",0.15},{"wr",0.15},
        {"xp",0.15},{"xt",0.15},{"xy",0.14},{"ze",0.14},{"zi",0.14},{"zo",0.14},
        // Additional common bigrams
        {"go",0.28},{"ni",0.26},{"lb",0.12},{"rg",0.14},{"gb",0.10},{"bg",0.10},
        {"og",0.16},{"ag",0.18},{"ig",0.16},{"ug",0.14},
        {"bi",0.16},{"bo",0.18},{"br",0.21},{"bu",0.21},{"by",0.16},
        {"nb",0.12},{"ob",0.18},{"ub",0.14},
    };

    // Ukrainian bigram frequencies (~130 bigrams, normalized to max=1.0)
    // Source: Ukrainian National Corpus analysis
    private static readonly Dictionary<string, double> UaBigramFreq =
        new(StringComparer.Ordinal)
    {
        {"на",1.00},{"ні",0.91},{"не",0.89},{"по",0.87},{"пр",0.85},{"ст",0.84},
        {"та",0.83},{"ти",0.82},{"то",0.81},{"ть",0.80},{"ся",0.79},{"ко",0.78},
        {"ра",0.77},{"ро",0.76},{"ри",0.75},{"ре",0.74},{"ві",0.73},{"ви",0.72},
        {"до",0.71},{"де",0.70},{"що",0.69},{"за",0.68},{"ін",0.67},{"ій",0.66},
        {"ки",0.65},{"ку",0.64},{"ка",0.63},{"ло",0.62},{"ли",0.61},{"ле",0.60},
        {"лі",0.59},{"ла",0.58},{"ми",0.57},{"ди",0.57},{"мо",0.57},{"ма",0.56},{"ме",0.56},
        {"но",0.55},{"ну",0.55},{"ни",0.54},{"ва",0.62},{"да",0.70},
        {"ів",0.53},{"іс",0.52},{"іт",0.52},{"ік",0.51},{"іл",0.50},
        {"ор",0.50},{"ом",0.49},{"ол",0.49},{"он",0.48},{"ос",0.48},{"от",0.48},
        {"ою",0.55},{"ий",0.60},{"ай",0.40},{"ве",0.50},{"па",0.42},{"са",0.35},
        {"об",0.47},{"ов",0.47},{"од",0.47},{"ен",0.46},{"ем",0.46},{"ел",0.46},
        {"ер",0.45},{"ан",0.45},{"ак",0.45},{"ар",0.44},{"ат",0.44},{"ав",0.44},
        {"ал",0.44},{"аз",0.43},{"тр",0.43},{"те",0.42},{"ту",0.42},
        {"сь",0.41},{"сл",0.40},{"со",0.40},{"си",0.40},{"се",0.40},{"ис",0.35},
        {"ша",0.39},{"шт",0.39},{"ши",0.38},{"ші",0.38},
        {"зн",0.38},{"зі",0.37},{"зо",0.37},{"бо",0.37},{"бу",0.36},{"бе",0.36},
        {"ба",0.36},{"чн",0.36},{"чи",0.35},{"ча",0.35},{"чо",0.35},{"чк",0.34},
        {"хо",0.34},{"хн",0.34},{"ха",0.34},{"ги",0.33},{"ге",0.33},
        {"го",0.33},{"га",0.33},{"гр",0.33},{"мі",0.32},{"му",0.32},{"пе",0.32},
        {"пі",0.32},{"пу",0.31},{"фо",0.31},{"фі",0.31},{"фа",0.31},
        {"цю",0.30},{"ці",0.30},{"це",0.30},{"цк",0.29},{"цн",0.29},
        {"ях",0.29},{"яв",0.28},{"яс",0.28},{"юч",0.27},{"юн",0.27},{"єм",0.27},
        {"єт",0.26},{"єс",0.26},{"лю",0.26},{"ля",0.25},{"ль",0.25},
        {"нь",0.24},{"дь",0.24},{"зь",0.23},
        {"кр",0.22},{"бр",0.21},{"вр",0.21},
        {"жн",0.21},{"жи",0.20},{"же",0.20},{"жа",0.30},{"жо",0.20},{"жу",0.19},
        {"їх",0.19},{"їм",0.19},{"їс",0.18},{"дн",0.18},{"дв",0.18},{"дж",0.18},
        {"вн",0.18},{"вж",0.17},{"вс",0.17},{"вт",0.17},{"кн",0.17},{"кл",0.17},
        {"фр",0.16},{"хв",0.16},{"цв",0.16},{"шк",0.16},{"шн",0.15},
        {"гі",0.30},{"лк",0.22},{"аб",0.38},{"иг",0.22},{"еж",0.28},
        {"ке",0.35},{"із",0.35},{"из",0.30},{"зу",0.22},{"ду",0.40},
        {"ок",0.32},{"оч",0.28},{"ід",0.28},{"ім",0.26},{"ич",0.24},
        {"аю",0.22},{"еш",0.20},
        {"ил",0.28},{"вч",0.20},{"др",0.32},{"ру",0.35},{"уг",0.20},{"гу",0.22},
        {"іа",0.30},{"ур",0.32},{"уд",0.28},{"иш",0.25},{"ше",0.30},
        {"ап",0.38},{"пк",0.22},{"оп",0.32},{"мп",0.20},
        {"рк",0.20},{"нк",0.22},{"уп",0.27},
    };

    // English trigram frequencies (top ~100 trigrams, normalized)
    // Source: Norvig corpus
    private static readonly Dictionary<string, double> EnTrigramFreq =
        new(StringComparer.Ordinal)
    {
        {"the",1.00},{"and",0.82},{"ing",0.76},{"ion",0.68},{"ent",0.65},{"tio",0.62},
        {"ati",0.60},{"for",0.57},{"her",0.55},{"ter",0.53},{"hat",0.52},{"tha",0.52},
        {"ere",0.51},{"con",0.50},{"res",0.49},{"ver",0.48},{"all",0.47},{"ons",0.46},
        {"nce",0.46},{"men",0.45},{"ith",0.44},{"ted",0.43},{"ers",0.43},{"pro",0.42},
        {"thi",0.42},{"wit",0.41},{"are",0.40},{"ess",0.40},{"not",0.40},{"ive",0.39},
        {"was",0.39},{"ect",0.38},{"rea",0.38},{"com",0.38},{"eve",0.37},{"per",0.37},
        {"int",0.36},{"est",0.36},{"sta",0.35},{"tly",0.35},{"ali",0.35},{"ine",0.34},
        {"ous",0.34},{"his",0.33},{"hen",0.33},{"one",0.32},{"out",0.32},{"tic",0.32},
        {"ble",0.31},{"tra",0.31},{"sti",0.31},{"ant",0.30},{"cal",0.30},{"rin",0.30},
        {"str",0.29},{"pre",0.29},{"rel",0.29},{"tin",0.28},{"tur",0.28},{"uni",0.28},
        {"ear",0.27},{"eri",0.27},{"ort",0.27},{"ran",0.27},{"sed",0.26},{"ser",0.26},
        {"ste",0.26},{"sub",0.25},{"tal",0.25},{"tan",0.25},{"tar",0.25},{"ten",0.25},
        {"til",0.24},{"tor",0.24},{"tri",0.24},{"und",0.23},
        {"use",0.23},{"ust",0.22},{"ute",0.22},{"ven",0.22},{"war",0.22},{"ork",0.22},
        {"orn",0.21},{"ock",0.21},{"ose",0.21},{"oth",0.21},{"ove",0.21},{"own",0.20},
        {"ple",0.20},{"por",0.20},{"pot",0.20},{"pri",0.20},{"pur",0.19},
        {"que",0.19},{"qui",0.19},{"rec",0.19},{"ref",0.19},{"reg",0.18},{"rep",0.18},
        {"ack",0.18},{"act",0.18},{"age",0.18},{"ale",0.18},{"ame",0.18},{"ard",0.17},
        {"ary",0.17},{"ase",0.17},{"ate",0.17},{"ath",0.17},{"end",0.17},
        {"ern",0.16},{"ish",0.16},{"ism",0.16},{"ist",0.16},{"ity",0.16},
        {"ize",0.15},{"ful",0.15},{"lar",0.15},{"led",0.15},{"ler",0.15},
        {"let",0.14},{"lin",0.14},{"lis",0.14},{"lit",0.14},{"lon",0.14},{"low",0.14},
        {"nde",0.14},{"ndi",0.13},{"ner",0.13},{"nes",0.13},{"net",0.13},{"nge",0.13},
        {"nal",0.13},{"nec",0.12},{"sel",0.12},{"set",0.12},
        {"sio",0.12},{"siv",0.12},{"ski",0.12},{"son",0.12},{"spe",0.11},{"spl",0.11},
        {"spr",0.11},{"squ",0.11},{"sse",0.11},{"ssi",0.11},{"sso",0.11},{"sst",0.11},
        {"enn",0.10},{"ens",0.10},{"fer",0.10},{"fic",0.10},
    };

    // Ukrainian trigram frequencies (top ~80 trigrams, normalized)
    private static readonly Dictionary<string, double> UaTrigramFreq =
        new(StringComparer.Ordinal)
    {
        {"ого",1.00},{"ння",0.93},
        {"ств",0.88},{"ані",0.84},{"ати",0.82},{"ити",0.80},{"ові",0.79},
        {"ним",0.76},{"них",0.74},{"але",0.73},{"про",0.72},{"або",0.70},
        {"тис",0.68},{"ків",0.67},{"час",0.66},{"ній",0.64},
        {"ної",0.63},{"все",0.62},{"при",0.61},{"між",0.59},{"має",0.58},
        {"над",0.57},{"для",0.55},{"уже",0.52},
        {"вже",0.51},{"хто",0.50},{"від",0.49},{"під",0.48},
        {"цих",0.44},{"цьо",0.43},{"цею",0.42},{"цим",0.42},{"ням",0.41},{"нях",0.40},
        {"ник",0.40},{"ниц",0.39},{"нис",0.39},{"нит",0.38},{"ниш",0.38},
        {"нів",0.37},{"ніс",0.37},{"нік",0.37},{"ніт",0.36},
        {"нін",0.35},{"ріс",0.35},{"рій",0.34},{"рів",0.34},{"рим",0.33},
        {"рих",0.33},{"рин",0.32},{"рис",0.32},{"рит",0.32},{"рик",0.31},
        {"тим",0.30},{"тих",0.30},{"тин",0.30},
        {"стр",0.29},{"сти",0.28},{"сте",0.28},{"сто",0.28},
        {"пра",0.27},{"пре",0.27},{"кра",0.26},
        {"кри",0.26},{"кро",0.26},{"хар",0.25},{"хор",0.25},{"шко",0.25},
        {"шок",0.24},{"цін",0.24},{"цік",0.24},{"жит",0.23},{"жив",0.23},
        {"лис",0.23},{"лит",0.22},{"лих",0.22},{"лін",0.22},{"мак",0.21},
        {"мас",0.21},{"мат",0.20},{"мен",0.20},{"мер",0.20},{"мес",0.19},
        {"мет",0.19},{"мис",0.18},{"мит",0.18},{"моч",0.17},{"мох",0.17},
        {"дар",0.17},{"дат",0.16},{"две",0.16},{"дви",0.16},{"ден",0.16},
        {"дер",0.16},{"диш",0.15},{"дим",0.15},{"дих",0.15},{"дій",0.15},
        {"дін",0.14},{"дів",0.14},{"діл",0.14},{"діс",0.13},{"діт",0.13},
        {"ілк",0.32},{"кни",0.35},{"ере",0.35},{"гру",0.34},{"руп",0.28},{"упу",0.18},
    };

    /// <summary>Scores how plausible a lowercase string is as English text (0.0–1.0).
    /// Uses corpus-based bigram/trigram frequency tables — independent of word list size.</summary>
    private static double ScoreEnglish(string lower)
    {
        if (lower.Length == 0) return 0;

        int letters = lower.Count(char.IsLetter);
        if (letters == 0) return 0;

        // Fast reject: non-Latin letters → cannot be English
        if (lower.Any(c => char.IsLetter(c) && c >= 128)) return 0.0;

        // Dictionary/morphology shortcuts give a strong signal but are NOT required
        if (EnDictionary.Contains(lower)) return 1.0;
        if (HasEnglishDictionaryBaseForm(lower)) return 0.92;

        // Corpus bigram score
        double bigramScore = ScoreWithFreqTable(lower, EnBigramFreq, 2);

        // Corpus trigram score
        double trigramScore = lower.Length >= 3
            ? ScoreWithFreqTable(lower, EnTrigramFreq, 3)
            : 0.0;

        // Consonant/vowel ratio (English ~38-42% vowels)
        double cvScore = ConsonantVowelScore(lower, isEnglish: true);

        double score = lower.Length <= 3
            ? Clamp01((bigramScore * 0.65) + (cvScore * 0.35))
            : Clamp01((bigramScore * 0.40) + (trigramScore * 0.40) + (cvScore * 0.20));

        // Penalty if word contains Cyrillic-only keyboard layout symbols
        if (lower.Any(KeyboardLayoutMap.IsLayoutLetterChar))
            score *= 0.42;

        return Clamp01(score);
    }

    /// <summary>Scores how plausible a lowercase string is as Ukrainian text (0.0–1.0).
    /// Uses corpus-based bigram/trigram frequency tables — independent of word list size.</summary>
    private static double ScoreCyrillic(string lower)
    {
        if (lower.Length == 0) return 0;

        int letters = lower.Count(char.IsLetter);
        if (letters == 0) return 0;

        // Fast reject: non-Cyrillic letters → cannot be Ukrainian
        if (lower.Any(c => char.IsLetter(c) && (c < '\u0400' || c > '\u04FF'))) return 0.0;

        // Dictionary shortcut — bonus signal but not required
        if (UaDictionary.Contains(lower)) return 1.0;

        // Corpus bigram score
        double bigramScore = ScoreWithFreqTable(lower, UaBigramFreq, 2);

        // Corpus trigram score
        double trigramScore = lower.Length >= 3
            ? ScoreWithFreqTable(lower, UaTrigramFreq, 3)
            : 0.0;

        // Consonant/vowel ratio (Ukrainian ~40-50% vowels)
        double cvScore = ConsonantVowelScore(lower, isEnglish: false);

        return lower.Length <= 3
            ? Clamp01((bigramScore * 0.65) + (cvScore * 0.35))
            : Clamp01((bigramScore * 0.40) + (trigramScore * 0.40) + (cvScore * 0.20));
    }

    /// <summary>Computes average frequency-table score for n-grams in a string.</summary>
    private static double ScoreWithFreqTable(string lower, Dictionary<string, double> table, int n)
    {
        if (lower.Length < n) return 0.0;

        double total = 0.0;
        int count = 0;

        for (int i = 0; i <= lower.Length - n; i++)
        {
            // Only score letter-only n-grams
            bool allLetters = true;
            for (int k = 0; k < n; k++)
            {
                if (!char.IsLetter(lower[i + k])) { allLetters = false; break; }
            }
            if (!allLetters) continue;

            string gram = lower.Substring(i, n);
            total += table.TryGetValue(gram, out double freq) ? freq : 0.0;
            count++;
        }

        return count == 0 ? 0.0 : Clamp01(total / count);
    }

    private static bool ShouldConvert(
        double originalScore,
        double targetScore,
        double confidence,
        CorrectionMode mode,
        int tokenLength,
        bool sourceDictionaryHit,
        bool targetDictionaryHit,
        bool sourceHasLayoutSymbols)
    {
        double sourceHybrid = ComputeHybridScore(originalScore, sourceDictionaryHit, mode, tokenLength);
        double targetHybrid = ComputeHybridScore(targetScore, targetDictionaryHit, mode, tokenLength);
        double margin = targetHybrid - sourceHybrid;

        if (targetHybrid <= sourceHybrid)
            return false;

        if (targetHybrid < ComputeMinimumHybridScore(mode, tokenLength, targetDictionaryHit, sourceHasLayoutSymbols))
            return false;

        if (margin < ComputeInertiaMargin(mode, tokenLength, targetDictionaryHit, sourceDictionaryHit))
            return false;

        if (mode == CorrectionMode.Safe)
            return confidence >= 0.18;

        if (tokenLength >= 5 && !sourceDictionaryHit)
            return confidence >= 0.18;

        if (tokenLength >= 3 && !sourceDictionaryHit)
            return confidence >= 0.16;

        return confidence >= 0.22;
    }

    private static bool ShouldConvertShortToken(
        double originalScore,
        double targetScore,
        bool sourceDictionaryHit,
        bool targetDictionaryHit,
        string convertedLower,
        CorrectionDirection direction)
    {
        if (sourceDictionaryHit)
            return false;

        double sourceHybrid = ComputeHybridScore(originalScore, sourceDictionaryHit, CorrectionMode.Auto, tokenLength: 2);
        double targetHybrid = ComputeHybridScore(targetScore, targetDictionaryHit, CorrectionMode.Auto, tokenLength: 2);
        double margin = targetHybrid - sourceHybrid;

        if (targetHybrid <= sourceHybrid)
            return false;

        if (direction == CorrectionDirection.UaToEn && AutoShortEnAllowlist.Contains(convertedLower))
            return targetScore >= 0.22 && margin >= 0.10;

        if (targetDictionaryHit)
            return targetHybrid >= 0.40 && margin >= AutoShortInertiaMargin;

        if (direction == CorrectionDirection.EnToUa && AutoShortUaAllowlist.Contains(convertedLower))
            return targetScore >= 0.28 && margin >= (AutoShortInertiaMargin + 0.02);

        return false;
    }

    private static bool ShouldConvertSingleLetterToken(
        string originalToken,
        double originalScore,
        double targetScore,
        bool sourceDictionaryHit,
        bool targetDictionaryHit,
        string convertedLower,
        CorrectionDirection direction)
    {
        if (sourceDictionaryHit)
            return false;

        bool allowlisted = IsAutoSingleAllowlisted(convertedLower, direction);
        if (!targetDictionaryHit && !allowlisted)
            return false;

        if (allowlisted && !targetDictionaryHit)
        {
            string toggled = KeyboardLayoutMap.ToggleLayoutText(originalToken, out int changedCount).ToLowerInvariant();
            return changedCount > 0
                && string.Equals(toggled, convertedLower, StringComparison.Ordinal)
                && originalScore <= 0.18;
        }

        double sourceHybrid = ComputeHybridScore(originalScore, sourceDictionaryHit, CorrectionMode.Auto, tokenLength: 1);
        double targetHybrid = ComputeHybridScore(targetScore, targetDictionaryHit, CorrectionMode.Auto, tokenLength: 1);
        double margin = targetHybrid - sourceHybrid;

        if (targetHybrid <= sourceHybrid || targetScore < 0.22)
            return false;

        if (targetDictionaryHit)
            return margin >= 0.08;

        return margin >= 0.12 && originalScore <= 0.18;
    }

    private static double ComputeConfidence(
        double originalScore,
        double targetScore,
        bool targetDictionaryHit,
        CorrectionMode mode)
    {
        double sourceHybrid = ComputeHybridScore(originalScore, dictionaryHit: false, mode, tokenLength: 4);
        double targetHybrid = ComputeHybridScore(targetScore, targetDictionaryHit, mode, tokenLength: 4);
        double delta = Math.Max(0.0, targetHybrid - sourceHybrid);
        double confidence = (targetHybrid * 0.68) + (delta * 0.32);
        return Clamp01(confidence);
    }

    private static double ComputeHybridScore(
        double score,
        bool dictionaryHit,
        CorrectionMode mode,
        int tokenLength)
    {
        double hybrid = Clamp01(score) + ComputeDictionaryBonus(dictionaryHit, mode, tokenLength);
        return Clamp01(hybrid);
    }

    private static double ComputeDictionaryBonus(bool dictionaryHit, CorrectionMode mode, int tokenLength)
    {
        if (!dictionaryHit)
            return 0.0;

        double bonus = mode == CorrectionMode.Auto ? AutoDictionaryBonus : SafeDictionaryBonus;
        if (tokenLength <= 2)
            bonus += 0.06;
        else if (tokenLength == 3)
            bonus += 0.03;

        return bonus;
    }

    private static double ComputeInertiaMargin(
        CorrectionMode mode,
        int tokenLength,
        bool targetDictionaryHit,
        bool sourceDictionaryHit)
    {
        double margin = mode switch
        {
            CorrectionMode.Auto when tokenLength <= 2 => AutoShortInertiaMargin,
            CorrectionMode.Auto when tokenLength == 3 => AutoMediumInertiaMargin,
            CorrectionMode.Safe when tokenLength <= 2 => SafeShortInertiaMargin,
            CorrectionMode.Safe when tokenLength == 3 => SafeMediumInertiaMargin,
            CorrectionMode.Auto => AutoBaseInertiaMargin,
            _ => SafeBaseInertiaMargin,
        };

        if (targetDictionaryHit)
            margin -= tokenLength <= 2 ? 0.02 : 0.01;

        if (sourceDictionaryHit)
            margin += 0.04;

        return Math.Max(0.03, margin);
    }

    private static double ComputeMinimumHybridScore(
        CorrectionMode mode,
        int tokenLength,
        bool targetDictionaryHit,
        bool sourceHasLayoutSymbols)
    {
        double floor = mode == CorrectionMode.Auto ? 0.24 : 0.20;

        if (tokenLength <= 2)
            floor += 0.10;
        else if (tokenLength == 3)
            floor += 0.03;

        if (!targetDictionaryHit)
            floor += tokenLength <= 3 ? 0.03 : 0.01;

        if (sourceHasLayoutSymbols)
            floor -= 0.02;

        return Clamp01(floor);
    }

    private static double ComputeStrongShapeBonus(
        double targetScore,
        double targetZeroRatio,
        bool targetDictionaryHit,
        int tokenLength)
    {
        if (targetDictionaryHit || tokenLength < 4)
            return 0.0;

        if (targetZeroRatio > 0.18 || targetScore < 0.34)
            return 0.0;

        double bonus = 0.04;

        if (targetZeroRatio <= 0.10)
            bonus += 0.03;

        if (targetScore >= 0.36)
            bonus += 0.02;

        return bonus;
    }

    private static CorrectionCandidate? FinalizeCandidate(
        string originalFull,
        string convertedFull,
        string originalCore,
        string convertedCore,
        CorrectionDirection direction,
        double confidence,
        string reason,
        CorrectionMode mode,
        double sourceScore,
        double targetScore,
        bool sourceDictionaryHit,
        bool targetDictionaryHit)
    {
        return new CorrectionCandidate(
            originalFull,
            convertedFull,
            direction,
            confidence,
            reason);
    }

    private static SelectorFeatureVector? BuildSelectorFeatures(
        string original,
        string converted,
        CorrectionDirection direction,
        double sourceScore,
        double targetScore,
        double confidence,
        bool sourceDictionaryHit,
        bool targetDictionaryHit)
    {
        if (direction == CorrectionDirection.None
            || string.IsNullOrWhiteSpace(original)
            || string.IsNullOrWhiteSpace(converted))
            return null;

        string source = StripPunctuation(original, out string sourcePrefix, out string sourceSuffix);
        string target = StripPunctuation(converted, out string targetPrefix, out string targetSuffix);
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
            return null;

        string sourceLower = source.ToLowerInvariant();
        string targetLower = target.ToLowerInvariant();
        double delta = Math.Max(0.0, targetScore - sourceScore);
        int totalChars = Math.Max(source.Length, target.Length);
        int lettersAndDigits = Math.Max(
            source.Count(char.IsLetterOrDigit),
            target.Count(char.IsLetterOrDigit));

        return new SelectorFeatureVector(
            direction,
            source,
            target,
            SourceScore: Clamp01(sourceScore),
            TargetScore: Clamp01(targetScore),
            Confidence: Clamp01(confidence),
            Delta: Clamp01(delta),
            LengthNorm: Clamp01((double)Math.Max(lettersAndDigits, 1) / 14.0),
            OriginalDistinctRatio: ComputeDistinctRatio(source),
            ConvertedDistinctRatio: ComputeDistinctRatio(target),
            OriginalVowelRatio: ComputeVowelRatio(source, direction == CorrectionDirection.EnToUa),
            ConvertedVowelRatio: ComputeVowelRatio(target, direction == CorrectionDirection.UaToEn),
            DigitRatio: ComputeDigitRatio(source),
            BoundaryAffinity: ComputeBoundaryAffinity(
                targetLower,
                direction,
                sourcePrefix.Length > 0 || sourceSuffix.Length > 0 || targetPrefix.Length > 0 || targetSuffix.Length > 0),
            RepeatPenalty: ComputeRepeatPenalty(targetLower),
            SourceDictionarySignal: sourceDictionaryHit ? 1.0 : 0.0,
            TargetDictionarySignal: targetDictionaryHit ? 1.0 : 0.0,
            TechnicalMarkerSignal: HasTechnicalSignal(source, target, direction) ? 1.0 : 0.0);
    }

    private static readonly HashSet<char> EnVowels = new() { 'a','e','i','o','u' };
    private static readonly HashSet<char> UaVowels = new()
    { 'а','е','є','и','і','ї','о','у','ю','я' };

    private static readonly HashSet<string> AutoShortUaAllowlist = new(StringComparer.Ordinal)
    {
        "ти", "па"
    };

    private static readonly HashSet<string> AutoSingleUaAllowlist = new(StringComparer.Ordinal)
    {
        "я", "є"
    };

    private static readonly HashSet<string> AutoShortEnAllowlist = new(StringComparer.Ordinal)
    {
        "am", "oh", "uh", "ok"
    };

    private static readonly HashSet<string> AutoSingleEnAllowlist = new(StringComparer.Ordinal)
    {
        "i"
    };

    private static readonly HashSet<string> EnglishConversationalTokens = new(StringComparer.Ordinal)
    {
        "yeah", "okay", "maybe", "thanks", "remember",
        "guy", "guys", "fine", "night", "dad", "may",
        "isn", "doesn", "wasn", "docker", "build", "issue",
        "tool", "tools", "driver", "update"
    };

    private static readonly HashSet<string> ProtectedShortLatinTokens = new(StringComparer.Ordinal)
    {
        "wsl", "html", "json", "yaml", "xml", "css"
    };

    private static readonly string[] UkrainianMorphologyEndings =
    [
        "ами", "ями", "ові", "еві", "ому", "ими", "іми",
        "ість", "ення", "ання", "увати", "ити", "ати", "яти",
        "кою", "ці", "ку", "ки", "ка", "ів", "ям", "ах", "ях",
        "ну", "ші", "іш"
    ];

    private static bool IsAutoShortAllowlisted(string convertedLower, CorrectionDirection direction) =>
        direction == CorrectionDirection.EnToUa
            ? AutoShortUaAllowlist.Contains(convertedLower)
            : AutoShortEnAllowlist.Contains(convertedLower);

    private static bool IsAutoSingleAllowlisted(string convertedLower, CorrectionDirection direction) =>
        direction == CorrectionDirection.EnToUa
            ? AutoSingleUaAllowlist.Contains(convertedLower)
            : AutoSingleEnAllowlist.Contains(convertedLower);

    private static bool HasProtectedShortLatinToken(string lower) =>
        !string.IsNullOrWhiteSpace(lower)
        && ProtectedShortLatinTokens.Contains(lower);

    private static readonly HashSet<string> EnglishBoundaryTrigrams = new(StringComparer.Ordinal)
    {
        "^re", "^co", "^de", "^in", "^be", "^st", "^sh", "^ch",
        "^th", "^wh", "^tr", "^br", "^gr", "^cl", "^cr", "^pr",
        "ing", "ion", "ed$", "er$", "ly$", "est", "ter", "ers",
        "all", "ate", "nce", "own", "out", "and", "tch", "ick"
    };

    private static double ComputeDistinctRatio(string text)
    {
        var chars = text
            .ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || KeyboardLayoutMap.IsLayoutLetterChar(c))
            .ToArray();

        if (chars.Length == 0)
            return 0.0;

        return Clamp01((double)chars.Distinct().Count() / chars.Length);
    }

    private static double ComputeVowelRatio(string text, bool englishLike)
    {
        string lower = text.ToLowerInvariant();
        var vowels = englishLike ? EnVowels : UaVowels;
        int letters = 0;
        int vowelCount = 0;

        foreach (char c in lower)
        {
            if (!char.IsLetter(c))
                continue;

            letters++;
            if (vowels.Contains(c))
                vowelCount++;
        }

        return letters == 0 ? 0.0 : Clamp01((double)vowelCount / letters);
    }

    private static double ComputeDigitRatio(string text)
    {
        int relevant = text.Count(char.IsLetterOrDigit);
        if (relevant == 0)
            return 0.0;

        return Clamp01((double)text.Count(char.IsDigit) / relevant);
    }

    private static double ComputeBoundaryAffinity(string token, CorrectionDirection direction, bool wrapped)
    {
        if (string.IsNullOrWhiteSpace(token))
            return 0.0;

        string lower = token.ToLowerInvariant();
        double score = 0.0;
        int signals = 0;

        if (direction == CorrectionDirection.UaToEn)
        {
            string padded = $"^{lower}$";
            int trigramSignals = 0;
            int trigramHits = 0;
            for (int i = 0; i <= padded.Length - 3; i++)
            {
                string gram = padded.Substring(i, 3);
                if (!gram.Contains('^') && !gram.Contains('$'))
                    continue;

                trigramSignals++;
                if (EnglishBoundaryTrigrams.Contains(gram))
                    trigramHits++;
            }

            if (trigramSignals > 0)
            {
                score += (double)trigramHits / trigramSignals;
                signals++;
            }
        }
            {
                string lettersOnly = ExtractLettersOnly(lower);
                if (lettersOnly.Length >= 2)
                {
                    int hits = 0;
                    if (UaBigramFreq.ContainsKey(lettersOnly[..2]))
                        hits++;
                    if (UaBigramFreq.ContainsKey(lettersOnly[^2..]))
                        hits++;

                    score += hits / 2.0;
                    signals++;
                }
            }

        if (wrapped)
        {
            score += 0.15;
            signals++;
        }

        return signals == 0 ? 0.0 : Clamp01(score / signals);
    }

    private static double ComputeRepeatPenalty(string text)
    {
        string lower = text.ToLowerInvariant();
        double penalty = 0.0;

        if (HasSuspiciousRepeatPattern(lower))
            penalty += 0.28;

        var bigramCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < lower.Length - 1; i++)
        {
            if (!char.IsLetter(lower[i]) || !char.IsLetter(lower[i + 1]))
                continue;

            string gram = lower.Substring(i, 2);
            bigramCounts.TryGetValue(gram, out int count);
            bigramCounts[gram] = count + 1;
        }

        if (bigramCounts.Values.Any(count => count >= 2))
            penalty += 0.12;

        return Clamp01(penalty);
    }

    private static bool HasTechnicalSignal(string source, string target, CorrectionDirection direction)
    {
        string sourceLetters = ExtractLettersOnly(source).ToLowerInvariant();
        string targetLetters = ExtractLettersOnly(target).ToLowerInvariant();

        return direction switch
        {
            CorrectionDirection.EnToUa =>
                LooksLikeTechnicalLatinToken(sourceLetters)
                || LooksLikeTechnicalTextToken(sourceLetters),
            CorrectionDirection.UaToEn =>
                LooksLikeTechnicalLatinToken(targetLetters)
                || LooksLikeTechnicalTextToken(targetLetters)
                || HasTechnicalMarker(targetLetters),
            _ => false
        };
    }

    private static bool IsLikelyEnglishToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        string lower = token.ToLowerInvariant();
        if (!lower.All(char.IsLetter))
            return false;

        return EnDictionary.Contains(lower)
            || HasEnglishDictionaryBaseForm(lower)
            || AutoSingleEnAllowlist.Contains(lower)
            || AutoShortEnAllowlist.Contains(lower)
            || EnglishConversationalTokens.Contains(lower)
            || LooksLikeTechnicalTextToken(lower);
    }

    private static bool HasStrongEnglishShapeSignal(string lower)
    {
        if (string.IsNullOrWhiteSpace(lower) || !lower.All(char.IsLetter))
            return false;

        if (lower.Length < 3 || lower.Length > 12)
            return false;

        if (HasSuspiciousRepeatPattern(lower))
            return false;

        double zeroRatio = ZeroBigramRatio(lower, EnBigramFreq);
        double englishScore = ScoreEnglish(lower);

        if (lower.Length <= 4)
            return englishScore >= 0.52 && zeroRatio <= 0.34;

        if (lower.Length <= 6)
            return englishScore >= 0.40 && zeroRatio <= 0.28;

        return englishScore >= 0.32 && zeroRatio <= 0.22;
    }

    private static bool HasEnglishDictionaryBaseForm(string lower)
    {
        if (lower.Length < 4)
            return false;

        if (lower.EndsWith("ies", StringComparison.Ordinal) && lower.Length > 4)
        {
            string singularY = lower[..^3] + "y";
            if (EnDictionary.Contains(singularY))
                return true;
        }

        foreach (string suffix in new[] { "ers", "ing", "ed", "er", "es", "s", "ly" })
        {
            if (!lower.EndsWith(suffix, StringComparison.Ordinal) || lower.Length <= suffix.Length + 2)
                continue;

            string stem = lower[..^suffix.Length];
            if (EnDictionary.Contains(stem))
                return true;

            if ((suffix is "ing" or "ed" or "er" or "es") && EnDictionary.Contains(stem + "e"))
                return true;

            if ((suffix is "ing" or "ed" or "er")
                && stem.Length >= 3
                && stem[^1] == stem[^2]
                && EnDictionary.Contains(stem[..^1]))
                return true;
        }

        return false;
    }

    private static double ConsonantVowelScore(string lower, bool isEnglish)
    {
        var vowels = isEnglish ? EnVowels : UaVowels;
        int letters = 0, standardVowelCount = 0, yNonInitialCount = 0;
        for (int i = 0; i < lower.Length; i++)
        {
            char c = lower[i];
            if (!char.IsLetter(c)) continue;
            letters++;
            if (vowels.Contains(c))
                standardVowelCount++;
            else if (isEnglish && c == 'y' && i > 0)
                yNonInitialCount++;
        }
        if (letters == 0) return 0;

        // English: treat non-initial 'y' as vowel when word has 0-1 standard vowels
        // (sync, symbol, style, syntax — but NOT easy, eye)
        int vowelCount = standardVowelCount;
        if (isEnglish && standardVowelCount <= 1)
            vowelCount += yNonInitialCount;

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

    /// <summary>
    /// Fraction of letter-bigrams in the word that have zero frequency in the given table.
    /// High ratio (&gt; 0.5) means the word contains many impossible bigram combinations
    /// for that language — a strong algorithmic signal that it's not real text.
    /// This is the core of the "Punto Switcher" approach: no dictionary needed.
    /// </summary>
    private static double ZeroBigramRatio(string lower, Dictionary<string, double> bigramTable)
    {
        int total = 0, zeros = 0;
        for (int i = 0; i < lower.Length - 1; i++)
        {
            char a = lower[i], b = lower[i + 1];
            if (!char.IsLetter(a) || !char.IsLetter(b)) continue;
            total++;
            if (!bigramTable.ContainsKey(lower.Substring(i, 2)))
                zeros++;
        }
        return total == 0 ? 0.0 : (double)zeros / total;
    }

    private static int CountTokenChars(string text) =>
        text.Count(c => char.IsLetter(c) || KeyboardLayoutMap.IsLayoutLetterChar(c));

    private static string ExtractLettersOnly(string text) =>
        new(text.Where(char.IsLetter).ToArray());

    private static bool ShouldSkipAlphaNumericToken(string text)
    {
        string lettersOnly = ExtractLettersOnly(text);
        if (lettersOnly.Length < 2)
            return true;

        var script = KeyboardLayoutMap.ClassifyScript(lettersOnly);
        string lower = lettersOnly.ToLowerInvariant();

        if (script == ScriptType.Latin)
        {
            if (EnDictionary.Contains(lower))
                return true;

            string? converted = KeyboardLayoutMap.ConvertEnToUa(lettersOnly, strict: true);
            return converted == null || !UaDictionary.Contains(converted.ToLowerInvariant());
        }

        if (script == ScriptType.Cyrillic)
        {
            if (UaDictionary.Contains(lower))
                return true;

            string? converted = KeyboardLayoutMap.ConvertUaToEn(lettersOnly, strict: true);
            if (converted == null)
                return true;

            string convertedLower = converted.ToLowerInvariant();
            if (EnDictionary.Contains(convertedLower))
                return false;

            return !LooksLikeTechnicalLatinToken(convertedLower);
        }

        return true;
    }

    private static bool LooksLikeTechnicalLatinToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (!token.All(c => char.IsLetter(c) || KeyboardLayoutMap.IsWordConnector(c)))
            return false;

        if (token.Length < 2 || token.Length > 12)
            return false;

        if (token.Count(char.IsLetter) < 2)
            return false;

        int vowelCount = token.Count(c => EnVowels.Contains(char.ToLowerInvariant(c)));

        int maxRun = 1;
        int currentRun = 1;
        for (int i = 1; i < token.Length; i++)
        {
            if (token[i] == token[i - 1])
            {
                currentRun++;
                maxRun = Math.Max(maxRun, currentRun);
            }
            else
            {
                currentRun = 1;
            }
        }

        if (maxRun >= 3)
            return false;

        string lower = token.ToLowerInvariant();
        if (lower.Length % 2 == 0)
        {
            int half = lower.Length / 2;
            if (string.Equals(lower[..half], lower[half..], StringComparison.Ordinal))
                return false;
        }

        if (lower.Length <= 4 && vowelCount == 0)
            return lower.Any(c => c is 'x' or 'z' or 'v' or 'k' or 't' or 'p' or 'g');

        if (vowelCount == 0)
            return false;

        double englishScore = ScoreEnglish(lower);
        if (englishScore >= 0.46)
            return true;

        return lower.Length >= 4
            && vowelCount >= 1
            && lower.Any(c => c is 'p' or 'h' or 'x' or 'g' or 'k' or 'v');
    }

    private static bool LooksLikeTechnicalLatinWord(string lower)
    {
        if (string.IsNullOrWhiteSpace(lower) || !lower.All(char.IsLetter))
            return false;

        int vowelCount = lower.Count(c => EnVowels.Contains(c));
        if (lower.Length <= 4 && vowelCount == 0)
            return lower.Any(c => c is 'x' or 'z' or 'v' or 'k' or 't' or 'p' or 'g');

        if (vowelCount == 0 || lower.Length < 4)
            return false;

        return HasTechnicalMarker(lower)
            || (ScoreEnglish(lower) >= 0.34 && lower.Any(c => c is 'p' or 'h' or 'x' or 'g' or 'k' or 'v'));
    }

    private static bool LooksLikeTechnicalTextToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        string lower = token.ToLowerInvariant();
        if (!lower.All(char.IsLetter))
            return false;

        if (lower.Length < 5 || lower.Length > 16)
            return false;

        if (!HasTechnicalMarker(lower))
            return false;

        int vowelCount = lower.Count(c => EnVowels.Contains(c));
        if (vowelCount == 0)
            return false;

        if (HasSuspiciousRepeatPattern(lower))
            return false;

        return ScoreEnglish(lower) >= 0.16 || HasEnglishDictionaryBaseForm(lower);
    }

    private static bool LooksLikeBoundaryTechnicalToken(string rawToken, string coreToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || string.IsNullOrWhiteSpace(coreToken))
            return false;

        string coreLetters = ExtractLettersOnly(coreToken);
        if (coreLetters.Length < 2 || coreLetters.Length > 12)
            return false;

        string lower = coreToken.ToLowerInvariant();
        if (HasTechnicalMarker(lower)
            || LooksLikeTechnicalTextToken(lower))
            return true;

        return coreLetters.Length <= 6 && coreLetters.All(char.IsUpper);
    }

    private static bool HasUkrainianMorphologySignal(string lower) =>
        !string.IsNullOrWhiteSpace(lower)
        && lower.Length >= 4
        && (lower.Any(c => c is 'ї' or 'є' or 'ґ')
            || lower.Contains('\'')
            || lower.Contains('’')
            || lower.Contains('ʼ')
            || HasInternalSoftSignSignal(lower)
            || UkrainianMorphologyEndings.Any(ending => lower.EndsWith(ending, StringComparison.Ordinal)));

    private static bool HasInternalSoftSignSignal(string lower)
    {
        if (string.IsNullOrWhiteSpace(lower) || lower.Length < 4)
            return false;

        for (int i = 1; i < lower.Length - 1; i++)
        {
            if (lower[i] != 'ь')
                continue;

            if (!char.IsLetter(lower[i - 1]) || !char.IsLetter(lower[i + 1]))
                continue;

            return true;
        }

        return false;
    }

    private static bool HasTechnicalMarker(string lower) =>
        lower.Contains("code", StringComparison.Ordinal)
        || lower.Contains("net", StringComparison.Ordinal)
        || lower.Contains("track", StringComparison.Ordinal)
        || lower.Contains("tracker", StringComparison.Ordinal)
        || lower.Contains("gram", StringComparison.Ordinal)
        || lower.Contains("chat", StringComparison.Ordinal)
        || lower.Contains("repo", StringComparison.Ordinal)
        || lower.Contains("node", StringComparison.Ordinal)
        || lower.Contains("proxy", StringComparison.Ordinal)
        || lower.Contains("cloud", StringComparison.Ordinal)
        || lower.Contains("server", StringComparison.Ordinal)
        || lower.Contains("client", StringComparison.Ordinal)
        || lower.Contains("script", StringComparison.Ordinal)
        || lower.Contains("api", StringComparison.Ordinal)
        || lower.Contains("sdk", StringComparison.Ordinal)
        || lower.Contains("json", StringComparison.Ordinal)
        || lower.Contains("yaml", StringComparison.Ordinal)
        || lower.Contains("docker", StringComparison.Ordinal)
        || lower.Contains("git", StringComparison.Ordinal)
        || lower.Contains("vsc", StringComparison.Ordinal)
        || lower.Contains("nvidia", StringComparison.Ordinal);

    private static bool HasSuspiciousRepeatPattern(string lower)
    {
        int maxRun = 1;
        int currentRun = 1;
        for (int i = 1; i < lower.Length; i++)
        {
            if (lower[i] == lower[i - 1])
            {
                currentRun++;
                maxRun = Math.Max(maxRun, currentRun);
            }
            else
            {
                currentRun = 1;
            }
        }

        if (maxRun >= 3)
            return true;

        if (lower.Length >= 4 && lower.Length % 2 == 0)
        {
            int half = lower.Length / 2;
            if (string.Equals(lower[..half], lower[half..], StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string NormalizePossibleUkrainianApostropheMistype(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('є'))
            return text;

        char[] chars = text.ToCharArray();
        bool changed = false;

        for (int i = 1; i < chars.Length - 1; i++)
        {
            if (chars[i] != 'є' && chars[i] != 'Є')
                continue;

            char prev = chars[i - 1];
            char next = chars[i + 1];
            if (!CanPrecedeUkrainianApostrophe(prev) || !IsIotatedUkrainianVowel(next))
                continue;

            chars[i] = '\'';
            changed = true;
        }

        return changed ? new string(chars) : text;
    }

    private static string TrimTrailingLiteralPunctuation(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        int end = text.Length;
        while (end > 0 && char.IsPunctuation(text[end - 1]) && !KeyboardLayoutMap.IsWordConnector(text[end - 1]))
            end--;

        return end > 0 ? text[..end] : string.Empty;
    }

    private static string TrimBoundaryLiteralPunctuation(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        int start = 0;
        int end = text.Length;

        while (start < end && char.IsPunctuation(text[start]) && !KeyboardLayoutMap.IsWordConnector(text[start]))
            start++;

        while (end > start && char.IsPunctuation(text[end - 1]) && !KeyboardLayoutMap.IsWordConnector(text[end - 1]))
            end--;

        return end > start ? text[start..end] : string.Empty;
    }

    private static string TrimLeadingLiteralPunctuation(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        int start = 0;
        while (start < text.Length && char.IsPunctuation(text[start]) && !KeyboardLayoutMap.IsWordConnector(text[start]))
            start++;

        return start < text.Length ? text[start..] : string.Empty;
    }

    private static CorrectionCandidate? EvaluateCompoundToken(
        string word,
        string prefix,
        string suffix,
        CorrectionMode mode)
    {
        if (string.IsNullOrWhiteSpace(word)
            || word.Length < 3
            || word.StartsWith('-')
            || word.EndsWith('-')
            || word.Contains("--", StringComparison.Ordinal))
            return null;

        string[] parts = word.Split('-', StringSplitOptions.None);
        if (parts.Length < 2 || parts.Any(string.IsNullOrWhiteSpace))
            return null;

        var convertedParts = new string[parts.Length];
        CorrectionDirection? direction = null;
        double totalConfidence = 0;

        for (int i = 0; i < parts.Length; i++)
        {
            var candidate = Evaluate(parts[i], mode);
            if (candidate is null)
                return null;

            if (direction is null)
                direction = candidate.Direction;
            else if (direction != candidate.Direction)
                return null;

            convertedParts[i] = candidate.ConvertedText;
            totalConfidence += candidate.Confidence;
        }

        if (direction is null)
            return null;

        return new CorrectionCandidate(
            prefix + word + suffix,
            prefix + string.Join('-', convertedParts) + suffix,
            direction.Value,
            Clamp01(totalConfidence / parts.Length),
            $"Compound {direction.Value} parts={parts.Length}");
    }

    private static bool CanPrecedeUkrainianApostrophe(char c) =>
        char.ToLowerInvariant(c) is 'б' or 'п' or 'в' or 'м' or 'ф' or 'р';

    private static bool IsIotatedUkrainianVowel(char c) =>
        char.ToLowerInvariant(c) is 'я' or 'ю' or 'є' or 'ї';

    // ─── Punctuation stripping ───────────────────────────────────────────────

    // Characters that look like punctuation but ARE keyboard keys mapping to UA letters:
    //   [  → х,  ] → ї,  \ → ї,  { → Х,  } → Ї,  | → Ї
    //   .  → ю,  ,  → б,  ;  → ж,  :  → Ж,  <  → Б,  >  → Ю
    // These must NOT be stripped as trailing/leading punctuation, otherwise words like
    // "ndj]" (typed in EN while meaning "твої") lose their last letter,
    // or "cd'nj." loses the trailing "ю" (свєтою → свєто).
    private static string StripPunctuation(string word, out string prefix, out string suffix)
    {
        string alphaNumericLiteralPrefix = string.Empty;
        string alphaNumericLiteralSuffix = string.Empty;
        word = TrimLiteralPunctuationAroundAlphaNumericToken(word, out alphaNumericLiteralPrefix, out alphaNumericLiteralSuffix);

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

        if (start < end && IsWrappingQuote(word[start]))
        {
            int closingQuote = FindClosingWrappingQuote(word, start + 1, end);
            if (closingQuote > start + 1 && TailAfterQuoteIsLiteral(word, closingQuote + 1, end))
            {
                start++;
                prefix = word[..start];
                suffix = word[closingQuote..];
                return word[start..closingQuote];
            }
        }

        while ((end - start) >= 2
               && IsWrappingQuote(word[start])
               && IsWrappingQuote(word[end - 1])
               && QuotesMatch(word[start], word[end - 1]))
        {
            start++;
            end--;
        }

        prefix = alphaNumericLiteralPrefix + word[..start];
        suffix = word[end..] + alphaNumericLiteralSuffix;
        return word[start..end];
    }

    private static string TrimLiteralPunctuationAroundAlphaNumericToken(string word, out string prefix, out string suffix)
    {
        prefix = string.Empty;
        suffix = string.Empty;

        if (string.IsNullOrWhiteSpace(word)
            || !word.Any(char.IsDigit)
            || !word.Any(char.IsLetter))
            return word;

        int start = 0;
        int end = word.Length;

        while (start < end && IsAlphaNumericLiteralBoundaryPunctuation(word[start]))
            start++;

        while (end > start && IsAlphaNumericLiteralBoundaryPunctuation(word[end - 1]))
            end--;

        prefix = start > 0 ? word[..start] : string.Empty;
        suffix = end < word.Length ? word[end..] : string.Empty;

        return start == 0 && end == word.Length
            ? word
            : (end > start ? word[start..end] : string.Empty);
    }

    private static bool IsAlphaNumericLiteralBoundaryPunctuation(char c) =>
        c is '.' or ',' or ';' or ':';

    private static bool IsWrappingQuote(char c) =>
        c is '"' or '«' or '»' or '“' or '”' or '„';

    private static bool QuotesMatch(char start, char end) =>
        (start == '"' && end == '"')
        || (start == '«' && end == '»')
        || (start == '“' && end == '”')
        || (start == '„' && end == '”');

    private static int FindClosingWrappingQuote(string text, int start, int endExclusive)
    {
        for (int i = endExclusive - 1; i >= start; i--)
        {
            if (IsWrappingQuote(text[i]))
                return i;
        }

        return -1;
    }

    private static bool TailAfterQuoteIsLiteral(string text, int start, int endExclusive)
    {
        for (int i = start; i < endExclusive; i++)
        {
            char c = text[i];
            if (!char.IsPunctuation(c) || KeyboardLayoutMap.IsWordConnector(c))
                return false;
        }

        return true;
    }
}
