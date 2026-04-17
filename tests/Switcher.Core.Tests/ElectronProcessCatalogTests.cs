using Switcher.Core;

namespace Switcher.Core.Tests;

public class ElectronProcessCatalogTests
{
    [Theory]
    [InlineData("code")]
    [InlineData("Code")]
    [InlineData("CODE")]
    [InlineData("slack")]
    [InlineData("Slack")]
    [InlineData("discord")]
    [InlineData("teams")]
    [InlineData("ms-teams")]
    [InlineData("obsidian")]
    [InlineData("element")]
    [InlineData("element-desktop")]
    [InlineData("notion")]
    [InlineData("figma")]
    [InlineData("telegram")]
    [InlineData("whatsapp")]
    public void IsElectronProcess_ReturnsTrue_ForKnownElectronApps(string processName)
    {
        Assert.True(ElectronProcessCatalog.IsElectronProcess(processName),
            $"Expected '{processName}' to be recognized as Electron.");
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("msedge")]
    [InlineData("brave")]
    [InlineData("firefox")]
    [InlineData("notepad")]
    [InlineData("explorer")]
    [InlineData("winword")]
    [InlineData("excel")]
    [InlineData("")]
    public void IsElectronProcess_ReturnsFalse_ForNonElectronApps(string processName)
    {
        Assert.False(ElectronProcessCatalog.IsElectronProcess(processName),
            $"Expected '{processName}' NOT to be recognized as Electron.");
    }

    [Fact]
    public void IsElectronProcess_ReturnsFalse_ForNull()
    {
        Assert.False(ElectronProcessCatalog.IsElectronProcess(null));
    }

    [Fact]
    public void IsElectronProcess_ReturnsFalse_ForWhitespace()
    {
        Assert.False(ElectronProcessCatalog.IsElectronProcess("   "));
    }

    [Theory]
    [InlineData("code.exe")]
    [InlineData("Slack.EXE")]
    [InlineData("discord.exe")]
    public void IsElectronProcess_StripsExeSuffix(string processName)
    {
        Assert.True(ElectronProcessCatalog.IsElectronProcess(processName),
            $"Expected '{processName}' to be recognized after stripping .exe.");
    }

    [Fact]
    public void Processes_ContainsExpectedCoreEntries()
    {
        var processes = ElectronProcessCatalog.Processes;
        Assert.Contains("code", processes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("slack", processes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("discord", processes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("obsidian", processes, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Processes_DoesNotContainRealBrowsers()
    {
        // Real Chromium browsers are handled by AutoModeHandler.BrowserProcesses,
        // not by the Electron catalog. Double-listing them would send real
        // browsers down the Electron-skip path and suppress valid corrections.
        var processes = ElectronProcessCatalog.Processes;
        Assert.DoesNotContain("chrome", processes, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("msedge", processes, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("brave", processes, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("opera", processes, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("vivaldi", processes, StringComparer.OrdinalIgnoreCase);
    }
}
