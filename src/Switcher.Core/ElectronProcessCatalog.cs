using System;
using System.Collections.Generic;

namespace Switcher.Core;

/// <summary>
/// Catalog of known Electron-based applications where special UIA handling might be needed.
/// </summary>
public static class ElectronProcessCatalog
{
    private static readonly HashSet<string> ElectronProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        // Development
        "code", "code - insiders", "cursor", "vscodium", "windsurf", "codex", "atom",
        // Communication
        "slack", "discord", "discordcanary", "discordptb",
        "teams", "ms-teams", "msteams",
        "element", "element-desktop", "signal",
        "telegram", "telegram-desktop", "whatsapp", "skype",
        // AI / Assistants
        "claude",
        // Notes/Docs
        "obsidian", "notion", "logseq",
        // Social/Other
        "viber", "spotify", "figma", "figma-agent", "postman",
    };

    /// <summary>Read-only view of all known Electron process names (no .exe, lowercase).</summary>
    public static IReadOnlyCollection<string> Processes => ElectronProcesses;

    public static bool IsElectronProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        
        string name = processName.Trim().ToLowerInvariant();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return ElectronProcesses.Contains(name);
    }
}
