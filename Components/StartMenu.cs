using System.Security;
using TermuiX;
using TermuiX.Widgets;

namespace Termesktop.Components;

public class StartMenu
{
    private readonly TermuiX.TermuiX _termui;
    private readonly AppRegistry _appRegistry = new();
    private Container? _root;
    private Container? _contentContainer;
    private Container? _rootContainer;
    private StackPanel? _appListPanel;
    private Button? _viewToggleBtn;

    private Container? _pinPopup;
    private int _pinPopupX;
    private int _pinPopupY;
    private int _pinPopupCounter;

    private string? _dragAppId;
    private int _dragStartX;
    private int _dragStartY;
    private bool _dragMoved;
    private Text? _dragIndicator;
    private readonly Dictionary<string, (int x, int y, int w, int h)> _pinnedHitAreas = new();

    private bool _showingAll;

    private const int TargetWidth = 36;
    private const int TargetHeight = 22;

    private int _currentHeight;
    private bool _animating;
    private bool _opening;
    private int _posX = 2;

    public bool IsOpen => _root is not null && _root.Visible;

    public event Action<string>? OnAppClicked;
    public event Action? OnShutdown;

    private Taskbar? _taskbar;

    public StartMenu(TermuiX.TermuiX termui)
    {
        _termui = termui;
    }

    public void Build(Container rootContainer, int taskbarHeight, Taskbar? taskbar = null)
    {
        _rootContainer = rootContainer;
        _taskbar = taskbar;

        rootContainer.Add($@"
            <Container Name='startMenu' Width='{TargetWidth}ch' Height='0ch'
                PositionX='{_posX}ch' PositionY='0ch'
                BackgroundColor='{Theme.Darker}' BorderStyle='Single' RoundedCorners='true'
                BorderColor='{Theme.Border}' Visible='false'>

                <Button Name='startMenuShield' Width='100%' Height='100%'
                    BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                    TextColor='Inherit' FocusTextColor='Inherit'
                    BorderStyle='None' PaddingTop='0ch' PaddingBottom='0ch' />

                <Container Name='startMenuContent' Width='100%' Height='100%'
                    BackgroundColor='Inherit' Visible='false'>
                    <StackPanel Direction='Vertical' Width='100%' Height='100%'
                        BackgroundColor='Inherit'>

                        <!-- Search -->
                        <Container Width='100%' Height='3ch' BackgroundColor='Inherit'
                            PaddingLeft='1ch' PaddingRight='1ch'>
                            <Container Name='startMenuSearchBorder' Width='100%' Height='3ch'
                                BorderStyle='Single' RoundedCorners='true'
                                BackgroundColor='Inherit' ForegroundColor='{Theme.Border}'>
                                <Input Name='startMenuSearch' Width='100%' Height='1ch'
                                    Placeholder='🔍 Search apps...'
                                    BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                                    ForegroundColor='#cccccc' FocusForegroundColor='#cccccc'
                                    PlaceholderColor='#555555'
                                    CursorColor='#cccccc'
                                    PaddingLeft='0ch' PaddingRight='0ch' />
                            </Container>
                        </Container>

                        <!-- View Header: Pinned / All apps toggle -->
                        <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                            BackgroundColor='Inherit' Justify='SpaceBetween' Align='Center'>
                            <Text Width='fill' Height='1ch' PaddingLeft='2ch'
                                ForegroundColor='#888888' BackgroundColor='Inherit'
                                Style='Bold'>Pinned</Text>
                            <Button Name='startMenuViewToggle' Width='12ch' Height='1ch'
                                BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                                TextColor='#666666' FocusTextColor='#cccccc'
                                BorderStyle='None' TextAlign='Right'
                                PaddingTop='0ch' PaddingBottom='0ch' PaddingRight='1ch'>All apps ></Button>
                        </StackPanel>

                        <!-- App Area -->
                        <Container Width='100%' Height='fill' ScrollY='true'
                            BackgroundColor='Inherit'>
                            <StackPanel Name='startMenuApps' Direction='Vertical'
                                Width='100%' Height='auto' BackgroundColor='Inherit' />
                        </Container>

                        <!-- Footer -->
                        <Line Orientation='Horizontal' Type='Dotted' Width='100%'
                            ForegroundColor='{Theme.Hover}' BackgroundColor='Inherit' />
                        <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                            BackgroundColor='{Theme.Subtle}' Justify='SpaceBetween' Align='Center'>
                            <Text Name='startMenuUser' Width='fill' Height='1ch'
                                ForegroundColor='#777777' BackgroundColor='Inherit'
                                PaddingLeft='2ch' />
                            <Button Name='startMenuSettings' Width='4ch' Height='1ch'
                                BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                                TextColor='#777777' FocusTextColor='#cccccc'
                                BorderStyle='None' TextAlign='Center'
                                PaddingTop='0ch' PaddingBottom='0ch'>⚙</Button>
                            <Button Name='startMenuQuit' Width='4ch' Height='1ch'
                                BackgroundColor='Inherit' FocusBackgroundColor='#3a1010'
                                TextColor='#553333' FocusTextColor='#ff5555'
                                BorderStyle='None' TextAlign='Center'
                                PaddingTop='0ch' PaddingBottom='0ch'>⏻</Button>
                        </StackPanel>

                    </StackPanel>
                </Container>

            </Container>");

        _root = _termui.GetWidget<Container>("startMenu");
        _contentContainer = _termui.GetWidget<Container>("startMenuContent");
        _appListPanel = _termui.GetWidget<StackPanel>("startMenuApps");
        _viewToggleBtn = _termui.GetWidget<Button>("startMenuViewToggle");

        var userText = _termui.GetWidget<Text>("startMenuUser");
        if (userText is not null)
            userText.Content = Environment.UserName;

        var quitBtn = _termui.GetWidget<Button>("startMenuQuit");
        if (quitBtn is not null)
            quitBtn.Click += (_, _) => OnShutdown?.Invoke();

        var settingsBtn = _termui.GetWidget<Button>("startMenuSettings");
        if (settingsBtn is not null)
            settingsBtn.Click += (_, _) =>
            {
                Close();
                OnAppClicked?.Invoke("Settings");
            };

        if (_viewToggleBtn is not null)
            _viewToggleBtn.Click += (_, _) => ToggleView();

        var searchInput = _termui.GetWidget<Input>("startMenuSearch");
        if (searchInput is not null)
        {
            searchInput.TextChanged += (_, text) =>
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    if (_showingAll)
                        BuildAllAppsView();
                    else
                        BuildPinnedView();
                }
                else
                {
                    BuildFilteredView(text);
                }
            };
        }

        BuildPinnedView();

        _termui.Shortcut += (_, key) =>
        {
            if (key.Key == ConsoleKey.Escape)
            {
                ClosePinPopup();
                if (IsOpen) Close();
            }
        };

        _termui.MouseClick += (_, args) =>
        {
            if (args.EventType == MouseEventType.LeftButtonPressed)
            {
                if (_pinPopup is not null && _pinPopup.Visible)
                {
                    ClosePinPopup();
                    return;
                }

                // Start drag if clicking on a pinned item
                if (IsOpen && !_animating && !_showingAll)
                {
                    var hitApp = GetPinnedAppAt(args.X, args.Y);
                    if (hitApp is not null)
                    {
                        _dragAppId = hitApp;
                        _dragStartX = args.X;
                        _dragStartY = args.Y;
                        _dragMoved = false;
                        return;
                    }
                }

                if (IsOpen && !_animating && !HitTest(args.X, args.Y))
                    Close();
            }
            else if (args.EventType == MouseEventType.Moved && _dragAppId is not null)
            {
                var dx = Math.Abs(args.X - _dragStartX);
                var dy = Math.Abs(args.Y - _dragStartY);
                if (dx > 1 || dy > 1)
                {
                    _dragMoved = true;
                    UpdateDragIndicator(args.X, args.Y);
                }
            }
            else if (args.EventType == MouseEventType.LeftButtonReleased && _dragAppId is not null)
            {
                RemoveDragIndicator();
                var draggedId = _dragAppId;
                var wasDrag = _dragMoved;
                _dragAppId = null;
                _dragMoved = false;

                if (wasDrag)
                {
                    // Drop: check if landed on a different pinned item to reorder
                    var dropApp = GetPinnedAppAt(args.X, args.Y);
                    if (dropApp is not null && dropApp != draggedId)
                    {
                        var fromIdx = _appRegistry.GetPinnedIndex(draggedId);
                        var toIdx = _appRegistry.GetPinnedIndex(dropApp);
                        if (fromIdx >= 0 && toIdx >= 0)
                        {
                            int delta = toIdx - fromIdx;
                            int step = delta > 0 ? 1 : -1;
                            for (int i = 0; i < Math.Abs(delta); i++)
                                _appRegistry.MovePinned(draggedId, step);

                            BuildPinnedView();
                        }
                    }
                }
                else
                {
                    // Not a drag, treat as click to open the app
                    Close();
                    OnAppClicked?.Invoke(draggedId);
                }
            }
        };
    }

    private void ToggleView()
    {
        _showingAll = !_showingAll;

        if (_viewToggleBtn is not null)
            _viewToggleBtn.Text = _showingAll ? "< Pinned" : "All apps >";

        // No direct access to the header label; the toggle button text indicates the current view

        if (_showingAll)
            BuildAllAppsView();
        else
            BuildPinnedView();
    }

    private int _appBtnCounter;

    private void BuildPinnedView()
    {
        if (_appListPanel is null) return;
        _appListPanel.Clear();
        _pinnedHitAreas.Clear();

        var pinned = _appRegistry.GetPinned();

        // Compute absolute positions for drag hit-testing.
        // X: _posX + 1(border) + 1(padding). Y: menuTop + 1(border) + 3(search) + 1(label).
        // Each row is 3ch tall.
        int termHeight = Console.WindowHeight;
        int menuTopY = termHeight - 4 - TargetHeight;
        int gridBaseX = _posX + 1 + 1; // border + paddingLeft
        int gridBaseY = menuTopY + 1 + 3 + 1; // border + search + label

        for (int row = 0; row < pinned.Count; row += 3)
        {
            var rowXml = @"<StackPanel Direction='Horizontal' Width='100%' Height='3ch'
                BackgroundColor='Inherit' Justify='Start' PaddingLeft='1ch' PaddingRight='1ch'>";

            var rowApps = new List<AppEntry>();
            for (int col = 0; col < 3 && row + col < pinned.Count; col++)
                rowApps.Add(pinned[row + col]);

            foreach (var app in rowApps)
            {
                var btnName = $"smA_{_appBtnCounter++}";
                var escaped = SecurityElement.Escape(app.Name);
                rowXml += $@"
                    <Button Name='{btnName}' Width='10ch' Height='3ch'
                        BorderStyle='None' TextAlign='Center'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#aaaaaa' FocusTextColor='#ffffff'
                        PaddingTop='0ch' PaddingBottom='0ch'>\n{app.Icon}\n{escaped}</Button>";
            }

            rowXml += "</StackPanel>";
            _appListPanel.Add(rowXml);

            // Register events and hit-areas for drag support
            int i = 0;
            foreach (var app in rowApps)
            {
                var btnName = $"smA_{_appBtnCounter - rowApps.Count + i}";
                var btn = _termui.GetWidget<Button>(btnName);
                var appId = app.Id;

                int colIdx = (row + i) % 3;
                int rowIdx = (row + i) / 3;
                _pinnedHitAreas[appId] = (gridBaseX + colIdx * 10, gridBaseY + rowIdx * 3, 10, 3);

                if (btn is not null)
                {
                    // No click handler here; click-to-open and drag are
                    // handled entirely by the global mouse handler
                    btn.RightClick += (_, args) => ShowPinPopup(appId, args.X, args.Y, true);
                }
                i++;
            }
        }
    }

    private void BuildAllAppsView()
    {
        if (_appListPanel is null) return;
        _appListPanel.Clear();

        var all = _appRegistry.GetAll();

        foreach (var app in all)
        {
            var btnName = $"smA_{_appBtnCounter++}";
            var escaped = SecurityElement.Escape(app.Name);
            var pinIndicator = _appRegistry.IsPinned(app.Id) ? " 📌" : "";

            _appListPanel.Add($@"
                <Button Name='{btnName}' Width='100%' Height='1ch'
                    BorderStyle='None' TextAlign='Left' PaddingLeft='2ch'
                    BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                    TextColor='#aaaaaa' FocusTextColor='#ffffff'
                    PaddingTop='0ch' PaddingBottom='0ch'>{app.Icon}  {escaped}{pinIndicator}</Button>");

            var btn = _termui.GetWidget<Button>(btnName);
            var appId = app.Id;
            if (btn is not null)
            {
                btn.Click += (_, _) =>
                {
                    Close();
                    OnAppClicked?.Invoke(appId);
                };
                btn.RightClick += (_, args) => ShowPinPopup(appId, args.X, args.Y, false);
            }
        }
    }

    private void BuildFilteredView(string query)
    {
        if (_appListPanel is null) return;
        _appListPanel.Clear();

        var filtered = _appRegistry.GetAll()
            .Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (filtered.Count == 0)
        {
            _appListPanel.Add($@"
                <Text Width='100%' Height='1ch' PaddingLeft='2ch'
                    ForegroundColor='#555555' BackgroundColor='Inherit'>No results</Text>");
            return;
        }

        foreach (var app in filtered)
        {
            var btnName = $"smA_{_appBtnCounter++}";
            var escaped = SecurityElement.Escape(app.Name);

            _appListPanel.Add($@"
                <Button Name='{btnName}' Width='100%' Height='1ch'
                    BorderStyle='None' TextAlign='Left' PaddingLeft='2ch'
                    BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                    TextColor='#aaaaaa' FocusTextColor='#ffffff'
                    PaddingTop='0ch' PaddingBottom='0ch'>{app.Icon}  {escaped}</Button>");

            var btn = _termui.GetWidget<Button>(btnName);
            var appId = app.Id;
            if (btn is not null)
                btn.Click += (_, _) =>
                {
                    Close();
                    OnAppClicked?.Invoke(appId);
                };
        }
    }

    private void ShowPinPopup(string appId, int x, int y, bool isPinned)
    {
        ClosePinPopup();
        _pinPopupX = x;
        _pinPopupY = y;

        var items = new List<(string label, Action action)>();

        if (isPinned)
        {
            var idx = _appRegistry.GetPinnedIndex(appId);
            var pinnedCount = _appRegistry.GetPinned().Count;

            if (idx > 0)
                items.Add(("← Move left", () =>
                {
                    _appRegistry.MovePinned(appId, -1);
                    ClosePinPopup();
                    BuildPinnedView();
                }));

            if (idx < pinnedCount - 1)
                items.Add(("→ Move right", () =>
                {
                    _appRegistry.MovePinned(appId, 1);
                    ClosePinPopup();
                    BuildPinnedView();
                }));

            items.Add(("✕ Unpin from Start", () =>
            {
                _appRegistry.Unpin(appId);
                ClosePinPopup();
                BuildPinnedView();
            }));

            if (_taskbar is not null)
            {
                if (_taskbar.IsTaskbarPinned(appId))
                    items.Add(("✕ Unpin from taskbar", () =>
                    {
                        _taskbar.UnpinFromTaskbar(appId);
                        ClosePinPopup();
                    }));
                else
                    items.Add(("📌 Pin to taskbar", () =>
                    {
                        _taskbar.PinToTaskbar(appId);
                        ClosePinPopup();
                    }));
            }
        }
        else
        {
            items.Add(("📌 Pin to Start", () =>
            {
                _appRegistry.Pin(appId);
                ClosePinPopup();
                if (_showingAll) BuildAllAppsView();
            }));

            if (_taskbar is not null)
            {
                if (_taskbar.IsTaskbarPinned(appId))
                    items.Add(("✕ Unpin from taskbar", () =>
                    {
                        _taskbar.UnpinFromTaskbar(appId);
                        ClosePinPopup();
                    }));
                else
                    items.Add(("📌 Pin to taskbar", () =>
                    {
                        _taskbar.PinToTaskbar(appId);
                        ClosePinPopup();
                    }));
            }
        }

        var popupW = 18;
        var popupH = items.Count + 2;

        // Shift popup upward if it would extend beyond the screen edge
        var adjustedY = Math.Max(0, y - popupH);

        _rootContainer?.Add($@"
            <Container Name='pinPopup' Width='{popupW}ch' Height='{popupH}ch'
                PositionX='{x}ch' PositionY='{adjustedY}ch'
                BackgroundColor='{Theme.Subtle}' BorderStyle='Single' RoundedCorners='true'
                BorderColor='{Theme.Border}'>
                <Button Name='pinPopupShield_{_pinPopupCounter}' Width='100%' Height='100%'
                    BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                    TextColor='Inherit' FocusTextColor='Inherit'
                    BorderStyle='None' PaddingTop='0ch' PaddingBottom='0ch' />
                <StackPanel Name='pinPopupItems_{_pinPopupCounter}' Direction='Vertical'
                    Width='100%' Height='auto' BackgroundColor='Inherit' />
            </Container>");

        _pinPopup = _termui.GetWidget<Container>("pinPopup");
        var popupList = _termui.GetWidget<StackPanel>($"pinPopupItems_{_pinPopupCounter}");
        _pinPopupCounter++;

        if (popupList is null) return;

        int btnIdx = 0;
        foreach (var (label, action) in items)
        {
            var btnName = $"ppBtn_{_pinPopupCounter}_{btnIdx++}";
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

    private string? GetPinnedAppAt(int x, int y)
    {
        foreach (var (appId, area) in _pinnedHitAreas)
        {
            if (x >= area.x && x < area.x + area.w && y >= area.y && y < area.y + area.h)
                return appId;
        }
        return null;
    }

    private void UpdateDragIndicator(int x, int y)
    {
        RemoveDragIndicator();

        if (_dragAppId is null) return;
        var app = AppRegistry.AllApps.FirstOrDefault(a => a.Id == _dragAppId);
        if (app is null) return;

        _rootContainer?.Add($@"
            <Text Name='dragIndicator' Width='10ch' Height='1ch'
                PositionX='{x + 1}ch' PositionY='{y}ch'
                ForegroundColor='#ffffff' BackgroundColor='#3a1515'
                Style='Bold'> {app.Icon} {SecurityElement.Escape(app.Name)}</Text>");

        _dragIndicator = _termui.GetWidget<Text>("dragIndicator");
    }

    private void RemoveDragIndicator()
    {
        if (_dragIndicator is not null && _rootContainer is not null)
        {
            _rootContainer.Remove(_dragIndicator);
            _dragIndicator = null;
        }
    }

    private void ClosePinPopup()
    {
        if (_pinPopup is null) return;
        _rootContainer?.Remove(_pinPopup);
        _pinPopup = null;
    }

    public void ApplyTheme()
    {
        if (_root is null) return;

        var darker = Color.Parse(Theme.Darker);
        var subtle = Color.Parse(Theme.Subtle);
        var border = Color.Parse(Theme.Border);
        var hover = Color.Parse(Theme.Hover);

        // Root container
        _root.BackgroundColor = darker;
        _root.ForegroundColor = border;

        // Walk all children and update colors based on their current values
        UpdateChildTheme((IWidget)_root, darker, subtle, border, hover);

        // Search border
        var searchBorder = _termui.GetWidget<Container>("startMenuSearchBorder");
        if (searchBorder is not null)
        {
            searchBorder.BackgroundColor = Color.Parse("Inherit");
            searchBorder.ForegroundColor = border;
        }

        // Rebuild pinned/all apps to pick up new Theme.Hover
        if (_showingAll)
            BuildAllAppsView();
        else
            BuildPinnedView();
    }

    private void UpdateChildTheme(IWidget widget, Color darker, Color subtle, Color border, Color hover)
    {
        foreach (var child in widget.Children)
        {
            // Update FocusBackgroundColor on buttons (except semantic ones like quit/delete)
            if (child is Button btn)
            {
                // Skip semantic colors (red for quit/delete)
                var focusBg = btn.FocusBackgroundColor;
                if (!focusBg.IsRgb || (focusBg.R > 50 && focusBg.G < 30))
                    continue; // Skip red-tinted buttons

                btn.FocusBackgroundColor = hover;
            }

            // Update line separators
            if (child is Line line)
            {
                line.ForegroundColor = child.ForegroundColor.IsInherit ? border : hover;
            }

            // Update subtle/darker backgrounds on StackPanels
            if (child is StackPanel sp && !child.BackgroundColor.IsInherit)
            {
                // Distinguish subtle vs darker by checking current brightness
                if (child.BackgroundColor.IsRgb)
                {
                    var brightness = child.BackgroundColor.R + child.BackgroundColor.G + child.BackgroundColor.B;
                    if (brightness < 80)
                        sp.BackgroundColor = darker;
                    else
                        sp.BackgroundColor = subtle;
                }
            }

            // Recurse
            UpdateChildTheme(child, darker, subtle, border, hover);
        }
    }

    public void Toggle()
    {
        if (_animating) return;

        if (IsOpen)
            Close();
        else
            Open();
    }

    public void Open()
    {
        if (_root is null || IsOpen || _animating) return;

        if (_rootContainer is not null)
        {
            _rootContainer.Remove(_root);
            _rootContainer.Add(_root);
        }

        // Reset to pinned view on open
        _showingAll = false;
        if (_viewToggleBtn is not null)
            _viewToggleBtn.Text = "All apps >";
        BuildPinnedView();

        _root.Visible = true;
        _currentHeight = 2;
        _opening = true;
        _animating = true;

        if (_contentContainer is not null)
            _contentContainer.Visible = false;

        ApplySize();
    }

    public void Close()
    {
        if (_root is null || !IsOpen || _animating) return;

        ClosePinPopup();
        _opening = false;
        _animating = true;

        if (_contentContainer is not null)
            _contentContainer.Visible = false;
    }

    public void Update()
    {
        if (!_animating || _root is null) return;

        if (_opening)
        {
            int step = Math.Max(2, (TargetHeight - _currentHeight) / 2);
            _currentHeight = Math.Min(_currentHeight + step, TargetHeight);

            if (_currentHeight >= TargetHeight)
            {
                _currentHeight = TargetHeight;
                _animating = false;
                if (_contentContainer is not null)
                    _contentContainer.Visible = true;
            }
        }
        else
        {
            int step = Math.Max(2, _currentHeight / 2);
            _currentHeight = Math.Max(0, _currentHeight - step);

            if (_currentHeight <= 0)
            {
                _currentHeight = 0;
                _animating = false;
                _root.Visible = false;
                return;
            }
        }

        ApplySize();
    }

    private void ApplySize()
    {
        if (_root is null) return;

        int termHeight = Console.WindowHeight;
        int bottomY = termHeight - 4;
        int topY = bottomY - _currentHeight;

        _root.Height = $"{_currentHeight}ch";
        _root.PositionY = $"{topY}ch";
    }

    private bool HitTest(int x, int y)
    {
        if (_root is null || !_root.Visible) return false;
        int termHeight = Console.WindowHeight;
        int bottomY = termHeight - 4;
        int topY = bottomY - _currentHeight;
        return x >= _posX && x < _posX + TargetWidth
            && y >= topY && y < bottomY;
    }
}
