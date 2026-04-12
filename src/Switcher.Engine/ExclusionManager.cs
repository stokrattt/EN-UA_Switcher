using Switcher.Core;

namespace Switcher.Engine;

public class ExclusionManager
{
    private readonly SettingsManager _settings;

    public ExclusionManager(SettingsManager settings)
    {
        _settings = settings;
    }

    public bool IsExcluded(string processName)
    {
        if (string.IsNullOrEmpty(processName)) return false;
        return _settings.Current.ExcludedProcessNames
            .Any(x => string.Equals(x, processName, StringComparison.OrdinalIgnoreCase));
    }
}
