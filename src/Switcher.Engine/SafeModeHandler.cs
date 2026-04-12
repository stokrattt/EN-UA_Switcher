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
    private readonly ForegroundContextProvider _contextProvider;
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
    {
        _contextProvider = contextProvider;
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
        var context = _contextProvider.GetCurrent();
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

        var (adapter, support) = _coordinator.Resolve(context);

        if (adapter == null || support == TargetSupport.Unsupported)
        {
            _diagnostics.Log(context.ProcessName, context.FocusedControlClass,
                adapter?.AdapterName ?? "none", false, opType, "", null,
                DiagnosticResult.Unsupported,
                $"No adapter supports this target: class={context.FocusedControlClass}");
            return;
        }

        if (support == TargetSupport.ReadOnly)
        {
            _diagnostics.Log(context.ProcessName, context.FocusedControlClass,
                adapter.AdapterName, true, opType, "", null,
                DiagnosticResult.Unsupported, "Target is read-only");
            return;
        }

        if (isSelection)
            FixSelectedText(context, adapter, opType);
        else
            FixLastWordInTarget(context, adapter, opType);
    }

    private void FixLastWordInTarget(ForegroundContext context, ITextTargetAdapter adapter, OperationType opType)
    {
        string? word = adapter.TryGetLastWord(context);
        if (string.IsNullOrEmpty(word))
        {
            _diagnostics.Log(context.ProcessName, context.FocusedControlClass,
                adapter.AdapterName, true, opType, "", null,
                DiagnosticResult.Skipped, "No word found before caret");
            return;
        }

        var candidate = CorrectionHeuristics.Evaluate(word, CorrectionMode.Safe);
        if (candidate == null)
        {
            _diagnostics.Log(context.ProcessName, context.FocusedControlClass,
                adapter.AdapterName, true, opType, word, null,
                DiagnosticResult.Skipped, $"No conversion candidate: word appears correct in its script");
            return;
        }

        bool success = adapter.TryReplaceLastWord(context, candidate.ConvertedText);
        _diagnostics.Log(context.ProcessName, context.FocusedControlClass,
            adapter.AdapterName, true, opType, candidate.OriginalText, candidate.ConvertedText,
            success ? DiagnosticResult.Replaced : DiagnosticResult.Error,
            success ? candidate.Reason : "Replacement failed");

        if (success)
            NativeMethods.SwitchInputLanguage(context.Hwnd, context.FocusedControlHwnd,
                toUkrainian: candidate.Direction == CorrectionDirection.EnToUa);
    }

    private void FixSelectedText(ForegroundContext context, ITextTargetAdapter adapter, OperationType opType)
    {
        string? selected = adapter.TryGetSelectedText(context);
        if (string.IsNullOrEmpty(selected))
        {
            _diagnostics.Log(context.ProcessName, context.FocusedControlClass,
                adapter.AdapterName, true, opType, "", null,
                DiagnosticResult.Skipped, "No text selected");
            return;
        }

        // Process selection: if it's a single multi-word string, try converting all words
        string converted = ConvertSelection(selected, out int changedCount);

        if (changedCount == 0)
        {
            _diagnostics.Log(context.ProcessName, context.FocusedControlClass,
                adapter.AdapterName, true, opType, selected, null,
                DiagnosticResult.Skipped, "No words in selection needed conversion");
            return;
        }

        bool success = adapter.TryReplaceSelection(context, converted);
        _diagnostics.Log(context.ProcessName, context.FocusedControlClass,
            adapter.AdapterName, true, opType, selected, converted,
            success ? DiagnosticResult.Replaced : DiagnosticResult.Error,
            success ? $"Converted {changedCount} word(s)" : "Replacement failed");

        if (success)
        {
            var script = KeyboardLayoutMap.ClassifyScript(selected);
            NativeMethods.SwitchInputLanguage(context.Hwnd, context.FocusedControlHwnd,
                toUkrainian: script == ScriptType.Latin);
        }
    }

    private static string ConvertSelection(string text, out int changedCount)
    {
        // Split preserving delimiters, evaluate each token
        changedCount = 0;
        var parts = SplitPreservingDelimiters(text);
        var result = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            if (IsWordToken(part))
            {
                var candidate = CorrectionHeuristics.Evaluate(part, CorrectionMode.Safe);
                if (candidate != null)
                {
                    result.Append(candidate.ConvertedText);
                    changedCount++;
                    continue;
                }
            }
            result.Append(part);
        }
        return result.ToString();
    }

    private static List<string> SplitPreservingDelimiters(string text)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inWord = false;

        foreach (char c in text)
        {
            bool isDelim = c is ' ' or '\t' or '\n' or '\r';
            if (inWord && isDelim)
            {
                parts.Add(current.ToString());
                current.Clear();
                inWord = false;
            }
            else if (!inWord && !isDelim)
            {
                if (current.Length > 0) { parts.Add(current.ToString()); current.Clear(); }
                inWord = true;
            }
            current.Append(c);
        }
        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }

    private static bool IsWordToken(string s) =>
        s.Any(c => char.IsLetter(c));
}
