using System.Text;

namespace Switcher.Infrastructure;

/// <summary>
/// Text target adapter for classic Win32 EDIT and RichEdit controls.
/// Uses direct Win32 messages (EM_GETSEL, EM_SETSEL, EM_REPLACESEL, WM_GETTEXT).
/// This is the most reliable adapter — works for Notepad, WordPad, classic apps.
/// </summary>
public class NativeEditTargetAdapter : ITextTargetAdapter
{
    public string AdapterName => "NativeEditTargetAdapter";

    private static readonly HashSet<string> SupportedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Edit", "RichEdit", "RichEdit20A", "RichEdit20W", "RichEdit50W",
        "MSFTEDIT_CLASS", "RICHEDIT50W", "RichEditD2DPT"
    };

    /// <inheritdoc/>
    public TargetSupport CanHandle(ForegroundContext context)
    {
        string cls = context.FocusedControlClass;
        if (string.IsNullOrEmpty(cls))
            cls = context.WindowClass;
        return IsSupportedClass(cls) ? TargetSupport.Full : TargetSupport.Unsupported;
    }

    /// <inheritdoc/>
    public string DescribeSupport(ForegroundContext context)
    {
        string cls = context.FocusedControlClass;
        if (string.IsNullOrEmpty(cls)) cls = context.WindowClass;
        bool supported = IsSupportedClass(cls);
        return supported
            ? $"Supported: native Win32 EDIT/RichEdit control ({cls})"
            : $"Not supported: class={cls} is not a native edit control";
    }

    /// <inheritdoc/>
    public string? TryGetLastWord(ForegroundContext context)
    {
        IntPtr hwnd = GetEditHwnd(context);
        if (hwnd == IntPtr.Zero) return null;

        string? fullText = GetFullText(hwnd);
        if (fullText == null) return null;

        int caretPos = GetCaretPosition(hwnd);
        if (caretPos < 0) caretPos = fullText.Length;

        return ExtractLastWord(fullText, caretPos);
    }

    /// <inheritdoc/>
    public string? TryGetSelectedText(ForegroundContext context)
    {
        IntPtr hwnd = GetEditHwnd(context);
        if (hwnd == IntPtr.Zero) return null;

        string? fullText = GetFullText(hwnd);
        if (fullText == null) return null;

        GetSelection(hwnd, out int start, out int end);
        if (start >= end) return null;
        if (end > fullText.Length) end = fullText.Length;

        return fullText[start..end];
    }

    /// <inheritdoc/>
    public string? TryGetCurrentSentence(ForegroundContext context)
    {
        IntPtr hwnd = GetEditHwnd(context);
        if (hwnd == IntPtr.Zero) return null;

        string? fullText = GetFullText(hwnd);
        if (fullText == null) return null;

        int caretPos = GetCaretPosition(hwnd);
        if (caretPos < 0) caretPos = fullText.Length;

        (int start, int end) = FindSentenceBounds(fullText, caretPos);
        return start < 0 ? null : fullText[start..end];
    }

    /// <inheritdoc/>
    public bool TryReplaceLastWord(ForegroundContext context, string replacement)
    {
        IntPtr hwnd = GetEditHwnd(context);
        if (hwnd == IntPtr.Zero) return false;

        string? fullText = GetFullText(hwnd);
        if (fullText == null) return false;

        int caretPos = GetCaretPosition(hwnd);
        if (caretPos < 0) caretPos = fullText.Length;

        (int wordStart, int wordEnd) = FindLastWordBounds(fullText, caretPos);
        if (wordStart < 0) return false;

        // Select the word and replace
        NativeMethods.SendMessage(hwnd, NativeMethods.EM_SETSEL, (IntPtr)wordStart, (IntPtr)wordEnd);
        NativeMethods.SendMessage(hwnd, NativeMethods.EM_REPLACESEL, (IntPtr)1, replacement);
        return true;
    }

    /// <inheritdoc/>
    public bool TryReplaceSelection(ForegroundContext context, string replacement)
    {
        IntPtr hwnd = GetEditHwnd(context);
        if (hwnd == IntPtr.Zero) return false;

        GetSelection(hwnd, out int start, out int end);
        if (start >= end) return false;

        NativeMethods.SendMessage(hwnd, NativeMethods.EM_REPLACESEL, (IntPtr)1, replacement);
        return true;
    }

    /// <inheritdoc/>
    public bool TryReplaceCurrentSentence(ForegroundContext context, string replacement)
    {
        IntPtr hwnd = GetEditHwnd(context);
        if (hwnd == IntPtr.Zero) return false;

        string? fullText = GetFullText(hwnd);
        if (fullText == null) return false;

        int caretPos = GetCaretPosition(hwnd);
        if (caretPos < 0) caretPos = fullText.Length;

        (int start, int end) = FindSentenceBounds(fullText, caretPos);
        if (start < 0) return false;

        NativeMethods.SendMessage(hwnd, NativeMethods.EM_SETSEL, (IntPtr)start, (IntPtr)end);
        NativeMethods.SendMessage(hwnd, NativeMethods.EM_REPLACESEL, (IntPtr)1, replacement);
        return true;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static IntPtr GetEditHwnd(ForegroundContext context)
    {
        if (context.FocusedControlHwnd != IntPtr.Zero &&
            IsSupportedClass(context.FocusedControlClass))
            return context.FocusedControlHwnd;

        if (IsSupportedClass(context.WindowClass))
            return context.Hwnd;

        return IntPtr.Zero;
    }

    private static bool IsSupportedClass(string? cls)
    {
        if (string.IsNullOrWhiteSpace(cls))
            return false;

        if (SupportedClasses.Contains(cls))
            return true;

        return cls.StartsWith("WindowsForms10.EDIT", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetFullText(IntPtr hwnd)
    {
        int length = (int)NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        if (length <= 0) return string.Empty;

        var sb = new StringBuilder(length + 2);
        int got = (int)NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETTEXT, (IntPtr)(length + 1), sb);
        return got >= 0 ? sb.ToString() : null;
    }

    private static int GetCaretPosition(IntPtr hwnd)
    {
        NativeMethods.SendMessage(hwnd, NativeMethods.EM_GETSEL, out int start, out int end);
        return end; // caret is at end of selection
    }

    private static void GetSelection(IntPtr hwnd, out int start, out int end)
    {
        NativeMethods.SendMessage(hwnd, NativeMethods.EM_GETSEL, out start, out end);
    }

    /// <summary>
    /// Extracts the last word before <paramref name="caretPos"/> in <paramref name="text"/>.
    /// A "word" is a sequence of letter/digit characters. Stops at any whitespace or punctuation.
    /// </summary>
    private static string? ExtractLastWord(string text, int caretPos)
    {
        (int start, int end) = FindLastWordBounds(text, caretPos);
        if (start < 0) return null;
        return text[start..end];
    }

    private static (int start, int end) FindLastWordBounds(string text, int caretPos)
    {
        // Move back from caret, skip trailing punctuation attached to word
        int end = caretPos;
        // Allow punctuation as part of word (e.g. trailing comma stays as suffix in heuristics)
        // Find word end: walk back from caret to find first non-delimiter
        while (end > 0 && IsDelimiter(text[end - 1]))
            end--;

        if (end == 0) return (-1, -1);

        int start = end;
        while (start > 0 && !IsDelimiter(text[start - 1]))
            start--;

        string word = text[start..end];
        if (word.Length == 0) return (-1, -1);

        // For replacement, include trailing punctuation that is part of the word token
        // (handle by including up to caretPos if adjacent chars are punctuation)
        return (start, end);
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
}
