using Termesktop;
using System.Security;
using System.Text.Json;
using TermuiX;
using TermuiX.Widgets;

namespace Termesktop.Components;

public class Taskbar
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".termesktop");
    private static readonly string TaskbarConfigPath = Path.Combine(ConfigDir, "taskbar.json");

    private readonly TermuiX.TermuiX _termui;
    private Text? _timeLabel;
    private Text? _dateLabel;
    private StackPanel? _appIconArea;
    private int _iconBtnCounter;

    private Container? _popup;
    private int _popupBtnCounter;

    public event Action? OnStartClicked;
    public event Action<string>? OnAppClicked;
    public event Action<string>? OnNewInstance;
    public event Action<string>? OnCloseAll;
    public event Action<Window>? OnBringToFront;

    private readonly Dictionary<string, List<Window>> _appWindows = new();
    private readonly Dictionary<string, int> _appCycleIndex = new();

    private List<string> _pinnedAppIds;

    public Taskbar(TermuiX.TermuiX termui)
    {
        _termui = termui;
        _pinnedAppIds = LoadTaskbarPins();
    }

    public string BuildXml()
    {
        return $@"
            <Container Name='taskbar' Width='100%' Height='4ch'
                BackgroundColor='Inherit'
                BorderStyle='Single' RoundedCorners='true'
                PaddingLeft='0ch' PaddingRight='0ch'>

                <StackPanel Direction='Horizontal' Width='100%' Height='2ch'
                    Align='Center'>

                    <!-- Start -->
                    <Button Name='startButton' Width='8ch' Height='2ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#cccccc' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>⬜⬜\n⬜⬜</Button>

                    <Line Orientation='Vertical' Type='Solid' Height='2ch'
                        ForegroundColor='{Theme.Hover}' BackgroundColor='Inherit' />

                    <!-- Dynamic App-Icons -->
                    <StackPanel Name='taskbarIcons' Direction='Horizontal' Width='auto' Height='2ch'
                        BackgroundColor='Inherit' />

                    <Text Width='fill' BackgroundColor='Inherit' />

                    <Line Orientation='Vertical' Type='Solid' Height='2ch'
                        ForegroundColor='{Theme.Hover}' BackgroundColor='Inherit' />

                    <!-- System Tray -->
                    <StackPanel Direction='Vertical' Width='22ch' Height='2ch'
                        Justify='Center' Align='End'>
                        <Text Name='taskbarTime' Width='22ch' Height='1ch'
                            ForegroundColor='#cccccc' BackgroundColor='Inherit'
                            TextAlign='Right' />
                        <Text Name='taskbarDate' Width='22ch' Height='1ch'
                            ForegroundColor='#888888' BackgroundColor='Inherit'
                            TextAlign='Right' />
                    </StackPanel>

                </StackPanel>
            </Container>";
    }

    public void Initialize()
    {
        _timeLabel = _termui.GetWidget<Text>("taskbarTime");
        _dateLabel = _termui.GetWidget<Text>("taskbarDate");
        _appIconArea = _termui.GetWidget<StackPanel>("taskbarIcons");

        var startBtn = _termui.GetWidget<Button>("startButton");
        if (startBtn is not null)
            startBtn.Click += (_, _) => OnStartClicked?.Invoke();

        BuildAppIcons();

        _termui.MouseClick += (_, args) =>
        {
            if ((args.EventType == MouseEventType.LeftButtonPressed || args.EventType == MouseEventType.RightButtonPressed) && _popup is not null)
            {
                ClosePopup();
            }
        };

        _termui.Shortcut += (_, key) =>
        {
            if (key.Key == ConsoleKey.Escape)
                ClosePopup();
        };

        ForceUpdate();
    }

    private void BuildAppIcons()
    {
        if (_appIconArea is null) return;
        _appIconArea.Clear();

        foreach (var appId in _pinnedAppIds)
        {
            var app = AppRegistry.AllApps.FirstOrDefault(a => a.Id == appId);
            if (app is null) continue;

            var btnName = $"tbIcon_{_iconBtnCounter++}";
            var escaped = SecurityElement.Escape(app.Name);

            var count = _appWindows.TryGetValue(appId, out var wins) ? wins.Count : 0;
            var label = count == 0 ? escaped
                : new string('·', Math.Min(count, 3)) + (count > 3 ? count.ToString() : "");

            _appIconArea.Add($@"
                <Button Name='{btnName}' Width='8ch' Height='2ch'
                    BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                    TextColor='#cccccc' FocusTextColor='#ffffff'
                    BorderStyle='None' TextAlign='Center'
                    PaddingTop='0ch' PaddingBottom='0ch'>{app.Icon}\n{label}</Button>");

            var btn = _termui.GetWidget<Button>(btnName);
            var id = app.Id;
            if (btn is not null)
            {
                btn.Click += (_, _) => HandleAppClick(id);
                btn.RightClick += (_, args) => ShowAppPopup(id, args.X, args.Y);
            }
        }
    }

    private void HandleAppClick(string appType)
    {
        ClosePopup();

        if (!_appWindows.TryGetValue(appType, out var windows) || windows.Count == 0)
        {
            OnAppClicked?.Invoke(appType);
            return;
        }

        if (windows.Count == 1)
        {
            var win = windows[0];
            if (win.IsMinimized)
                win.Restore();
            else if (win.IsVisible)
                win.Minimize();
            return;
        }

        if (!_appCycleIndex.TryGetValue(appType, out var idx))
            idx = 0;

        idx = (idx + 1) % windows.Count;
        _appCycleIndex[appType] = idx;

        var target = windows[idx];
        if (target.IsMinimized)
            target.Restore();

        OnBringToFront?.Invoke(target);
    }

    private void ShowAppPopup(string appType, int x, int y)
    {
        ClosePopup();

        var items = new List<(string label, Action action)>();

        items.Add(("New window", () =>
        {
            ClosePopup();
            OnAppClicked?.Invoke(appType);
        }));

        // Quick-access folders for Files
        if (appType == "Files")
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            items.Add(("──────────", () => { }));
            items.Add(("🏠 Home", () => { ClosePopup(); OnNewInstance?.Invoke(home); }));
            items.Add(("📄 Documents", () => { ClosePopup(); OnNewInstance?.Invoke(Path.Combine(home, "Documents")); }));
            items.Add(("⬇ Downloads", () => { ClosePopup(); OnNewInstance?.Invoke(Path.Combine(home, "Downloads")); }));
        }

        if (_appWindows.TryGetValue(appType, out var windows) && windows.Count > 0)
        {
            items.Add(("──────────", () => { }));
            items.Add(("Close all", () =>
            {
                ClosePopup();
                OnCloseAll?.Invoke(appType);
            }));
        }

        items.Add(("──────────", () => { }));
        items.Add(("✕ Unpin from taskbar", () =>
        {
            ClosePopup();
            UnpinFromTaskbar(appType);
        }));

        var popupW = 22;
        var popupH = items.Count + 2;
        var popupY = Math.Max(0, y - popupH);

        var rootContainer = _termui.GetWidget<Container>("rootContainer");
        if (rootContainer is null) return;

        rootContainer.Add($@"
            <Container Name='taskbarPopup' Width='{popupW}ch' Height='{popupH}ch'
                PositionX='{x}ch' PositionY='{popupY}ch'
                BackgroundColor='{Theme.WindowBg}' BorderStyle='Single' RoundedCorners='true'
                BorderColor='{Theme.Border}'>
                <Button Name='tbPopupShield' Width='100%' Height='100%'
                    BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                    TextColor='Inherit' FocusTextColor='Inherit'
                    BorderStyle='None' PaddingTop='0ch' PaddingBottom='0ch' />
                <StackPanel Name='tbPopupItems' Direction='Vertical'
                    Width='100%' Height='auto' BackgroundColor='Inherit' />
            </Container>");

        _popup = _termui.GetWidget<Container>("taskbarPopup");
        var popupList = _termui.GetWidget<StackPanel>("tbPopupItems");
        if (popupList is null) return;

        foreach (var (label, action) in items)
        {
            if (label.StartsWith("──"))
            {
                popupList.Add($@"
                    <Line Orientation='Horizontal' Type='Solid' Width='100%'
                        ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />");
                continue;
            }

            var btnName = $"tbPop_{_popupBtnCounter++}";
            var escaped = SecurityElement.Escape(label);
            popupList.Add($@"
                <Button Name='{btnName}' Width='100%' Height='1ch'
                    BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                    BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                    TextColor='#cccccc' FocusTextColor='#ffffff'
                    PaddingTop='0ch' PaddingBottom='0ch'>{escaped}</Button>");

            var btn = _termui.GetWidget<Button>(btnName);
            var act = action;
            if (btn is not null)
                btn.Click += (_, _) => act();
        }
    }

    private void ClosePopup()
    {
        if (_popup is null) return;
        var rootContainer = _termui.GetWidget<Container>("rootContainer");
        if (rootContainer is not null)
            rootContainer.Remove(_popup);
        _popup = null;
    }

    public void PinToTaskbar(string appId)
    {
        if (_pinnedAppIds.Contains(appId)) return;
        _pinnedAppIds.Add(appId);
        SaveTaskbarPins();
        BuildAppIcons();
    }

    public void UnpinFromTaskbar(string appId)
    {
        _pinnedAppIds.Remove(appId);
        SaveTaskbarPins();
        BuildAppIcons();
    }

    public bool IsTaskbarPinned(string appId) => _pinnedAppIds.Contains(appId);

    public void RegisterWindow(string appType, Window window)
    {
        if (!_appWindows.ContainsKey(appType))
            _appWindows[appType] = [];

        _appWindows[appType].Add(window);
        window.Closed += (_, _) => UnregisterWindow(appType, window);
        UpdateAppIndicator(appType);
    }

    private void UnregisterWindow(string appType, Window window)
    {
        if (_appWindows.TryGetValue(appType, out var list))
        {
            list.Remove(window);
            UpdateAppIndicator(appType);
        }
    }

    private void UpdateAppIndicator(string appType)
    {
        // Rebuild icons to update indicator dots
        BuildAppIcons();
    }

    // Kept for interface compatibility; icon updates are now handled by BuildAppIcons
    public void AddWindowButton(Window window) { }
    public void RemoveWindowButton(Window window) { }

    public void ApplyTheme()
    {
        var taskbar = _termui.GetWidget<Container>("taskbar");
        if (taskbar is null) return;

        var hover = Color.Parse(Theme.Hover);

        // Update all buttons and lines in the taskbar
        UpdateChildTheme((IWidget)taskbar, hover);

        // Rebuild icon buttons to pick up new colors
        BuildAppIcons();
    }

    private void UpdateChildTheme(IWidget widget, Color hover)
    {
        foreach (var child in widget.Children)
        {
            if (child is Button btn)
                btn.FocusBackgroundColor = hover;

            if (child is Line line)
                line.ForegroundColor = hover;

            UpdateChildTheme(child, hover);
        }
    }

    public void Update()
    {
        var now = DateTime.Now;

        if (_timeLabel is not null)
            _timeLabel.Content = now.ToString("HH:mm");

        if (_dateLabel is not null)
            _dateLabel.Content = now.ToString("dd.MM.yyyy");
    }

    private void ForceUpdate()
    {
        Update();
    }

    private void SaveTaskbarPins()
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(TaskbarConfigPath, JsonSerializer.Serialize(_pinnedAppIds, AppJsonContext.Default.ListString));
    }

    private static List<string> LoadTaskbarPins()
    {
        try
        {
            if (File.Exists(TaskbarConfigPath))
            {
                var json = File.ReadAllText(TaskbarConfigPath);
                return JsonSerializer.Deserialize(json, AppJsonContext.Default.ListString) ?? DefaultPins();
            }
        }
        catch { }
        return DefaultPins();
    }

    private static List<string> DefaultPins()
    {
        return ["Files", "Terminal", "Editor", "Monitor"];
    }
}
