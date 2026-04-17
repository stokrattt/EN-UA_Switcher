namespace Switcher.Core;

/// <summary>
/// Known Electron / Chromium-shell desktop applications where the Auto-mode
/// clipboard fallback (Shift+Left selection + Ctrl+C) is dangerous because
/// the selection remains live for hundreds of milliseconds while the user
/// may already be typing the next character — which Electron then happily
/// uses to replace the selection, destroying the previous word.
///
/// For processes in this catalog we prefer:
///   1. UI Automation read (ValuePattern / TextPattern), then SendInput
///      Backspace+Unicode replacement (no selection, no race).
///   2. If UIA is unavailable → skip auto-correction entirely.
///
/// NOTE: This intentionally does NOT include real Chromium browsers
/// (chrome, msedge, brave, opera, vivaldi). Those are handled by the
/// existing <c>BrowserProcesses</c> path which attempts UIA first and
/// falls back to the (shortened, cancellable) clipboard flow.
/// </summary>
public static class ElectronProcessCatalog
{
    /// <summary>
    /// Process name (no extension, case-insensitive) → Electron app.
    /// Keep names short: <c>Path.GetFileNameWithoutExtension</c>-form.
    /// </summary>
    public static readonly IReadOnlyCollection<string> Processes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Editors / IDEs
            "code",              // VS Code
            "code - insiders",   // VS Code Insiders
            "cursor",            // Cursor
            "windsurf",          // Windsurf
            "atom",              // Atom

            // Chat / collab
            "slack",
            "discord",
            "discordcanary",
            "discordptb",
            "teams",             // Microsoft Teams (legacy Electron)
            "ms-teams",          // Microsoft Teams (new)
            "msteams",
            "element",
            "element-desktop",
            "signal",
            "telegram",          // Telegram Desktop is NOT Electron, but its Qt-based
            "telegram-desktop",  // editor has similar selection race behavior, so we
                                 // treat it the same way. Safe: we only gate clipboard
                                 // fallback / selection behavior here.
            "whatsapp",
            "skype",

            // Productivity
            "notion",
            "obsidian",
            "figma",             // Figma desktop
            "figma-agent",

            // AI / misc
            "codex",
        };

    /// <summary>
    /// True when <paramref name="processName"/> (case-insensitive, no .exe suffix)
    /// is a known Electron-class desktop app where clipboard-selection fallback
    /// is unsafe.
    /// </summary>
    public static bool IsElectronProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        // Normalize: strip .exe if present (defensive — callers usually pass
        // ForegroundContext.ProcessName which is already without extension).
        string name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        return Processes.Contains(name);
    }
}
