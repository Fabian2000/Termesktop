using TermuiX;
using TermuiX.Widgets;

namespace Termesktop.Components;

public class WindowManager
{
    private readonly TermuiX.TermuiX _termui;
    private readonly List<Window> _windows = [];
    private Container? _windowLayer;
    private Taskbar? _taskbar;
    private int _nextId;
    private int _cascadeOffset;
    private Window? _activeWindow;
    private string _themeBg = "#1a1218";
    private string _themeTitleBg = "#241a1a";
    private string _themeBorder = "#3a2a2a";

    public WindowManager(TermuiX.TermuiX termui)
    {
        _termui = termui;
    }

    public void Initialize(Container windowLayer, Taskbar taskbar)
    {
        _windowLayer = windowLayer;
        _taskbar = taskbar;

        _termui.MouseClick += (_, args) =>
        {
            if (args.EventType == MouseEventType.LeftButtonPressed)
                OnMouseDown(args.X, args.Y);
            else if (args.EventType == MouseEventType.Moved)
                OnMouseMove(args.X, args.Y);
            else if (args.EventType == MouseEventType.LeftButtonReleased)
                OnMouseUp();
        };
    }

    public Window OpenWindow(string title, int width, int height, Action<Container, TermuiX.TermuiX>? buildContent = null)
    {
        if (_windowLayer is null)
            throw new InvalidOperationException("WindowManager not initialized");

        var id = $"w{_nextId++}";

        int posX = 5 + _cascadeOffset;
        int posY = 2 + _cascadeOffset;
        _cascadeOffset = (_cascadeOffset + 3) % 15;

        var window = new Window(_termui, id, title, posX, posY, width, height,
            _themeBg, _themeTitleBg, _themeBorder);

        _windowLayer.Add(window.BuildXml());
        window.Initialize();

        if (buildContent is not null && window.ContentArea is not null)
            buildContent(window.ContentArea, _termui);

        window.Closed += (_, _) => OnWindowClosed(window);
        _windows.Add(window);
        _activeWindow = window;

        _taskbar?.AddWindowButton(window);

        window.ApplyTheme(_themeBg, _themeTitleBg, _themeBorder);

        return window;
    }

    public void CloseWindow(Window window)
    {
        window.Close();
    }

    private void OnWindowClosed(Window window)
    {
        if (_windowLayer is null || window.Root is null) return;

        _taskbar?.RemoveWindowButton(window);
        _windowLayer.Remove(window.Root);
        _windows.Remove(window);

        if (_activeWindow == window)
            _activeWindow = _windows.Count > 0 ? _windows[^1] : null;
    }

    private void OnMouseDown(int x, int y)
    {
        // Iterate back-to-front since last element is the topmost window
        for (int i = _windows.Count - 1; i >= 0; i--)
        {
            var win = _windows[i];
            if (win.HitTest(x, y))
            {
                // Bring to front in z-order
                if (win != _activeWindow && _windowLayer is not null)
                {
                    win.BringToFront(_windowLayer);
                    _windows.Remove(win);
                    _windows.Add(win);
                    _activeWindow = win;
                }

                win.HandleMouseDown(x, y);
                return;
            }
        }
    }

    private void OnMouseMove(int x, int y)
    {
        foreach (var win in _windows)
        {
            if (win.IsDragging)
            {
                win.HandleMouseMove(x, y);
                return;
            }
        }
    }

    private void OnMouseUp()
    {
        foreach (var win in _windows)
            win.HandleMouseUp();
    }

    public void BringWindowToFront(Window window)
    {
        if (_windowLayer is null || window.Root is null) return;
        window.BringToFront(_windowLayer);
        _windows.Remove(window);
        _windows.Add(window);
        _activeWindow = window;
    }

    public void CloseAllByType(string title)
    {
        var toClose = _windows.Where(w => w.Title == title).ToList();
        foreach (var win in toClose)
            win.Close();
    }

    public void ApplyThemeToAll(string windowBg, string titleBarBg, string borderColor)
    {
        _themeBg = windowBg;
        _themeTitleBg = titleBarBg;
        _themeBorder = borderColor;

        foreach (var win in _windows)
            win.ApplyTheme(windowBg, titleBarBg, borderColor);
    }

    public IReadOnlyList<Window> Windows => _windows;
}
