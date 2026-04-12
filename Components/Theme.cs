using Termesktop.Apps;

namespace Termesktop.Components;

/// <summary>
/// Global theme colors derived from DesktopSettings.
/// Updated whenever settings change. Accessible from all apps.
/// </summary>
public static class Theme
{
    // Window
    public static string WindowBg { get; private set; } = "#1a1218";
    public static string TitleBar { get; private set; } = "#241a1a";
    public static string Border { get; private set; } = "#3a2a2a";

    // Computed accents
    public static string Darker { get; private set; } = "#140c0e";
    public static string Lighter { get; private set; } = "#2a1a1a";
    public static string Hover { get; private set; } = "#2a1515";
    public static string Subtle { get; private set; } = "#1e1015";

    // Text
    public static string TextPrimary { get; private set; } = "#cccccc";
    public static string TextSecondary { get; private set; } = "#888888";
    public static string TextMuted { get; private set; } = "#666666";

    public static void Apply(DesktopSettings settings)
    {
        WindowBg = settings.WindowBackgroundColor;
        TitleBar = settings.WindowTitleBarColor;
        Border = settings.WindowBorderColor;

        Darker = Adjust(WindowBg, -0.3);
        Lighter = Adjust(WindowBg, 0.4);
        Hover = Adjust(WindowBg, 0.25);
        Subtle = Adjust(WindowBg, 0.15);
    }

    private static string Adjust(string hex, double factor)
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
}
