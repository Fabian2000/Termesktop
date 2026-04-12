using System.Security;
using TermuiX;
using TermuiX.Widgets;
using Termesktop.Components;

namespace Termesktop.Apps;

public class SettingsApp
{
    private static int _instanceCount;

    private readonly TermuiX.TermuiX _termui;
    private readonly int _instanceId;
    private readonly string _prefix;
    private readonly DesktopSettings _settings;

    private Container? _rootContainer;
    private Container? _contentPanel;
    private Text? _resolutionText;


    public event Action<DesktopSettings>? OnSettingsChanged;

    public SettingsApp(TermuiX.TermuiX termui)
    {
        _termui = termui;
        _instanceId = _instanceCount++;
        _prefix = $"set{_instanceId}";
        _settings = DesktopSettings.Load();
    }

    public static string Title => "Settings";

    public void BuildContent(Container contentArea, TermuiX.TermuiX termui)
    {
        _rootContainer = termui.GetWidget<Container>("rootContainer");

        contentArea.Add($@"
            <StackPanel Direction='Horizontal' Width='100%' Height='100%' BackgroundColor='Inherit'>

                <!-- Sidebar -->
                <StackPanel Direction='Vertical' Width='16ch' Height='100%'
                    BackgroundColor='{Theme.Darker}'>
                    <Text Width='16ch' Height='1ch' PaddingLeft='1ch'
                        ForegroundColor='#666666' BackgroundColor='Inherit'
                        Style='Bold'>Settings</Text>
                    <Line Orientation='Horizontal' Type='Dotted' Width='100%'
                        ForegroundColor='{Theme.Hover}' BackgroundColor='Inherit' />
                    <Button Name='{_prefix}_navDesign' Width='16ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#aaaaaa' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                        PaddingTop='0ch' PaddingBottom='0ch'>🎨 Design</Button>
                    <Button Name='{_prefix}_navDisplay' Width='16ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#aaaaaa' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                        PaddingTop='0ch' PaddingBottom='0ch'>🖥 Display</Button>
                    <Button Name='{_prefix}_navDesktop' Width='16ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#aaaaaa' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                        PaddingTop='0ch' PaddingBottom='0ch'>📂 Desktop</Button>
                    <Button Name='{_prefix}_navSystem' Width='16ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#aaaaaa' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                        PaddingTop='0ch' PaddingBottom='0ch'>⌨ System</Button>
                </StackPanel>

                <Line Orientation='Vertical' Type='Solid' Height='100%'
                    ForegroundColor='{Theme.Hover}' BackgroundColor='Inherit' />

                <!-- Content area -->
                <Container Name='{_prefix}_content' Width='fill' Height='100%'
                    BackgroundColor='Inherit' />

            </StackPanel>");

        _contentPanel = termui.GetWidget<Container>($"{_prefix}_content");

        var navDesign = termui.GetWidget<Button>($"{_prefix}_navDesign");
        var navDisplay = termui.GetWidget<Button>($"{_prefix}_navDisplay");
        var navDesktop = termui.GetWidget<Button>($"{_prefix}_navDesktop");
        var navSystem = termui.GetWidget<Button>($"{_prefix}_navSystem");

        if (navDesign is not null) navDesign.Click += (_, _) => ShowDesignPage();
        if (navDisplay is not null) navDisplay.Click += (_, _) => ShowDisplayPage();
        if (navDesktop is not null) navDesktop.Click += (_, _) => ShowDesktopPage();
        if (navSystem is not null) navSystem.Click += (_, _) => ShowSystemPage();

        ShowDesignPage();
    }

    private void ClearContent()
    {
        _contentPanel?.Clear();
    }

    private void ApplyAndSave()
    {
        _settings.Save();
        OnSettingsChanged?.Invoke(_settings);
    }

    // ===== Design Page =====

    private void ShowDesignPage()
    {
        ClearContent();
        if (_contentPanel is null) return;

        _contentPanel.Add($@"
            <StackPanel Direction='Vertical' Width='100%' Height='100%'
                BackgroundColor='Inherit' PaddingLeft='1ch' PaddingRight='1ch'>

                <Text Width='100%' Height='1ch' ForegroundColor='#cccccc'
                    BackgroundColor='Inherit' Style='Bold'>🎨 Design</Text>
                <Line Orientation='Horizontal' Type='Dotted' Width='100%'
                    ForegroundColor='{Theme.Hover}' BackgroundColor='Inherit' />

                {ColorRow("Background", _settings.BackgroundColor, "bgColor")}
                {ColorRow("Clock", _settings.ClockColor, "clockColor")}
                {ColorRow("Date", _settings.DateColor, "dateColor")}
                {ColorRow("Window BG", _settings.WindowBackgroundColor, "winBgColor")}
                {ColorRow("Window Title", _settings.WindowTitleBarColor, "winTitleColor")}
                {ColorRow("Window Border", _settings.WindowBorderColor, "winBorderColor")}

                <Text Width='100%' Height='1ch' ForegroundColor='#666666'
                    BackgroundColor='Inherit' MarginTop='1ch'>Presets</Text>
                <StackPanel Direction='Horizontal' Width='100%' Height='3ch'
                    BackgroundColor='Inherit'>
                    {PresetButton("Dark Red", "#1a0a0a", "#cccccc", "pDark")}
                    {PresetButton("Midnight", "#0a0a1a", "#aaaacc", "pMid")}
                    {PresetButton("Forest", "#0a1a0a", "#aaccaa", "pFor")}
                    {PresetButton("Noir", "#0d0d0d", "#ffffff", "pNoir")}
                </StackPanel>

            </StackPanel>");

        BindColorRow("bgColor", _settings.BackgroundColor, c => { _settings.BackgroundColor = c; ApplyAndSave(); });
        BindColorRow("clockColor", _settings.ClockColor, c => { _settings.ClockColor = c; ApplyAndSave(); });
        BindColorRow("dateColor", _settings.DateColor, c => { _settings.DateColor = c; ApplyAndSave(); });
        BindColorRow("winBgColor", _settings.WindowBackgroundColor, c => { _settings.WindowBackgroundColor = c; ApplyAndSave(); });
        BindColorRow("winTitleColor", _settings.WindowTitleBarColor, c => { _settings.WindowTitleBarColor = c; ApplyAndSave(); });
        BindColorRow("winBorderColor", _settings.WindowBorderColor, c => { _settings.WindowBorderColor = c; ApplyAndSave(); });

        BindPreset("pDark", "#1a0a0a", "#cccccc", "#888888", "#1a1218", "#241a1a", "#3a2a2a");
        BindPreset("pMid", "#0a0a1a", "#aaaacc", "#6666aa", "#12121a", "#1a1a24", "#2a2a3a");
        BindPreset("pFor", "#0a1a0a", "#aaccaa", "#66aa66", "#0e1a12", "#1a241a", "#2a3a2a");
        BindPreset("pNoir", "#0d0d0d", "#ffffff", "#888888", "#151515", "#1e1e1e", "#333333");
    }

    private string ColorRow(string label, string color, string id)
    {
        var escaped = SecurityElement.Escape(color);
        var name = $"{_prefix}_{id}";
        return $@"
            <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                BackgroundColor='Inherit' Align='Center'>
                <Text Width='15ch' Height='1ch' ForegroundColor='#888888'
                    BackgroundColor='Inherit'>{label}</Text>
                <Container Name='{name}_preview' Width='3ch' Height='1ch'
                    BackgroundColor='{escaped}' />
                <Text Name='{name}_label' Width='10ch' Height='1ch'
                    ForegroundColor='#666666' BackgroundColor='Inherit'
                    PaddingLeft='1ch'>{escaped}</Text>
                <Button Name='{name}_btn' Width='8ch' Height='1ch'
                    BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                    TextColor='#888888' FocusTextColor='#cccccc'
                    BorderStyle='None' TextAlign='Center'
                    PaddingTop='0ch' PaddingBottom='0ch'>Change</Button>
            </StackPanel>";
    }

    private void BindColorRow(string id, string currentColor, Action<string> onChanged)
    {
        var name = $"{_prefix}_{id}";
        var btn = _termui.GetWidget<Button>($"{name}_btn");
        if (btn is not null && _rootContainer is not null)
        {
            btn.Click += (_, _) =>
            {
                var picker = new ColorPicker(_termui);
                picker.Show(_rootContainer, currentColor, newColor =>
                {
                    // Update preview and label
                    var preview = _termui.GetWidget<Container>($"{name}_preview");
                    var label = _termui.GetWidget<Text>($"{name}_label");
                    if (preview is not null) preview.BackgroundColor = Color.Parse(newColor);
                    if (label is not null) label.Content = newColor;
                    onChanged(newColor);
                });
            };
        }
    }

    private string PresetButton(string label, string bg, string fg, string id)
    {
        var name = $"{_prefix}_{id}";
        return $@"
            <Button Name='{name}' Width='10ch'
                BackgroundColor='{bg}' FocusBackgroundColor='{bg}'
                TextColor='{fg}' FocusTextColor='#ffffff'
                BorderStyle='Single' RoundedCorners='true'
                BorderColor='#3a3a3a' MarginRight='1ch'>{SecurityElement.Escape(label)}</Button>";
    }

    private void BindPreset(string id, string bg, string clock, string date,
        string winBg, string winTitle, string winBorder)
    {
        var btn = _termui.GetWidget<Button>($"{_prefix}_{id}");
        if (btn is not null)
        {
            btn.Click += (_, _) =>
            {
                _settings.BackgroundColor = bg;
                _settings.ClockColor = clock;
                _settings.DateColor = date;
                _settings.WindowBackgroundColor = winBg;
                _settings.WindowTitleBarColor = winTitle;
                _settings.WindowBorderColor = winBorder;
                ApplyAndSave();
                ShowDesignPage();
            };
        }
    }

    // ===== Display Page =====

    private void ShowDisplayPage()
    {
        ClearContent();
        if (_contentPanel is null) return;

        var clock24 = _settings.Use24HourFormat ? "24h ✓" : "12h";
        var clockOn = _settings.ShowClock ? "on ✓" : "off";
        var dateOn = _settings.ShowDate ? "on ✓" : "off";

        _contentPanel.Add($@"
            <StackPanel Direction='Vertical' Width='100%' Height='100%'
                BackgroundColor='Inherit' PaddingLeft='1ch' PaddingRight='1ch'>

                <Text Width='100%' Height='1ch' ForegroundColor='#cccccc'
                    BackgroundColor='Inherit' Style='Bold'>🖥 Display</Text>
                <Line Orientation='Horizontal' Type='Dotted' Width='100%'
                    ForegroundColor='{Theme.Hover}' BackgroundColor='Inherit' />

                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='Inherit' Align='Center'>
                    <Text Width='15ch' Height='1ch' ForegroundColor='#888888'
                        BackgroundColor='Inherit'>Show Clock</Text>
                    <Button Name='{_prefix}_toggleClock' Width='8ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#cccccc' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>{clockOn}</Button>
                </StackPanel>

                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='Inherit' Align='Center'>
                    <Text Width='15ch' Height='1ch' ForegroundColor='#888888'
                        BackgroundColor='Inherit'>Show Date</Text>
                    <Button Name='{_prefix}_toggleDate' Width='8ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#cccccc' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>{dateOn}</Button>
                </StackPanel>

                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='Inherit' Align='Center'>
                    <Text Width='15ch' Height='1ch' ForegroundColor='#888888'
                        BackgroundColor='Inherit'>Time Format</Text>
                    <Button Name='{_prefix}_toggleFormat' Width='8ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#cccccc' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>{clock24}</Button>
                </StackPanel>

                <Text Width='100%' Height='1ch' ForegroundColor='#666666'
                    BackgroundColor='Inherit' MarginTop='1ch'>Terminal Size</Text>
                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='Inherit' Align='Center'>
                    <Text Width='15ch' Height='1ch' ForegroundColor='#888888'
                        BackgroundColor='Inherit'>Resolution</Text>
                    <Text Name='{_prefix}_resolution' Width='fill' Height='1ch'
                        ForegroundColor='#cccccc'
                        BackgroundColor='Inherit'>{Console.WindowWidth} x {Console.WindowHeight}</Text>
                </StackPanel>

            </StackPanel>");

        var toggleClock = _termui.GetWidget<Button>($"{_prefix}_toggleClock");
        if (toggleClock is not null) toggleClock.Click += (_, _) =>
        {
            _settings.ShowClock = !_settings.ShowClock;
            ApplyAndSave();
            ShowDisplayPage();
        };

        var toggleDate = _termui.GetWidget<Button>($"{_prefix}_toggleDate");
        if (toggleDate is not null) toggleDate.Click += (_, _) =>
        {
            _settings.ShowDate = !_settings.ShowDate;
            ApplyAndSave();
            ShowDisplayPage();
        };

        _resolutionText = _termui.GetWidget<Text>($"{_prefix}_resolution");

        var toggleFormat = _termui.GetWidget<Button>($"{_prefix}_toggleFormat");
        if (toggleFormat is not null) toggleFormat.Click += (_, _) =>
        {
            _settings.Use24HourFormat = !_settings.Use24HourFormat;
            ApplyAndSave();
            ShowDisplayPage();
        };

    }

    public void Update()
    {
        if (_resolutionText is not null)
            _resolutionText.Content = $"{Console.WindowWidth} x {Console.WindowHeight}";
    }

    // ===== Desktop Page =====

    private void ShowDesktopPage()
    {
        ClearContent();
        if (_contentPanel is null) return;

        var enabled = _settings.DesktopFolder is not null;
        var folder = _settings.DesktopFolder ?? "";
        var statusText = enabled ? $"Active: {folder}" : "Desktop folder is not set";

        _contentPanel.Add($@"
            <StackPanel Direction='Vertical' Width='100%' Height='100%'
                BackgroundColor='Inherit' PaddingLeft='1ch' PaddingRight='1ch'>

                <Text Width='100%' Height='1ch' ForegroundColor='#cccccc'
                    BackgroundColor='Inherit' Style='Bold'>📂 Desktop</Text>
                <Line Orientation='Horizontal' Type='Dotted' Width='100%'
                    ForegroundColor='{Theme.Hover}' BackgroundColor='Inherit' />

                <Text Width='100%' Height='1ch' ForegroundColor='#666666'
                    BackgroundColor='Inherit'>Set a folder for desktop. Clear to disable.</Text>

                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='Inherit' Align='Center' MarginTop='1ch'>
                    <Text Width='8ch' Height='1ch' ForegroundColor='#888888'
                        BackgroundColor='Inherit'>Folder</Text>
                    <Input Name='{_prefix}_desktopPath' Width='fill' Height='1ch'
                        Value='{SecurityElement.Escape(folder)}'
                        Placeholder='/home/user/Desktop'
                        ForegroundColor='#cccccc' FocusForegroundColor='#cccccc'
                        BackgroundColor='#1a1218' FocusBackgroundColor='#1a1218'
                        CursorColor='#cccccc' PlaceholderColor='#444444'
                        PaddingLeft='0ch' PaddingRight='0ch' />
                </StackPanel>

                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='Inherit' MarginTop='0ch'>
                    <Button Name='{_prefix}_clearDesktop' Width='10ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#666666' FocusTextColor='#ff6666'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>Disable</Button>
                    <Text Name='{_prefix}_desktopStatus' Width='fill' Height='1ch'
                        ForegroundColor='#666666' BackgroundColor='Inherit'
                        PaddingLeft='1ch'>{SecurityElement.Escape(statusText)}</Text>
                </StackPanel>

            </StackPanel>");

        var desktopInput = _termui.GetWidget<Input>($"{_prefix}_desktopPath");
        if (desktopInput is not null)
        {
            desktopInput.TextChanged += (_, text) =>
            {
                var path = text.Trim();
                var status = _termui.GetWidget<Text>($"{_prefix}_desktopStatus");

                if (string.IsNullOrEmpty(path))
                {
                    _settings.DesktopFolder = null;
                    ApplyAndSave();
                    if (status is not null) status.Content = "Desktop disabled";
                }
                else if (Directory.Exists(path))
                {
                    _settings.DesktopFolder = path;
                    ApplyAndSave();
                    if (status is not null) status.Content = $"✓ Active: {path}";
                }
                else
                {
                    if (status is not null) status.Content = "Directory not found";
                }
            };
        }

        var clearBtn = _termui.GetWidget<Button>($"{_prefix}_clearDesktop");
        if (clearBtn is not null) clearBtn.Click += (_, _) =>
        {
            _settings.DesktopFolder = null;
            ApplyAndSave();
            ShowDesktopPage();
        };
    }

    // ===== System Page =====

    private void ShowSystemPage()
    {
        ClearContent();
        if (_contentPanel is null) return;

        _contentPanel.Add($@"
            <StackPanel Direction='Vertical' Width='100%' Height='100%'
                BackgroundColor='Inherit' PaddingLeft='1ch' PaddingRight='1ch'>

                <Text Width='100%' Height='1ch' ForegroundColor='#cccccc'
                    BackgroundColor='Inherit' Style='Bold'>⌨ System</Text>
                <Line Orientation='Horizontal' Type='Dotted' Width='100%'
                    ForegroundColor='{Theme.Hover}' BackgroundColor='Inherit' />

                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='Inherit' Align='Center'>
                    <Text Width='15ch' Height='1ch' ForegroundColor='#888888'
                        BackgroundColor='Inherit'>User</Text>
                    <Text Width='fill' Height='1ch' ForegroundColor='#cccccc'
                        BackgroundColor='Inherit'>{SecurityElement.Escape(Environment.UserName)}</Text>
                </StackPanel>

                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='Inherit' Align='Center'>
                    <Text Width='15ch' Height='1ch' ForegroundColor='#888888'
                        BackgroundColor='Inherit'>Hostname</Text>
                    <Text Width='fill' Height='1ch' ForegroundColor='#cccccc'
                        BackgroundColor='Inherit'>{SecurityElement.Escape(Environment.MachineName)}</Text>
                </StackPanel>

                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='Inherit' Align='Center'>
                    <Text Width='15ch' Height='1ch' ForegroundColor='#888888'
                        BackgroundColor='Inherit'>Shell</Text>
                    <Input Name='{_prefix}_shell' Width='fill' Height='1ch'
                        Value='{SecurityElement.Escape(_settings.DefaultShell)}'
                        ForegroundColor='#cccccc' FocusForegroundColor='#cccccc'
                        BackgroundColor='#1a1218' FocusBackgroundColor='#1a1218'
                        CursorColor='#cccccc'
                        PaddingLeft='0ch' PaddingRight='0ch' />
                </StackPanel>

                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='Inherit' Align='Center'>
                    <Text Width='15ch' Height='1ch' ForegroundColor='#888888'
                        BackgroundColor='Inherit'>Download Path</Text>
                    <Input Name='{_prefix}_dlPath' Width='fill' Height='1ch'
                        Value='{SecurityElement.Escape(_settings.DownloadPath)}'
                        ForegroundColor='#cccccc' FocusForegroundColor='#cccccc'
                        BackgroundColor='#1a1218' FocusBackgroundColor='#1a1218'
                        CursorColor='#cccccc'
                        PaddingLeft='0ch' PaddingRight='0ch' />
                </StackPanel>

                <Button Name='{_prefix}_saveSystem' Width='8ch' Height='1ch'
                    BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                    TextColor='#888888' FocusTextColor='#cccccc'
                    BorderStyle='None' TextAlign='Center'
                    PaddingTop='0ch' PaddingBottom='0ch' MarginTop='1ch'>Save</Button>

                <Text Width='100%' Height='1ch' ForegroundColor='#444444'
                    BackgroundColor='Inherit' MarginTop='2ch'>Termesktop v{Desktop.Version}</Text>

            </StackPanel>");

        var saveBtn = _termui.GetWidget<Button>($"{_prefix}_saveSystem");
        if (saveBtn is not null) saveBtn.Click += (_, _) =>
        {
            var shellInput = _termui.GetWidget<Input>($"{_prefix}_shell");
            var dlInput = _termui.GetWidget<Input>($"{_prefix}_dlPath");

            if (shellInput is not null) _settings.DefaultShell = shellInput.Value.Trim();
            if (dlInput is not null) _settings.DownloadPath = dlInput.Value.Trim();
            ApplyAndSave();
        };
    }
}
