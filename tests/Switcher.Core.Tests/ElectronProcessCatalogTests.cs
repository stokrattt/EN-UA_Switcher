using Switcher.Core;
using Xunit;

namespace Switcher.Core.Tests;

public class ElectronProcessCatalogTests
{
    // ─── Known Electron apps ────────────────────────────────────────────────

    [Theory]
    [InlineData("code")]
    [InlineData("Code")]
    [InlineData("CODE")]
    [InlineData("cursor")]
    [InlineData("windsurf")]
    [InlineData("vscodium")]
    [InlineData("slack")]
    [InlineData("Slack")]
    [InlineData("discord")]
    [InlineData("discordcanary")]
    [InlineData("discordptb")]
    [InlineData("teams")]
    [InlineData("ms-teams")]
    [InlineData("msteams")]
    [InlineData("element")]
    [InlineData("element-desktop")]
    [InlineData("signal")]
    [InlineData("telegram")]
    [InlineData("telegram-desktop")]
    [InlineData("whatsapp")]
    [InlineData("skype")]
    [InlineData("obsidian")]
    [InlineData("notion")]
    [InlineData("figma")]
    [InlineData("spotify")]
    public void IsElectronProcess_ReturnsTrue_ForKnownElectronApps(string processName)
    {
        Assert.True(ElectronProcessCatalog.IsElectronProcess(processName),
            $"Expected '{processName}' to be recognized as Electron.");
    }

    // ─── .exe suffix normalization ──────────────────────────────────────────

    [Theory]
    [InlineData("code.exe")]
    [InlineData("Slack.EXE")]
    [InlineData("discord.exe")]
    [InlineData("TEAMS.EXE")]
    public void IsElectronProcess_StripsExeSuffix(string processName)
    {
        Assert.True(ElectronProcessCatalog.IsElectronProcess(processName),
            $"Expected '{processName}' to be recognized after stripping .exe.");
    }

    // ─── Real browsers must NOT be in the catalog ───────────────────────────

    [Theory]
    [InlineData("chrome")]
    [InlineData("msedge")]
    [InlineData("brave")]
    [InlineData("opera")]
    [InlineData("vivaldi")]
    [InlineData("firefox")]
    public void IsElectronProcess_ReturnsFalse_ForRealBrowsers(string processName)
    {
        Assert.False(ElectronProcessCatalog.IsElectronProcess(processName),
            $"Real browser '{processName}' must NOT be in ElectronProcessCatalog.");
    }

    // ─── Non-Electron desktop apps ──────────────────────────────────────────

    [Theory]
    [InlineData("notepad")]
    [InlineData("explorer")]
    [InlineData("winword")]
    [InlineData("excel")]
    [InlineData("devenv")]
    [InlineData("rider")]
    public void IsElectronProcess_ReturnsFalse_ForNativeDesktopApps(string processName)
    {
        Assert.False(ElectronProcessCatalog.IsElectronProcess(processName),
            $"'{processName}' is a native app and must NOT be in ElectronProcessCatalog.");
    }

    // ─── Edge cases ─────────────────────────────────────────────────────────

    [Fact]
    public void IsElectronProcess_ReturnsFalse_ForNull()
        => Assert.False(ElectronProcessCatalog.IsElectronProcess(null));

    [Fact]
    public void IsElectronProcess_ReturnsFalse_ForEmptyString()
        => Assert.False(ElectronProcessCatalog.IsElectronProcess(string.Empty));

    [Fact]
    public void IsElectronProcess_ReturnsFalse_ForWhitespace()
        => Assert.False(ElectronProcessCatalog.IsElectronProcess("   "));

    // ─── Catalog integrity ──────────────────────────────────────────────────

    [Fact]
    public void Processes_ContainsExpectedCoreEntries()
    {
        var processes = ElectronProcessCatalog.Processes;
        Assert.Contains("code", processes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("slack", processes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("discord", processes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("obsidian", processes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("teams", processes, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Processes_DoesNotContainRealBrowsers()
    {
        var processes = ElectronProcessCatalog.Processes;
        Assert.DoesNotContain("chrome", processes, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("msedge", processes, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("brave", processes, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("opera", processes, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("vivaldi", processes, StringComparer.OrdinalIgnoreCase);
    }
}
