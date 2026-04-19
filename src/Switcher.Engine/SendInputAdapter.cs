using System.Runtime.InteropServices;
using System.Threading;
using Switcher.Core;
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
    private static readonly HashSet<string> WordSelectionClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Chrome_RenderWidgetHostHWND",
        "Chrome_WidgetWin_1",
        "MozillaWindowClass",
        "ApplicationFrameWindow",
        "Windows.UI.Core.CoreWindow"
    };

    // Real Chromium browsers only — Electron desktop apps are handled via
    // ElectronProcessCatalog (single source of truth) to avoid two lists drifting.
    private static readonly HashSet<string> BrowserWordSelectionProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome",
        "msedge",
        "brave",
        "opera",
        "vivaldi",
    };

    private readonly KeyboardObserver _observer;
    private string? _lastSelectedText;

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
        var word = _observer.GetVisibleWordNearCaret(context);
        return string.IsNullOrEmpty(word) ? null : word;
    }

    /// <inheritdoc/>
    public string? TryGetSelectedText(ForegroundContext context)
    {
        _lastSelectedText = CaptureSelectionTextViaClipboard();
        return _lastSelectedText;
    }

    /// <inheritdoc/>
    public string? TryGetCurrentSentence(ForegroundContext context) => null;

    /// <inheritdoc/>
    public bool TryReplaceLastWord(ForegroundContext context, string replacement)
    {
        var current = _observer.GetVisibleWordNearCaret(context);
        if (string.IsNullOrEmpty(current)) return false;

        NativeMethods.INPUT[] inputs = ShouldUseWordSelectionReplace(context)
            ? BuildWordSelectionReplaceInputs(current.Length, replacement)
            : BuildBackspaceReplaceInputs(current.Length, replacement);

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        bool success = sent == inputs.Length;

        if (success)
            _observer.ResetBuffer();

        return success;
    }

    /// <inheritdoc/>
    public bool TryReplaceSelection(ForegroundContext context, string replacement)
    {
        string? selected = _lastSelectedText ?? TryGetSelectedText(context);
        if (string.IsNullOrEmpty(selected))
            return false;

        var modifierRelease = NativeMethods.BuildModifierReleaseInputs();
        var inputs = new NativeMethods.INPUT[modifierRelease.Length + replacement.Length * 2];
        int idx = 0;

        foreach (var input in modifierRelease)
            inputs[idx++] = input;

        foreach (char c in replacement)
        {
            inputs[idx++] = NativeMethods.MakeUnicodeInput(c, keyUp: false);
            inputs[idx++] = NativeMethods.MakeUnicodeInput(c, keyUp: true);
        }

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        bool success = sent == inputs.Length;

        if (success)
        {
            _observer.ResetBuffer();
            _lastSelectedText = null;
        }

        return success;
    }

    /// <inheritdoc/>
    public bool TryReplaceCurrentSentence(ForegroundContext context, string replacement) => false;

    private static bool ShouldUseWordSelectionReplace(ForegroundContext context)
    {
        string cls = string.IsNullOrWhiteSpace(context.FocusedControlClass)
            ? context.WindowClass
            : context.FocusedControlClass;

        return WordSelectionClasses.Contains(cls)
            || BrowserWordSelectionProcesses.Contains(context.ProcessName)
            || ElectronProcessCatalog.IsElectronProcess(context.ProcessName);
    }

    private static NativeMethods.INPUT[] BuildBackspaceReplaceInputs(int backCount, string replacement)
    {
        var modifierRelease = NativeMethods.BuildModifierReleaseInputs();
        var inputs = new NativeMethods.INPUT[modifierRelease.Length + backCount * 2 + replacement.Length * 2];
        int idx = 0;

        foreach (var input in modifierRelease)
            inputs[idx++] = input;

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

        return inputs;
    }

    private static NativeMethods.INPUT[] BuildWordSelectionReplaceInputs(int selectionLength, string replacement)
    {
        var modifierRelease = NativeMethods.BuildModifierReleaseInputs();
        var inputs = new NativeMethods.INPUT[modifierRelease.Length + 2 + (selectionLength * 2) + replacement.Length * 2];
        int idx = 0;

        foreach (var input in modifierRelease)
            inputs[idx++] = input;

        inputs[idx++] = NativeMethods.MakeKeyInput(NativeMethods.VK_SHIFT, keyUp: false);
        for (int i = 0; i < selectionLength; i++)
        {
            inputs[idx++] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_LEFT, keyUp: false);
            inputs[idx++] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_LEFT, keyUp: true);
        }
        inputs[idx++] = NativeMethods.MakeKeyInput(NativeMethods.VK_SHIFT, keyUp: true);

        foreach (char c in replacement)
        {
            inputs[idx++] = NativeMethods.MakeUnicodeInput(c, keyUp: false);
            inputs[idx++] = NativeMethods.MakeUnicodeInput(c, keyUp: true);
        }

        return inputs;
    }

    private static string? CaptureSelectionTextViaClipboard()
    {
        string? savedClipboard = NativeMethods.GetClipboardText();
        NativeMethods.SetClipboardText(null);

        try
        {
            var modifierRelease = NativeMethods.BuildModifierReleaseInputs();
            var copyInputs = new NativeMethods.INPUT[modifierRelease.Length + 4];
            int idx = 0;

            foreach (var input in modifierRelease)
                copyInputs[idx++] = input;

            copyInputs[idx++] = NativeMethods.MakeKeyInput(NativeMethods.VK_CONTROL, keyUp: false);
            copyInputs[idx++] = NativeMethods.MakeKeyInput(0x43, keyUp: false); // C
            copyInputs[idx++] = NativeMethods.MakeKeyInput(0x43, keyUp: true);
            copyInputs[idx++] = NativeMethods.MakeKeyInput(NativeMethods.VK_CONTROL, keyUp: true);

            Thread.Sleep(30);
            NativeMethods.SendInput((uint)copyInputs.Length, copyInputs, Marshal.SizeOf<NativeMethods.INPUT>());

            for (int attempt = 0; attempt < 5; attempt++)
            {
                Thread.Sleep(attempt == 0 ? 60 : 90);
                string? selected = NativeMethods.GetClipboardText();
                if (!string.IsNullOrEmpty(selected))
                    return selected;
            }

            return null;
        }
        finally
        {
            NativeMethods.SetClipboardText(savedClipboard);
        }
    }
}
