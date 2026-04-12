using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;

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
    public string AdapterName => "UIAutomationTargetAdapter";

    // Cache: word read by TryGetLastWord, used by TryReplaceLastWord for backspace count
    private string? _lastReadWord;

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

        // Try to get UIA element and check for writable ValuePattern
        IntPtr hwnd = context.FocusedControlHwnd != IntPtr.Zero
            ? context.FocusedControlHwnd
            : context.Hwnd;

        if (hwnd == IntPtr.Zero) return TargetSupport.Unsupported;

        try
        {
            var element = AutomationElement.FocusedElement;
            if (element == null) return TargetSupport.Unsupported;

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
            _ => $"Unsupported: no writable UIA pattern found for class={context.FocusedControlClass}"
        };
    }

    /// <inheritdoc/>
    public string? TryGetLastWord(ForegroundContext context)
    {
        string? value = TryGetValue();
        if (value == null) { _lastReadWord = null; return null; }

        // Get caret position via TextPattern if available
        int caretPos = TryGetCaretPosition() ?? value.Length;
        _lastReadWord = ExtractLastWord(value, caretPos);
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
        // Use cached word from TryGetLastWord for the backspace count.
        // We do NOT re-read from UIA here because the element may be stale.
        string? original = _lastReadWord;
        if (string.IsNullOrEmpty(original)) return false;

        int backCount = original.Length;
        var inputs = new NativeMethods.INPUT[backCount * 2 + replacement.Length * 2];
        int idx = 0;

        for (int i = 0; i < backCount; i++)
        {
            inputs[idx++] = NativeMethods.MakeKeyInput(NativeMethods.VK_BACK, keyUp: false);
            inputs[idx++] = NativeMethods.MakeKeyInput(NativeMethods.VK_BACK, keyUp: true);
        }

        foreach (char c in replacement)
        {
            inputs[idx++] = NativeMethods.MakeUnicodeInput(c, keyUp: false);
            inputs[idx++] = NativeMethods.MakeUnicodeInput(c, keyUp: true);
        }

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs,
            Marshal.SizeOf<NativeMethods.INPUT>());

        _lastReadWord = null;
        return sent == inputs.Length;
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
            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp)) return null;
            return ((ValuePattern)vp).Current.Value;
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

    private static (int start, int end) FindLastWordBounds(string text, int caretPos)
    {
        int end = Math.Min(caretPos, text.Length);
        while (end > 0 && IsDelimiter(text[end - 1])) end--;
        if (end == 0) return (-1, -1);

        int start = end;
        while (start > 0 && !IsDelimiter(text[start - 1])) start--;

        return text[start..end].Length == 0 ? (-1, -1) : (start, end);
    }

    private static bool IsDelimiter(char c) =>
        c is ' ' or '\t' or '\n' or '\r' or '\0';
}
