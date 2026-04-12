using System.Security;
using TermuiX;
using TermuiX.Widgets;

using Termesktop.Components;

namespace Termesktop.Components;

public class ColorPicker
{
    private static int _instanceCount;

    private readonly TermuiX.TermuiX _termui;
    private readonly int _instanceId;
    private readonly string _prefix;

    private Container? _overlay;
    private Container? _rootContainer;
    private Input? _hexInput;
    private Container? _preview;
    private Action<string>? _callback;

    // Palette: useful dark-theme colors
    private static readonly string[] Palette =
    [
        // Reds
        "#1a0a0a", "#2a0a0a", "#3a1515", "#5a2020", "#802020",
        "#a03030", "#cc4444", "#ff5555", "#ff8888", "#ffaaaa",
        // Greens
        "#0a1a0a", "#0a2a0a", "#153a15", "#205a20", "#208020",
        "#30a030", "#44cc44", "#55ff55", "#88ff88", "#aaffaa",
        // Blues
        "#0a0a1a", "#0a0a2a", "#15153a", "#20205a", "#202080",
        "#3030a0", "#4444cc", "#5555ff", "#8888ff", "#aaaaff",
        // Neutrals
        "#000000", "#0d0d0d", "#1a1a1a", "#2a2a2a", "#3a3a3a",
        "#555555", "#777777", "#999999", "#cccccc", "#ffffff",
        // Warm
        "#1a1210", "#2a1a10", "#3a2a1a", "#5a3a20", "#806020",
        "#a08030", "#ccaa44", "#ffcc55", "#ffdd88", "#ffeeaa",
        // Purple/Cyan
        "#1a0a1a", "#2a0a2a", "#0a1a1a", "#0a2a2a", "#3a153a",
        "#153a3a", "#aa44cc", "#44cccc", "#cc88ff", "#88ffff",
    ];

    public ColorPicker(TermuiX.TermuiX termui)
    {
        _termui = termui;
        _instanceId = _instanceCount++;
        _prefix = $"cp{_instanceId}";
    }

    public void Show(Container rootContainer, string currentColor, Action<string> callback)
    {
        _rootContainer = rootContainer;
        _callback = callback;

        var escaped = SecurityElement.Escape(currentColor);

        // Build palette grid XML
        var paletteXml = "";
        for (int row = 0; row < Palette.Length; row += 10)
        {
            paletteXml += "<StackPanel Direction='Horizontal' Width='100%' Height='1ch' BackgroundColor='Inherit'>";
            for (int col = 0; col < 10 && row + col < Palette.Length; col++)
            {
                var color = Palette[row + col];
                var btnName = $"{_prefix}_c{row + col}";
                paletteXml += $@"
                    <Button Name='{btnName}' Width='3ch' Height='1ch'
                        BackgroundColor='{color}' FocusBackgroundColor='{color}'
                        TextColor='{color}' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>●</Button>";
            }
            paletteXml += "</StackPanel>";
        }

        rootContainer.Add($@"
            <Container Name='{_prefix}_overlay' Width='100%' Height='100%'>
                <Button Name='{_prefix}_shield' Width='100%' Height='100%'
                    BackgroundColor='#000000' FocusBackgroundColor='#000000'
                    TextColor='#000000' FocusTextColor='#000000'
                    BorderStyle='None' PaddingTop='0ch' PaddingBottom='0ch' />

                <Container Width='34ch' Height='14ch'
                    PositionX='12ch' PositionY='4ch'
                    BackgroundColor='#1a1218' BorderStyle='Single' RoundedCorners='true'
                    BorderColor='{Theme.Lighter}'>

                    <StackPanel Direction='Vertical' Width='100%' Height='100%'
                        BackgroundColor='Inherit'>

                        <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                            BackgroundColor='{Theme.TitleBar}' Align='Center'>
                            <Text Width='1ch' Height='1ch' BackgroundColor='Inherit' />
                            <Text Width='fill' Height='1ch'
                                ForegroundColor='#cccccc' BackgroundColor='Inherit'
                                Style='Bold'>Pick a Color</Text>
                            <Button Name='{_prefix}_close' Width='3ch' Height='1ch'
                                BackgroundColor='Inherit' FocusBackgroundColor='#5a2020'
                                TextColor='#888888' FocusTextColor='#ff6666'
                                BorderStyle='None' TextAlign='Center'
                                PaddingTop='0ch' PaddingBottom='0ch'>✕</Button>
                        </StackPanel>

                        <Line Orientation='Horizontal' Type='Solid' Width='100%'
                            ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />

                        <!-- Palette -->
                        <Container Width='100%' Height='fill' ScrollY='true'
                            BackgroundColor='Inherit' PaddingLeft='1ch'>
                            <StackPanel Direction='Vertical' Width='100%' Height='auto'
                                BackgroundColor='Inherit'>
                                {paletteXml}
                            </StackPanel>
                        </Container>

                        <Line Orientation='Horizontal' Type='Solid' Width='100%'
                            ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />

                        <!-- Custom input + preview -->
                        <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                            BackgroundColor='{Theme.Darker}' Align='Center'>
                            <Text Width='2ch' Height='1ch' PaddingLeft='1ch'
                                ForegroundColor='#888888' BackgroundColor='Inherit'>#</Text>
                            <Input Name='{_prefix}_hex' Width='fill' Height='1ch'
                                Value='{escaped}'
                                ForegroundColor='#cccccc' FocusForegroundColor='#cccccc'
                                BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                                CursorColor='#cccccc'
                                PaddingLeft='0ch' PaddingRight='0ch' />
                            <Container Name='{_prefix}_preview' Width='4ch' Height='1ch'
                                BackgroundColor='{escaped}' />
                            <Button Name='{_prefix}_ok' Width='5ch' Height='1ch'
                                BackgroundColor='Inherit' FocusBackgroundColor='#2a2015'
                                TextColor='#cccccc' FocusTextColor='#ffffff'
                                BorderStyle='None' TextAlign='Center'
                                PaddingTop='0ch' PaddingBottom='0ch'>OK</Button>
                        </StackPanel>

                    </StackPanel>
                </Container>
            </Container>");

        _overlay = _termui.GetWidget<Container>($"{_prefix}_overlay");
        _hexInput = _termui.GetWidget<Input>($"{_prefix}_hex");
        _preview = _termui.GetWidget<Container>($"{_prefix}_preview");

        // Close/cancel
        var closeBtn = _termui.GetWidget<Button>($"{_prefix}_close");
        if (closeBtn is not null) closeBtn.Click += (_, _) => Close();

        // OK button
        var okBtn = _termui.GetWidget<Button>($"{_prefix}_ok");
        if (okBtn is not null) okBtn.Click += (_, _) => Confirm();

        // Hex input: live preview + enter to confirm
        if (_hexInput is not null)
        {
            _hexInput.TextChanged += (_, text) =>
            {
                if (IsValidColor(text) && _preview is not null)
                    _preview.BackgroundColor = Color.Parse(text);
            };
            _hexInput.EnterPressed += (_, _) => Confirm();
        }

        // Palette buttons
        for (int i = 0; i < Palette.Length; i++)
        {
            var btn = _termui.GetWidget<Button>($"{_prefix}_c{i}");
            var color = Palette[i];
            if (btn is not null)
            {
                btn.Click += (_, _) =>
                {
                    if (_hexInput is not null) _hexInput.Value = color;
                    if (_preview is not null) _preview.BackgroundColor = Color.Parse(color);
                };
            }
        }
    }

    private void Confirm()
    {
        var color = _hexInput?.Value?.Trim() ?? "";
        if (IsValidColor(color))
        {
            Close();
            _callback?.Invoke(color);
        }
    }

    private void Close()
    {
        if (_overlay is not null && _rootContainer is not null)
        {
            _rootContainer.Remove(_overlay);
            _overlay = null;
        }
    }

    private static bool IsValidColor(string color)
    {
        if (string.IsNullOrWhiteSpace(color)) return false;
        try { Color.Parse(color); return true; }
        catch { return false; }
    }
}
