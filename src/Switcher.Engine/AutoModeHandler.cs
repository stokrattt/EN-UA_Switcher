using System.Runtime.InteropServices;
using Switcher.Core;
using Switcher.Infrastructure;

namespace Switcher.Engine;

/// <summary>
/// Handles auto-mode correction. On word boundary (Space/Enter/Tab), reads the typed word
/// from the KeyboardObserver buffer, evaluates heuristics, and replaces via SendInput.
///
/// KEY DESIGN: Auto-mode does NOT use UIAutomation or any cross-process COM calls.
/// Everything in the hook callback is instant (buffer read + heuristics = CPU-only).
/// Only the SendInput replacement and language switch run async on a threadpool thread.
/// This eliminates LowLevelHooksTimeout issues and works universally in all apps
/// (Chrome, Element, Telegram, VS Code, native Win32, etc.).
///
/// UIAutomation is reserved for SafeMode only (user-initiated, no hook timeout concern).
/// </summary>
public class AutoModeHandler
{
    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "brave", "opera", "vivaldi"
    };

    private readonly ForegroundContextProvider _contextProvider;
    private readonly ExclusionManager _exclusions;
    private readonly DiagnosticsLogger _diagnostics;
    private readonly SettingsManager _settings;
    private readonly KeyboardObserver _observer;

    // ─── Undo state ──────────────────────────────────────────────────────────
    private record UndoState(string OriginalText, string ReplacementText, CorrectionDirection Direction, uint DelimiterVk);
    private UndoState? _lastCorrection;
    private int _autoOperationInFlight;

    public AutoModeHandler(
        ForegroundContextProvider contextProvider,
        ExclusionManager exclusions,
        DiagnosticsLogger diagnostics,
        SettingsManager settings,
        KeyboardObserver observer)
    {
        _contextProvider = contextProvider;
        _exclusions = exclusions;
        _diagnostics = diagnostics;
        _settings = settings;
        _observer = observer;
    }

    /// <summary>
    /// Called synchronously from the keyboard hook when a word boundary delimiter key is pressed.
    /// Must return FAST to avoid LowLevelHooksTimeout.
    /// Reads word from observer buffer and evaluates heuristics synchronously (both are instant),
    /// then offloads only the SendInput replacement to a threadpool task.
    /// </summary>
    public void OnWordBoundary(int approxWordLength)
    {
        if (!_settings.Current.AutoModeEnabled) return;

        // Any new word boundary invalidates the undo state
        _lastCorrection = null;

        // Read ALL buffer interpretations upfront for diagnostics
        string wordEN = _observer.CurrentWordEN;
        string wordUA = _observer.CurrentWordUA;
        string rawDebug = _observer.LastVkDebug;
        int recCount = _observer.LastScanRecoveryCount;
        int droppedKeys = _observer.LastDroppedKeyCount;
        int seqScans = _observer.LastSequentialScanCount;
        string vkEN = _observer.CurrentWordEN_VkOnly;
        string vkUA = _observer.CurrentWordUA_VkOnly;
        string visibleWord = _observer.CurrentVisibleWord;
        string layoutTag = _observer.LastLayoutWasUkrainian ? "UA" : "EN";
        string visibleTrailingSuffix = ResolveVisibleTrailingPunctuation(visibleWord, layoutTag == "UA" ? wordUA : wordEN, layoutTag == "UA" ? wordEN : wordUA);
        string analysisWordEN = StripVisibleSuffixFromInterpretation(wordEN, visibleTrailingSuffix);
        string analysisWordUA = StripVisibleSuffixFromInterpretation(wordUA, visibleTrailingSuffix);
        string analysisVkEN = StripVisibleSuffixFromInterpretation(vkEN, visibleTrailingSuffix);
        string analysisVkUA = StripVisibleSuffixFromInterpretation(vkUA, visibleTrailingSuffix);

        // Chrome sends COMPLETELY FAKE scan AND VK codes (sequential counters).
        // When ≥3 adjacent scans are sequential, both scan and VK data is garbage —
        // clipboard fallback is the ONLY viable path.
        bool chromeGarbage = seqScans >= 3;

        // True when the typed word contains digits (e.g. "Win11", "Office365").
        // VK-only path silently drops digit VK codes (0x30–0x39), producing a shorter
        // string than what is actually in the editor. Layer 2 must be skipped for such
        // words to prevent incorrect Backspace count (the "WiWin" bug).
        bool wordContainsDigits = wordEN.Any(char.IsDigit) || wordUA.Any(char.IsDigit);

        // Original: short decoded words (visible in UI column)
        string originalDisplay = $"{wordEN} | {wordUA}";
        // Technical detail for Reason column (wider)
        string techDetail = $"keys={approxWordLength} rec={recCount} drop={droppedKeys} seq={seqScans} lay={layoutTag}" +
                            (vkEN != wordEN ? $" vkEn={vkEN}" : "") +
                            (vkUA != wordUA ? $" vkUa={vkUA}" : "");

        // Fast context snapshot (Win32 calls only — no UIA/COM)
        var context = _contextProvider.GetCurrent();
        string proc = context?.ProcessName ?? "?";
        string cls = context?.FocusedControlClass ?? "?";

        if (approxWordLength < 2 || (analysisWordEN.Length < 2 && analysisWordUA.Length < 2))
        {
            // If we have enough keys but buffer decoding failed (garbage scan/VK codes),
            // fall back to clipboard-based word reading. This handles Chrome which
            // sometimes sends fake sequential scan/VK codes instead of real hardware data.
            if (approxWordLength >= 3 && wordEN.Length < 2 && wordUA.Length < 2)
            {
                if (!TryBeginAutoOperation(proc, cls, originalDisplay,
                        $"Skipped empty-buffer fallback: previous auto operation still in progress {techDetail} [{rawDebug}]"))
                    return;

                _diagnostics.Log(proc, cls, "SendInput", false, OperationType.AutoMode,
                    originalDisplay, null, DiagnosticResult.Skipped,
                    $"L1 empty → clipboard fallback {techDetail} [{rawDebug}]");
                _observer.SuppressCurrentDelimiter();
                uint fallbackDelimVk = _observer.LastDelimiterVk;
                var fallbackContext = context;
                _ = Task.Run(() => ClipboardFallback(approxWordLength, fallbackDelimVk,
                    fallbackContext, layoutTag, rawDebug));
                return;
            }

            _diagnostics.Log(proc, cls, "SendInput", false, OperationType.AutoMode,
                originalDisplay, null, DiagnosticResult.Skipped,
                $"Too short {techDetail} [{rawDebug}]");
            return;
        }

        if (context == null)
        {
            _diagnostics.Log("?", "?", "SendInput", false, OperationType.AutoMode,
                originalDisplay, null, DiagnosticResult.Error,
                $"Context is null {techDetail}");
            return;
        }

        if (_exclusions.IsExcluded(context.ProcessName))
        {
            _diagnostics.Log(proc, cls, "SendInput", false, OperationType.AutoMode, originalDisplay, null,
                DiagnosticResult.Skipped, $"Process is excluded {techDetail}");
            return;
        }

        if (IsExcludedAutoWord(wordEN, wordUA, vkEN, vkUA, analysisWordEN, analysisWordUA, analysisVkEN, analysisVkUA))
        {
            _diagnostics.Log(proc, cls, "SendInput", false, OperationType.AutoMode, originalDisplay, null,
                DiagnosticResult.Skipped, $"Word is excluded from Auto Mode {techDetail}");
            return;
        }

        if (BrowserProcesses.Contains(context.ProcessName) && approxWordLength >= 2)
        {
            if (!TryBeginAutoOperation(proc, cls, originalDisplay,
                    $"Skipped browser fallback: previous auto operation still in progress {techDetail} [{rawDebug}]"))
                return;

            _diagnostics.Log(proc, cls, "UIAutomationTargetAdapter", true, OperationType.AutoMode,
                originalDisplay, null, DiagnosticResult.Skipped,
                $"Browser target detected → prefer UIA/clipboard fallback {techDetail} [{rawDebug}]");
            _observer.SuppressCurrentDelimiter();
            uint fallbackDelimVk = _observer.LastDelimiterVk;
            var fallbackContext = context;
            _ = Task.Run(() => BrowserAutoFallback(approxWordLength, fallbackDelimVk, fallbackContext, layoutTag, rawDebug));
            return;
        }

        // ─── Chrome garbage fast-path ─────────────────────────────────────────
        // Chrome sends COMPLETELY FAKE sequential scan AND VK codes.
        // Both scan-based and VK-based interpretations are garbage.
        // Skip L1/L2 entirely, go straight to clipboard fallback.
        // Exception: words with digits (e.g. "Win11", "Office365") — clipboard fallback
        // triggers Shift+Left selection which can cause side-effects in Electron apps
        // like Obsidian (e.g. "Win11" → "Win "). Since CorrectionHeuristics would reject
        // digit-containing words anyway, just skip clipboard fallback for them entirely.
        if (chromeGarbage && approxWordLength >= 3 && !wordContainsDigits)
        {
            if (!TryBeginAutoOperation(proc, cls, originalDisplay,
                    $"Skipped Chromium fallback: previous auto operation still in progress {techDetail} [{rawDebug}]"))
                return;

            _diagnostics.Log(proc, cls, "SendInput", true, OperationType.AutoMode,
                originalDisplay, null, DiagnosticResult.Skipped,
                $"Sequential scans detected → clipboard fallback {techDetail} [{rawDebug}]");
            _observer.SuppressCurrentDelimiter();
            uint fallbackDelimVk = _observer.LastDelimiterVk;
            var fallbackContext = context;
            _ = Task.Run(() => ClipboardFallback(approxWordLength, fallbackDelimVk,
                fallbackContext, layoutTag, rawDebug));
            return;
        }

        // ─── Layer 1: Primary evaluation (scan+VK hybrid) ───────────────────
        CorrectionCandidate? candidate = null;

        // 1a) EN→UA (e.g. "ghbdsn" → привіт): require converted text in UA dictionary
        if (analysisWordEN.Length >= 2)
        {
            var c = CorrectionHeuristics.Evaluate(analysisWordEN, CorrectionMode.Auto);
            if (c != null)
                candidate = ApplyVisibleTrailingPunctuation(c, visibleTrailingSuffix);
        }

        // 1b) UA→EN (e.g. "руддщ" → hello): normal threshold, no extra dictionary gate
        if (candidate == null && analysisWordUA.Length >= 2)
        {
            var c = CorrectionHeuristics.Evaluate(analysisWordUA, CorrectionMode.Auto);
            if (c != null)
                candidate = ApplyVisibleTrailingPunctuation(c, visibleTrailingSuffix);
        }

        if (candidate != null)
        {
            // Layer 1 found a candidate — proceed to replacement (below)
        }
        else if (approxWordLength >= 3 && (recCount > 0 || droppedKeys > 0) && !wordContainsDigits)
        {
            // ─── Layer 2: VK-only re-evaluation ─────────────────────────────
            // Buffer data may be unreliable (scan recovery or dropped keys).
            // Try VK-only interpretation (instant, no API calls).
            _diagnostics.Log(proc, cls, "SendInput", true, OperationType.AutoMode,
                originalDisplay, null, DiagnosticResult.Skipped,
                $"L1 no match → trying L2 VK-only {techDetail} [{rawDebug}]");

            if (analysisVkEN != analysisWordEN || analysisVkUA != analysisWordUA)
            {
                if (analysisVkEN.Length >= 2)
                {
                    var c = CorrectionHeuristics.Evaluate(analysisVkEN, CorrectionMode.Auto);
                    if (c != null)
                        candidate = ApplyVisibleTrailingPunctuation(c, visibleTrailingSuffix);
                }
                if (candidate == null && analysisVkUA.Length >= 2)
                {
                    var c = CorrectionHeuristics.Evaluate(analysisVkUA, CorrectionMode.Auto);
                    if (c != null)
                        candidate = ApplyVisibleTrailingPunctuation(c, visibleTrailingSuffix);
                }
            }

            if (candidate == null)
            {
                if (!TryBeginAutoOperation(proc, cls, originalDisplay,
                        $"Skipped clipboard fallback: previous auto operation still in progress {techDetail} [{rawDebug}]"))
                    return;

                // ─── Layer 3: Clipboard fallback ────────────────────────────
                _diagnostics.Log(proc, cls, "SendInput", true, OperationType.AutoMode,
                    originalDisplay, null, DiagnosticResult.Skipped,
                    $"L2 no match → clipboard fallback {techDetail} [{rawDebug}]");
                _observer.SuppressCurrentDelimiter();
                uint fallbackDelimVk = _observer.LastDelimiterVk;
                var fallbackContext = context;
                _ = Task.Run(() => ClipboardFallback(approxWordLength, fallbackDelimVk,
                    fallbackContext, layoutTag, rawDebug));
                return;
            }
            // candidate was found via VK-only — fall through to replacement below
        }
        else
        {
            _diagnostics.Log(proc, cls, "SendInput", true, OperationType.AutoMode,
                originalDisplay, null,
                DiagnosticResult.Skipped,
                $"No conversion {techDetail} [{rawDebug}]");
            return;
        }

        // We have a candidate — suppress delimiter and replace async
        if (!TryBeginAutoOperation(proc, cls, originalDisplay,
                $"Skipped auto replacement: previous auto operation still in progress {techDetail} [{rawDebug}]"))
            return;

        uint delimVk = _observer.LastDelimiterVk;
        _observer.SuppressCurrentDelimiter();

        string replacement = candidate.ConvertedText;
        var direction = candidate.Direction;
        string reason = candidate.Reason;
        string original = candidate.OriginalText;
        string replacementCore = TrimLiteralTrailingSuffix(replacement, visibleTrailingSuffix);
        string originalCore = TrimLiteralTrailingSuffix(original, visibleTrailingSuffix);

        // Use approxWordLength as erase count when actual key count exceeds decoded text length.
        // This prevents under-erasing when digits or other non-VK-mapped chars were present.
        int eraseCount = Math.Max(approxWordLength - visibleTrailingSuffix.Length, originalCore.Length);

        Task.Run(() =>
        {
            try
            {
                var inputs = BuildAutoReplacementInputs(originalCore, replacementCore, visibleTrailingSuffix, eraseCount);

                uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs,
                    Marshal.SizeOf<NativeMethods.INPUT>());
                bool success = sent == (uint)inputs.Length;

                _diagnostics.Log(context.ProcessName, context.FocusedControlClass,
                    "SendInput", true, OperationType.AutoMode,
                    original, replacement,
                    success ? DiagnosticResult.Replaced : DiagnosticResult.Error,
                    success ? $"{reason} {techDetail}" : $"SendInput returned {sent}/{inputs.Length} {techDetail}");

                if (success)
                {
                    _observer.ClearBuffer();
                    NativeMethods.SwitchInputLanguage(context.Hwnd, context.FocusedControlHwnd,
                        toUkrainian: direction == CorrectionDirection.EnToUa);

                    // Save undo state so Backspace can revert this correction
                    _lastCorrection = new UndoState(original, replacement, direction, delimVk);
                }

                // Re-inject the suppressed delimiter after replacement is processed.
                Thread.Sleep(30);
                ReinjectDelimiter(delimVk);
            }
            catch
            {
                ReinjectDelimiter(delimVk);
            }
            finally
            {
                EndAutoOperation();
            }
        });
    }

    private bool TryBeginAutoOperation(string proc, string cls, string originalDisplay, string reason)
    {
        if (Interlocked.CompareExchange(ref _autoOperationInFlight, 1, 0) == 0)
            return true;

        _diagnostics.Log(proc, cls, "SendInput", true, OperationType.AutoMode,
            originalDisplay, null, DiagnosticResult.Skipped, reason);
        return false;
    }

    private void EndAutoOperation()
    {
        Volatile.Write(ref _autoOperationInFlight, 0);
    }

    private static void ReinjectDelimiter(uint delimVk)
    {
        var inputs = new NativeMethods.INPUT[2];
        inputs[0] = NativeMethods.MakeKeyInput(delimVk, keyUp: false);
        inputs[1] = NativeMethods.MakeKeyInput(delimVk, keyUp: true);
        NativeMethods.SendInput(2, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    /// <summary>
    /// Clipboard-based fallback for when the scan code buffer produces garbage.
    /// Selects the word using Shift+Left, copies via Ctrl+C, reads clipboard,
    /// evaluates heuristics on the actual screen text, and replaces if needed.
    /// </summary>
    private void ClipboardFallback(int wordLen, uint delimVk,
        ForegroundContext? context, string layoutTag, string rawDebug)
    {
        string proc = context?.ProcessName ?? "?";
        string cls = context?.FocusedControlClass ?? "?";

        try
        {
            // 0. Give the app time to finish processing the typed characters.
            //    Chrome's async input pipeline may still be committing the last keys.
            Thread.Sleep(150);

            if (context != null && TryUiAutomationFallback(context, delimVk, layoutTag, rawDebug))
                return;

            // 1. Save current clipboard and CLEAR it so we can detect when copy succeeds
            string? savedClipboard = NativeMethods.GetClipboardText();
            NativeMethods.SetClipboardText(null);

            // 2. Select the word: Shift held, Left×N, Shift released — ONE atomic SendInput
            //    Arrow keys MUST use KEYEVENTF_EXTENDEDKEY or Chrome ignores them.
            var sel = new NativeMethods.INPUT[2 + wordLen * 2]; // Shift↓, Left↓↑ × N, Shift↑
            int si = 0;
            sel[si++] = NativeMethods.MakeKeyInput(NativeMethods.VK_SHIFT, keyUp: false);
            for (int i = 0; i < wordLen; i++)
            {
                sel[si++] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_LEFT, keyUp: false);
                sel[si++] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_LEFT, keyUp: true);
            }
            sel[si++] = NativeMethods.MakeKeyInput(NativeMethods.VK_SHIFT, keyUp: true);
            NativeMethods.SendInput((uint)sel.Length, sel, Marshal.SizeOf<NativeMethods.INPUT>());
            Thread.Sleep(100);

            // 3. Copy: Ctrl+C
            var copy = new NativeMethods.INPUT[4];
            copy[0] = NativeMethods.MakeKeyInput(NativeMethods.VK_CONTROL, keyUp: false);
            copy[1] = NativeMethods.MakeKeyInput(0x43 /* VK_C */, keyUp: false);
            copy[2] = NativeMethods.MakeKeyInput(0x43 /* VK_C */, keyUp: true);
            copy[3] = NativeMethods.MakeKeyInput(NativeMethods.VK_CONTROL, keyUp: true);
            NativeMethods.SendInput(4, copy, Marshal.SizeOf<NativeMethods.INPUT>());

            // 4. Wait for clipboard to be populated — Chrome needs time
            string? selectedText = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                Thread.Sleep(attempt == 0 ? 150 : 200);
                selectedText = NativeMethods.GetClipboardText();
                if (!string.IsNullOrEmpty(selectedText))
                    break;
            }

            // 5. Restore original clipboard content
            NativeMethods.SetClipboardText(savedClipboard);

            // 6. Deselect: press Right to collapse selection to the right end
            var desel = new NativeMethods.INPUT[2];
            desel[0] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_RIGHT, keyUp: false);
            desel[1] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_RIGHT, keyUp: true);
            NativeMethods.SendInput(2, desel, Marshal.SizeOf<NativeMethods.INPUT>());

            if (string.IsNullOrEmpty(selectedText) || selectedText.Length < 2)
            {
                _diagnostics.Log(proc, cls, "SendInput", false, OperationType.AutoMode,
                    $"clipboard:'{selectedText}'", null, DiagnosticResult.Skipped,
                    $"Clipboard fallback: copy failed (empty) [{rawDebug}]");
                ReinjectDelimiter(delimVk);
                return;
            }

            // 7. Evaluate heuristics on the actual screen text
            string trimmed = selectedText.Trim();
            if (_exclusions.IsWordExcluded(trimmed))
            {
                _diagnostics.Log(proc, cls, "SendInput", true, OperationType.AutoMode,
                    trimmed, null, DiagnosticResult.Skipped,
                    $"Clipboard fallback: word is excluded from Auto Mode (layout={layoutTag}) [{rawDebug}]");
                ReinjectDelimiter(delimVk);
                return;
            }

            string visibleSuffix = ExtractLiteralTrailingPunctuation(trimmed);
            string analysis = TrimTrailingChars(trimmed, visibleSuffix.Length);
            var candidate = string.IsNullOrWhiteSpace(analysis)
                ? null
                : CorrectionHeuristics.Evaluate(analysis, CorrectionMode.Auto);
            if (candidate != null)
                candidate = ApplyVisibleTrailingPunctuation(candidate, visibleSuffix);

            if (candidate == null)
            {
                _diagnostics.Log(proc, cls, "SendInput", true, OperationType.AutoMode,
                    $"clipboard:'{selectedText}'", null, DiagnosticResult.Skipped,
                    $"Clipboard fallback: no conversion (layout={layoutTag}) [{rawDebug}]");
                ReinjectDelimiter(delimVk);
                return;
            }

            // 8. Replace: select the word again, then type replacement over it
            int replaceLen = trimmed.Length; // use actual text length, not key count
            var reSel = new NativeMethods.INPUT[2 + replaceLen * 2];
            int ri = 0;
            reSel[ri++] = NativeMethods.MakeKeyInput(NativeMethods.VK_SHIFT, keyUp: false);
            for (int i = 0; i < replaceLen; i++)
            {
                reSel[ri++] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_LEFT, keyUp: false);
                reSel[ri++] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_LEFT, keyUp: true);
            }
            reSel[ri++] = NativeMethods.MakeKeyInput(NativeMethods.VK_SHIFT, keyUp: true);
            NativeMethods.SendInput((uint)reSel.Length, reSel, Marshal.SizeOf<NativeMethods.INPUT>());
            Thread.Sleep(50);

            // Type replacement (overwrites selection)
            var typeInputs = new NativeMethods.INPUT[candidate.ConvertedText.Length * 2];
            int idx = 0;
            foreach (char c in candidate.ConvertedText)
            {
                typeInputs[idx++] = NativeMethods.MakeUnicodeInput(c, keyUp: false);
                typeInputs[idx++] = NativeMethods.MakeUnicodeInput(c, keyUp: true);
            }
            uint sent = NativeMethods.SendInput((uint)typeInputs.Length, typeInputs,
                Marshal.SizeOf<NativeMethods.INPUT>());
            bool success = sent == (uint)typeInputs.Length;

            _diagnostics.Log(proc, cls, "SendInput", true, OperationType.AutoMode,
                candidate.OriginalText, candidate.ConvertedText,
                success ? DiagnosticResult.Replaced : DiagnosticResult.Error,
                success ? $"Clipboard fallback: {candidate.Reason} layout={layoutTag}"
                        : $"Clipboard fallback: SendInput {sent}/{typeInputs.Length}");

            if (success)
            {
                _observer.ClearBuffer();
                if (context != null)
                    NativeMethods.SwitchInputLanguage(context.Hwnd, context.FocusedControlHwnd,
                        toUkrainian: candidate.Direction == CorrectionDirection.EnToUa);

                _lastCorrection = new UndoState(candidate.OriginalText, candidate.ConvertedText, candidate.Direction, delimVk);
            }

            Thread.Sleep(30);
            ReinjectDelimiter(delimVk);
        }
        catch (Exception ex)
        {
            _diagnostics.Log(proc, cls, "SendInput", false, OperationType.AutoMode,
                "", null, DiagnosticResult.Error,
                $"Clipboard fallback error: {ex.Message}");
            ReinjectDelimiter(delimVk);
        }
        finally
        {
            EndAutoOperation();
        }
    }

    private bool TryUiAutomationFallback(ForegroundContext context, uint delimVk, string layoutTag, string rawDebug)
    {
        try
        {
            var adapter = new UIAutomationTargetAdapter();
            if (adapter.CanHandle(context) != TargetSupport.Full)
                return false;

            string? actualWord = adapter.TryGetLastWord(context);
            if (string.IsNullOrWhiteSpace(actualWord) || actualWord.Length < 2)
                return false;

            if (_exclusions.IsWordExcluded(actualWord))
                return false;

            string visibleSuffix = ExtractLiteralTrailingPunctuation(actualWord);
            string analysis = TrimTrailingChars(actualWord, visibleSuffix.Length);
            var candidate = string.IsNullOrWhiteSpace(analysis)
                ? null
                : CorrectionHeuristics.Evaluate(analysis, CorrectionMode.Auto);
            if (candidate != null)
                candidate = ApplyVisibleTrailingPunctuation(candidate, visibleSuffix);

            if (candidate == null)
                return false;

            bool success = adapter.TryReplaceLastWord(context, candidate.ConvertedText);
            _diagnostics.Log(context.ProcessName, context.FocusedControlClass,
                adapter.AdapterName, true, OperationType.AutoMode,
                candidate.OriginalText, candidate.ConvertedText,
                success ? DiagnosticResult.Replaced : DiagnosticResult.Error,
                success
                    ? $"UIA fallback: {candidate.Reason} layout={layoutTag}"
                    : "UIA fallback: replacement failed");

            if (!success)
                return false;

            _observer.ClearBuffer();
            NativeMethods.SwitchInputLanguage(context.Hwnd, context.FocusedControlHwnd,
                toUkrainian: candidate.Direction == CorrectionDirection.EnToUa);
            _lastCorrection = new UndoState(candidate.OriginalText, candidate.ConvertedText, candidate.Direction, delimVk);
            Thread.Sleep(30);
            ReinjectDelimiter(delimVk);
            return true;
        }
        catch (Exception ex)
        {
            _diagnostics.Log(context.ProcessName, context.FocusedControlClass,
                "UIAutomationTargetAdapter", true, OperationType.AutoMode,
                "", null, DiagnosticResult.Error,
                $"UIA fallback error: {ex.Message} [{rawDebug}]");
            return false;
        }
    }

    private void BrowserAutoFallback(int wordLen, uint delimVk, ForegroundContext context, string layoutTag, string rawDebug)
    {
        try
        {
            // Let browser commit the typed text before reading via UIA.
            Thread.Sleep(120);

            if (TryUiAutomationFallback(context, delimVk, layoutTag, rawDebug))
            {
                EndAutoOperation();
                return;
            }

            ClipboardFallback(wordLen, delimVk, context, layoutTag, rawDebug);
        }
        catch (Exception ex)
        {
            _diagnostics.Log(context.ProcessName, context.FocusedControlClass,
                "UIAutomationTargetAdapter", true, OperationType.AutoMode,
                "", null, DiagnosticResult.Error,
                $"Browser auto fallback error: {ex.Message}");
            ReinjectDelimiter(delimVk);
            EndAutoOperation();
        }
    }

    /// <summary>
    /// Called synchronously from the keyboard hook when Backspace is pressed
    /// immediately after a correction (no new chars typed yet).
    /// Undoes the last auto-correction: erases replacement + space, types original + space,
    /// and switches the language back.
    /// </summary>
    public void OnUndoRequested()
    {
        if (!_settings.Current.UndoOnBackspace) return;

        var undo = _lastCorrection;
        if (undo == null) return;

        _lastCorrection = null;
        _observer.SuppressCurrentBackspace();

        var context = _contextProvider.GetCurrent();

        Task.Run(() =>
        {
            try
            {
                var inputs = BuildUndoInputs(undo.ReplacementText, undo.OriginalText);

                uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs,
                    Marshal.SizeOf<NativeMethods.INPUT>());

                // Switch language back (reverse the original direction)
                if (context != null)
                {
                    NativeMethods.SwitchInputLanguage(context.Hwnd, context.FocusedControlHwnd,
                        toUkrainian: undo.Direction != CorrectionDirection.EnToUa);
                }

                // Re-inject the same delimiter that originally triggered correction.
                Thread.Sleep(30);
                ReinjectDelimiter(undo.DelimiterVk);

                _diagnostics.Log(
                    context?.ProcessName ?? "?",
                    context?.FocusedControlClass ?? "?",
                    "SendInput", true, OperationType.AutoMode,
                    undo.ReplacementText, undo.OriginalText,
                    sent == (uint)inputs.Length ? DiagnosticResult.Replaced : DiagnosticResult.Error,
                    "Undo via Backspace");
            }
            catch { /* undo is best-effort */ }
        });
    }

    private static NativeMethods.INPUT[] BuildUndoInputs(string replacementText, string restoreText)
    {
        int eraseCount = replacementText.Length + 1; // replacement + delimiter
        var modifierRelease = NativeMethods.BuildModifierReleaseInputs();
        var inputs = new NativeMethods.INPUT[modifierRelease.Length + eraseCount * 2 + restoreText.Length * 2];
        int idx = 0;

        foreach (var input in modifierRelease)
            inputs[idx++] = input;

        for (int i = 0; i < eraseCount; i++)
        {
            inputs[idx++] = NativeMethods.MakeKeyInput(NativeMethods.VK_BACK, keyUp: false);
            inputs[idx++] = NativeMethods.MakeKeyInput(NativeMethods.VK_BACK, keyUp: true);
        }
        foreach (char c in restoreText)
        {
            inputs[idx++] = NativeMethods.MakeUnicodeInput(c, keyUp: false);
            inputs[idx++] = NativeMethods.MakeUnicodeInput(c, keyUp: true);
        }

        return inputs;
    }

    private bool IsExcludedAutoWord(params string?[] words)
    {
        foreach (string? word in words)
        {
            if (string.IsNullOrWhiteSpace(word))
                continue;

            if (_exclusions.IsWordExcluded(word))
                return true;
        }

        return false;
    }

    private static CorrectionCandidate ApplyVisibleTrailingPunctuation(CorrectionCandidate candidate, string visibleSuffix)
    {
        if (string.IsNullOrEmpty(visibleSuffix))
            return candidate;

        string original = candidate.OriginalText;
        if (!original.EndsWith(visibleSuffix, StringComparison.Ordinal))
            original += visibleSuffix;

        string converted = candidate.ConvertedText;
        if (!converted.EndsWith(visibleSuffix, StringComparison.Ordinal))
        {
            string toggledSuffix = KeyboardLayoutMap.ToggleLayoutText(visibleSuffix, out int changedCount);
            if (changedCount > 0 && converted.EndsWith(toggledSuffix, StringComparison.Ordinal))
                converted = converted[..^toggledSuffix.Length] + visibleSuffix;
            else
                converted += visibleSuffix;
        }

        if (original == candidate.OriginalText && converted == candidate.ConvertedText)
            return candidate;

        return candidate with
        {
            OriginalText = original,
            ConvertedText = converted,
            Reason = $"{candidate.Reason} + preserved trailing punctuation"
        };
    }

    private static string ExtractLiteralTrailingPunctuation(string visibleWord)
    {
        if (string.IsNullOrEmpty(visibleWord) || visibleWord.Length < 2)
            return string.Empty;

        int start = visibleWord.Length;
        while (start > 0 && IsLiteralTrailingPunctuation(visibleWord[start - 1]))
            start--;

        if (start == visibleWord.Length || start == 0)
            return string.Empty;

        return char.IsLetterOrDigit(visibleWord[start - 1])
            ? visibleWord[start..]
            : string.Empty;
    }

    private static string ResolveVisibleTrailingPunctuation(params string[] visibleWords)
    {
        foreach (string visibleWord in visibleWords)
        {
            string suffix = ExtractLiteralTrailingPunctuation(visibleWord);
            if (!string.IsNullOrEmpty(suffix))
                return suffix;
        }

        return string.Empty;
    }

    private static string TrimTrailingChars(string text, int count)
    {
        if (count <= 0 || string.IsNullOrEmpty(text))
            return text;

        return text.Length > count ? text[..^count] : string.Empty;
    }

    private static string TrimLiteralTrailingSuffix(string text, string suffix)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(suffix))
            return text;

        return text.EndsWith(suffix, StringComparison.Ordinal)
            ? text[..^suffix.Length]
            : text;
    }

    private static string StripVisibleSuffixFromInterpretation(string text, string visibleSuffix)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(visibleSuffix))
            return text;

        if (text.EndsWith(visibleSuffix, StringComparison.Ordinal))
            return text[..^visibleSuffix.Length];

        string toggledSuffix = KeyboardLayoutMap.ToggleLayoutText(visibleSuffix, out int changedCount);
        if (changedCount > 0 && text.EndsWith(toggledSuffix, StringComparison.Ordinal))
            return text[..^toggledSuffix.Length];

        return text;
    }

    private static NativeMethods.INPUT[] BuildAutoReplacementInputs(
        string originalCore, string replacementCore, string trailingSuffix,
        int? eraseCountOverride = null)
    {
        var modifierRelease = NativeMethods.BuildModifierReleaseInputs();
        int suffixLen = trailingSuffix.Length;
        // eraseCountOverride allows the caller to erase more characters than originalCore.Length,
        // e.g. when the typed word contained digits that were stripped by the VK-only path.
        int eraseCount = eraseCountOverride ?? originalCore.Length;
        int totalInputs = modifierRelease.Length + (suffixLen * 2) + (eraseCount * 2) + (replacementCore.Length * 2) + (suffixLen * 2);
        var inputs = new NativeMethods.INPUT[totalInputs];
        int idx = 0;

        foreach (var input in modifierRelease)
            inputs[idx++] = input;

        for (int i = 0; i < suffixLen; i++)
        {
            inputs[idx++] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_LEFT, keyUp: false);
            inputs[idx++] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_LEFT, keyUp: true);
        }

        for (int i = 0; i < eraseCount; i++)
        {
            inputs[idx++] = NativeMethods.MakeKeyInput(NativeMethods.VK_BACK, keyUp: false);
            inputs[idx++] = NativeMethods.MakeKeyInput(NativeMethods.VK_BACK, keyUp: true);
        }

        foreach (char c in replacementCore)
        {
            inputs[idx++] = NativeMethods.MakeUnicodeInput(c, keyUp: false);
            inputs[idx++] = NativeMethods.MakeUnicodeInput(c, keyUp: true);
        }

        for (int i = 0; i < suffixLen; i++)
        {
            inputs[idx++] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_RIGHT, keyUp: false);
            inputs[idx++] = NativeMethods.MakeExtKeyInput(NativeMethods.VK_RIGHT, keyUp: true);
        }

        return inputs;
    }

    private static bool IsLiteralTrailingPunctuation(char c) =>
        char.IsPunctuation(c) && !KeyboardLayoutMap.IsWordConnector(c);
}
