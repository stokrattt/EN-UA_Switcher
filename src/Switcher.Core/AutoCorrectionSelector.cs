namespace Switcher.Core;

/// <summary>
/// A lightweight selector layer for Auto Mode.
///
/// Research on short-text and realtime language identification keeps converging on
/// the same pattern: raw character n-gram scores are useful, but a second selector
/// is needed to suppress confident-looking nonsense on very short or noisy tokens.
///
/// This is a hand-tuned first step toward that architecture. It currently focuses on
/// the riskier UA→EN automatic direction, where false positives are more damaging.
/// Safe Mode is intentionally left more permissive.
/// </summary>
internal static class AutoCorrectionSelector
{
    private static readonly HashSet<string> EnglishBoundaryTrigrams = new(StringComparer.Ordinal)
    {
        "^re", "^co", "^de", "^in", "^be", "^st", "^sh", "^ch",
        "^th", "^wh", "^tr", "^br", "^gr", "^cl", "^cr", "^pr",
        "ing", "ion", "ed$", "er$", "ly$", "est", "ter", "ers",
        "all", "ate", "nce", "own", "out", "and", "tch", "ick"
    };

    internal readonly record struct Decision(bool Accept, double Score, string Reason);

    public static Decision Evaluate(
        string original,
        string converted,
        CorrectionDirection direction,
        double sourceScore,
        double targetScore,
        double confidence)
    {
        if (direction != CorrectionDirection.UaToEn)
            return new(true, 1.0, "selector=bypass");

        string lower = converted.ToLowerInvariant();
        double delta = Math.Max(0.0, targetScore - sourceScore);
        double boundaryAffinity = ScoreBoundaryAffinity(lower);
        double diversity = ScoreCharacterDiversity(lower);
        double repeatPenalty = ScoreRepeatPenalty(lower);
        double digitPenalty = lower.Any(char.IsDigit) ? 0.16 : 0.0;
        double shortNonDictionaryPenalty = lower.Length <= 4 && targetScore < 0.60 ? 0.20 : 0.0;
        double sourcePenalty = Math.Max(0.0, sourceScore - 0.18) * 0.45;
        bool shortWordFastPath =
            lower.Length <= 3
            && confidence >= 0.32
            && targetScore >= 0.40
            && delta >= 0.08
            && repeatPenalty < 0.12;

        double score =
            (confidence * 0.45) +
            (targetScore * 0.30) +
            (delta * 0.25) +
            (boundaryAffinity * 0.15) +
            (diversity * 0.05) -
            repeatPenalty -
            shortNonDictionaryPenalty -
            digitPenalty -
            sourcePenalty;

        bool accept =
            shortWordFastPath ||
            (score >= 0.30 &&
             targetScore >= 0.24 &&
             delta >= 0.04 &&
             repeatPenalty < 0.26);

        return new(
            accept,
            Clamp01(score),
            $"selector={score:F2} delta={delta:F2} edge={boundaryAffinity:F2} repeat={repeatPenalty:F2} short={shortNonDictionaryPenalty:F2}");
    }

    private static double ScoreBoundaryAffinity(string word)
    {
        if (word.Length < 3)
            return 0.0;

        string padded = $"^{word}$";
        int total = 0;
        int hits = 0;

        for (int i = 0; i <= padded.Length - 3; i++)
        {
            string gram = padded.Substring(i, 3);
            if (!gram.Contains('^') && !gram.Contains('$'))
                continue;

            total++;
            if (EnglishBoundaryTrigrams.Contains(gram))
                hits++;
        }

        return total == 0 ? 0.0 : (double)hits / total;
    }

    private static double ScoreCharacterDiversity(string word)
    {
        var letters = word.Where(char.IsLetter).ToArray();
        if (letters.Length == 0)
            return 0.0;

        int distinct = letters.Distinct().Count();
        return Clamp01((double)distinct / letters.Length);
    }

    private static double ScoreRepeatPenalty(string word)
    {
        double penalty = 0.0;

        if (word.Length >= 4 && word.Length % 2 == 0)
        {
            int half = word.Length / 2;
            if (string.Equals(word[..half], word[half..], StringComparison.Ordinal))
                penalty += 0.30;
        }

        int maxRun = 1;
        int currentRun = 1;
        for (int i = 1; i < word.Length; i++)
        {
            if (word[i] == word[i - 1])
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
            penalty += 0.12;

        var bigramCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < word.Length - 1; i++)
        {
            string gram = word.Substring(i, 2);
            bigramCounts.TryGetValue(gram, out int count);
            bigramCounts[gram] = count + 1;
        }

        if (bigramCounts.Values.Any(count => count >= 2))
            penalty += 0.18;

        return Math.Min(0.45, penalty);
    }

    private static double Clamp01(double value)
    {
        if (value < 0.0) return 0.0;
        if (value > 1.0) return 1.0;
        return value;
    }
}
