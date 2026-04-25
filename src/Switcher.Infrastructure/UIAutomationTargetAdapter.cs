using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Switcher.Core;

namespace Switcher.Infrastructure;

/// <summary>
/// Text target adapter using UI Automation ValuePattern/TextPattern.
/// Works for browser inputs/textareas that expose ValuePattern (Chrome, Edge with accessibility enabled).
///
/// KNOWN LIMITATIONS:
/// - SetValue replaces the entire field value (cursor position resets to end).
/// - contenteditable elements do NOT expose ValuePattern → marked Unsupported.
/// - Monaco Editor, CodeMirror, custom web editors → Unsupported.
/// - Chrome requires accessibility to be enabled (happens automatically when UIA client connects).
/// - TextPattern in browsers is often read-only; we use ValuePattern for write operations.
/// </summary>
public class UIAutomationTargetAdapter : ITextTargetAdapter
{
    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "brave", "opera", "vivaldi"
    };

    private static readonly string[] BrowserAddressBarNameMarkers =
    [
        "address and search bar",
        "address bar",
        "search or enter address",
        "search with google or enter address",
        "адресний рядок",
        "адресная строка"
    ];

    public string AdapterName => "UIAutomationTargetAdapter";

    // Cache: last UIA read, used by TryReplaceLastWord to replace the exact slice
    private string? _lastReadWord;
    private string? _lastReadValue;
    private int _lastWordStart = -1;
    private int _lastWordEnd = -1;

    // Classes that indicate browser/modern app windows (not native edit)
    private static readonly HashSet<string> BrowserWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Chrome_WidgetWin_1", "MozillaWindowClass", "Chrome_RenderWidgetHostHWND",
        "ApplicationFrameWindow", "Windows.UI.Core.CoreWindow"
    };

    // Native edit classes — handled by NativeEditTargetAdapter, not us
    private static readonly HashSet<string> NativeEditClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Edit", "RichEdit", "RichEdit20A", "RichEdit20W", "RichEdit50W",
        "MSFTEDIT_CLASS", "RICHEDIT50W", "RichEditD2DPT"
    };

    /// <inheritdoc/>
    public TargetSupport CanHandle(ForegroundContext context)
    {
        string cls = context.FocusedControlClass;
        if (string.IsNullOrEmpty(cls)) cls = context.WindowClass;

        // Don't compete with native edit adapter
        if (NativeEditClasses.Contains(cls)) return TargetSupport.Unsupported;

        // If it's explicitly an unsafe custom editor (determined earlier by AutoModeHandler), it should've been routed elsewhere,
        // but if it arrives here, we evaluate it directly on whether it has a writable ValuePattern.
        IntPtr hwnd = context.FocusedControlHwnd != IntPtr.Zero
            ? context.FocusedControlHwnd
            : context.Hwnd;

        if (hwnd == IntPtr.Zero) return TargetSupport.Unsupported;

        try
        {
            var element = AutomationElement.FocusedElement;
            if (element == null) return TargetSupport.Unsupported;
            if (LooksLikeBrowserAddressBar(
                    context.ProcessName,
                    element.Current.ClassName ?? string.Empty,
                    element.Current.AutomationId ?? string.Empty,
                    element.Current.Name ?? string.Empty))
                return TargetSupport.Unsupported;

            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp))
            {
                var valuePattern = (ValuePattern)vp;
                return valuePattern.Current.IsReadOnly
                    ? TargetSupport.ReadOnly
                    : TargetSupport.Full;
            }

            // TextPattern alone (no ValuePattern) → read-only for our purposes
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out _))
                return TargetSupport.ReadOnly;

            return TargetSupport.Unsupported;
        }
        catch
        {
            return TargetSupport.Unsupported;
        }
    }

    /// <inheritdoc/>
    public string DescribeSupport(ForegroundContext context)
    {
        var support = CanHandle(context);
        return support switch
        {
            TargetSupport.Full => "Supported: UIA ValuePattern (writable)",
            TargetSupport.ReadOnly => "Read-only: UIA ValuePattern or TextPattern (cannot write)",
            _ => $"Unsupported: process={context.ProcessName}, class={context.FocusedControlClass}"
        };
    }

    /// <inheritdoc/>
    public string? TryGetLastWord(ForegroundContext context)
    {
        string? value = TryGetValue();
        if (value == null)
        {
            ClearLastWordCache();
            return null;
        }

        // Get caret position via TextPattern if available
        int caretPos = TryGetCaretPosition() ?? value.Length;
        (int start, int end) = FindLastWordBounds(value, caretPos);
        if (start < 0)
        {
            ClearLastWordCache();
            return null;
        }

        _lastReadValue = value;
        _lastWordStart = start;
        _lastWordEnd = end;
        _lastReadWord = value[start..end];
        return _lastReadWord;
    }

    /// <inheritdoc/>
    public string? TryGetSelectedText(ForegroundContext context)
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element == null) return null;

            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? tp))
            {
                var textPattern = (TextPattern)tp;
                var ranges = textPattern.GetSelection();
                if (ranges.Length > 0)
                {
                    string selected = ranges[0].GetText(10000);
                    return string.IsNullOrEmpty(selected) ? null : selected;
                }
            }

            // Fallback: try to get selection from value by comparing with known selection range
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public bool TryReplaceLastWord(ForegroundContext context, string replacement)
    {
        string? original = _lastReadWord;
        string? cachedValue = _lastReadValue;
        if (string.IsNullOrEmpty(original) || cachedValue == null || _lastWordStart < 0 || _lastWordEnd < _lastWordStart)
            return false;

        try
        {
            var element = AutomationElement.FocusedElement;
            if (element == null) return false;
            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp)) return false;

            var valuePattern = (ValuePattern)vp;
            if (valuePattern.Current.IsReadOnly) return false;

            string? currentValue = valuePattern.Current.Value;
            if (currentValue == null) return false;
            int currentCaretPos = TryGetCaretPosition() ?? currentValue.Length;

            if (!TryBuildReplacementValue(
                    currentValue,
                    cachedValue,
                    original,
                    _lastWordStart,
                    _lastWordEnd,
                    replacement,
                    currentCaretPos,
                    out string newValue,
                    out int targetCaretIndex))
                return false;
            valuePattern.SetValue(newValue);
            RestoreCaretPosition(newValue.Length, targetCaretIndex);
            ClearLastWordCache();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public string? TryGetCurrentSentence(ForegroundContext context)
    {
        string? value = TryGetValue();
        if (value == null) return null;

        int caretPos = TryGetCaretPosition() ?? value.Length;
        (int start, int end) = FindSentenceBounds(value, caretPos);
        return start < 0 ? null : value[start..end];
    }

    /// <inheritdoc/>
    public bool TryReplaceSelection(ForegroundContext context, string replacement)
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element == null) return false;
            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp)) return false;

            var valuePattern = (ValuePattern)vp;
            if (valuePattern.Current.IsReadOnly) return false;

            string? fullValue = valuePattern.Current.Value;
            if (fullValue == null) return false;

            // Get selection via TextPattern
            int selStart = -1, selEnd = -1;
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? tp))
            {
                // Try to determine selection offsets — TextPattern ranges don't expose offsets directly
                // so we use a heuristic: find the selected text in the value
                var textPattern = (TextPattern)tp;
                var ranges = textPattern.GetSelection();
                if (ranges.Length > 0)
                {
                    string selectedText = ranges[0].GetText(10000);
                    if (!string.IsNullOrEmpty(selectedText))
                    {
                        // Find last occurrence before caret
                        int idx = fullValue.LastIndexOf(selectedText, StringComparison.Ordinal);
                        if (idx >= 0)
                        {
                            selStart = idx;
                            selEnd = idx + selectedText.Length;
                        }
                    }
                }
            }

            if (selStart < 0) return false;

            string newValue = fullValue[..selStart] + replacement + fullValue[selEnd..];
            valuePattern.SetValue(newValue);
            RestoreCaretPosition(newValue.Length, selStart + replacement.Length);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string? TryGetValue()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element == null) return null;

            // Strategy 1: ValuePattern (most browsers)
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp))
                return ((ValuePattern)vp).Current.Value;

            // Strategy 2: TextPattern (backup for some Electron/WPF editors)
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? tp))
                return ((TextPattern)tp).DocumentRange.GetText(-1);

            return null;
        }
        catch { return null; }
    }

    private static int? TryGetCaretPosition()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element == null) return null;
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out object? tp)) return null;

            var textPattern = (TextPattern)tp;
            // DocumentRange start to selection start = characters before caret
            var docRange = textPattern.DocumentRange;
            var selections = textPattern.GetSelection();
            if (selections.Length == 0) return null;

            var beforeCaret = docRange.Clone();
            beforeCaret.MoveEndpointByRange(
                TextPatternRangeEndpoint.End,
                selections[0],
                TextPatternRangeEndpoint.Start);

            string before = beforeCaret.GetText(int.MaxValue);
            return before.Length;
        }
        catch { return null; }
    }

    private static string? ExtractLastWord(string text, int caretPos)
    {
        (int start, int end) = FindLastWordBounds(text, caretPos);
        if (start < 0) return null;
        return text[start..end];
    }

    private static bool TryBuildReplacementValue(
        string currentValue,
        string cachedValue,
        string original,
        int cachedStart,
        int cachedEnd,
        string replacement,
        int currentCaretPos,
        out string newValue,
        out int targetCaretIndex)
    {
        newValue = currentValue;
        targetCaretIndex = -1;

        if (string.IsNullOrEmpty(original) || cachedStart < 0 || cachedEnd < cachedStart)
            return false;

        int start = cachedStart;
        int end = cachedEnd;

        // If the field changed after read, relocate to the latest exact match first.
        if (!string.Equals(currentValue, cachedValue, StringComparison.Ordinal))
        {
            int idx = currentValue.LastIndexOf(original, StringComparison.Ordinal);
            if (idx >= 0)
            {
                start = idx;
                end = idx + original.Length;
            }
            else
            {
                int growth = Math.Max(0, currentValue.Length - cachedValue.Length);
                int relocatedEnd = Math.Min(currentValue.Length, cachedStart + original.Length + growth);
                int relocatedStart = Math.Max(0, relocatedEnd - original.Length);
                start = relocatedStart;
                end = relocatedEnd;
            }
        }

        if (start < 0 || end < start || end > currentValue.Length)
            return false;

        string currentSlice = currentValue[start..end];
        if (!string.Equals(currentSlice, original, StringComparison.Ordinal))
            return false;

        newValue = currentValue[..start] + replacement + currentValue[end..];

        int newEnd = start + replacement.Length;
        int tailOffset = Math.Max(0, Math.Min(currentValue.Length, currentCaretPos) - end);
        targetCaretIndex = Math.Min(newValue.Length, newEnd + tailOffset);
        return true;
    }

    private static void RestoreCaretPosition(int textLength, int targetCaretIndex)
    {
        if (targetCaretIndex < 0 || targetCaretIndex > textLength)
            return;

        if (TryRestoreCaretViaTextPattern(targetCaretIndex))
            return;

        var inputs = BuildCaretRestoreInputs(textLength, targetCaretIndex);
        if (inputs.Length == 0)
            return;

        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static bool TryRestoreCaretViaTextPattern(int targetCaretIndex)
    {
        // TextPattern.Select() is unreliable in Electron apps (Telegram, VS Code, etc.) —
        // it can misplace the caret by 1+ positions causing the delimiter (Space) to land
        // mid-word. Always fall through to BuildCaretRestoreInputs (Ctrl+End + Left×N).
        _ = targetCaretIndex;
        return false;
    }

    /// <inheritdoc/>
    public bool TryReplaceCurrentSentence(ForegroundContext context, string replacement)
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element == null) return false;
            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp)) return false;

            var valuePattern = (ValuePattern)vp;
            if (valuePattern.Current.IsReadOnly) return false;

            string? fullValue = valuePattern.Current.Value;
            if (fullValue == null) return false;

            int caretPos = TryGetCaretPosition() ?? fullValue.Length;
            (int start, int end) = FindSentenceBounds(fullValue, caretPos);
            if (start < 0) return false;

            string newValue = fullValue[..start] + replacement + fullValue[end..];
            valuePattern.SetValue(newValue);
            RestoreCaretPosition(newValue.Length, start + replacement.Length);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static NativeMethods.INPUT[] BuildCaretRestoreInputs(int textLength, int targetCaretIndex)
    {
        if (targetCaretIndex < 0 || targetCaretIndex > textLength)
            return [];

        int moveLeftCount = textLength - targetCaretIndex;
        var inputs = new List<NativeMethods.INPUT>(NativeMethods.BuildModifierReleaseInputs().Length + 4 + moveLeftCount * 2);

        inputs.AddRange(NativeMethods.BuildModifierReleaseInputs());

        // Anchor at the absolute end of the field, then walk left to the desired caret.
        inputs.Add(NativeMethods.MakeKeyInput(NativeMethods.VK_CONTROL, keyUp: false));
        inputs.Add(NativeMethods.MakeExtKeyInput(NativeMethods.VK_END, keyUp: false));
        inputs.Add(NativeMethods.MakeExtKeyInput(NativeMethods.VK_END, keyUp: true));
        inputs.Add(NativeMethods.MakeKeyInput(NativeMethods.VK_CONTROL, keyUp: true));

        for (int i = 0; i < moveLeftCount; i++)
        {
            inputs.Add(NativeMethods.MakeExtKeyInput(NativeMethods.VK_LEFT, keyUp: false));
            inputs.Add(NativeMethods.MakeExtKeyInput(NativeMethods.VK_LEFT, keyUp: true));
        }

        return inputs.ToArray();
    }

    private static (int start, int end) FindLastWordBounds(string text, int caretPos)
    {
        int end = Math.Min(caretPos, text.Length);
        while (end > 0 && IsDelimiter(text[end - 1])) end--;
        if (end == 0) return (-1, -1);

        int start = end;
        while (start > 0 && !IsDelimiter(text[start - 1])) start--;

        return text[start..end].Length == 0 ? (-1, -1) : (start, end);
    }

    private static (int start, int end) FindSentenceBounds(string text, int caretPos)
    {
        if (string.IsNullOrEmpty(text))
            return (-1, -1);

        int caret = Math.Clamp(caretPos, 0, text.Length);
        int start = caret;
        while (start > 0 && !IsSentenceBoundary(text[start - 1]))
            start--;
        while (start < text.Length && char.IsWhiteSpace(text[start]))
            start++;

        int end = caret;
        while (end < text.Length && !IsSentenceBoundary(text[end]))
            end++;
        while (end > start && char.IsWhiteSpace(text[end - 1]))
            end--;

        return end > start ? (start, end) : (-1, -1);
    }

    private static bool IsDelimiter(char c) =>
        c is ' ' or '\t' or '\n' or '\r' or '\0';

    private static bool IsSentenceBoundary(char c) =>
        c is '.' or '!' or '?' or '\n' or '\r';

    private void ClearLastWordCache()
    {
        _lastReadWord = null;
        _lastReadValue = null;
        _lastWordStart = -1;
        _lastWordEnd = -1;
    }

    private static bool LooksLikeBrowserAddressBar(
        string? processName,
        string className,
        string automationId,
        string elementName)
    {
        if (string.IsNullOrWhiteSpace(processName)
            || !BrowserProcesses.Contains(processName.Trim()))
        {
            return false;
        }

        string merged = $"{className} {automationId} {elementName}".ToLowerInvariant();
        if (merged.Contains("omnibox", StringComparison.Ordinal))
            return true;

        string normalizedName = (elementName ?? string.Empty).Trim().ToLowerInvariant();
        return BrowserAddressBarNameMarkers.Any(marker => string.Equals(normalizedName, marker, StringComparison.Ordinal));
    }
}
