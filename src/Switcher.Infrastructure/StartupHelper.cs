using Microsoft.Win32;
using System.Diagnostics;

namespace Switcher.Infrastructure;

public static class StartupHelper
{
    private const string AppName = "EN-UA_Switcher";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void SetStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return;

            if (enabled)
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(AppName, false);
            }
        }
        catch
        {
            // Best effort
        }
    }
}
