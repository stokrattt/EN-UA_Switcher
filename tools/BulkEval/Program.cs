using Switcher.Core;

var repoRoot = GetRepoRoot();
string uaDictionaryPath = Path.Combine(repoRoot, "src", "Switcher.Core", "Dictionaries", "ua-common.txt");
string enPath = ParsePath(args, "--en-path", ResolveCorpusPath(
    repoRoot,
    preferredFile: "en-top50000.txt",
    fallbackFile: "en-top10000.txt",
    dictionaryPath: Path.Combine(repoRoot, "src", "Switcher.Core", "Dictionaries", "en-common.txt")));
string uaPath = ParsePath(args, "--ua-path", ResolveCorpusPath(
    repoRoot,
    preferredFile: "uk-top50000.txt",
    fallbackFile: "uk-top10000.txt",
    dictionaryPath: uaDictionaryPath));

int enLimit = ParseLimit(args, "--en", 50000);
int uaLimit = ParseLimit(args, "--ua", 50000);
int contextSample = ParseLimit(args, "--context-sample", 1500);
int clusterTop = ParseLimit(args, "--cluster-top", 8);

var enWords = LoadWords(enPath, enLimit);
var uaCorpusWords = LoadWords(uaPath, uaLimit);
var uaDictionaryWords = LoadWords(uaDictionaryPath, int.MaxValue).ToHashSet(StringComparer.Ordinal);
var uaWords = uaCorpusWords
    .Where(word => LooksUkrainianWord(word, uaDictionaryWords))
    .ToList();
var uaFilteredOutWords = uaCorpusWords
    .Where(word => !LooksUkrainianWord(word, uaDictionaryWords))
    .ToList();

var enOriginalFailures = new List<string>();
var uaOriginalFailures = new List<string>();
var enMistypeMisses = new List<string>();
var uaMistypeMisses = new List<string>();
var enSentenceOriginalFailures = new List<string>();
var uaSentenceOriginalFailures = new List<string>();
var enSentenceMistypeMisses = new List<string>();
var uaSentenceMistypeMisses = new List<string>();

var enOriginalHotspots = new HotspotAccumulator();
var uaOriginalHotspots = new HotspotAccumulator();
var enMistypeHotspots = new HotspotAccumulator();
var uaMistypeHotspots = new HotspotAccumulator();
var enSentenceOriginalHotspots = new HotspotAccumulator();
var uaSentenceOriginalHotspots = new HotspotAccumulator();
var enSentenceMistypeHotspots = new HotspotAccumulator();
var uaSentenceMistypeHotspots = new HotspotAccumulator();

int enOriginalFalsePositives = 0;
int uaOriginalFalsePositives = 0;
int enRecovered = 0;
int uaRecovered = 0;
int enSentenceOriginalFalsePositives = 0;
int uaSentenceOriginalFalsePositives = 0;
int enSentenceRecovered = 0;
int uaSentenceRecovered = 0;

var enSentences = BuildSentences(
    enWords,
    new[] { "win11", "iphone15", "rtx4090", "gpt4o", "vs2022" },
    isEnglish: true);
var uaSentences = BuildSentences(
    uaWords,
    new[] { "win11", "iphone15", "rtx4090", "gpt4o", "vs2022" },
    isEnglish: false);

foreach (string word in enWords)
{
    var result = CorrectionHeuristics.Evaluate(word, CorrectionMode.Auto);
    if (result is not null)
    {
        enOriginalFalsePositives++;
        Collect(enOriginalFailures, $"{word} -> {result.ConvertedText} | {result.Reason}");
        RecordWordHotspot(enOriginalHotspots, word, result.ConvertedText, result.Reason);
    }

    string? mistyped = KeyboardLayoutMap.ConvertEnToUa(word, strict: true);
    if (mistyped is null)
        continue;

    var recovered = CorrectionHeuristics.Evaluate(mistyped, CorrectionMode.Auto);
    if (recovered?.ConvertedText == word)
        enRecovered++;
    else
    {
        Collect(enMistypeMisses, $"{mistyped} -> {(recovered?.ConvertedText ?? "<null>")} | expected {word}");
        RecordWordHotspot(enMistypeHotspots, word, recovered?.ConvertedText ?? "<null>", recovered?.Reason ?? "<null>");
    }
}

foreach (string word in uaWords)
{
    var result = CorrectionHeuristics.Evaluate(word, CorrectionMode.Auto);
    if (result is not null)
    {
        uaOriginalFalsePositives++;
        Collect(uaOriginalFailures, $"{word} -> {result.ConvertedText} | {result.Reason}");
        RecordWordHotspot(uaOriginalHotspots, word, result.ConvertedText, result.Reason);
    }

    string? mistyped = KeyboardLayoutMap.ConvertUaToEn(word, strict: true);
    if (mistyped is null)
        continue;

    var recovered = CorrectionHeuristics.Evaluate(mistyped, CorrectionMode.Auto);
    if (recovered?.ConvertedText == word)
        uaRecovered++;
    else
    {
        Collect(uaMistypeMisses, $"{mistyped} -> {(recovered?.ConvertedText ?? "<null>")} | expected {word}");
        RecordWordHotspot(uaMistypeHotspots, word, recovered?.ConvertedText ?? "<null>", recovered?.Reason ?? "<null>");
    }
}

foreach (string sentence in enSentences)
{
    string untouched = SimulateSentence(sentence);
    if (!string.Equals(untouched, sentence, StringComparison.Ordinal))
    {
        enSentenceOriginalFalsePositives++;
        Collect(enSentenceOriginalFailures, $"{sentence} -> {untouched}");
        RecordSentenceHotspot(enSentenceOriginalHotspots, sentence, untouched, reason: "sentence-original-fp");
    }

    string mistyped = MistypeSentence(sentence, sourceIsEnglish: true);
    string recovered = SimulateSentence(mistyped);
    if (string.Equals(recovered, sentence, StringComparison.Ordinal))
        enSentenceRecovered++;
    else
    {
        Collect(enSentenceMistypeMisses, $"{mistyped} -> {recovered} | expected {sentence}");
        RecordSentenceHotspot(enSentenceMistypeHotspots, sentence, recovered, reason: "sentence-mistype-miss");
    }
}

foreach (string sentence in uaSentences)
{
    string untouched = SimulateSentence(sentence);
    if (!string.Equals(untouched, sentence, StringComparison.Ordinal))
    {
        uaSentenceOriginalFalsePositives++;
        Collect(uaSentenceOriginalFailures, $"{sentence} -> {untouched}");
        RecordSentenceHotspot(uaSentenceOriginalHotspots, sentence, untouched, reason: "sentence-original-fp");
    }

    string mistyped = MistypeSentence(sentence, sourceIsEnglish: false);
    string recovered = SimulateSentence(mistyped);
    if (string.Equals(recovered, sentence, StringComparison.Ordinal))
        uaSentenceRecovered++;
    else
    {
        Collect(uaSentenceMistypeMisses, $"{mistyped} -> {recovered} | expected {sentence}");
        RecordSentenceHotspot(uaSentenceMistypeHotspots, sentence, recovered, reason: "sentence-mistype-miss");
    }
}

var enChatAudit = AuditScenarios(
    "EN CHAT CONTEXT",
    BuildChatScenarios(enWords, isEnglish: true, contextSample),
    MistypeExpectation.RecoverToOriginal);
var uaChatAudit = AuditScenarios(
    "UA CHAT CONTEXT",
    BuildChatScenarios(uaWords, isEnglish: false, contextSample),
    MistypeExpectation.RecoverToOriginal);
var shortAudit = AuditScenarios(
    "SHORT TOKEN SAFETY",
    BuildShortTokenScenarios(),
    MistypeExpectation.HoldMistyped);
var urlAudit = AuditScenarios(
    "URL SAFETY",
    BuildUrlScenarios(GetScenarioPool(enWords, contextSample)),
    MistypeExpectation.HoldMistyped);
var commandAudit = AuditScenarios(
    "COMMAND SAFETY",
    BuildCommandScenarios(GetScenarioPool(enWords, contextSample)),
    MistypeExpectation.HoldMistyped);
var codeAudit = AuditScenarios(
    "CODE SAFETY",
    BuildCodeScenarios(GetScenarioPool(enWords, contextSample)),
    MistypeExpectation.HoldMistyped);
var technicalAudit = AuditScenarios(
    "TECH TOKEN SAFETY",
    BuildTechnicalTokenScenarios(),
    MistypeExpectation.HoldMistyped);
var shortPhraseAudit = AuditScenarios(
    "SHORT PHRASE RECOVERY",
    BuildShortPhraseRecoveryScenarios(),
    MistypeExpectation.RecoverToOriginal);
var brandChatAudit = AuditScenarios(
    "BRAND CHAT RECOVERY",
    BuildBrandChatScenarios(),
    MistypeExpectation.RecoverToOriginal);
var mixedTechChatAudit = AuditScenarios(
    "MIXED TECH CHAT RECOVERY",
    BuildMixedTechChatScenarios(),
    MistypeExpectation.RecoverToOriginal);

Console.WriteLine("=== SUMMARY ===");
Console.WriteLine($"EN source: {enPath}");
Console.WriteLine($"UA source: {uaPath}");
Console.WriteLine($"EN original words tested: {enWords.Count}");
Console.WriteLine($"EN false positives: {enOriginalFalsePositives}");
Console.WriteLine($"UA original words tested: {uaWords.Count}");
Console.WriteLine($"UA false positives: {uaOriginalFalsePositives}");
Console.WriteLine($"UA filtered non-Ukrainian corpus words: {uaFilteredOutWords.Count}");
Console.WriteLine($"EN mistypes tested: {enWords.Count}");
Console.WriteLine($"EN mistypes recovered: {enRecovered} ({Percent(enRecovered, enWords.Count):F2}%)");
Console.WriteLine($"UA mistypes tested: {uaWords.Count}");
Console.WriteLine($"UA mistypes recovered: {uaRecovered} ({Percent(uaRecovered, uaWords.Count):F2}%)");
Console.WriteLine($"EN original sentences tested: {enSentences.Count}");
Console.WriteLine($"EN sentence false positives: {enSentenceOriginalFalsePositives}");
Console.WriteLine($"UA original sentences tested: {uaSentences.Count}");
Console.WriteLine($"UA sentence false positives: {uaSentenceOriginalFalsePositives}");
Console.WriteLine($"EN mistyped sentences recovered: {enSentenceRecovered} ({Percent(enSentenceRecovered, enSentences.Count):F2}%)");
Console.WriteLine($"UA mistyped sentences recovered: {uaSentenceRecovered} ({Percent(uaSentenceRecovered, uaSentences.Count):F2}%)");

PrintHotspotSummary("EN ORIGINAL FALSE POSITIVE HOTSPOTS", enOriginalHotspots, clusterTop);
PrintHotspotSummary("UA ORIGINAL FALSE POSITIVE HOTSPOTS", uaOriginalHotspots, clusterTop);
PrintHotspotSummary("EN MISTYPE MISS HOTSPOTS", enMistypeHotspots, clusterTop);
PrintHotspotSummary("UA MISTYPE MISS HOTSPOTS", uaMistypeHotspots, clusterTop);
PrintHotspotSummary("EN SENTENCE ORIGINAL HOTSPOTS", enSentenceOriginalHotspots, clusterTop);
PrintHotspotSummary("UA SENTENCE ORIGINAL HOTSPOTS", uaSentenceOriginalHotspots, clusterTop);
PrintHotspotSummary("EN SENTENCE MISTYPE HOTSPOTS", enSentenceMistypeHotspots, clusterTop);
PrintHotspotSummary("UA SENTENCE MISTYPE HOTSPOTS", uaSentenceMistypeHotspots, clusterTop);

PrintScenarioAudit(enChatAudit);
PrintScenarioAudit(uaChatAudit);
PrintScenarioAudit(shortAudit);
PrintScenarioAudit(urlAudit);
PrintScenarioAudit(commandAudit);
PrintScenarioAudit(codeAudit);
PrintScenarioAudit(technicalAudit);
PrintScenarioAudit(shortPhraseAudit);
PrintScenarioAudit(brandChatAudit);
PrintScenarioAudit(mixedTechChatAudit);

PrintSample("EN ORIGINAL FALSE POSITIVES", enOriginalFailures);
PrintSample("UA ORIGINAL FALSE POSITIVES", uaOriginalFailures);
PrintSample("UA FILTERED NON-UKRAINIAN CORPUS WORDS", uaFilteredOutWords.Select(word => word).Take(20).ToList());
PrintSample("EN MISTYPE MISSES", enMistypeMisses);
PrintSample("UA MISTYPE MISSES", uaMistypeMisses);
PrintSample("EN SENTENCE ORIGINAL FALSE POSITIVES", enSentenceOriginalFailures);
PrintSample("UA SENTENCE ORIGINAL FALSE POSITIVES", uaSentenceOriginalFailures);
PrintSample("EN SENTENCE MISTYPE MISSES", enSentenceMistypeMisses);
PrintSample("UA SENTENCE MISTYPE MISSES", uaSentenceMistypeMisses);

static string GetRepoRoot()
{
    string current = AppContext.BaseDirectory;
    for (int i = 0; i < 6; i++)
    {
        string? parent = Directory.GetParent(current)?.FullName;
        if (parent is null)
            break;

        if (File.Exists(Path.Combine(parent, "Switcher.sln")))
            return parent;

        current = parent;
    }

    throw new InvalidOperationException("Could not locate repo root.");
}

static int ParseLimit(string[] args, string flag, int fallback)
{
    int index = Array.IndexOf(args, flag);
    if (index < 0 || index + 1 >= args.Length)
        return fallback;

    return int.TryParse(args[index + 1], out int value) ? value : fallback;
}

static string ResolveCorpusPath(string repoRoot, string preferredFile, string fallbackFile, string dictionaryPath)
{
    string preferredPath = Path.Combine(repoRoot, "artifacts", "bulk-audit", preferredFile);
    if (HasUsableCorpusFile(preferredPath))
        return preferredPath;

    string fallbackPath = Path.Combine(repoRoot, "artifacts", "bulk-audit", fallbackFile);
    if (HasUsableCorpusFile(fallbackPath))
        return fallbackPath;

    return dictionaryPath;
}

static string ParsePath(string[] args, string flag, string fallback)
{
    int index = Array.IndexOf(args, flag);
    if (index < 0 || index + 1 >= args.Length)
        return fallback;

    return Path.GetFullPath(args[index + 1]);
}

static List<string> LoadWords(string path, int limit)
{
    return File.ReadLines(path)
        .Select(NormalizeCorpusLine)
        .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
        .Where(IsSimpleToken)
        .Take(limit)
        .ToList();
}

static bool HasUsableCorpusFile(string path)
{
    if (!File.Exists(path))
        return false;

    foreach (string line in File.ReadLines(path).Take(20))
    {
        if (!string.IsNullOrWhiteSpace(NormalizeCorpusLine(line)))
            return true;
    }

    return false;
}

static string NormalizeCorpusLine(string line) =>
    line.Trim().TrimStart('\uFEFF').ToLowerInvariant();

static bool IsSimpleToken(string word) =>
    word.All(c => char.IsLetter(c) || c is '\'' or '-' or '’' or 'ʼ');

static List<string> GetScenarioPool(List<string> words, int limit) =>
    words
        .Where(w => w.Length >= 4)
        .Where(w => !w.Contains('\'') && !w.Contains('’') && !w.Contains('ʼ'))
        .Take(limit)
        .ToList();

static List<string> BuildSentences(List<string> words, string[] techTokens, bool isEnglish)
{
    var pool = words
        .Where(w => w.Length >= 4)
        .Where(w => !w.Contains('\'') && !w.Contains('’') && !w.Contains('ʼ'))
        .ToList();

    if (pool.Count < 3)
        pool = words;

    var sentences = new List<string>();
    int triplets = Math.Min(pool.Count / 3, 120);

    for (int i = 0; i < triplets; i++)
    {
        string w1 = pool[i * 3];
        string w2 = pool[(i * 3) + 1];
        string w3 = pool[(i * 3) + 2];
        string tech = techTokens[i % techTokens.Length];

        sentences.Add($"{Cap(w1)} {w2}.");
        sentences.Add($"{Cap(w1)} {w2}, {w3}!");
        sentences.Add($"{Cap(w1)} \"{w2}\".");
        sentences.Add($"{Cap(w1)}-{w2} {w3}?");
        sentences.Add($"{Cap(w1)} {tech} {w2}.");
        sentences.Add($"{Cap(w1)}: {w2}; {w3}.");
        sentences.Add($"{w1} {w2} {w3}.");

        if (isEnglish)
            sentences.Add($"{Cap(w1)} {w2} with {tech}, {w3}.");
        else
            sentences.Add($"{Cap(w1)} {w2} також {tech}, {w3}.");
    }

    if (!isEnglish)
    {
        sentences.AddRange(new[]
        {
            "П'ять людей, здоров'я і win11.",
            "Обов'язок, запам'ятовувати і gpt4o.",
            "Дев'ять років: iphone15, rtx4090 і vs2022."
        });
    }

    return sentences;
}

static string SimulateSentence(string sentence, string processName = "chat")
{
    var parts = sentence.Split(' ');
    for (int i = 0; i < parts.Length; i++)
    {
        var result = EvaluateLikeAutoMode(parts[i], sentence, processName);
        if (result is not null)
            parts[i] = result.ConvertedText;
    }

    return string.Join(" ", parts);
}

static string MistypeSentence(string sentence, bool sourceIsEnglish)
{
    var parts = sentence.Split(' ');
    for (int i = 0; i < parts.Length; i++)
        parts[i] = MistypeToken(parts[i], sourceIsEnglish);

    return string.Join(" ", parts);
}

static CorrectionCandidate? EvaluateLikeAutoMode(string token, string sentenceContext, string processName)
{
    if (AutoContextGuards.GetUnsafeAutoCorrectionReason(token, sentenceContext, processName) is not null)
        return null;

    string toggledToken = KeyboardLayoutMap.ToggleLayoutText(token, out _);
    string visibleSuffix = ResolveVisibleTrailingPunctuation(token, toggledToken);
    string analysis = TrimTrailingChars(token, visibleSuffix.Length);
    var candidate = string.IsNullOrWhiteSpace(analysis)
        ? null
        : CorrectionHeuristics.Evaluate(analysis, CorrectionMode.Auto);

    return candidate is null
        ? null
        : ApplyVisibleTrailingPunctuation(candidate, visibleSuffix);
}

static string MistypeToken(string token, bool sourceIsEnglish)
{
    int start = 0;
    int end = token.Length;

    while (start < end
           && !char.IsLetterOrDigit(token[start])
           && (!KeyboardLayoutMap.IsLayoutLetterChar(token[start]) || IsWrappingQuote(token[start]))
           && !KeyboardLayoutMap.IsWordConnector(token[start]))
        start++;

    while (end > start
           && !char.IsLetterOrDigit(token[end - 1])
           && !KeyboardLayoutMap.IsWordConnector(token[end - 1]))
        end--;

    if (end <= start)
        return token;

    string prefix = token[..start];
    string core = token[start..end];
    string suffix = token[end..];

    string? converted = sourceIsEnglish
        ? KeyboardLayoutMap.ConvertEnToUa(core, strict: false)
        : KeyboardLayoutMap.ConvertUaToEn(core, strict: false);

    return prefix + (converted ?? core) + suffix;
}

static ScenarioAudit AuditScenarios(string title, List<ContextScenario> scenarios, MistypeExpectation mistypeExpectation)
{
    var originalFailures = new List<string>();
    var mistypeFailures = new List<string>();
    var originalHotspots = new HotspotAccumulator();
    var mistypeHotspots = new HotspotAccumulator();
    int originalFailureCount = 0;
    int mistypeSuccesses = 0;

    foreach (ContextScenario scenario in scenarios)
    {
        string untouched = SimulateSentence(scenario.Sentence, scenario.ProcessName);
        if (!string.Equals(untouched, scenario.Sentence, StringComparison.Ordinal))
        {
            originalFailureCount++;
            Collect(originalFailures, $"{scenario.Sentence} -> {untouched}");
            RecordSentenceHotspot(originalHotspots, scenario.Sentence, untouched, reason: "scenario-original-fp");
        }

        string mistyped = MistypeSentence(scenario.Sentence, scenario.SourceIsEnglish);
        string recovered = SimulateSentence(mistyped, scenario.ProcessName);
        string expected = mistypeExpectation == MistypeExpectation.RecoverToOriginal
            ? scenario.Sentence
            : mistyped;

        if (string.Equals(recovered, expected, StringComparison.Ordinal))
        {
            mistypeSuccesses++;
        }
        else
        {
            Collect(mistypeFailures, $"{mistyped} -> {recovered} | expected {expected}");
            RecordSentenceHotspot(mistypeHotspots, expected, recovered, reason: "scenario-mistype-miss");
        }
    }

    return new ScenarioAudit(
        title,
        scenarios.Count,
        originalFailureCount,
        mistypeSuccesses,
        originalFailures,
        mistypeFailures,
        originalHotspots,
        mistypeHotspots);
}

static List<ContextScenario> BuildChatScenarios(List<string> words, bool isEnglish, int limit)
{
    var scenarios = new List<ContextScenario>();
    foreach (string word in GetScenarioPool(words, limit))
    {
        if (isEnglish)
        {
            scenarios.Add(new ContextScenario($"Hey, {word}!", SourceIsEnglish: true, ProcessName: "telegram"));
            scenarios.Add(new ContextScenario($"\"{word}\", are you here?", SourceIsEnglish: true, ProcessName: "telegram"));
        }
        else
        {
            scenarios.Add(new ContextScenario($"Привіт, {word}!", SourceIsEnglish: false, ProcessName: "telegram"));
            scenarios.Add(new ContextScenario($"\"{word}\", ти тут?", SourceIsEnglish: false, ProcessName: "telegram"));
        }
    }

    return scenarios;
}

static List<ContextScenario> BuildShortTokenScenarios() =>
    new()
    {
        new ContextScenario("ok", SourceIsEnglish: true, ProcessName: "telegram"),
        new ContextScenario("hi", SourceIsEnglish: true, ProcessName: "telegram"),
        new ContextScenario("we", SourceIsEnglish: true, ProcessName: "telegram"),
        new ContextScenario("go", SourceIsEnglish: true, ProcessName: "telegram"),
        new ContextScenario("я", SourceIsEnglish: false, ProcessName: "telegram"),
        new ContextScenario("ти", SourceIsEnglish: false, ProcessName: "telegram"),
        new ContextScenario("ми", SourceIsEnglish: false, ProcessName: "telegram"),
        new ContextScenario("це", SourceIsEnglish: false, ProcessName: "telegram")
    };

static List<ContextScenario> BuildUrlScenarios(List<string> words)
{
    var scenarios = new List<ContextScenario>();
    foreach (string word in words)
    {
        scenarios.Add(new ContextScenario($"https://example.com/{word}?tab=1", SourceIsEnglish: true, ProcessName: "chrome"));
        scenarios.Add(new ContextScenario($"www.example.com/{word}/index", SourceIsEnglish: true, ProcessName: "chrome"));
    }

    return scenarios;
}

static List<ContextScenario> BuildCommandScenarios(List<string> words)
{
    var scenarios = new List<ContextScenario>();
    foreach (string word in words)
    {
        scenarios.Add(new ContextScenario($"dotnet {word} Switcher.sln", SourceIsEnglish: true, ProcessName: "pwsh"));
        scenarios.Add(new ContextScenario($"git {word} src/Switcher.Engine", SourceIsEnglish: true, ProcessName: "pwsh"));
    }

    return scenarios;
}

static List<ContextScenario> BuildCodeScenarios(List<string> words)
{
    var scenarios = new List<ContextScenario>();
    foreach (string word in words)
    {
        scenarios.Add(new ContextScenario($"const {word} = value;", SourceIsEnglish: true, ProcessName: "code"));
        scenarios.Add(new ContextScenario($"return {word};", SourceIsEnglish: true, ProcessName: "code"));
    }

    return scenarios;
}

static List<ContextScenario> BuildTechnicalTokenScenarios() =>
    new()
    {
        new ContextScenario("win11", SourceIsEnglish: true, ProcessName: "code"),
        new ContextScenario("gpt4o", SourceIsEnglish: true, ProcessName: "code"),
        new ContextScenario("rtx4090", SourceIsEnglish: true, ProcessName: "code"),
        new ContextScenario("vs2022", SourceIsEnglish: true, ProcessName: "code"),
        new ContextScenario("README.md", SourceIsEnglish: true, ProcessName: "code"),
        new ContextScenario("appsettings.json", SourceIsEnglish: true, ProcessName: "code"),
        new ContextScenario("switcher_engine", SourceIsEnglish: true, ProcessName: "code"),
        new ContextScenario("--help", SourceIsEnglish: true, ProcessName: "pwsh")
    };

static List<ContextScenario> BuildShortPhraseRecoveryScenarios() =>
    new()
    {
        new ContextScenario("in my defence", SourceIsEnglish: true, ProcessName: "telegram"),
        new ContextScenario("what are you doing", SourceIsEnglish: true, ProcessName: "telegram"),
        new ContextScenario("am i late", SourceIsEnglish: true, ProcessName: "telegram"),
        new ContextScenario("oh ok", SourceIsEnglish: true, ProcessName: "telegram"),
        new ContextScenario("що ти робиш", SourceIsEnglish: false, ProcessName: "telegram"),
        new ContextScenario("що ти тут", SourceIsEnglish: false, ProcessName: "telegram"),
        new ContextScenario("це не я", SourceIsEnglish: false, ProcessName: "telegram"),
        new ContextScenario("де ти є", SourceIsEnglish: false, ProcessName: "telegram")
    };

static List<ContextScenario> BuildBrandChatScenarios()
{
    string[] brands =
    {
        "nvidia", "amd", "intel", "spotify", "steam", "telegram", "discord",
        "chrome", "firefox", "github", "docker", "browser", "client", "server"
    };

    var scenarios = new List<ContextScenario>();
    foreach (string brand in brands)
    {
        scenarios.Add(new ContextScenario($"check {brand} now", SourceIsEnglish: true, ProcessName: "telegram"));
        scenarios.Add(new ContextScenario($"{Cap(brand)} update today", SourceIsEnglish: true, ProcessName: "telegram"));
        scenarios.Add(new ContextScenario($"need {brand} help", SourceIsEnglish: true, ProcessName: "telegram"));
    }

    return scenarios;
}

static List<ContextScenario> BuildMixedTechChatScenarios() =>
    new()
    {
        new ContextScenario("win11 driver update", SourceIsEnglish: true, ProcessName: "telegram"),
        new ContextScenario("rtx4050 driver issue", SourceIsEnglish: true, ProcessName: "telegram"),
        new ContextScenario("gpt4o api update", SourceIsEnglish: true, ProcessName: "telegram"),
        new ContextScenario("vs2022 build tools", SourceIsEnglish: true, ProcessName: "telegram"),
        new ContextScenario("nvidia rtx4050 driver", SourceIsEnglish: true, ProcessName: "telegram"),
        new ContextScenario("win11 and spotify", SourceIsEnglish: true, ProcessName: "telegram")
    };

static CorrectionCandidate ApplyVisibleTrailingPunctuation(CorrectionCandidate candidate, string visibleSuffix)
{
    if (string.IsNullOrEmpty(visibleSuffix))
        return candidate;

    string original = candidate.OriginalText;
    if (!original.EndsWith(visibleSuffix, StringComparison.Ordinal))
        original += visibleSuffix;

    string converted = candidate.ConvertedText;
    if (!converted.EndsWith(visibleSuffix, StringComparison.Ordinal))
    {
        string toggledSuffix = KeyboardLayoutMap.ToggleLayoutText(visibleSuffix, out int changedCount);
        if (changedCount > 0 && converted.EndsWith(toggledSuffix, StringComparison.Ordinal))
            converted = converted[..^toggledSuffix.Length] + visibleSuffix;
        else
            converted += visibleSuffix;
    }

    return candidate with
    {
        OriginalText = original,
        ConvertedText = converted
    };
}

static string ExtractLiteralTrailingPunctuation(string visibleWord)
{
    if (string.IsNullOrEmpty(visibleWord) || visibleWord.Length < 2)
        return string.Empty;

    int start = visibleWord.Length;
    while (start > 0 && IsLiteralTrailingPunctuation(visibleWord[start - 1]))
        start--;

    while (start < visibleWord.Length
           && IsWrappingQuote(visibleWord[start])
           && start > 0
           && (char.IsLetterOrDigit(visibleWord[start - 1])
               || KeyboardLayoutMap.IsLayoutLetterChar(visibleWord[start - 1])
               || KeyboardLayoutMap.IsWordConnector(visibleWord[start - 1])))
    {
        start++;
    }

    if (start == visibleWord.Length || start == 0)
        return string.Empty;

    return char.IsLetterOrDigit(visibleWord[start - 1])
        ? visibleWord[start..]
        : string.Empty;
}

static string ResolveVisibleTrailingPunctuation(params string[] visibleWords)
{
    foreach (string visibleWord in visibleWords)
    {
        string run = ExtractLiteralTrailingPunctuation(visibleWord);
        if (string.IsNullOrEmpty(run))
            continue;

        for (int offset = run.Length - 1; offset >= 0; offset--)
        {
            string suffix = run[offset..];
            if (ShouldTreatTrailingSuffixAsLiteral(suffix, visibleWords))
                return suffix;
        }
    }

    return string.Empty;
}

static bool ShouldTreatTrailingSuffixAsLiteral(string suffix, params string[] interpretations)
{
    string toggledSuffix = KeyboardLayoutMap.ToggleLayoutText(suffix, out int changedCount);
    if (changedCount > 0 && toggledSuffix.Any(char.IsLetter))
    {
        foreach (string interpretation in interpretations)
        {
            if (!string.IsNullOrWhiteSpace(interpretation)
                && interpretation.EndsWith(toggledSuffix, StringComparison.Ordinal)
                && interpretation.Length > toggledSuffix.Length)
            {
                string core = interpretation[..^toggledSuffix.Length];

                if (CorrectionHeuristics.LooksCorrectAsTyped(interpretation)
                    && !CorrectionHeuristics.HasStrongAsTypedSignal(core))
                {
                    return false;
                }

                var fullCandidate = CorrectionHeuristics.Evaluate(interpretation, CorrectionMode.Auto);
                var coreCandidate = CorrectionHeuristics.Evaluate(core, CorrectionMode.Auto);

                if (fullCandidate is not null
                    && (coreCandidate is null
                        || (fullCandidate.Confidence >= coreCandidate.Confidence
                            && fullCandidate.ConvertedText.Length > coreCandidate.ConvertedText.Length)))
                {
                return false;
                }
            }
        }
    }

    foreach (string interpretation in interpretations)
    {
        if (string.IsNullOrWhiteSpace(interpretation)
            || !interpretation.EndsWith(suffix, StringComparison.Ordinal)
            || interpretation.Length <= suffix.Length)
            continue;

        string core = interpretation[..^suffix.Length];
        if (string.IsNullOrWhiteSpace(core))
            continue;

        if (changedCount > 0 && toggledSuffix.Any(char.IsLetter))
        {
            var fullCandidate = CorrectionHeuristics.Evaluate(interpretation, CorrectionMode.Auto);
            var coreCandidate = CorrectionHeuristics.Evaluate(core, CorrectionMode.Auto);
            if (fullCandidate is not null
                && coreCandidate is not null
                && fullCandidate.Confidence >= coreCandidate.Confidence
                && fullCandidate.ConvertedText.Length > coreCandidate.ConvertedText.Length)
            {
                return false;
            }
        }

        bool interpretationStable = CorrectionHeuristics.HasStrongAsTypedSignal(interpretation);
        bool coreStable = CorrectionHeuristics.HasStrongAsTypedSignal(core);
        bool coreConvertible = CorrectionHeuristics.Evaluate(core, CorrectionMode.Auto) is not null;

        if ((interpretationStable && coreStable) || (!interpretationStable && coreConvertible))
            return true;
    }

    return false;
}

static string TrimTrailingChars(string text, int count) =>
    count <= 0 || string.IsNullOrEmpty(text)
        ? text
        : (text.Length > count ? text[..^count] : string.Empty);

static bool IsLiteralTrailingPunctuation(char c) =>
    char.IsPunctuation(c) && !KeyboardLayoutMap.IsWordConnector(c);

static bool IsWrappingQuote(char c) =>
    c is '"' or '«' or '»' or '“' or '”' or '„';

static string Cap(string word)
{
    if (string.IsNullOrEmpty(word))
        return word;

    if (word.Length == 1)
        return word.ToUpperInvariant();

    return char.ToUpperInvariant(word[0]) + word[1..];
}

static void Collect(List<string> bucket, string value)
{
    if (bucket.Count < 20)
        bucket.Add(value);
}

static double Percent(int part, int total) =>
    total == 0 ? 0 : 100.0 * part / total;

static bool LooksUkrainianWord(string word, HashSet<string> uaDictionaryWords)
{
    if (string.IsNullOrWhiteSpace(word))
        return false;

    string lower = word.Trim().ToLowerInvariant();
    if (!lower.Any(char.IsLetter))
        return false;

    if (lower.Any(c => c is 'ы' or 'э' or 'ё' or 'ъ'))
        return false;

    if (uaDictionaryWords.Contains(lower))
        return true;

    if (lower.Any(c => c is 'ї' or 'є' or 'ґ' or 'і'))
        return true;

    return !lower.EndsWith("ешь", StringComparison.Ordinal)
        && !lower.EndsWith("ишь", StringComparison.Ordinal)
        && !lower.EndsWith("ого", StringComparison.Ordinal)
        && !lower.EndsWith("ему", StringComparison.Ordinal)
        && !lower.EndsWith("ами", StringComparison.Ordinal)
        && !lower.EndsWith("ями", StringComparison.Ordinal)
        && !lower.EndsWith("ться", StringComparison.Ordinal)
        && !lower.EndsWith("ешься", StringComparison.Ordinal)
        && !lower.EndsWith("ить", StringComparison.Ordinal)
        && !lower.EndsWith("ать", StringComparison.Ordinal);
}

static void PrintSample(string title, List<string> rows)
{
    Console.WriteLine($"=== {title} ===");
    if (rows.Count == 0)
    {
        Console.WriteLine("(none)");
        return;
    }

    foreach (string row in rows)
        Console.WriteLine(row);
}

static void PrintScenarioAudit(ScenarioAudit audit)
{
    Console.WriteLine($"=== {audit.Title} ===");
    Console.WriteLine($"Original scenarios tested: {audit.Total}");
    Console.WriteLine($"Original unexpected replacements: {audit.OriginalFailures}");
    Console.WriteLine($"Mistyped success count: {audit.MistypeSuccesses} ({Percent(audit.MistypeSuccesses, audit.Total):F2}%)");
    PrintCompactHotspotLines("Original hotspot", audit.OriginalHotspots, 5);
    PrintCompactHotspotLines("Mistype hotspot", audit.MistypeHotspots, 5);
    PrintSample($"{audit.Title} ORIGINAL FAILURES", audit.OriginalFailureSamples);
    PrintSample($"{audit.Title} MISTYPE FAILURES", audit.MistypeFailureSamples);
}

static void RecordWordHotspot(HotspotAccumulator accumulator, string sourceToken, string actualToken, string reason)
{
    RecordTokenForms(accumulator.SourceTokens, sourceToken);
    RecordPrefixSuffixForms(accumulator.SourcePrefixes, accumulator.SourceSuffixes, sourceToken);

    if (!string.IsNullOrWhiteSpace(actualToken) && !string.Equals(actualToken, "<null>", StringComparison.Ordinal))
    {
        RecordTokenForms(accumulator.TargetTokens, actualToken);
        RecordPrefixSuffixForms(accumulator.TargetPrefixes, accumulator.TargetSuffixes, actualToken);
    }

    Increment(accumulator.ReasonKinds, SimplifyReason(reason));
}

static void RecordSentenceHotspot(HotspotAccumulator accumulator, string expectedSentence, string actualSentence, string reason)
{
    var mismatch = FindFirstTokenMismatch(expectedSentence, actualSentence);
    if (!string.IsNullOrWhiteSpace(mismatch.ExpectedToken))
    {
        RecordTokenForms(accumulator.SourceTokens, mismatch.ExpectedToken);
        RecordPrefixSuffixForms(accumulator.SourcePrefixes, accumulator.SourceSuffixes, mismatch.ExpectedToken);
    }

    if (!string.IsNullOrWhiteSpace(mismatch.ActualToken))
    {
        RecordTokenForms(accumulator.TargetTokens, mismatch.ActualToken);
        RecordPrefixSuffixForms(accumulator.TargetPrefixes, accumulator.TargetSuffixes, mismatch.ActualToken);
    }

    Increment(accumulator.ReasonKinds, reason);
}

static void PrintHotspotSummary(string title, HotspotAccumulator accumulator, int top)
{
    Console.WriteLine($"=== {title} ===");
    PrintTopCounts("Top reasons", accumulator.ReasonKinds, top);
    PrintTopCounts("Top source tokens", accumulator.SourceTokens, top);
    PrintTopCounts("Top source prefixes", accumulator.SourcePrefixes, top);
    PrintTopCounts("Top source suffixes", accumulator.SourceSuffixes, top);
    PrintTopCounts("Top target tokens", accumulator.TargetTokens, top);
}

static void PrintCompactHotspotLines(string label, HotspotAccumulator accumulator, int top)
{
    string reasonLine = FormatCompactTopLine(accumulator.ReasonKinds, top);
    if (!string.IsNullOrEmpty(reasonLine))
        Console.WriteLine($"{label} reasons: {reasonLine}");

    string tokenLine = FormatCompactTopLine(accumulator.SourceTokens, top);
    if (!string.IsNullOrEmpty(tokenLine))
        Console.WriteLine($"{label} source tokens: {tokenLine}");

    string suffixLine = FormatCompactTopLine(accumulator.SourceSuffixes, top);
    if (!string.IsNullOrEmpty(suffixLine))
        Console.WriteLine($"{label} source suffixes: {suffixLine}");
}

static void PrintTopCounts(string title, Dictionary<string, int> counts, int top)
{
    Console.WriteLine($"{title}: {FormatCompactTopLine(counts, top)}");
}

static string FormatCompactTopLine(Dictionary<string, int> counts, int top)
{
    if (counts.Count == 0)
        return "(none)";

    return string.Join(", ",
        counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(top)
            .Select(pair => $"{pair.Key}={pair.Value}"));
}

static void RecordTokenForms(Dictionary<string, int> counts, string token)
{
    string normalized = NormalizeHotspotToken(token);
    if (string.IsNullOrEmpty(normalized))
        return;

    Increment(counts, normalized);
}

static void RecordPrefixSuffixForms(Dictionary<string, int> prefixes, Dictionary<string, int> suffixes, string token)
{
    string normalized = NormalizeHotspotToken(token);
    if (string.IsNullOrEmpty(normalized))
        return;

    Increment(prefixes, BuildEdgeKey(normalized, takePrefix: true));
    Increment(suffixes, BuildEdgeKey(normalized, takePrefix: false));
}

static string NormalizeHotspotToken(string token) =>
    new(token
        .Trim()
        .ToLowerInvariant()
        .Where(c => char.IsLetterOrDigit(c) || c is '\'' or '-' or '’' or 'ʼ')
        .ToArray());

static string BuildEdgeKey(string normalized, bool takePrefix)
{
    if (normalized.Length <= 4)
        return normalized;

    return takePrefix ? normalized[..4] : normalized[^4..];
}

static string SimplifyReason(string reason)
{
    if (string.IsNullOrWhiteSpace(reason))
        return "<empty>";

    int pipeIndex = reason.IndexOf('|');
    string trimmed = pipeIndex >= 0 ? reason[..pipeIndex] : reason;

    int srcIndex = trimmed.IndexOf(" src=", StringComparison.Ordinal);
    if (srcIndex >= 0)
        trimmed = trimmed[..srcIndex];

    int selectorIndex = trimmed.IndexOf(" selector=", StringComparison.Ordinal);
    if (selectorIndex >= 0)
        trimmed = trimmed[..selectorIndex];

    return trimmed.Trim();
}

static TokenMismatch FindFirstTokenMismatch(string expectedSentence, string actualSentence)
{
    string[] expectedTokens = TokenizeForComparison(expectedSentence);
    string[] actualTokens = TokenizeForComparison(actualSentence);
    int count = Math.Max(expectedTokens.Length, actualTokens.Length);

    for (int i = 0; i < count; i++)
    {
        string expected = i < expectedTokens.Length ? expectedTokens[i] : string.Empty;
        string actual = i < actualTokens.Length ? actualTokens[i] : string.Empty;
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            return new TokenMismatch(expected, actual);
    }

    return new TokenMismatch(string.Empty, string.Empty);
}

static string[] TokenizeForComparison(string sentence) =>
    sentence
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(NormalizeHotspotToken)
        .Where(token => !string.IsNullOrEmpty(token))
        .ToArray();

static void Increment(Dictionary<string, int> counts, string key)
{
    if (string.IsNullOrEmpty(key))
        return;

    counts.TryGetValue(key, out int current);
    counts[key] = current + 1;
}

internal enum MistypeExpectation
{
    RecoverToOriginal,
    HoldMistyped
}

internal sealed record ContextScenario(string Sentence, bool SourceIsEnglish, string ProcessName);

internal sealed record ScenarioAudit(
    string Title,
    int Total,
    int OriginalFailures,
    int MistypeSuccesses,
    List<string> OriginalFailureSamples,
    List<string> MistypeFailureSamples,
    HotspotAccumulator OriginalHotspots,
    HotspotAccumulator MistypeHotspots);

internal sealed class HotspotAccumulator
{
    public Dictionary<string, int> ReasonKinds { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> SourceTokens { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> SourcePrefixes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> SourceSuffixes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> TargetTokens { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> TargetPrefixes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> TargetSuffixes { get; } = new(StringComparer.Ordinal);
}

internal sealed record TokenMismatch(string ExpectedToken, string ActualToken);
