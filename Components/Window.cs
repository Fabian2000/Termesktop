using TermuiX;
using TermuiX.Widgets;

namespace Termesktop.Components;

public class Window
{
    private readonly TermuiX.TermuiX _termui;
    private readonly string _id;
    private readonly string _title;
    private Container? _root;
    private Button? _closeButton;
    private Container? _contentArea;

    private int _posX;
    private int _posY;
    private int _width;
    private int _height;

    // Saved size/position to restore after unmaximize
    private int _savedPosX;
    private int _savedPosY;
    private int _savedWidth;
    private int _savedHeight;
    private bool _isMaximized;

    private bool _dragging;
    private int _dragOffsetX;
    private int _dragOffsetY;

    private bool _resizing;
    private int _resizeStartX;
    private int _resizeStartY;
    private int _resizeStartWidth;
    private int _resizeStartHeight;

    private const int MinWidth = 20;
    private const int MinHeight = 8;
    private const int ResizeGripSize = 2;

    // Theme colors
    private string _bgColor;
    private string _titleColor;
    private string _borderColor;

    public string Id => _id;
    public string Title => _title;
    public bool IsVisible => _root?.Visible ?? false;
    public bool IsMinimized { get; private set; }
    public bool IsMaximized => _isMaximized;
    public Container? ContentArea => _contentArea;
    public Container? Root => _root;

    public event EventHandler? Closed;
    public event EventHandler? Minimized;
    public event EventHandler? Restored;

    public Window(TermuiX.TermuiX termui, string id, string title, int posX, int posY, int width, int height,
        string bgColor = "#1a1218", string titleColor = "#241a1a", string borderColor = "#3a2a2a")
    {
        _termui = termui;
        _id = id;
        _title = title;
        _posX = posX;
        _posY = posY;
        _width = width;
        _height = height;
        _bgColor = bgColor;
        _titleColor = titleColor;
        _borderColor = borderColor;
    }

    public string BuildXml()
    {
        var titleTruncated = _title.Length > _width - 8
            ? _title[..(_width - 11)] + "..."
            : _title;

        return $@"
            <Container Name='win_{_id}' Width='{_width}ch' Height='{_height}ch'
                PositionX='{_posX}ch' PositionY='{_posY}ch'
                BackgroundColor='{_bgColor}' BorderStyle='Single' RoundedCorners='true'
                ForegroundColor='{_borderColor}'>

                <!-- Click shield: captures clicks on empty areas to prevent fall-through -->
                <Button Name='winShield_{_id}' Width='100%' Height='100%'
                    BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                    TextColor='Inherit' FocusTextColor='Inherit'
                    BorderStyle='None'
                    PaddingTop='0ch' PaddingBottom='0ch' />

                <!-- Window content: title bar + separator + content area -->
                <StackPanel Direction='Vertical' Width='100%' Height='100%'
                    BackgroundColor='Inherit'>

                    <!-- Title bar -->
                    <StackPanel Name='winTitle_{_id}' Direction='Horizontal' Width='100%' Height='1ch'
                        BackgroundColor='{_titleColor}' Align='Center'>
                        <Text Width='1ch' Height='1ch' BackgroundColor='Inherit' />
                        <Text Name='winTitleText_{_id}' Width='fill' Height='1ch'
                            ForegroundColor='#cccccc' BackgroundColor='Inherit'
                            Style='Bold'>{System.Security.SecurityElement.Escape(titleTruncated)}</Text>
                        <Button Name='winMin_{_id}' Width='3ch' Height='1ch'
                            BackgroundColor='Inherit' FocusBackgroundColor='#3a3a20'
                            TextColor='#888888' FocusTextColor='#ffcc66'
                            BorderStyle='None' TextAlign='Center'
                            PaddingTop='0ch' PaddingBottom='0ch'>─</Button>
                        <Button Name='winMax_{_id}' Width='3ch' Height='1ch'
                            BackgroundColor='Inherit' FocusBackgroundColor='#203a20'
                            TextColor='#888888' FocusTextColor='#66ff66'
                            BorderStyle='None' TextAlign='Center'
                            PaddingTop='0ch' PaddingBottom='0ch'>□</Button>
                        <Button Name='winClose_{_id}' Width='3ch' Height='1ch'
                            BackgroundColor='Inherit' FocusBackgroundColor='#5a2020'
                            TextColor='#888888' FocusTextColor='#ff6666'
                            BorderStyle='None' TextAlign='Center'
                            PaddingTop='0ch' PaddingBottom='0ch'>✕</Button>
                    </StackPanel>

                    <!-- Separator -->
                    <Line Orientation='Horizontal' Type='Solid' Width='100%'
                        ForegroundColor='{_borderColor}' BackgroundColor='Inherit' />

                    <!-- Content area -->
                    <Container Name='winContent_{_id}' Width='100%' Height='fill'
                        BackgroundColor='Inherit' />

                </StackPanel>

                <!-- Resize grip in the bottom-right corner, overlapping the border -->
                <Button Name='winGrip_{_id}' Width='2ch' Height='1ch'
                    PositionX='{_width - 4}ch' PositionY='{_height - 2}ch'
                    BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                    TextColor='{_borderColor}' FocusTextColor='#666666'
                    BorderStyle='None' TextAlign='Right'
                    PaddingTop='0ch' PaddingBottom='0ch'>◢</Button>

            </Container>";
    }

    public void Initialize()
    {
        _root = _termui.GetWidget<Container>($"win_{_id}");
        _closeButton = _termui.GetWidget<Button>($"winClose_{_id}");
        _contentArea = _termui.GetWidget<Container>($"winContent_{_id}");

        if (_closeButton is not null)
            _closeButton.Click += (_, _) => Close();

        var minButton = _termui.GetWidget<Button>($"winMin_{_id}");
        if (minButton is not null)
            minButton.Click += (_, _) => Minimize();

        var maxButton = _termui.GetWidget<Button>($"winMax_{_id}");
        if (maxButton is not null)
            maxButton.Click += (_, _) => ToggleMaximize();
    }

    public void ApplyTheme(string windowBg, string titleBarBg, string borderColor)
    {
        if (_root is null) return;
        _root.BackgroundColor = Color.Parse(windowBg);
        _root.ForegroundColor = Color.Parse(borderColor); // Container border uses ForegroundColor

        var titleBar = _termui.GetWidget<StackPanel>($"winTitle_{_id}");
        if (titleBar is not null)
            titleBar.BackgroundColor = Color.Parse(titleBarBg);
    }

    public void Close()
    {
        if (_root is not null)
            _root.Visible = false;

        Closed?.Invoke(this, EventArgs.Empty);
    }

    public void Minimize()
    {
        if (_root is not null)
            _root.Visible = false;
        IsMinimized = true;
        Minimized?.Invoke(this, EventArgs.Empty);
    }

    public void Restore()
    {
        if (_root is not null)
            _root.Visible = true;
        IsMinimized = false;
        Restored?.Invoke(this, EventArgs.Empty);
    }

    public void Show()
    {
        if (_root is not null)
            _root.Visible = true;
        IsMinimized = false;
    }

    public void ToggleMaximize()
    {
        if (_isMaximized)
            RestoreSize();
        else
            Maximize();
    }

    public void Maximize()
    {
        if (_root is null || _isMaximized) return;

        // Save current size/position for later restore
        _savedPosX = _posX;
        _savedPosY = _posY;
        _savedWidth = _width;
        _savedHeight = _height;
        _isMaximized = true;

        // Go fullscreen but reserve space for the taskbar
        var termWidth = Console.WindowWidth;
        var termHeight = Console.WindowHeight - 4; // Reserve space for taskbar

        _posX = 0;
        _posY = 0;
        _width = termWidth;
        _height = termHeight;

        ApplySize();
        UpdateMaxButton();
    }

    public void RestoreSize()
    {
        if (_root is null || !_isMaximized) return;

        _posX = _savedPosX;
        _posY = _savedPosY;
        _width = _savedWidth;
        _height = _savedHeight;
        _isMaximized = false;

        ApplySize();
        UpdateMaxButton();
    }

    private void ApplySize()
    {
        if (_root is null) return;

        _root.PositionX = $"{_posX}ch";
        _root.PositionY = $"{_posY}ch";
        _root.Width = $"{_width}ch";
        _root.Height = $"{_height}ch";

        // Reposition resize grip to match new window dimensions
        var grip = _termui.GetWidget<Button>($"winGrip_{_id}");
        if (grip is not null)
        {
            grip.PositionX = $"{_width - 4}ch";
            grip.PositionY = $"{_height - 2}ch";
        }
    }

    private void UpdateMaxButton()
    {
        var maxButton = _termui.GetWidget<Button>($"winMax_{_id}");
        if (maxButton is not null)
            maxButton.Text = _isMaximized ? "◻" : "□";
    }

    public void BringToFront(Container desktopLayer)
    {
        if (_root is null) return;
        desktopLayer.Remove(_root);
        desktopLayer.Add(_root);
    }

    public bool HandleMouseDown(int screenX, int screenY)
    {
        if (_root is null || !_root.Visible) return false;

        if (screenX < _posX || screenX >= _posX + _width) return false;
        if (screenY < _posY || screenY >= _posY + _height) return false;

        // Resize grip: bottom-right corner detection
        if (!_isMaximized
            && screenX >= _posX + _width - ResizeGripSize - 1
            && screenY >= _posY + _height - 2)
        {
            _resizing = true;
            _resizeStartX = screenX;
            _resizeStartY = screenY;
            _resizeStartWidth = _width;
            _resizeStartHeight = _height;
            return true;
        }

        // Title bar drag, excluding the button area on the right
        if (screenY == _posY + 1 && screenX < _posX + _width - 10)
        {
            if (_isMaximized) return true; // Disable drag when maximized

            _dragging = true;
            _dragOffsetX = screenX - _posX;
            _dragOffsetY = screenY - _posY;
            return true;
        }

        return true;
    }

    public bool HandleMouseMove(int screenX, int screenY)
    {
        if (_resizing && _root is not null)
        {
            int deltaX = screenX - _resizeStartX;
            int deltaY = screenY - _resizeStartY;

            _width = Math.Max(MinWidth, _resizeStartWidth + deltaX);
            _height = Math.Max(MinHeight, _resizeStartHeight + deltaY);

            ApplySize();
            return true;
        }

        if (_dragging && _root is not null)
        {
            _posX = screenX - _dragOffsetX;
            _posY = screenY - _dragOffsetY;

            if (_posX < 0) _posX = 0;
            if (_posY < 0) _posY = 0;

            _root.PositionX = $"{_posX}ch";
            _root.PositionY = $"{_posY}ch";
            return true;
        }

        return false;
    }

    public void HandleMouseUp()
    {
        _dragging = false;
        _resizing = false;
    }

    public bool IsDragging => _dragging || _resizing;

    public bool HitTest(int screenX, int screenY)
    {
        if (_root is null || !_root.Visible) return false;
        return screenX >= _posX && screenX < _posX + _width
            && screenY >= _posY && screenY < _posY + _height;
    }
}
