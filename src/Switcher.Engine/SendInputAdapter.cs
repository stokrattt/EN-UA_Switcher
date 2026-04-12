using System.Runtime.InteropServices;
using Switcher.Infrastructure;

namespace Switcher.Engine;

/// <summary>
/// Universal fallback text adapter that replaces words via Backspace×N + SendInput.
/// Works in any application where Win32/UIA text access is unavailable:
/// Electron, contenteditable, Telegram Desktop, Slack, VS Code, etc.
///
/// Mechanism:
///   1. TryGetLastWord()   → reads the buffered word from KeyboardObserver.CurrentWord
///   2. TryReplaceLastWord() → sends N Backspace keystrokes then types the replacement
///      as Unicode key events (KEYEVENTF_UNICODE). All injected events have
///      LLKHF_INJECTED set by Windows, so the hook ignores them (no feedback loop).
///
/// Priority: lowest — only used when NativeEditTargetAdapter and
/// UIAutomationTargetAdapter both return Unsupported.
/// </summary>
public class SendInputAdapter : ITextTargetAdapter
{
    private readonly KeyboardObserver _observer;

    public string AdapterName => "SendInputAdapter";

    public SendInputAdapter(KeyboardObserver observer)
    {
        _observer = observer;
    }

    /// <inheritdoc/>
    public TargetSupport CanHandle(ForegroundContext context)
    {
        // Universal fallback: always claims Full support.
        // TextTargetCoordinator tries us last, so this only activates when
        // NativeEdit and UIA have both returned Unsupported.
        return TargetSupport.Full;
    }

    /// <inheritdoc/>
    public string DescribeSupport(ForegroundContext context) =>
        "Supported: SendInput fallback (Backspace+Unicode — universal)";

    /// <inheritdoc/>
    public string? TryGetLastWord(ForegroundContext context)
    {
        var word = _observer.CurrentWord;
        return string.IsNullOrEmpty(word) ? null : word;
    }

    /// <inheritdoc/>
    public string? TryGetSelectedText(ForegroundContext context) => null;

    /// <inheritdoc/>
    public bool TryReplaceLastWord(ForegroundContext context, string replacement)
    {
        var current = _observer.CurrentWord;
        if (string.IsNullOrEmpty(current)) return false;

        // Build input sequence: Backspace×N then Unicode chars for replacement
        int backCount = current.Length;
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

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        bool success = sent == inputs.Length;

        if (success)
            _observer.ClearBuffer();

        return success;
    }

    /// <inheritdoc/>
    public bool TryReplaceSelection(ForegroundContext context, string replacement)
    {
        // Selection replacement not supported in SendInput fallback —
        // we don't know the selection bounds without UIA/Win32 access.
        return false;
    }
}
