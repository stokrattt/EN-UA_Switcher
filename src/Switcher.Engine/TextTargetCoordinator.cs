using Switcher.Core;
using Switcher.Infrastructure;

namespace Switcher.Engine;

/// <summary>
/// Selects the best available ITextTargetAdapter for a given foreground context.
/// Adapters are tried in priority order:
///   1. NativeEditTargetAdapter — Win32 EDIT/RichEdit (Notepad, WordPad, etc.)
///   2. UIAutomationTargetAdapter — UIA ValuePattern (Chrome inputs, etc.)
///   3. SendInputAdapter — universal Backspace+SendInput fallback (Electron, contenteditable)
/// </summary>
public class TextTargetCoordinator
{
    private readonly IReadOnlyList<ITextTargetAdapter> _adapters;

    public TextTargetCoordinator(KeyboardObserver observer)
    {
        _adapters = new ITextTargetAdapter[]
        {
            new NativeEditTargetAdapter(),
            new UIAutomationTargetAdapter(),
            new SendInputAdapter(observer),
        };
    }

    /// <summary>
    /// Returns (adapter, support) for the first adapter that can handle the context,
    /// or (null, Unsupported) if none can.
    /// </summary>
    public (ITextTargetAdapter? Adapter, TargetSupport Support) Resolve(ForegroundContext context)
    {
        foreach (var adapter in _adapters)
        {
            var support = adapter.CanHandle(context);
            if (support != TargetSupport.Unsupported)
                return (adapter, support);
        }
        return (null, TargetSupport.Unsupported);
    }
}
