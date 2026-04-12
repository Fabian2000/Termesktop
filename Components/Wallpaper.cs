using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using TermuiX;
using TermuiX.Widgets;

namespace Termesktop.Components;

public class Wallpaper
{
    private readonly TermuiX.TermuiX _termui;
    private Container? _layer;
    private string? _currentPath;
    private int _lastWidth;
    private int _lastHeight;
    private int _widgetCounter;

    public Wallpaper(TermuiX.TermuiX termui)
    {
        _termui = termui;
    }

    public void Initialize(Container wallpaperLayer)
    {
        _layer = wallpaperLayer;
    }

    public void Update(string? wallpaperPath)
    {
        if (_layer is null) return;

        var w = Console.WindowWidth;
        var h = ((IWidget)_layer).ComputedHeight;
        if (h <= 0) h = Console.WindowHeight - 8;

        bool needsRender = wallpaperPath != _currentPath
            || (wallpaperPath is not null && (w != _lastWidth || h != _lastHeight));

        if (!needsRender) return;

        _currentPath = wallpaperPath;
        _lastWidth = w;
        _lastHeight = h;

        _layer.Clear();

        if (wallpaperPath is null || !File.Exists(wallpaperPath))
            return;

        RenderWallpaper(wallpaperPath, w, h);
    }

    private void RenderWallpaper(string path, int termWidth, int termHeight)
    {
        if (_layer is null) return;

        try
        {
            using var img = Image.Load<Rgba32>(path);

            int targetW = termWidth;
            int targetH = termHeight * 2;

            using var resized = img.Clone(ctx => ctx.Resize(targetW, targetH));

            for (int y = 0; y < targetH; y += 2)
            {
                int charY = y / 2;
                int x = 0;

                while (x < targetW)
                {
                    var topPx = resized[x, y];
                    var botPx = y + 1 < targetH ? resized[x, y + 1] : new Rgba32(0, 0, 0);

                    int segStart = x;
                    x++;

                    // Group consecutive pixels with similar colors
                    while (x < targetW)
                    {
                        var nextTop = resized[x, y];
                        var nextBot = y + 1 < targetH ? resized[x, y + 1] : new Rgba32(0, 0, 0);

                        if (ColorDist(topPx, nextTop) > 50 || ColorDist(botPx, nextBot) > 50)
                            break;
                        x++;
                    }

                    int segLen = x - segStart;

                    long fR = 0, fG = 0, fB = 0, bR = 0, bG = 0, bB = 0;
                    for (int sx = segStart; sx < x; sx++)
                    {
                        var t = resized[sx, y];
                        fR += t.R; fG += t.G; fB += t.B;
                        var b = y + 1 < targetH ? resized[sx, y + 1] : new Rgba32(0, 0, 0);
                        bR += b.R; bG += b.G; bB += b.B;
                    }

                    var fg = $"rgb({fR / segLen},{fG / segLen},{fB / segLen})";
                    var bg = $"rgb({bR / segLen},{bG / segLen},{bB / segLen})";
                    var content = new string('▀', segLen);
                    var name = $"wp_{_widgetCounter++}";

                    _layer.Add($@"<Text Name='{name}' Width='{segLen}ch' Height='1ch'
                        PositionX='{segStart}ch' PositionY='{charY}ch'
                        ForegroundColor='{fg}' BackgroundColor='{bg}'
                        AllowWrapping='false'>{content}</Text>");
                }
            }
        }
        catch { }
    }

    private static int ColorDist(Rgba32 a, Rgba32 b)
    {
        return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
    }
}
