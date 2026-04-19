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

    public bool IsWordExcluded(string word)
    {
        var candidateForms = KeyboardLayoutMap.GetEquivalentLayoutForms(word);
        if (candidateForms.Count == 0)
            return false;

        foreach (string excluded in _settings.Current.ExcludedWords)
        {
            var excludedForms = KeyboardLayoutMap.GetEquivalentLayoutForms(excluded);
            if (excludedForms.Count == 0)
                continue;

            if (candidateForms.Any(form => excludedForms.Contains(form, StringComparer.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }
}
