using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using TermuiX;
using TermuiX.Widgets;
using Termesktop.Components;

namespace Termesktop.Apps;

public class ImageViewer
{
    private static int _instanceCount;

    private readonly TermuiX.TermuiX _termui;
    private readonly int _instanceId;
    private readonly string _prefix;
    private string? _filePath;

    private StackPanel? _imagePanel;
    private Text? _statusText;
    private Container? _rootContainer;
    private int _viewWidth;
    private int _viewHeight;

    public ImageViewer(TermuiX.TermuiX termui, string? filePath = null)
    {
        _termui = termui;
        _instanceId = _instanceCount++;
        _prefix = $"img{_instanceId}";
        _filePath = filePath;
    }

    public static string Title => "Image";

    public void BuildContent(Container contentArea, TermuiX.TermuiX termui)
    {
        _rootContainer = termui.GetWidget<Container>("rootContainer");

        contentArea.Add($@"
            <StackPanel Direction='Vertical' Width='100%' Height='100%' BackgroundColor='Inherit'>

                <!-- Toolbar -->
                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='{Theme.Subtle}'>
                    <Button Name='{_prefix}_open' Width='6ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#888888' FocusTextColor='#cccccc'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>Open</Button>
                    <Line Orientation='Vertical' Type='Solid' Height='1ch'
                        ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />
                    <Button Name='{_prefix}_zoomIn' Width='3ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#888888' FocusTextColor='#cccccc'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>+</Button>
                    <Button Name='{_prefix}_zoomOut' Width='3ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#888888' FocusTextColor='#cccccc'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>-</Button>
                    <Button Name='{_prefix}_fit' Width='5ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#888888' FocusTextColor='#cccccc'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>Fit</Button>
                    <Text Width='fill' BackgroundColor='Inherit' />
                </StackPanel>

                <Line Orientation='Horizontal' Type='Solid' Width='100%'
                    ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />

                <!-- Image display -->
                <Container Name='{_prefix}_scroll'
                    Width='100%' Height='fill'
                    BackgroundColor='Inherit'>
                    <StackPanel Name='{_prefix}_image' Direction='Vertical'
                        Width='auto' Height='auto' BackgroundColor='Inherit' />
                </Container>

                <!-- Status bar -->
                <Line Orientation='Horizontal' Type='Solid' Width='100%'
                    ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />
                <Text Name='{_prefix}_status' Width='100%' Height='1ch'
                    ForegroundColor='#666666' BackgroundColor='{Theme.Subtle}'
                    PaddingLeft='1ch' />

            </StackPanel>");

        _imagePanel = termui.GetWidget<StackPanel>($"{_prefix}_image");
        _statusText = termui.GetWidget<Text>($"{_prefix}_status");

        var openBtn = termui.GetWidget<Button>($"{_prefix}_open");
        if (openBtn is not null) openBtn.Click += (_, _) => OpenFile();

        var zoomInBtn = termui.GetWidget<Button>($"{_prefix}_zoomIn");
        if (zoomInBtn is not null) zoomInBtn.Click += (_, _) => Zoom(1.5);

        var zoomOutBtn = termui.GetWidget<Button>($"{_prefix}_zoomOut");
        if (zoomOutBtn is not null) zoomOutBtn.Click += (_, _) => Zoom(0.67);

        var fitBtn = termui.GetWidget<Button>($"{_prefix}_fit");
        if (fitBtn is not null) fitBtn.Click += (_, _) => FitToWindow();

        // Mouse drag to pan
        _termui.MouseClick += (_, args) =>
        {
            if (args.EventType == MouseEventType.LeftButtonPressed)
            {
                _panning = true;
                _panStartMouseX = args.X;
                _panStartMouseY = args.Y;
                _panStartOffsetX = _panX;
                _panStartOffsetY = _panY;
            }
            else if (args.EventType == MouseEventType.LeftButtonReleased)
            {
                _panning = false;
            }
            else if (args.EventType == MouseEventType.Moved && _panning)
            {
                var dx = args.X - _panStartMouseX;
                var dy = args.Y - _panStartMouseY;
                _panX = _panStartOffsetX + dx;
                _panY = _panStartOffsetY + dy;
                ApplyPan();
            }
        };

        if (_filePath is not null)
            LoadImage(_filePath);
        else
        {
            _imagePanel?.Add($@"
                <Text Width='100%' Height='auto' ForegroundColor='#555555'
                    BackgroundColor='Inherit' PaddingLeft='2ch' PaddingTop='3ch'>🖼  No image opened\n\nClick Open or double-click an image in Files</Text>");
            SetStatus("No image loaded");
        }
    }

    private double _zoom = 1.0;
    private bool _fitMode = true;
    private int _lastViewWidth;
    private int _lastViewHeight;
    private Image<Rgba32>? _loadedImage;

    // Pan state
    private int _panX;
    private int _panY;
    private bool _panning;
    private int _panStartMouseX;
    private int _panStartMouseY;
    private int _panStartOffsetX;
    private int _panStartOffsetY;

    private void OpenFile()
    {
        if (_rootContainer is null) return;

        var startPath = _filePath is not null
            ? Path.GetDirectoryName(_filePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var dialog = new FileDialog(_termui, FileDialogMode.Open, startPath);
        dialog.Show(_rootContainer, path =>
        {
            if (path is not null)
                LoadImage(path);
        });
    }

    private void LoadImage(string path)
    {
        _filePath = path;

        try
        {
            _loadedImage?.Dispose();
            _loadedImage = SixLabors.ImageSharp.Image.Load<Rgba32>(path);

            SetStatus($"{Path.GetFileName(path)}  {_loadedImage.Width}x{_loadedImage.Height}");
            FitToWindow();
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
    }

    public void Update()
    {
        if (!_fitMode || _loadedImage is null) return;

        var scrollPanel = _termui.GetWidget<Container>($"{_prefix}_scroll");
        if (scrollPanel is null) return;

        var w = ((IWidget)scrollPanel).ComputedWidth;
        var h = ((IWidget)scrollPanel).ComputedHeight * 2;

        if (w != _lastViewWidth || h != _lastViewHeight)
        {
            _lastViewWidth = w;
            _lastViewHeight = h;
            if (w > 0 && h > 0)
                FitToWindow();
        }
    }

    private void FitToWindow()
    {
        if (_loadedImage is null || _imagePanel is null) return;

        var scrollContainer = _termui.GetWidget<Container>($"{_prefix}_scroll");
        _viewWidth = scrollContainer is not null ? ((IWidget)scrollContainer).ComputedWidth : 60;
        _viewHeight = scrollContainer is not null ? (((IWidget)scrollContainer).ComputedHeight) * 2 : 30;

        if (_viewWidth <= 0) _viewWidth = 60;
        if (_viewHeight <= 0) _viewHeight = 30;

        double zoomW = (double)_viewWidth / _loadedImage.Width;
        double zoomH = (double)_viewHeight / _loadedImage.Height;
        _zoom = Math.Min(zoomW, zoomH);
        _fitMode = true;
        _panX = 0;
        _panY = 0;

        RenderImage();
        ApplyPan();
    }

    private void ApplyPan()
    {
        if (_imagePanel is null) return;
        _imagePanel.PositionX = $"{_panX}ch";
        _imagePanel.PositionY = $"{_panY}ch";
    }

    private void Zoom(double factor)
    {
        _fitMode = false;
        _zoom *= factor;
        _zoom = Math.Max(0.1, Math.Min(10.0, _zoom));
        RenderImage();
        ApplyPan();
    }

    private int _widgetCounter;

    private void RenderImage()
    {
        if (_loadedImage is null || _imagePanel is null) return;

        _imagePanel.Clear();

        int targetW = Math.Max(1, (int)(_loadedImage.Width * _zoom));
        int targetH = Math.Max(1, (int)(_loadedImage.Height * _zoom));

        using var resized = _loadedImage.Clone(ctx => ctx.Resize(targetW, targetH));

        for (int y = 0; y < targetH; y += 2)
        {
            // Build row as horizontal StackPanel with color-grouped segments
            var rowName = $"{_prefix}_row{_widgetCounter++}";
            var rowXml = $"<StackPanel Name='{rowName}' Direction='Horizontal' Width='{targetW}ch' Height='1ch' BackgroundColor='Inherit'>";

            int x = 0;
            while (x < targetW)
            {
                var top = resized[x, y];
                var bot = y + 1 < targetH ? resized[x, y + 1] : new Rgba32(0, 0, 0);

                // Collect consecutive characters with similar colors (tolerance: 30)
                int segStart = x;
                x++;
                while (x < targetW)
                {
                    var nextTop = resized[x, y];
                    var nextBot = y + 1 < targetH ? resized[x, y + 1] : new Rgba32(0, 0, 0);

                    if (ColorDist(top, nextTop) > 30 || ColorDist(bot, nextBot) > 30)
                        break;

                    x++;
                }

                int segLen = x - segStart;

                // Average color of segment
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
                var segName = $"{_prefix}_s{_widgetCounter++}";

                rowXml += $"<Text Name='{segName}' Width='{segLen}ch' Height='1ch' ForegroundColor='{fg}' BackgroundColor='{bg}' AllowWrapping='false'>{content}</Text>";
            }

            rowXml += "</StackPanel>";
            _imagePanel.Add(rowXml);
        }

        var zoomPercent = (int)(_zoom * 100);
        SetStatus($"{Path.GetFileName(_filePath)}  {_loadedImage.Width}x{_loadedImage.Height}  Zoom: {zoomPercent}%");
    }

    private static int ColorDist(Rgba32 a, Rgba32 b)
    {
        return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
    }

    private void SetStatus(string text)
    {
        if (_statusText is not null)
            _statusText.Content = text;
    }
}
