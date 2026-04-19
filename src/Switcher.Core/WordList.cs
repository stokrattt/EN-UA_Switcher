using System.Reflection;

namespace Switcher.Core;

/// <summary>Loads embedded word frequency lists.</summary>
internal static class WordList
{
    public static HashSet<string> LoadEn() => Load("en-common.txt");
    public static HashSet<string> LoadUa() => Load("ua-common.txt");

    private static HashSet<string> Load(string resourceName)
    {
        var asm = Assembly.GetExecutingAssembly();
        string fullName = $"Switcher.Core.Dictionaries.{resourceName}";
        using var stream = asm.GetManifestResourceStream(fullName);
        if (stream == null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (!string.IsNullOrEmpty(line) && !line.StartsWith('#'))
                result.Add(line.ToLowerInvariant());
        }
        return result;
    }
}
