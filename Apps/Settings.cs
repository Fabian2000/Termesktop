using System.Text.Json;
using System.Text.Json.Serialization;
using Termesktop;

namespace Termesktop.Apps;

public class DesktopSettings
{
    // Per-user config path: ~/.termesktop/settings.json
    private static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".termesktop");
    private static string ConfigPath => Path.Combine(ConfigDir, "settings.json");

    // Design
    public string BackgroundColor { get; set; } = "#1a0a0a";
    public string ClockColor { get; set; } = "#cccccc";
    public string DateColor { get; set; } = "#888888";
    public string TaskbarBorderColor { get; set; } = "#3a2a2a";
    public string WindowTitleBarColor { get; set; } = "#241a1a";
    public string WindowBorderColor { get; set; } = "#3a2a2a";
    public string WindowBackgroundColor { get; set; } = "#1a1218";

    // Display
    public bool ShowClock { get; set; } = true;
    public bool ShowDate { get; set; } = true;
    public bool Use24HourFormat { get; set; } = true;

    // Desktop
    public string? DesktopFolder { get; set; } = null;
    public string? WallpaperPath { get; set; } = null;

    // System
    public string DefaultShell { get; set; } = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash";
    public string DownloadPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    // Computed accent colors derived from window colors
    [JsonIgnore] public string AccentDarker => AdjustBrightness(WindowBackgroundColor, -0.3);
    [JsonIgnore] public string AccentLighter => AdjustBrightness(WindowBackgroundColor, 0.4);
    [JsonIgnore] public string AccentSubtle => AdjustBrightness(WindowBackgroundColor, 0.15);
    [JsonIgnore] public string AccentHover => AdjustBrightness(WindowBackgroundColor, 0.25);
    [JsonIgnore] public string AccentBorderSubtle => AdjustBrightness(WindowBorderColor, -0.2);

    private static string AdjustBrightness(string hex, double factor)
    {
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length != 6) return "#333333";
            int r = Convert.ToInt32(hex[..2], 16);
            int g = Convert.ToInt32(hex[2..4], 16);
            int b = Convert.ToInt32(hex[4..6], 16);

            if (factor > 0)
            {
                r = Math.Min(255, r + (int)((255 - r) * factor));
                g = Math.Min(255, g + (int)((255 - g) * factor));
                b = Math.Min(255, b + (int)((255 - b) * factor));
            }
            else
            {
                r = Math.Max(0, r + (int)(r * factor));
                g = Math.Max(0, g + (int)(g * factor));
                b = Math.Max(0, b + (int)(b * factor));
            }
            return $"#{r:X2}{g:X2}{b:X2}";
        }
        catch { return "#333333"; }
    }

    [JsonIgnore]
    private bool _suppressSave;

    public void Save()
    {
        if (_suppressSave) return;
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, AppJsonContext.Default.DesktopSettings));
    }

    public static DesktopSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize(json, AppJsonContext.Default.DesktopSettings) ?? new();
            }
        }
        catch { }
        return new();
    }

    /// <summary>
    /// Temporarily suppress auto-save (e.g. during initialization).
    /// </summary>
    public IDisposable SuppressSave()
    {
        _suppressSave = true;
        return new SaveGuard(this);
    }

    private class SaveGuard(DesktopSettings settings) : IDisposable
    {
        public void Dispose() => settings._suppressSave = false;
    }
}
