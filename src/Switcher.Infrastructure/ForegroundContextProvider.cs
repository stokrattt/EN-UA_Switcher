using System.Diagnostics;
using System.Text;

namespace Switcher.Infrastructure;

public record ForegroundContext(
    IntPtr Hwnd,
    IntPtr FocusedControlHwnd,
    string ProcessName,
    uint ProcessId,
    string WindowClass,
    string FocusedControlClass
);

public class ForegroundContextProvider
{
    public ForegroundContext? GetCurrent()
    {
        try
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint procId);
            string procName = TryGetProcessName(procId);
            string winClass = GetClassName(hwnd);

            // Get the actually focused child control
            IntPtr focusedHwnd = GetFocusedControl(hwnd);
            string focusedClass = focusedHwnd != IntPtr.Zero ? GetClassName(focusedHwnd) : winClass;

            return new ForegroundContext(hwnd, focusedHwnd, procName, procId, winClass, focusedClass);
        }
        catch
        {
            return null;
        }
    }

    private static IntPtr GetFocusedControl(IntPtr hwnd)
    {
        try
        {
            uint fgThreadId = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
            uint myThreadId = NativeMethods.GetCurrentThreadId();

            bool attached = false;
            if (fgThreadId != myThreadId)
            {
                attached = NativeMethods.AttachThreadInput(myThreadId, fgThreadId, true);
            }

            IntPtr focused = NativeMethods.GetFocus();

            if (attached)
                NativeMethods.AttachThreadInput(myThreadId, fgThreadId, false);

            return focused;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string TryGetProcessName(uint processId)
    {
        try
        {
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return $"pid:{processId}";
        }
    }
}
