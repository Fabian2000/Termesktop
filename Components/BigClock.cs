using TermuiX;
using TermuiX.Widgets;

namespace Termesktop.Components;

public class BigClock
{
    // Pixel bitmaps for digits (6 wide x 8 tall)
    // Rendered as half-block pairs, yielding 6x4 characters per digit
    private static readonly string[][] PixelDigits =
    [
        // 0
        [
            ".████.",
            "██..██",
            "██..██",
            "██..██",
            "██..██",
            "██..██",
            "██..██",
            ".████.",
        ],
        // 1
        [
            "..██..",
            ".███..",
            "..██..",
            "..██..",
            "..██..",
            "..██..",
            "..██..",
            ".████.",
        ],
        // 2
        [
            ".████.",
            "██..██",
            "....██",
            "...██.",
            "..██..",
            ".██...",
            "██....",
            "██████",
        ],
        // 3
        [
            ".████.",
            "██..██",
            "....██",
            "..███.",
            "....██",
            "....██",
            "██..██",
            ".████.",
        ],
        // 4
        [
            "...██.",
            "..███.",
            ".█.██.",
            "█..██.",
            "██████",
            "...██.",
            "...██.",
            "...██.",
        ],
        // 5
        [
            "██████",
            "██....",
            "██....",
            "█████.",
            "....██",
            "....██",
            "██..██",
            ".████.",
        ],
        // 6
        [
            ".████.",
            "██..██",
            "██....",
            "█████.",
            "██..██",
            "██..██",
            "██..██",
            ".████.",
        ],
        // 7
        [
            "██████",
            "....██",
            "...██.",
            "..██..",
            "..██..",
            ".██...",
            ".██...",
            ".██...",
        ],
        // 8
        [
            ".████.",
            "██..██",
            "██..██",
            ".████.",
            "██..██",
            "██..██",
            "██..██",
            ".████.",
        ],
        // 9
        [
            ".████.",
            "██..██",
            "██..██",
            "██..██",
            ".█████",
            "....██",
            "██..██",
            ".████.",
        ],
    ];

    private static readonly string[] PixelColon =
    [
        "..",
        "..",
        "██",
        "..",
        "..",
        "██",
        "..",
        "..",
    ];

    private readonly TermuiX.TermuiX _termui;
    private Text? _clockText;
    private Text? _dateText;
    private string _lastTime = "";
    private string _lastDate = "";

    public BigClock(TermuiX.TermuiX termui)
    {
        _termui = termui;
    }

    public string BuildXml()
    {
        return @"
            <StackPanel Name='clockPanel' Direction='Vertical' Width='auto' Height='auto'
                Align='Center'>

                <Text Name='clockText' Width='30ch' Height='4ch'
                    ForegroundColor='#cccccc'
                    BackgroundColor='Inherit'
                    AllowWrapping='false' />

                <Text Name='dateText' Width='30ch' Height='1ch'
                    ForegroundColor='#888888'
                    BackgroundColor='Inherit'
                    TextAlign='Center'
                    MarginTop='1ch' />

            </StackPanel>";
    }

    public void Initialize()
    {
        _clockText = _termui.GetWidget<Text>("clockText");
        _dateText = _termui.GetWidget<Text>("dateText");

        ForceUpdate();
    }

    public void Update()
    {
        var now = DateTime.Now;
        var timeStr = now.ToString("HH:mm");
        var dateStr = now.ToString("dddd, dd. MMMM yyyy");

        if (timeStr != _lastTime)
        {
            _lastTime = timeStr;
            if (_clockText is not null)
                _clockText.Content = RenderTime(timeStr);
        }

        if (dateStr != _lastDate)
        {
            _lastDate = dateStr;
            if (_dateText is not null)
                _dateText.Content = dateStr;
        }
    }

    private void ForceUpdate()
    {
        _lastTime = "";
        _lastDate = "";
        Update();
    }

    /// <summary>
    /// Converts pixel bitmap pairs into half-block Unicode characters.
    /// Every 2 pixel rows become 1 character row (▀ top, ▄ bottom, █ both, ' ' neither).
    /// </summary>
    private static string RenderTime(string time)
    {
        var glyphs = new List<string[]>();
        foreach (var c in time)
        {
            if (glyphs.Count > 0)
                glyphs.Add(MakeSpacer());

            if (c == ':')
                glyphs.Add(PixelColon);
            else
                glyphs.Add(PixelDigits[c - '0']);
        }

        int pixelRows = 8;
        var fullPixelRows = new string[pixelRows];
        for (int row = 0; row < pixelRows; row++)
        {
            fullPixelRows[row] = "";
            foreach (var glyph in glyphs)
                fullPixelRows[row] += glyph[row];
        }

        int charRows = pixelRows / 2;
        var lines = new string[charRows];
        for (int cr = 0; cr < charRows; cr++)
        {
            var topRow = fullPixelRows[cr * 2];
            var botRow = fullPixelRows[cr * 2 + 1];
            var sb = new System.Text.StringBuilder();

            for (int col = 0; col < topRow.Length; col++)
            {
                bool top = topRow[col] != '.';
                bool bot = botRow[col] != '.';

                if (top && bot) sb.Append('█');
                else if (top) sb.Append('▀');
                else if (bot) sb.Append('▄');
                else sb.Append(' ');
            }

            lines[cr] = sb.ToString();
        }

        return string.Join("\n", lines);
    }

    private static string[] MakeSpacer()
    {
        var spacer = new string[8];
        for (int i = 0; i < 8; i++)
            spacer[i] = ".";
        return spacer;
    }
}
