using Microsoft.Win32;
using System.Diagnostics;

namespace Switcher.Infrastructure;

/// <summary>
/// Manages Windows startup registration via the current-user Run key.
/// Uses HKCU (not HKLM) so no admin elevation is required.
/// </summary>
public static class StartupHelper
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "EN-UA Switcher";

    /// <summary>
    /// Ensures the registry Run key matches the desired state.
    /// If <paramref name="enabled"/> is true, writes the current exe path;
    /// otherwise removes the entry if it exists.
    /// </summary>
    public static void SetStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return;

            if (enabled)
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName
                    ?? string.Empty;

                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue(AppName, $"\"{exePath}\"");
            }
            else
            {
                if (key.GetValue(AppName) is not null)
                    key.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Best-effort: silently ignore registry errors
            // (e.g. restricted environments, group policy)
        }
    }

    /// <summary>
    /// Returns true if the app is currently registered to run at startup.
    /// </summary>
    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(AppName) is not null;
        }
        catch
        {
            return false;
        }
    }
}
