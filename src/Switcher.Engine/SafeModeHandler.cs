using Switcher.Core;
using Switcher.Infrastructure;

namespace Switcher.Engine;

/// <summary>
/// Handles safe-mode hotkey actions: fix last word and fix selected text.
/// Uses the adapter selected by <see cref="TextTargetCoordinator"/>.
/// Conservative: if no adapter supports the target, logs diagnostic and does nothing.
/// </summary>
public class SafeModeHandler
{
    private readonly Func<ForegroundContext?> _getContext;
    private readonly TextTargetCoordinator _coordinator;
    private readonly ExclusionManager _exclusions;
    private readonly DiagnosticsLogger _diagnostics;
    private readonly SettingsManager _settings;

    public SafeModeHandler(
        ForegroundContextProvider contextProvider,
        TextTargetCoordinator coordinator,
        ExclusionManager exclusions,
        DiagnosticsLogger diagnostics,
        SettingsManager settings)
        : this(contextProvider.GetCurrent, coordinator, exclusions, diagnostics, settings)
    {
    }

    internal SafeModeHandler(
        Func<ForegroundContext?> getContext,
        TextTargetCoordinator coordinator,
        ExclusionManager exclusions,
        DiagnosticsLogger diagnostics,
        SettingsManager settings)
    {
        _getContext = getContext;
        _coordinator = coordinator;
        _exclusions = exclusions;
        _diagnostics = diagnostics;
        _settings = settings;
    }

    /// <summary>Fix the last word before the caret.</summary>
    public void FixLastWord()
    {
        Execute(OperationType.SafeLastWord, isSelection: false);
    }

    /// <summary>Fix the currently selected text (may be multiple words).</summary>
    public void FixSelection()
    {
        Execute(OperationType.SafeSelection, isSelection: true);
    }

    private void Execute(OperationType opType, bool isSelection)
    {
        var context = _getContext();
        if (context == null)
        {
            _diagnostics.Log("?", "?", "none", false, opType, "", null,
                DiagnosticResult.Error, "Could not get foreground context");
            return;
        }

        if (_exclusions.IsExcluded(context.ProcessName))
        {
            _diagnostics.Log(context.ProcessName, context.FocusedControlClass, "none", false, opType,
                "", null, DiagnosticResult.Skipped, "Process is excluded");
            return;
        }

        var candidates = _coordinator.ResolveCandidates(context);
        if (candidates.Count == 0)
        {
            _diagnostics.Log(context.ProcessName, context.FocusedControlClass,
                "none", false, opType, "", null,
                DiagnosticResult.Unsupported,
                $"No adapter supports this target: class={context.FocusedControlClass}");
            return;
        }

        if (isSelection)
            FixSelectedText(context, candidates, opType);
        else
            FixLastWordInTarget(context, candidates, opType);
    }

    private void FixLastWordInTarget(
        ForegroundContext context,
        IReadOnlyList<(ITextTargetAdapter Adapter, TargetSupport Support)> candidates,
        OperationType opType)
    {
        bool sawReadOnly = false;
        string? lastFailureReason = null;
        string lastAdapterName = "none";

        foreach (var (adapter, support) in candidates)
        {
            if (support == TargetSupport.ReadOnly)
            {
                sawReadOnly = true;
                continue;
            }

            string? word = adapter.TryGetLastWord(context);
            if (string.IsNullOrEmpty(word))
            {
                lastFailureReason = "No word found before caret";
                lastAdapterName = adapter.AdapterName;
                continue;
            }

            string converted = KeyboardLayoutMap.ToggleLayoutText(word, out int changedCount);
            if (changedCount == 0 || string.Equals(converted, word, StringComparison.Ordinal))
            {
                _diagnostics.Log(context.ProcessName, context.FocusedControlClass,
                    adapter.AdapterName, true, opType, word, null,
                    DiagnosticResult.Skipped, "No mappable EN/UA layout characters found");
                return;
            }

            bool success = adapter.TryReplaceLastWord(context, converted);
            if (success)
            {
                _diagnostics.Log(context.ProcessName, context.FocusedControlClass,
                    adapter.AdapterName, true, opType, word, converted,
                    DiagnosticResult.Replaced, $"Layout toggle converted {changedCount} char(s)");

                SwitchInputLanguageForConvertedText(context, converted);
                return;
            }

            lastFailureReason = "Replacement failed";
            lastAdapterName = adapter.AdapterName;
        }

        if (sawReadOnly && lastFailureReason == null)
        {
            _diagnostics.Log(context.ProcessName, context.FocusedControlClass,
                candidates[0].Adapter.AdapterName, true, opType, "", null,
                DiagnosticResult.Unsupported, "Target is read-only");
            return;
        }

        _diagnostics.Log(context.ProcessName, context.FocusedControlClass,
            lastAdapterName, true, opType, "", null,
            lastFailureReason == "Replacement failed" ? DiagnosticResult.Error : DiagnosticResult.Skipped,
            lastFailureReason ?? "No word found before caret");
    }

    private void FixSelectedText(
        ForegroundContext context,
        IReadOnlyList<(ITextTargetAdapter Adapter, TargetSupport Support)> candidates,
        OperationType opType)
    {
        bool sawReadOnly = false;
        string? lastFailureReason = null;
        string lastAdapterName = "none";

        foreach (var (adapter, support) in candidates)
        {
            if (support == TargetSupport.ReadOnly)
            {
                sawReadOnly = true;
                continue;
            }

            string? selected = adapter.TryGetSelectedText(context);
            if (string.IsNullOrEmpty(selected))
            {
                lastFailureReason = "No text selected";
                lastAdapterName = adapter.AdapterName;
                continue;
            }

            string converted = KeyboardLayoutMap.ToggleLayoutText(selected, out int changedCount);
            if (changedCount == 0 || string.Equals(converted, selected, StringComparison.Ordinal))
            {
                _diagnostics.Log(context.ProcessName, context.FocusedControlClass,
                    adapter.AdapterName, true, opType, selected, null,
                    DiagnosticResult.Skipped, "No mappable EN/UA layout characters found");
                return;
            }

            bool success = adapter.TryReplaceSelection(context, converted);
            if (success)
            {
                _diagnostics.Log(context.ProcessName, context.FocusedControlClass,
                    adapter.AdapterName, true, opType, selected, converted,
                    DiagnosticResult.Replaced, $"Layout toggle converted {changedCount} char(s)");

                SwitchInputLanguageForConvertedText(context, converted);
                return;
            }

            lastFailureReason = "Replacement failed";
            lastAdapterName = adapter.AdapterName;
        }

        if (sawReadOnly && lastFailureReason == null)
        {
            _diagnostics.Log(context.ProcessName, context.FocusedControlClass,
                candidates[0].Adapter.AdapterName, true, opType, "", null,
                DiagnosticResult.Unsupported, "Target is read-only");
            return;
        }

        _diagnostics.Log(context.ProcessName, context.FocusedControlClass,
            lastAdapterName, true, opType, "", null,
            lastFailureReason == "Replacement failed" ? DiagnosticResult.Error : DiagnosticResult.Skipped,
            lastFailureReason ?? "No text selected");
    }

    private static void SwitchInputLanguageForConvertedText(ForegroundContext context, string convertedText)
    {
        foreach (char c in convertedText.Reverse())
        {
            if (!char.IsLetter(c))
                continue;

            var script = KeyboardLayoutMap.ClassifyScript(c.ToString());
            if (script == ScriptType.Latin)
            {
                NativeMethods.SwitchInputLanguage(context.Hwnd, context.FocusedControlHwnd, toUkrainian: false);
                return;
            }
            if (script == ScriptType.Cyrillic)
            {
                NativeMethods.SwitchInputLanguage(context.Hwnd, context.FocusedControlHwnd, toUkrainian: true);
                return;
            }
        }
    }
}
