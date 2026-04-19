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
        : this(new ITextTargetAdapter[]
        {
            new NativeEditTargetAdapter(),
            new UIAutomationTargetAdapter(),
            new SendInputAdapter(observer),
        })
    {
    }

    public TextTargetCoordinator(IEnumerable<ITextTargetAdapter> adapters)
    {
        _adapters = adapters.ToArray();
    }

    /// <summary>
    /// Returns (adapter, support) for the first adapter that can handle the context,
    /// or (null, Unsupported) if none can.
    /// </summary>
    public (ITextTargetAdapter? Adapter, TargetSupport Support) Resolve(ForegroundContext context)
    {
        (ITextTargetAdapter? Adapter, TargetSupport Support) readOnlyCandidate = (null, TargetSupport.Unsupported);

        foreach (var adapter in _adapters)
        {
            var support = adapter.CanHandle(context);
            if (support == TargetSupport.Full)
                return (adapter, support);
            if (support == TargetSupport.ReadOnly && readOnlyCandidate.Adapter == null)
                readOnlyCandidate = (adapter, support);
        }

        return readOnlyCandidate.Adapter != null
            ? readOnlyCandidate
            : (null, TargetSupport.Unsupported);
    }

    public IReadOnlyList<(ITextTargetAdapter Adapter, TargetSupport Support)> ResolveCandidates(ForegroundContext context)
    {
        var full = new List<(ITextTargetAdapter Adapter, TargetSupport Support)>();
        var readOnly = new List<(ITextTargetAdapter Adapter, TargetSupport Support)>();

        foreach (var adapter in _adapters)
        {
            var support = adapter.CanHandle(context);
            if (support == TargetSupport.Full)
                full.Add((adapter, support));
            else if (support == TargetSupport.ReadOnly)
                readOnly.Add((adapter, support));
        }

        return full.Concat(readOnly).ToArray();
    }
}
