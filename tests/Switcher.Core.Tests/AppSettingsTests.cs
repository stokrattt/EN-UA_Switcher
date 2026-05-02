using Switcher.Core;

namespace Switcher.Core.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_UseAutoLanguageAndSpaceEnterTabBoundaries()
    {
        var settings = new AppSettings();

        Assert.Equal("Auto", settings.InterfaceLanguage);
        Assert.True(settings.CorrectOnSpace);
        Assert.True(settings.CorrectOnEnter);
        Assert.True(settings.CorrectOnTab);
    }

    [Fact]
    public void Defaults_KeepBrowserAddressBarCorrectionEnabled()
    {
        var settings = new AppSettings();

        Assert.False(settings.DisableBrowserAddressBarAutoCorrection);
    }
}
