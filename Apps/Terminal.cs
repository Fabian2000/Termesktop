using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using TermuiX;
using TermuiX.Widgets;
using Termesktop.Components;

namespace Termesktop.Apps;

public partial class Terminal
{
    private static int _instanceCount;

    private readonly TermuiX.TermuiX _termui;
    private readonly int _instanceId;
    private readonly string _prefix;
    private readonly StringBuilder _output = new();
    private Text? _outputText;
    private StackPanel? _scrollContainer;
    private StackPanel? _innerContainer;
    private string _cwd;

    // PTY mode (Linux) — direct PTY master fd
    private PtyProcess? _pty;
    private bool _usePty;

    // Legacy mode (Windows) — Process with redirected streams
    private Process? _bashProcess;

    private bool _processRunning;

    private readonly object _bufferLock = new();
    private readonly StringBuilder _pendingOutput = new();
    private bool _hasPendingOutput;

    // VT emulator for fullscreen TUI apps
    private VtParser? _vtParser;
    private Container? _vtContainer;
    private StackPanel? _vtRowsPanel;
    private bool _vtMode;
    private int _lastVtCols;
    private int _lastVtRows;
    private StackPanel? _normalMode;
    private bool _initialSizeSent;
    private int _vtWidgetCounter;

    // Read buffer for PTY output
    private readonly byte[] _ptyReadBuf = new byte[4096];

    private const string PwdSentinel = "___TERMESKTOP_PWD___";
    private static string PwdCommand => OperatingSystem.IsWindows()
        ? $"echo {PwdSentinel}%cd% 1>&2"
        : $"echo \"{PwdSentinel}$(pwd)\" >&2";
    private DateTime _lastWidgetUpdate = DateTime.MinValue;
    private int _scrollNextFrames;

    public event Action? OnProcessExited;
    public bool IsRunning => _processRunning;
    private Input? _focusInput;
    public bool IsFocused => _focusInput is not null && ((IWidget)_focusInput).Focussed;

    public Terminal(TermuiX.TermuiX termui, string? startPath = null)
    {
        _termui = termui;
        _instanceId = _instanceCount++;
        _prefix = $"term{_instanceId}";
        _cwd = startPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public static string Title => "Terminal";

    public void SendCommand(string command)
    {
        if (!_processRunning) return;
        if (_usePty && _pty is not null)
        {
            _pty.WriteString(command + "\n");
        }
        else if (_bashProcess is not null)
        {
            BufferOutput($"{GetPromptString()}{command}\n");
            try { _bashProcess.StandardInput.WriteLine($"{command}; {PwdCommand}"); _bashProcess.StandardInput.Flush(); }
            catch { }
        }
    }

    public void SendInterrupt()
    {
        if (_usePty && _pty is not null)
            _pty.SendInterrupt();
        else if (_bashProcess is not null)
        {
            try { _bashProcess.StandardInput.Write('\x03'); _bashProcess.StandardInput.Flush(); BufferOutput("^C\n"); }
            catch { }
        }
    }

    public void SendRawChar(char c)
    {
        if (_usePty && _pty is not null)
            _pty.WriteByte((byte)c);
        else if (_bashProcess is not null)
        {
            try { _bashProcess.StandardInput.Write(c); _bashProcess.StandardInput.Flush(); }
            catch { }
        }
    }

    public void BuildContent(Container contentArea, TermuiX.TermuiX termui)
    {
        var promptStr = GetPromptString();
        var escaped = System.Security.SecurityElement.Escape(promptStr);
        var promptW = promptStr.Length + 1;

        contentArea.Add($@"
            <StackPanel Direction='Vertical' Width='100%' Height='100%' BackgroundColor='Inherit'>
                <Container Width='100%' Height='fill' BackgroundColor='Inherit'>
                    <StackPanel Name='{_prefix}_normalMode' Direction='Vertical' Width='100%' Height='100%'
                        BackgroundColor='Inherit'>
                        <StackPanel Name='{_prefix}_scroll' Direction='Vertical'
                            Width='100%' Height='100%' ScrollY='true' BackgroundColor='Inherit'>
                            <StackPanel Name='{_prefix}_inner' Direction='Vertical'
                                Width='100%' Height='auto' BackgroundColor='Inherit'>
                                <Text Name='{_prefix}_output' Width='100%' Height='auto'
                                    ForegroundColor='#cccccc' BackgroundColor='Inherit'
                                    PaddingLeft='1ch' AllowWrapping='true' />
                            </StackPanel>
                        </StackPanel>
                    </StackPanel>
                    <Container Name='{_prefix}_vtContainer' Width='100%' Height='100%'
                        BackgroundColor='#000000' Visible='false'>
                        <StackPanel Name='{_prefix}_vtRows' Direction='Vertical'
                            Width='100%' Height='100%' BackgroundColor='#000000' />
                    </Container>
                </Container>
                <!-- 1ch focus input at the bottom. Stdin is read directly, this is just a focus target. -->
                <Input Name='{_prefix}_focusInput' Width='100%' Height='1ch'
                    ForegroundColor='#cccccc' FocusForegroundColor='#cccccc'
                    BackgroundColor='#0d0d0d' FocusBackgroundColor='#0d0d0d'
                    CursorColor='#55ff55'
                    PaddingLeft='1ch' PaddingRight='0ch' />
            </StackPanel>");

        _outputText = termui.GetWidget<Text>($"{_prefix}_output");
        _focusInput = termui.GetWidget<Input>($"{_prefix}_focusInput");
        _scrollContainer = termui.GetWidget<StackPanel>($"{_prefix}_scroll");
        _innerContainer = termui.GetWidget<StackPanel>($"{_prefix}_inner");
        _vtContainer = termui.GetWidget<Container>($"{_prefix}_vtContainer");
        _vtRowsPanel = termui.GetWidget<StackPanel>($"{_prefix}_vtRows");
        _normalMode = termui.GetWidget<StackPanel>($"{_prefix}_normalMode");

        if (_focusInput is not null) _termui.SetFocus(_focusInput);

        StartBash();

        // In normal mode: user types in the Input widget; Enter sends command to PTY
        if (_focusInput is not null)
        {
            _focusInput.EnterPressed += (_, text) =>
            {
                if (!_processRunning) return;
                _focusInput.Value = "";
                if (_pty is not null)
                    _pty.WriteString(text + "\n");
            };
        }
    }

    public void Update()
    {
        // Check if PTY child has exited
        if (_usePty && _pty is not null && _processRunning && _pty.HasExited())
        {
            _processRunning = false;
            OnProcessExited?.Invoke();
            return;
        }

        // PTY output is read via background async task (started in StartPty)

        // Initial PTY size
        if (_usePty && !_initialSizeSent && _vtContainer is not null && _pty is not null)
        {
            var w = ((IWidget)_vtContainer).ComputedWidth;
            var h = ((IWidget)_vtContainer).ComputedHeight;
            if (w > 0 && h > 0)
            {
                _initialSizeSent = true;
                _lastVtCols = w; _lastVtRows = h;
                _vtParser?.Resize(w, h);
                _pty.SetSize(w, h);
            }
        }

        // VT mode switching
        if (_vtParser is not null && _usePty)
        {
            var wantVt = _vtParser.IsAltScreen;
            if (wantVt != _vtMode)
            {
                _vtMode = wantVt;
                if (_normalMode is not null) _normalMode.Visible = !_vtMode;
                if (_vtContainer is not null) _vtContainer.Visible = _vtMode;
            }

            // Resize
            if (_vtContainer is not null)
            {
                var w = ((IWidget)_vtContainer).ComputedWidth;
                var h = ((IWidget)_vtContainer).ComputedHeight;
                if (w > 0 && h > 0 && (w != _lastVtCols || h != _lastVtRows))
                {
                    _lastVtCols = w; _lastVtRows = h;
                    _vtParser.Resize(w, h);
                    _pty?.SetSize(w, h);
                }
            }

            // VT mode only: intercept keyboard (htop, vim, etc.)
            // In normal mode, TermuiX handles input; user types in Input widget, Enter sends to PTY.
            if (_vtMode && _pty is not null)
            {
                ReadStdinAndForwardToPty();
            }
            else if (_stdinRawMode)
            {
                RestoreStdin();
            }

            // Render VT grid
            if (_vtMode && _vtRowsPanel is not null)
                RenderVtColored();
        }

        // Scroll mode rendering
        if (_scrollNextFrames > 0)
        {
            _scrollNextFrames--;
            if (_innerContainer is not null && _scrollContainer is not null)
            {
                var inner = ((IWidget)_innerContainer).ComputedHeight;
                var outer = ((IWidget)_scrollContainer).ComputedHeight;
                if (inner > outer) ScrollToBottom();
            }
        }

        if (!_hasPendingOutput) return;
        var now = DateTime.Now;
        if ((now - _lastWidgetUpdate).TotalMilliseconds < 100) return;
        _lastWidgetUpdate = now;

        string pending;
        lock (_bufferLock)
        {
            if (_pendingOutput.Length == 0) return;
            pending = _pendingOutput.ToString();
            _pendingOutput.Clear();
            _hasPendingOutput = false;
        }

        var lines = pending.Split('\n');
        var filtered = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.Contains(PwdSentinel))
            {
                var idx = line.IndexOf(PwdSentinel);
                var path = line[(idx + PwdSentinel.Length)..].Trim();
                if (Directory.Exists(path)) { _cwd = path; UpdatePrompt(); }
            }
            else
            {
                filtered.Append(line);
                if (line != lines[^1]) filtered.Append('\n');
            }
        }

        _output.Append(filtered);
        if (_output.Length > 20000) _output.Remove(0, _output.Length - 15000);
        if (_outputText is not null) _outputText.Content = _output.ToString();
        _scrollNextFrames = 3;
    }

    // --- Raw stdin reading for VT mode ---

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int read(int fd, byte[] buf, int count);

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int fcntl(int fd, int cmd, int arg);

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int tcgetattr(int fd, byte[] termios);

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int tcsetattr(int fd, int action, byte[] termios);

    private const int F_GETFL = 3;
    private const int F_SETFL = 4;
    private const int O_NONBLOCK = 2048;
    private const int TCSANOW = 0;

    private bool _stdinRawMode;
    private byte[]? _savedTermios;
    private readonly byte[] _stdinBuf = new byte[256];

    private void EnterRawStdin()
    {
        if (_stdinRawMode) return;

        // Save current termios
        _savedTermios = new byte[256]; // termios struct is ~60 bytes, 256 is safe
        tcgetattr(0, _savedTermios);

        // Copy and modify: disable ISIG so 0x03 comes as data, not signal
        var raw = (byte[])_savedTermios.Clone();
        // c_lflag is at offset 12 on x86_64 Linux (glibc). ISIG = 0x01.
        // We also disable ICANON (0x02), ECHO (0x08) to be fully raw.
        int lflagOffset = 12;
        uint lflag = BitConverter.ToUInt32(raw, lflagOffset);
        lflag &= ~(0x01u | 0x02u | 0x08u); // ISIG | ICANON | ECHO
        BitConverter.GetBytes(lflag).CopyTo(raw, lflagOffset);
        tcsetattr(0, TCSANOW, raw);

        // Also set non-blocking
        int flags = fcntl(0, F_GETFL, 0);
        fcntl(0, F_SETFL, flags | O_NONBLOCK);

        _stdinRawMode = true;
    }

    private void RestoreStdin()
    {
        if (!_stdinRawMode) return;

        // Restore blocking
        int flags = fcntl(0, F_GETFL, 0);
        fcntl(0, F_SETFL, flags & ~O_NONBLOCK);

        // Restore original termios
        if (_savedTermios is not null)
            tcsetattr(0, TCSANOW, _savedTermios);

        _stdinRawMode = false;
    }

    private void ReadStdinAndForwardToPty()
    {
        if (_pty is null) return;

        if (!_stdinRawMode)
            EnterRawStdin();

        int n = read(0, _stdinBuf, _stdinBuf.Length);
        if (n <= 0) return;

        // Skip mouse event escape sequences — those belong to TermuiX, not the PTY.
        // Mouse sequences start with \x1b[M (X10) or \x1b[< (SGR). We detect and drop them.
        // Keyboard escape sequences like \x1b[A (arrow) don't have M/<.
        int writeStart = 0;
        for (int i = 0; i < n; i++)
        {
            if (_stdinBuf[i] == 0x1b && i + 2 < n && _stdinBuf[i + 1] == (byte)'[')
            {
                char c = (char)_stdinBuf[i + 2];
                if (c == 'M' || c == '<')
                {
                    // Flush anything before this sequence
                    if (i > writeStart)
                        _pty.WriteBytes(_stdinBuf, writeStart, i - writeStart);

                    // Skip the full mouse sequence: X10 is fixed 6 bytes, SGR ends at 'M' or 'm'
                    if (c == 'M')
                    {
                        i += 5; // ESC [ M b x y (6 bytes total, i advances by 5)
                    }
                    else // SGR: \x1b[<b;x;y(M|m)
                    {
                        int j = i + 3;
                        while (j < n && _stdinBuf[j] != (byte)'M' && _stdinBuf[j] != (byte)'m') j++;
                        i = j;
                    }
                    writeStart = i + 1;
                }
            }
        }

        if (writeStart < n)
            _pty.WriteBytes(_stdinBuf, writeStart, n - writeStart);
    }

    // --- Rendering ---

    private void RenderVtColored()
    {
        if (_vtParser is null || _vtRowsPanel is null) return;
        var segments = _vtParser.RenderSegments();
        _vtRowsPanel.Clear();

        foreach (var rowSegs in segments)
        {
            var rowName = $"{_prefix}_vr{_vtWidgetCounter++}";
            _vtRowsPanel.Add($@"
                <StackPanel Name='{rowName}' Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='#000000' />");
            var rowPanel = _termui.GetWidget<StackPanel>(rowName);
            if (rowPanel is null) continue;

            foreach (var seg in rowSegs)
            {
                var segName = $"{_prefix}_vs{_vtWidgetCounter++}";
                var fg = seg.Fg >= 0 ? $"#{seg.Fg:X6}" : "#cccccc";
                var bg = seg.Bg >= 0 ? $"#{seg.Bg:X6}" : "#000000";
                var style = seg.Bold ? " Style='Bold'" : "";
                var escaped = System.Security.SecurityElement.Escape(seg.Text);
                var w = seg.Text.Length;
                rowPanel.Add($@"<Text Name='{segName}' Width='{w}ch' Height='1ch'
                    ForegroundColor='{fg}' BackgroundColor='{bg}'{style}>{escaped}</Text>");
            }
        }
    }

    private void ScrollToBottom()
    {
        if (_scrollContainer is null || _innerContainer is null) return;
        var container = (IWidget)_scrollContainer;
        var inner = (IWidget)_innerContainer;
        long maxScroll = Math.Max(0, inner.ComputedHeight - container.ComputedHeight);
        container.ScrollOffsetY = maxScroll;
    }

    private void UpdatePrompt() { /* no separate prompt widget in PTY mode — bash shows its own */ }

    private string GetPromptString()
    {
        var dir = _cwd;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (dir.StartsWith(home)) dir = "~" + dir[home.Length..];
        return $"{Environment.UserName}@{Environment.MachineName}:{dir}$ ";
    }

    // --- Process startup ---

    private void StartBash()
    {
        if (OperatingSystem.IsLinux())
            StartPty();
        else
            StartLegacy();
    }

    private void StartPty()
    {
        _usePty = true;
        _pty = new PtyProcess();

        var shell = Platform.DefaultShell;
        if (!_pty.Start(shell, _cwd, 80, 24))
        {
            BufferOutput("Failed to start PTY.\n");
            _usePty = false;
            _pty = null;
            StartLegacy();
            return;
        }

        _processRunning = true;
        _vtParser = new VtParser(80, 24);

        // Read PTY output via stdout stream (blocking async read)
        _ = Task.Run(async () =>
        {
            var buf = new byte[4096];
            var stdout = _pty.StdoutStream;
            if (stdout is null) return;
            try
            {
                while (_pty is not null && !_pty.HasExited())
                {
                    int n = await stdout.ReadAsync(buf, 0, buf.Length);
                    if (n == 0) break;
                    var chars = Encoding.UTF8.GetString(buf, 0, n);
                    _vtParser?.Process(chars.AsSpan());
                    BufferOutput(StripAnsiCodes(chars));
                }
            }
            catch { }
            _processRunning = false;
            OnProcessExited?.Invoke();
        });
    }

    private void StartLegacy()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Platform.DefaultShell,
                Arguments = OperatingSystem.IsWindows() ? "/Q" : "--norc --noprofile --noediting",
                WorkingDirectory = _cwd,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
            };
            psi.Environment["TERM"] = "dumb";
            psi.Environment["PS1"] = "";
            psi.Environment["PS2"] = "";
            psi.Environment["PROMPT_COMMAND"] = "";
            psi.Environment["LANG"] = "en_US.UTF-8";

            _bashProcess = Process.Start(psi);
            if (_bashProcess is null) { BufferOutput("Failed to start bash.\n"); return; }

            _processRunning = true;
            _ = ReadStreamAsync(_bashProcess.StandardOutput, false);
            _ = ReadStreamAsync(_bashProcess.StandardError, true);

            _bashProcess.EnableRaisingEvents = true;
            _bashProcess.Exited += (_, _) => { _processRunning = false; OnProcessExited?.Invoke(); };
        }
        catch (Exception ex) { BufferOutput($"Error: {ex.Message}\n"); }
    }

    private async Task ReadStreamAsync(System.IO.StreamReader reader, bool isStderr)
    {
        var buffer = new char[1024];
        try
        {
            while (true)
            {
                int bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;
                var text = StripAnsiCodes(new string(buffer, 0, bytesRead));
                if (isStderr)
                {
                    if (text.Contains(PwdSentinel)) { BufferOutput(text); continue; }
                    if (text.Contains("no job control") || string.IsNullOrWhiteSpace(text)) continue;
                }
                BufferOutput(text);
            }
        }
        catch { }
    }

    private void BufferOutput(string text)
    {
        lock (_bufferLock) { _pendingOutput.Append(text); _hasPendingOutput = true; }
    }

    private static string StripAnsiCodes(string text) => AnsiRegex().Replace(text, "");

    [GeneratedRegex(@"\x1B(?:\[[0-9;?]*[a-zA-Z]|\][^\x07]*\x07|\([AB012]|>[=>]?|[78DEHM])|\r")]
    private static partial Regex AnsiRegex();

    public void Dispose()
    {
        RestoreStdin();
        _vtMode = false;
        _pty?.Dispose();
        _pty = null;
        try { if (_bashProcess is not null) { if (!_bashProcess.HasExited) _bashProcess.Kill(true); _bashProcess.Dispose(); } }
        catch { }
        _bashProcess = null;
        _processRunning = false;
    }
}
