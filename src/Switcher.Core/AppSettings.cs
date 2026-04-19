using System.Text.Json;
using System.Text.Json.Serialization;

namespace Switcher.Core;

public class AppSettings
{
    public bool AutoModeEnabled { get; set; } = false;
    public bool SafeOnlyAutoMode { get; set; } = false;
    public bool ElectronUiaPathEnabled { get; set; } = false;
    public bool DiagnosticsEnabled { get; set; } = true;
    public bool SelectorDiagnosticsExportEnabled { get; set; } = false;
    public bool LearnedSelectorGateEnabled { get; set; } = false;
    public bool RunAtStartup { get; set; } = false;
    public bool StartMinimized { get; set; } = true;
    public bool StrictAutoMode { get; set; } = true;
    public bool UndoOnBackspace { get; set; } = true;
    public bool CorrectOnSpace { get; set; } = true;
    public bool CorrectOnEnter { get; set; } = true;
    public bool CorrectOnTab { get; set; } = false;
    public bool CancelOnBackspace { get; set; } = true;
    public bool CancelOnLeftArrow { get; set; } = true;
    public List<string> ExcludedProcessNames { get; set; } = new();
    public List<string> ExcludedWords { get; set; } = new();
    public HotkeyDescriptor SafeLastWordHotkey { get; set; } = new(Modifiers: 6, VirtualKey: 0x4B); // Ctrl+Shift+K
    public HotkeyDescriptor SafeSelectionHotkey { get; set; } = new(Modifiers: 6, VirtualKey: 0x4C); // Ctrl+Shift+L
}

/// <summary>Hotkey descriptor. Modifiers: 1=Alt, 2=Ctrl, 4=Shift, 8=Win. Combinable.</summary>
public record HotkeyDescriptor(uint Modifiers, uint VirtualKey)
{
    public string FriendlyName
    {
        get
        {
            var parts = new List<string>();
            if ((Modifiers & 8) != 0) parts.Add("Win");
            if ((Modifiers & 2) != 0) parts.Add("Ctrl");
            if ((Modifiers & 1) != 0) parts.Add("Alt");
            if ((Modifiers & 4) != 0) parts.Add("Shift");
            parts.Add(VkToName(VirtualKey));
            return string.Join("+", parts);
        }
    }

    private static string VkToName(uint vk)
    {
        // Cover the common VK codes used for hotkeys; fall back to hex for anything else
        return vk switch
        {
            >= 0x41 and <= 0x5A => ((char)vk).ToString(), // A-Z
            >= 0x30 and <= 0x39 => ((char)vk).ToString(), // 0-9
            0x20 => "Space",
            0x0D => "Enter",
            0x1B => "Escape",
            0x09 => "Tab",
            0x08 => "Back",
            0x2E => "Delete",
            0x26 => "Up",
            0x28 => "Down",
            0x25 => "Left",
            0x27 => "Right",
            0x70 => "F1",  0x71 => "F2",  0x72 => "F3",  0x73 => "F4",
            0x74 => "F5",  0x75 => "F6",  0x76 => "F7",  0x77 => "F8",
            0x78 => "F9",  0x79 => "F10", 0x7A => "F11", 0x7B => "F12",
            _ => $"0x{vk:X2}"
        };
    }
}

public class SettingsManager
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Switcher");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            string json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* best effort */ }
    }
}
