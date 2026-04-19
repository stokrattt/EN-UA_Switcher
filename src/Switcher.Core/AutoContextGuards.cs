namespace Switcher.Core;

public static class AutoContextGuards
{
    private static readonly HashSet<string> CommandStarters = new(StringComparer.OrdinalIgnoreCase)
    {
        "git", "dotnet", "npm", "pnpm", "yarn", "node", "python", "pip", "poetry",
        "curl", "ssh", "scp", "docker", "kubectl", "winget", "choco", "cd", "ls",
        "dir", "cat", "type", "cp", "mv", "rm", "mkdir", "grep", "find", "code"
    };

    private static readonly HashSet<string> CodeKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "const", "let", "var", "class", "struct", "interface", "enum", "function",
        "def", "return", "public", "private", "protected", "internal", "static", "async",
        "await", "using", "import", "export", "from", "if", "else", "switch", "case"
    };

    private static readonly HashSet<string> CodeLikeProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "code", "code - insiders", "devenv", "rider", "windsurf", "cmd", "powershell",
        "pwsh", "bash", "mintty", "windowsterminal", "wt", "conhost"
    };

    public static string? GetUnsafeAutoCorrectionReason(string token, string? currentSentence = null, string? processName = null)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        string trimmedToken = token.Trim();
        string normalizedToken = KeyboardLayoutMap.NormalizeWordToken(trimmedToken);
        if (string.IsNullOrWhiteSpace(normalizedToken))
            normalizedToken = trimmedToken.ToLowerInvariant();

        if (CountLetters(normalizedToken) <= 2
            && ShouldHoldShortToken(currentSentence)
            && !ShouldAllowConvertibleLayoutShortToken(trimmedToken, normalizedToken))
            return "context=short-token";

        if (LooksLikeTechnicalMixedToken(trimmedToken)
            && !ShouldAllowConvertibleTechnicalMixedToken(trimmedToken, normalizedToken, processName))
            return "context=technical-mixed-token";

        if (LooksLikeUrlLikeToken(trimmedToken))
            return "context=url-like-token";

        if (LooksLikeEmailLikeToken(trimmedToken))
            return "context=email-like-token";

        if (LooksLikePathLikeToken(trimmedToken))
            return "context=path-like-token";

        if (LooksLikeCommandFlag(trimmedToken))
            return "context=command-flag";

        if (LooksLikeCodeIdentifier(trimmedToken))
            return "context=code-identifier";

        if (!string.IsNullOrWhiteSpace(currentSentence))
        {
            string? sentenceReason = GetSentenceContextReason(trimmedToken, normalizedToken, currentSentence!, processName);
            if (sentenceReason is not null)
                return sentenceReason;
        }

        if (LooksLikeCodeProcess(processName) && LooksLikeEditorUnsafeToken(trimmedToken))
            return "context=code-surface-token";

        return null;
    }

    private static string? GetSentenceContextReason(string rawToken, string normalizedToken, string sentence, string? processName)
    {
        string lowerSentence = sentence.ToLowerInvariant();
        int tokenIndex = FindTokenIndex(lowerSentence, rawToken, normalizedToken);
        if (tokenIndex < 0)
            return null;

        int tokenLength = rawToken.Trim().Length;
        char prev = tokenIndex > 0 ? lowerSentence[tokenIndex - 1] : '\0';
        char next = tokenIndex + tokenLength < lowerSentence.Length ? lowerSentence[tokenIndex + tokenLength] : '\0';

        if (lowerSentence.Contains("://", StringComparison.Ordinal)
            || lowerSentence.Contains("www.", StringComparison.Ordinal)
            || lowerSentence.Contains("mailto:", StringComparison.Ordinal))
        {
            if (prev is '/' or '.' or '?' or '&' or '=' or '#' or ':'
                || next is '/' or '.' or '?' or '&' or '=' or '#')
            {
                return "context=url-sentence";
            }
        }

        string? previousWord = ReadPreviousWord(lowerSentence, tokenIndex);
        if (IsCommandStarterLike(previousWord) && ShouldTreatSentenceAsCommandSurface(lowerSentence, processName))
            return "context=command-sentence";

        if (IsCodeKeywordLike(previousWord) && (SentenceLooksCodeLike(lowerSentence) || LooksLikeCodeProcess(processName)))
            return "context=code-sentence";

        if (IsCommandStarterLike(normalizedToken) && ShouldTreatSentenceAsCommandSurface(lowerSentence, processName))
            return "context=command-starter-token";

        if (IsCodeKeywordLike(normalizedToken) && SentenceLooksCodeLike(lowerSentence))
            return "context=code-keyword-token";

        if (LooksLikeCodeProcess(processName) && SentenceLooksCodeLike(lowerSentence))
            return "context=code-process-line";

        if (LooksLikeCodeProcess(processName))
        {
            if (prev is '(' or '[' or '{' or '=' or ':' or '.'
                || next is ')' or ']' or '}' or ';' or ':' or ',' or '.')
            {
                return "context=code-process-sentence";
            }
        }

        if (prev is '@' or '#' || next is '@' or '#')
            return "context=mention-or-tag";

        return null;
    }

    private static int FindTokenIndex(string lowerSentence, string rawToken, string normalizedToken)
    {
        string raw = rawToken.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(raw))
        {
            int rawIndex = lowerSentence.IndexOf(raw, StringComparison.Ordinal);
            if (rawIndex >= 0)
                return rawIndex;
        }

        if (!string.IsNullOrWhiteSpace(normalizedToken))
            return lowerSentence.IndexOf(normalizedToken, StringComparison.Ordinal);

        return -1;
    }

    private static string? ReadPreviousWord(string lowerSentence, int tokenIndex)
    {
        int end = tokenIndex - 1;
        while (end >= 0 && char.IsWhiteSpace(lowerSentence[end]))
            end--;

        if (end < 0)
            return null;

        int start = end;
        while (start >= 0 && (char.IsLetterOrDigit(lowerSentence[start]) || lowerSentence[start] is '-' or '_'))
            start--;

        return lowerSentence[(start + 1)..(end + 1)];
    }

    private static bool LooksLikeUrlLikeToken(string token)
    {
        string lower = token.ToLowerInvariant();
        return lower.Contains("://", StringComparison.Ordinal)
            || lower.StartsWith("www.", StringComparison.Ordinal)
            || (lower.Contains('/') && lower.Contains('.'))
            || (lower.Contains('?') && lower.Contains('='))
            || lower.Contains('#');
    }

    private static bool LooksLikeEmailLikeToken(string token)
    {
        int at = token.IndexOf('@');
        return at > 0 && at < token.Length - 1 && token[(at + 1)..].Contains('.');
    }

    private static bool LooksLikePathLikeToken(string token) =>
        token.Contains('/')
        || token.Contains('\\')
        || token.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        || token.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        || token.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
        || token.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
        || token.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
        || token.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
        || token.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeCommandFlag(string token) =>
        token.StartsWith("--", StringComparison.Ordinal)
        || (token.StartsWith("-", StringComparison.Ordinal) && token.Length >= 3 && token.Skip(1).All(c => char.IsLetterOrDigit(c) || c is '-' or '_'));

    private static bool LooksLikeCodeIdentifier(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        return token.Contains('_')
            || token.Contains("=>", StringComparison.Ordinal)
            || token.Contains("::", StringComparison.Ordinal)
            || (token.Any(char.IsLetter) && token.Any(char.IsDigit) && token.Contains('.'));
    }

    private static bool LooksLikeEditorUnsafeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        return token.Contains('_')
            || token.Contains('.')
            || token.Contains('/')
            || token.Contains('\\')
            || token.StartsWith("--", StringComparison.Ordinal)
            || token.Contains('@')
            || token.Contains('#');
    }

    private static bool LooksLikeTechnicalMixedToken(string token) =>
        token.Any(char.IsLetter) && token.Any(char.IsDigit);

    private static int CountLetters(string token) =>
        token.Count(char.IsLetter);

    private static bool SentenceLooksCommandLike(string lowerSentence) =>
        lowerSentence.Contains("/", StringComparison.Ordinal)
        || lowerSentence.Contains("\\", StringComparison.Ordinal)
        || lowerSentence.Contains("--", StringComparison.Ordinal)
        || lowerSentence.Contains(".sln", StringComparison.Ordinal)
        || lowerSentence.Contains(".csproj", StringComparison.Ordinal);

    private static bool ShouldTreatSentenceAsCommandSurface(string lowerSentence, string? processName) =>
        SentenceLooksCommandLike(lowerSentence)
        || LooksLikeCodeProcess(processName);

    private static bool SentenceLooksCodeLike(string lowerSentence) =>
        lowerSentence.Contains("=", StringComparison.Ordinal)
        || lowerSentence.Contains(";", StringComparison.Ordinal)
        || lowerSentence.Contains("(", StringComparison.Ordinal)
        || lowerSentence.Contains("{", StringComparison.Ordinal)
        || lowerSentence.Contains("=>", StringComparison.Ordinal);

    private static bool ShouldHoldShortToken(string? currentSentence)
    {
        if (string.IsNullOrWhiteSpace(currentSentence))
            return true;

        string trimmed = currentSentence.Trim();
        if (!trimmed.Contains(' '))
            return true;

        string lowerSentence = trimmed.ToLowerInvariant();
        return SentenceLooksCommandLike(lowerSentence)
            || SentenceLooksCodeLike(lowerSentence)
            || lowerSentence.Contains("://", StringComparison.Ordinal)
            || lowerSentence.Contains("www.", StringComparison.Ordinal);
    }

    private static bool ShouldAllowConvertibleLayoutShortToken(string rawToken, string normalizedToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || string.IsNullOrWhiteSpace(normalizedToken))
            return false;

        var candidate = EvaluateAutoCandidate(rawToken, normalizedToken);

        return candidate is not null
            && candidate.Direction != CorrectionDirection.None
            && candidate.ConvertedText.Count(char.IsLetter) >= 2;
    }

    private static bool ShouldAllowConvertibleTechnicalMixedToken(string rawToken, string normalizedToken, string? processName)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || string.IsNullOrWhiteSpace(normalizedToken))
            return false;

        if (LooksLikeCodeProcess(processName))
            return false;

        var candidate = EvaluateAutoCandidate(rawToken, normalizedToken);
        return candidate is not null
            && candidate.Direction == CorrectionDirection.UaToEn
            && candidate.ConvertedText.Any(char.IsDigit)
            && candidate.ConvertedText.Count(char.IsLetter) >= 2;
    }

    private static CorrectionCandidate? EvaluateAutoCandidate(string rawToken, string normalizedToken) =>
        CorrectionHeuristics.Evaluate(rawToken, CorrectionMode.Auto)
        ?? (!string.Equals(rawToken, normalizedToken, StringComparison.Ordinal)
            ? CorrectionHeuristics.Evaluate(normalizedToken, CorrectionMode.Auto)
            : null);

    private static bool LooksLikeCodeProcess(string? processName) =>
        !string.IsNullOrWhiteSpace(processName)
        && CodeLikeProcesses.Contains(processName.Trim());

    private static bool IsCommandStarterLike(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (CommandStarters.Contains(token))
            return true;

        string toggled = KeyboardLayoutMap.ToggleLayoutText(token, out int changedCount).ToLowerInvariant();
        return changedCount > 0 && CommandStarters.Contains(toggled);
    }

    private static bool IsCodeKeywordLike(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (CodeKeywords.Contains(token))
            return true;

        string toggled = KeyboardLayoutMap.ToggleLayoutText(token, out int changedCount).ToLowerInvariant();
        return changedCount > 0 && CodeKeywords.Contains(toggled);
    }
}
