namespace Switcher.Infrastructure;

public enum TargetSupport { Full, ReadOnly, Unsupported }

public interface ITextTargetAdapter
{
    string AdapterName { get; }

    /// <summary>Determines whether this adapter can handle the given context.</summary>
    TargetSupport CanHandle(ForegroundContext context);

    /// <summary>Human-readable description of support for diagnostics.</summary>
    string DescribeSupport(ForegroundContext context);

    /// <summary>
    /// Reads the last word before the caret position.
    /// Returns null if reading is not possible or no word is found.
    /// </summary>
    string? TryGetLastWord(ForegroundContext context);

    /// <summary>
    /// Reads the currently selected text.
    /// Returns null if no selection or reading is not possible.
    /// </summary>
    string? TryGetSelectedText(ForegroundContext context);

    /// <summary>
    /// Reads the current sentence around the caret position.
    /// Returns null if reading is not possible or no sentence is found.
    /// </summary>
    string? TryGetCurrentSentence(ForegroundContext context);

    /// <summary>
    /// Replaces the last word with <paramref name="replacement"/>.
    /// Returns false if not supported or operation failed.
    /// Must NEVER corrupt or lose existing text.
    /// </summary>
    bool TryReplaceLastWord(ForegroundContext context, string replacement);

    /// <summary>
    /// Replaces the current selection with <paramref name="replacement"/>.
    /// Returns false if not supported or operation failed.
    /// Must NEVER corrupt or lose existing text.
    /// </summary>
    bool TryReplaceSelection(ForegroundContext context, string replacement);

    /// <summary>
    /// Replaces the current sentence around the caret with <paramref name="replacement"/>.
    /// Returns false if not supported or operation failed.
    /// Must NEVER corrupt or lose existing text.
    /// </summary>
    bool TryReplaceCurrentSentence(ForegroundContext context, string replacement);
}
