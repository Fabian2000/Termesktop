namespace Termesktop.Components;

/// <summary>
/// VT100/xterm terminal emulator with full color support.
/// </summary>
public class VtParser
{
    public struct Cell
    {
        public char Ch;
        public int Fg;  // 0xRRGGBB or -1 for default
        public int Bg;  // 0xRRGGBB or -1 for default
        public bool Bold;
    }

    public struct Segment
    {
        public string Text;
        public int Fg;
        public int Bg;
        public bool Bold;
    }

    private Cell[][] _grid;
    private Cell[][] _altGrid;
    private int _rows, _cols;
    private int _curRow, _curCol;
    private int _savedRow, _savedCol;
    private int _scrollTop, _scrollBottom;
    private bool _altScreen;

    // Current text attributes
    private int _curFg = -1;
    private int _curBg = -1;
    private bool _curBold;
    private bool _curInverse;

    private enum State { Normal, Escape, Csi, Osc, CharSet }
    private State _state;
    private readonly List<int> _csiParams = [];
    private string _csiIntermediate = "";

    private readonly object _lock = new();
    public bool IsAltScreen => _altScreen;

    // Standard 256-color palette (0-15 = standard, 16-231 = 6x6x6 cube, 232-255 = grayscale)
    private static readonly int[] Palette256 = BuildPalette();

    private static int[] BuildPalette()
    {
        var p = new int[256];
        // Standard 8 colors
        p[0] = 0x000000; p[1] = 0xCC0000; p[2] = 0x00CC00; p[3] = 0xCCCC00;
        p[4] = 0x0000CC; p[5] = 0xCC00CC; p[6] = 0x00CCCC; p[7] = 0xCCCCCC;
        // Bright 8 colors
        p[8] = 0x666666; p[9] = 0xFF0000; p[10] = 0x00FF00; p[11] = 0xFFFF00;
        p[12] = 0x5555FF; p[13] = 0xFF00FF; p[14] = 0x00FFFF; p[15] = 0xFFFFFF;
        // 216 color cube (6x6x6)
        for (int i = 0; i < 216; i++)
        {
            int r = i / 36, g = (i / 6) % 6, b = i % 6;
            p[16 + i] = ((r == 0 ? 0 : 55 + r * 40) << 16) |
                         ((g == 0 ? 0 : 55 + g * 40) << 8) |
                          (b == 0 ? 0 : 55 + b * 40);
        }
        // 24 grayscale
        for (int i = 0; i < 24; i++)
        {
            int v = 8 + i * 10;
            p[232 + i] = (v << 16) | (v << 8) | v;
        }
        return p;
    }

    public VtParser(int cols, int rows)
    {
        _cols = cols; _rows = rows;
        _scrollBottom = rows - 1;
        _grid = CreateGrid(rows, cols);
        _altGrid = CreateGrid(rows, cols);
    }

    private static Cell[][] CreateGrid(int rows, int cols)
    {
        var g = new Cell[rows][];
        for (int r = 0; r < rows; r++)
        {
            g[r] = new Cell[cols];
            for (int c = 0; c < cols; c++)
                g[r][c] = new Cell { Ch = ' ', Fg = -1, Bg = -1 };
        }
        return g;
    }

    private static Cell[] CreateRow(int cols)
    {
        var row = new Cell[cols];
        for (int c = 0; c < cols; c++)
            row[c] = new Cell { Ch = ' ', Fg = -1, Bg = -1 };
        return row;
    }

    public void Resize(int cols, int rows)
    {
        lock (_lock)
        {
            if (cols == _cols && rows == _rows) return;
            _grid = ResizeGrid(_grid, _rows, _cols, rows, cols);
            _altGrid = ResizeGrid(_altGrid, _rows, _cols, rows, cols);
            _rows = rows; _cols = cols;
            _scrollTop = 0; _scrollBottom = rows - 1;
            _curRow = Math.Min(_curRow, rows - 1);
            _curCol = Math.Min(_curCol, cols - 1);
        }
    }

    private static Cell[][] ResizeGrid(Cell[][] old, int oldRows, int oldCols, int newRows, int newCols)
    {
        var g = CreateGrid(newRows, newCols);
        int copyRows = Math.Min(oldRows, newRows), copyCols = Math.Min(oldCols, newCols);
        for (int r = 0; r < copyRows; r++)
            Array.Copy(old[r], g[r], copyCols);
        return g;
    }

    public void Process(ReadOnlySpan<char> data)
    {
        lock (_lock)
        {
            foreach (var ch in data) ProcessChar(ch);
        }
    }

    private void ProcessChar(char ch)
    {
        switch (_state)
        {
            case State.Normal:
                if (ch == '\x1b') _state = State.Escape;
                else if (ch == '\n') LineFeed();
                else if (ch == '\r') _curCol = 0;
                else if (ch == '\b') { if (_curCol > 0) _curCol--; }
                else if (ch == '\t') _curCol = Math.Min(_cols - 1, (_curCol + 8) & ~7);
                else if (ch == '\a') { }
                else if (ch >= ' ') PutChar(ch);
                break;
            case State.Escape:
                if (ch == '[') { _state = State.Csi; _csiParams.Clear(); _csiIntermediate = ""; }
                else if (ch == ']') _state = State.Osc;
                else if (ch == 'M') { ReverseIndex(); _state = State.Normal; }
                else if (ch == '7') { _savedRow = _curRow; _savedCol = _curCol; _state = State.Normal; }
                else if (ch == '8') { _curRow = _savedRow; _curCol = _savedCol; _state = State.Normal; }
                else if (ch is '(' or ')' or '*' or '+') _state = State.CharSet;
                else _state = State.Normal;
                break;
            case State.Csi:
                if (ch >= '0' && ch <= '9')
                {
                    if (_csiParams.Count == 0) _csiParams.Add(0);
                    _csiParams[^1] = _csiParams[^1] * 10 + (ch - '0');
                }
                else if (ch == ';') _csiParams.Add(0);
                else if (ch is '?' or '>' or '!') _csiIntermediate += ch;
                else { ExecuteCsi(ch); _state = State.Normal; }
                break;
            case State.CharSet:
                _state = State.Normal;
                break;
            case State.Osc:
                if (ch is '\a' or '\x1b') _state = State.Normal;
                break;
        }
    }

    private void PutChar(char ch)
    {
        if (_curCol >= _cols) { _curCol = 0; LineFeed(); }
        var grid = _altScreen ? _altGrid : _grid;
        if (_curRow >= 0 && _curRow < _rows && _curCol < _cols)
        {
            int fg = _curInverse ? _curBg : _curFg;
            int bg = _curInverse ? _curFg : _curBg;
            grid[_curRow][_curCol] = new Cell { Ch = ch, Fg = fg, Bg = bg, Bold = _curBold };
        }
        _curCol++;
    }

    private void LineFeed()
    {
        if (_curRow == _scrollBottom) ScrollUp(1);
        else if (_curRow < _rows - 1) _curRow++;
    }

    private void ReverseIndex()
    {
        if (_curRow == _scrollTop) ScrollDown(1);
        else if (_curRow > 0) _curRow--;
    }

    private void ScrollUp(int n)
    {
        var grid = _altScreen ? _altGrid : _grid;
        for (int i = 0; i < n; i++)
        {
            for (int r = _scrollTop; r < _scrollBottom; r++) grid[r] = grid[r + 1];
            grid[_scrollBottom] = CreateRow(_cols);
        }
    }

    private void ScrollDown(int n)
    {
        var grid = _altScreen ? _altGrid : _grid;
        for (int i = 0; i < n; i++)
        {
            for (int r = _scrollBottom; r > _scrollTop; r--) grid[r] = grid[r - 1];
            grid[_scrollTop] = CreateRow(_cols);
        }
    }

    private void ExecuteCsi(char cmd)
    {
        int p0 = _csiParams.Count > 0 ? _csiParams[0] : 0;
        int p1 = _csiParams.Count > 1 ? _csiParams[1] : 0;
        switch (cmd)
        {
            case 'A': _curRow = Math.Max(_scrollTop, _curRow - Math.Max(1, p0)); break;
            case 'B': _curRow = Math.Min(_scrollBottom, _curRow + Math.Max(1, p0)); break;
            case 'C': _curCol = Math.Min(_cols - 1, _curCol + Math.Max(1, p0)); break;
            case 'D': _curCol = Math.Max(0, _curCol - Math.Max(1, p0)); break;
            case 'E': _curRow = Math.Min(_scrollBottom, _curRow + Math.Max(1, p0)); _curCol = 0; break;
            case 'F': _curRow = Math.Max(_scrollTop, _curRow - Math.Max(1, p0)); _curCol = 0; break;
            case 'G': _curCol = Math.Clamp((p0 == 0 ? 1 : p0) - 1, 0, _cols - 1); break;
            case 'H': case 'f':
                _curRow = Math.Clamp((p0 == 0 ? 1 : p0) - 1, 0, _rows - 1);
                _curCol = Math.Clamp((p1 == 0 ? 1 : p1) - 1, 0, _cols - 1); break;
            case 'J': EraseDisplay(p0); break;
            case 'K': EraseLine(p0); break;
            case 'L': InsertLines(Math.Max(1, p0)); break;
            case 'M': DeleteLines(Math.Max(1, p0)); break;
            case 'P': DeleteChars(Math.Max(1, p0)); break;
            case '@': InsertChars(Math.Max(1, p0)); break;
            case 'S': ScrollUp(Math.Max(1, p0)); break;
            case 'T': ScrollDown(Math.Max(1, p0)); break;
            case 'X': EraseChars(Math.Max(1, p0)); break;
            case 'd': _curRow = Math.Clamp((p0 == 0 ? 1 : p0) - 1, 0, _rows - 1); break;
            case 'r':
                _scrollTop = Math.Clamp((p0 == 0 ? 1 : p0) - 1, 0, _rows - 1);
                _scrollBottom = Math.Clamp((p1 == 0 ? _rows : p1) - 1, 0, _rows - 1);
                if (_scrollTop > _scrollBottom) (_scrollTop, _scrollBottom) = (_scrollBottom, _scrollTop);
                _curRow = 0; _curCol = 0; break;
            case 's': _savedRow = _curRow; _savedCol = _curCol; break;
            case 'u': _curRow = _savedRow; _curCol = _savedCol; break;
            case 'h': if (_csiIntermediate == "?") SetDecMode(p0, true); break;
            case 'l': if (_csiIntermediate == "?") SetDecMode(p0, false); break;
            case 'm': ExecuteSgr(); break;
        }
    }

    private void ExecuteSgr()
    {
        if (_csiParams.Count == 0) { ResetAttrs(); return; }

        for (int i = 0; i < _csiParams.Count; i++)
        {
            var p = _csiParams[i];
            switch (p)
            {
                case 0: ResetAttrs(); break;
                case 1: _curBold = true; break;
                case 7: _curInverse = true; break;
                case 22: _curBold = false; break;
                case 27: _curInverse = false; break;
                // Standard foreground (30-37)
                case >= 30 and <= 37: _curFg = Palette256[p - 30]; break;
                case 39: _curFg = -1; break;
                // Standard background (40-47)
                case >= 40 and <= 47: _curBg = Palette256[p - 40]; break;
                case 49: _curBg = -1; break;
                // Bright foreground (90-97)
                case >= 90 and <= 97: _curFg = Palette256[p - 90 + 8]; break;
                // Bright background (100-107)
                case >= 100 and <= 107: _curBg = Palette256[p - 100 + 8]; break;
                // 256-color and RGB
                case 38: // Foreground extended
                    if (i + 1 < _csiParams.Count && _csiParams[i + 1] == 5 && i + 2 < _csiParams.Count)
                    { _curFg = Palette256[Math.Clamp(_csiParams[i + 2], 0, 255)]; i += 2; }
                    else if (i + 1 < _csiParams.Count && _csiParams[i + 1] == 2 && i + 4 < _csiParams.Count)
                    { _curFg = (_csiParams[i + 2] << 16) | (_csiParams[i + 3] << 8) | _csiParams[i + 4]; i += 4; }
                    break;
                case 48: // Background extended
                    if (i + 1 < _csiParams.Count && _csiParams[i + 1] == 5 && i + 2 < _csiParams.Count)
                    { _curBg = Palette256[Math.Clamp(_csiParams[i + 2], 0, 255)]; i += 2; }
                    else if (i + 1 < _csiParams.Count && _csiParams[i + 1] == 2 && i + 4 < _csiParams.Count)
                    { _curBg = (_csiParams[i + 2] << 16) | (_csiParams[i + 3] << 8) | _csiParams[i + 4]; i += 4; }
                    break;
            }
        }
    }

    private void ResetAttrs()
    {
        _curFg = -1; _curBg = -1; _curBold = false; _curInverse = false;
    }

    private void SetDecMode(int mode, bool set)
    {
        switch (mode)
        {
            case 1049 or 47 or 1047:
                if (set && !_altScreen)
                {
                    _altScreen = true;
                    _savedRow = _curRow; _savedCol = _curCol;
                    ClearGrid(_altGrid); _curRow = 0; _curCol = 0;
                    _scrollTop = 0; _scrollBottom = _rows - 1;
                }
                else if (!set && _altScreen)
                {
                    _altScreen = false;
                    _curRow = _savedRow; _curCol = _savedCol;
                    _scrollTop = 0; _scrollBottom = _rows - 1;
                }
                break;
        }
    }

    private void ClearGrid(Cell[][] grid)
    {
        for (int r = 0; r < _rows; r++)
            grid[r] = CreateRow(_cols);
    }

    private void EraseDisplay(int mode)
    {
        var grid = _altScreen ? _altGrid : _grid;
        switch (mode)
        {
            case 0:
                if (_curRow < _rows && _curCol < _cols)
                    FillRow(grid[_curRow], _curCol, _cols - _curCol);
                for (int r = _curRow + 1; r < _rows; r++) grid[r] = CreateRow(_cols);
                break;
            case 1:
                for (int r = 0; r < _curRow; r++) grid[r] = CreateRow(_cols);
                if (_curRow < _rows) FillRow(grid[_curRow], 0, Math.Min(_curCol + 1, _cols));
                break;
            case 2: case 3: ClearGrid(grid); break;
        }
    }

    private void EraseLine(int mode)
    {
        if (_curRow < 0 || _curRow >= _rows) return;
        var grid = _altScreen ? _altGrid : _grid;
        switch (mode)
        {
            case 0: FillRow(grid[_curRow], _curCol, _cols - _curCol); break;
            case 1: FillRow(grid[_curRow], 0, Math.Min(_curCol + 1, _cols)); break;
            case 2: grid[_curRow] = CreateRow(_cols); break;
        }
    }

    private static void FillRow(Cell[] row, int start, int count)
    {
        for (int i = start; i < start + count && i < row.Length; i++)
            row[i] = new Cell { Ch = ' ', Fg = -1, Bg = -1 };
    }

    private void EraseChars(int n)
    {
        if (_curRow < 0 || _curRow >= _rows) return;
        FillRow((_altScreen ? _altGrid : _grid)[_curRow], _curCol, Math.Min(n, _cols - _curCol));
    }

    private void InsertLines(int n)
    {
        var grid = _altScreen ? _altGrid : _grid;
        n = Math.Min(n, _scrollBottom - _curRow + 1);
        for (int i = 0; i < n; i++)
        {
            for (int r = _scrollBottom; r > _curRow; r--) grid[r] = grid[r - 1];
            grid[_curRow] = CreateRow(_cols);
        }
    }

    private void DeleteLines(int n)
    {
        var grid = _altScreen ? _altGrid : _grid;
        n = Math.Min(n, _scrollBottom - _curRow + 1);
        for (int i = 0; i < n; i++)
        {
            for (int r = _curRow; r < _scrollBottom; r++) grid[r] = grid[r + 1];
            grid[_scrollBottom] = CreateRow(_cols);
        }
    }

    private void InsertChars(int n)
    {
        if (_curRow < 0 || _curRow >= _rows) return;
        var row = (_altScreen ? _altGrid : _grid)[_curRow];
        for (int c = _cols - 1; c >= _curCol + n; c--) row[c] = row[c - n];
        FillRow(row, _curCol, Math.Min(n, _cols - _curCol));
    }

    private void DeleteChars(int n)
    {
        if (_curRow < 0 || _curRow >= _rows) return;
        var row = (_altScreen ? _altGrid : _grid)[_curRow];
        for (int c = _curCol; c < _cols - n; c++) row[c] = c + n < _cols ? row[c + n] : new Cell { Ch = ' ', Fg = -1, Bg = -1 };
        FillRow(row, Math.Max(_curCol, _cols - n), Math.Min(n, _cols - _curCol));
    }

    /// <summary>
    /// Render the grid as colored segments per row.
    /// </summary>
    public List<List<Segment>> RenderSegments()
    {
        lock (_lock)
        {
            var grid = _altScreen ? _altGrid : _grid;
            var result = new List<List<Segment>>(_rows);

            for (int r = 0; r < _rows; r++)
            {
                var row = grid[r];
                var segments = new List<Segment>();
                int segStart = 0;
                int curFg = row[0].Fg, curBg = row[0].Bg;
                bool curBold = row[0].Bold;

                for (int c = 1; c <= _cols; c++)
                {
                    bool flush = c == _cols;
                    if (!flush)
                    {
                        var cell = row[c];
                        if (cell.Fg != curFg || cell.Bg != curBg || cell.Bold != curBold)
                            flush = true;
                    }

                    if (flush)
                    {
                        var sb = new System.Text.StringBuilder(c - segStart);
                        for (int k = segStart; k < c; k++) sb.Append(row[k].Ch);
                        segments.Add(new Segment { Text = sb.ToString(), Fg = curFg, Bg = curBg, Bold = curBold });

                        if (c < _cols)
                        {
                            segStart = c;
                            curFg = row[c].Fg; curBg = row[c].Bg; curBold = row[c].Bold;
                        }
                    }
                }

                result.Add(segments);
            }
            return result;
        }
    }

    /// <summary>
    /// Render as plain text (fallback).
    /// </summary>
    public string Render()
    {
        lock (_lock)
        {
            var grid = _altScreen ? _altGrid : _grid;
            var sb = new System.Text.StringBuilder(_rows * (_cols + 1));
            for (int r = 0; r < _rows; r++)
            {
                if (r > 0) sb.Append('\n');
                int last = _cols - 1;
                while (last >= 0 && grid[r][last].Ch == ' ') last--;
                for (int c = 0; c <= last; c++) sb.Append(grid[r][c].Ch);
            }
            return sb.ToString();
        }
    }
}
