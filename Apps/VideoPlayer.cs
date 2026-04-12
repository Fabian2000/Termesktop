using System.Diagnostics;
using System.Text;
using TermuiX;
using TermuiX.Widgets;
using Termesktop.Components;

namespace Termesktop.Apps;

public class VideoPlayer
{
    private static int _instanceCount;

    private readonly TermuiX.TermuiX _termui;
    private readonly int _instanceId;
    private readonly string _prefix;
    private string? _filePath;
    private string _ffmpegPath;

    private StackPanel? _framePanel;
    private Text? _timeCurText;
    private Text? _timeRemText;
    private Text? _statusText;
    private Button? _playBtn;
    private Slider? _seekSlider;
    private Container? _rootContainer;

    private Process? _ffmpegProcess;
    private bool _playing;
    private bool _paused;
    private int _frameWidth;
    private int _frameHeight;
    private int _widgetCounter;

    // Frame buffer (thread-safe)
    private readonly object _frameLock = new();
    private byte[]? _pendingFrame;
    private bool _hasNewFrame;
    private int _generation; // Incremented on each Stop/Play to discard stale frames

    // Playback info
    private int _frameCount;
    private DateTime _playStart;
    private double _videoDuration; // seconds
    private double _currentTime;   // seconds
    private bool _seeking;
    private int _seekCooldown;

    public VideoPlayer(TermuiX.TermuiX termui, string? filePath = null)
    {
        _termui = termui;
        _instanceId = _instanceCount++;
        _prefix = $"vid{_instanceId}";
        _filePath = filePath;
        _ffmpegPath = FindFfmpeg();
    }

    public static string Title => "Video";

    private static string FindFfmpeg()
    {
        var paths = OperatingSystem.IsWindows()
            ? new[] { "ffmpeg", "ffmpeg.exe", @"C:\ffmpeg\bin\ffmpeg.exe" }
            : new[] { "ffmpeg", "/usr/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/opt/homebrew/bin/ffmpeg" };
        foreach (var path in paths)
        {
            try
            {
                var psi = new ProcessStartInfo(path, "-version")
                {
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(2000);
                if (p?.ExitCode == 0) return path;
            }
            catch { }
        }
        return "";
    }

    public void BuildContent(Container contentArea, TermuiX.TermuiX termui)
    {
        _rootContainer = termui.GetWidget<Container>("rootContainer");

        contentArea.Add($@"
            <StackPanel Direction='Vertical' Width='100%' Height='100%' BackgroundColor='#000000'>

                <!-- Video frame display (fills most of the space) -->
                <Container Name='{_prefix}_display' Width='100%' Height='fill'
                    BackgroundColor='#000000'>
                    <StackPanel Name='{_prefix}_frame' Direction='Vertical'
                        Width='auto' Height='auto' BackgroundColor='Inherit' />
                </Container>

                <!-- Controls bar -->
                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='{Theme.Subtle}' Align='Center'>

                    <Button Name='{_prefix}_rew' Width='4ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#888888' FocusTextColor='#cccccc'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>⏪</Button>

                    <Button Name='{_prefix}_play' Width='4ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#88cc88' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>▶</Button>

                    <Button Name='{_prefix}_stop' Width='4ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#cc8888' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>■</Button>

                    <Button Name='{_prefix}_fwd' Width='4ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#888888' FocusTextColor='#cccccc'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>⏩</Button>

                </StackPanel>

                <!-- Seek bar with time -->
                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='{Theme.Darker}' Align='Center'>
                    <Text Name='{_prefix}_timeCur' Width='6ch' Height='1ch'
                        ForegroundColor='#cccccc' BackgroundColor='Inherit'
                        TextAlign='Center'>00:00</Text>
                    <Slider Name='{_prefix}_seek' Width='fill' Min='0' Max='1000' Value='0' Step='1'
                        ForegroundColor='#cccccc' FocusForegroundColor='#ffffff'
                        BackgroundColor='Inherit' />
                    <Text Name='{_prefix}_timeRem' Width='6ch' Height='1ch'
                        ForegroundColor='#888888' BackgroundColor='Inherit'
                        TextAlign='Center'>00:00</Text>
                </StackPanel>

                <!-- Status bar -->
                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='{Theme.Subtle}'>
                    <Button Name='{_prefix}_open' Width='6ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#888888' FocusTextColor='#cccccc'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>Open</Button>
                    <Line Orientation='Vertical' Type='Solid' Height='1ch'
                        ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />
                    <Text Name='{_prefix}_status' Width='fill' Height='1ch'
                        ForegroundColor='#666666' BackgroundColor='Inherit'
                        PaddingLeft='1ch' />
                </StackPanel>

            </StackPanel>");

        _framePanel = termui.GetWidget<StackPanel>($"{_prefix}_frame");
        _statusText = termui.GetWidget<Text>($"{_prefix}_status");
        _timeCurText = termui.GetWidget<Text>($"{_prefix}_timeCur");
        _timeRemText = termui.GetWidget<Text>($"{_prefix}_timeRem");
        _playBtn = termui.GetWidget<Button>($"{_prefix}_play");
        _seekSlider = termui.GetWidget<Slider>($"{_prefix}_seek");
        if (_seekSlider is not null)
            _seekSlider.ShowValue = false;

        var openBtn = termui.GetWidget<Button>($"{_prefix}_open");
        if (openBtn is not null) openBtn.Click += (_, _) => OpenFile();

        if (_playBtn is not null) _playBtn.Click += (_, _) => TogglePlay();

        var stopBtn = termui.GetWidget<Button>($"{_prefix}_stop");
        if (stopBtn is not null) stopBtn.Click += (_, _) => Stop();

        var rewBtn = termui.GetWidget<Button>($"{_prefix}_rew");
        if (rewBtn is not null) rewBtn.Click += (_, _) => Seek(-5);

        var fwdBtn = termui.GetWidget<Button>($"{_prefix}_fwd");
        if (fwdBtn is not null) fwdBtn.Click += (_, _) => Seek(5);

        if (_seekSlider is not null)
        {
            // Only seek when user interacts, not on programmatic updates
            _seekSlider.ValueChanged += (_, val) =>
            {
                if (_videoDuration > 0 && !_seeking && _seekCooldown <= 0 && _frameCount > 0)
                {
                    var targetTime = val / 1000.0 * _videoDuration;
                    if (Math.Abs(targetTime - _currentTime) > 2.0)
                        SeekTo(targetTime);
                }
            };
        }

        UpdateTime(0, 0);

        if (string.IsNullOrEmpty(_ffmpegPath))
        {
            ShowFfmpegMissing();
            return;
        }

        if (_filePath is not null)
        {
            _videoDuration = GetDuration(_filePath);
            SetStatus($"Ready: {Path.GetFileName(_filePath)}");
        }
        else
        {
            ShowEmptyState();
        }
    }

    private void ShowEmptyState()
    {
        _framePanel?.Add($@"
            <Text Width='100%' Height='auto' ForegroundColor='#555555'
                BackgroundColor='Inherit' PaddingLeft='2ch' PaddingTop='3ch'>🎬  No video loaded\n\nClick Open or double-click a video in Files</Text>");
    }

    private void ShowFfmpegMissing()
    {
        _framePanel?.Add($@"
            <StackPanel Direction='Vertical' Width='100%' Height='auto'
                BackgroundColor='Inherit' PaddingLeft='2ch' PaddingTop='2ch'>
                <Text Width='100%' Height='1ch' ForegroundColor='#ff8888'
                    BackgroundColor='Inherit' Style='Bold'>ffmpeg not found!</Text>
                <Text Width='100%' Height='1ch' ForegroundColor='#cccccc'
                    BackgroundColor='Inherit'>Install: sudo apt install ffmpeg</Text>
                <Text Width='100%' Height='1ch' ForegroundColor='#888888'
                    BackgroundColor='Inherit'>Or enter custom path:</Text>
                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='Inherit'>
                    <Input Name='{_prefix}_ffpath' Width='30ch' Height='1ch'
                        Placeholder='/path/to/ffmpeg'
                        ForegroundColor='#cccccc' FocusForegroundColor='#cccccc'
                        BackgroundColor='{Theme.Darker}' FocusBackgroundColor='{Theme.Darker}'
                        CursorColor='#cccccc'
                        PaddingLeft='0ch' PaddingRight='0ch' />
                    <Button Name='{_prefix}_setpath' Width='6ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#888888' FocusTextColor='#cccccc'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>Set</Button>
                </StackPanel>
            </StackPanel>");

        var setBtn = _termui.GetWidget<Button>($"{_prefix}_setpath");
        if (setBtn is not null) setBtn.Click += (_, _) =>
        {
            var input = _termui.GetWidget<Input>($"{_prefix}_ffpath");
            var path = input?.Value?.Trim() ?? "";
            if (File.Exists(path))
            {
                _ffmpegPath = path;
                _framePanel?.Clear();
                SetStatus($"ffmpeg: {path}");
                ShowEmptyState();
            }
            else
                SetStatus("ffmpeg not found at that path");
        };
    }

    private void OpenFile()
    {
        if (_rootContainer is null || string.IsNullOrEmpty(_ffmpegPath)) return;

        var startPath = _filePath is not null
            ? Path.GetDirectoryName(_filePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var dialog = new FileDialog(_termui, FileDialogMode.Open, startPath);
        dialog.Show(_rootContainer, path =>
        {
            if (path is not null)
            {
                Stop();
                _filePath = path;
                _videoDuration = GetDuration(path);
                SetStatus($"Ready: {Path.GetFileName(path)}");
                UpdateTime(0, _videoDuration);
            }
        });
    }

    private void TogglePlay()
    {
        if (_playing && !_paused)
        {
            _paused = true;
            if (_playBtn is not null) _playBtn.Text = "▶";
            SetStatus("Paused");
            return;
        }

        if (_playing && _paused)
        {
            _paused = false;
            if (_playBtn is not null) _playBtn.Text = "⏸";
            SetStatus("Playing");
            return;
        }

        if (string.IsNullOrEmpty(_filePath) || string.IsNullOrEmpty(_ffmpegPath)) return;
        PlayFrom(0);
    }

    private void Seek(double deltaSeconds)
    {
        if (string.IsNullOrEmpty(_filePath) || _videoDuration <= 0) return;
        var target = _currentTime + deltaSeconds;

        // Don't seek past boundaries
        if (target < 0) target = 0;
        if (target >= _videoDuration - 0.5) target = _videoDuration - 0.5;
        if (target < 0) return;

        SeekTo(target);
    }

    private void SeekTo(double seconds)
    {
        if (string.IsNullOrEmpty(_filePath)) return;
        _seeking = true;
        _seekCooldown = 20; // Ignore slider events for ~20 frames after seek
        Stop();
        PlayFrom(seconds);
        _seeking = false;
    }

    private void PlayFrom(double startSeconds)
    {
        Stop();

        var display = _termui.GetWidget<Container>($"{_prefix}_display");
        _frameWidth = display is not null ? ((IWidget)display).ComputedWidth : 60;
        _frameHeight = display is not null ? ((IWidget)display).ComputedHeight * 2 : 30;
        if (_frameWidth <= 0) _frameWidth = 60;
        if (_frameHeight <= 0) _frameHeight = 30;

        _playing = true;
        _paused = false;
        _frameCount = 0;
        _currentTime = startSeconds;
        _playStart = DateTime.Now;
        if (_playBtn is not null) _playBtn.Text = "⏸";

        var seekArg = startSeconds > 0 ? $"-ss {startSeconds:F2}" : "";

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = $"{seekArg} -i \"{_filePath}\" -vf \"scale={_frameWidth}:{_frameHeight}:force_original_aspect_ratio=decrease,pad={_frameWidth}:{_frameHeight}:(ow-iw)/2:(oh-ih)/2\" -r 8 -f rawvideo -pix_fmt rgb24 -",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            _ffmpegProcess = Process.Start(psi);
            if (_ffmpegProcess is null)
            {
                SetStatus("Failed to start ffmpeg");
                _playing = false;
                return;
            }
            SetStatus("Playing");
            _ = ReadFramesAsync(startSeconds);
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            _playing = false;
        }
    }

    private async Task ReadFramesAsync(double startTime)
    {
        if (_ffmpegProcess is null) return;

        var myGeneration = _generation;
        var stream = _ffmpegProcess.StandardOutput.BaseStream;
        var frameSize = _frameWidth * _frameHeight * 3;
        var buffer = new byte[frameSize];
        var frameInterval = 1.0 / 8.0; // 8 fps

        try
        {
            while (_playing && _generation == myGeneration)
            {
                int totalRead = 0;
                while (totalRead < frameSize)
                {
                    int read = await stream.ReadAsync(buffer, totalRead, frameSize - totalRead);
                    if (read == 0)
                    {
                        _playing = false;
                        return;
                    }
                    totalRead += read;
                }

                // Discard frame if a new seek/stop happened
                if (_generation != myGeneration) return;

                if (!_paused)
                {
                    lock (_frameLock)
                    {
                        _pendingFrame = (byte[])buffer.Clone();
                        _hasNewFrame = true;
                    }
                    _frameCount++;
                    _currentTime = startTime + _frameCount * frameInterval;
                }

                await Task.Delay(125);
            }
        }
        catch { }
        finally
        {
            if (_generation == myGeneration)
                _playing = false;
        }
    }

    public void Update()
    {
        if (_seekCooldown > 0) _seekCooldown--;

        // Dynamically size slider to fill available width (12ch for time labels)
        if (_seekSlider is not null)
        {
            var display = _termui.GetWidget<Container>($"{_prefix}_display");
            if (display is not null)
            {
                var w = ((IWidget)display).ComputedWidth - 12;
                if (w > 10) _seekSlider.Width = $"{w}ch";
            }
        }

        // Reset to play button when video ends
        if (!_playing && _playBtn is not null && _playBtn.Text != "▶")
        {
            _playBtn.Text = "▶";
            SetStatus(_filePath is not null ? $"Finished: {Path.GetFileName(_filePath)}" : "");
        }

        if (!_hasNewFrame || _framePanel is null) return;

        byte[] frame;
        lock (_frameLock)
        {
            if (_pendingFrame is null) return;
            frame = _pendingFrame;
            _pendingFrame = null;
            _hasNewFrame = false;
        }

        RenderFrame(frame);
        UpdateTime(_currentTime, _videoDuration);

        // Update slider position
        if (_seekSlider is not null && _videoDuration > 0 && !_seeking)
        {
            _seekSlider.Value = _currentTime / _videoDuration * 1000;
        }
    }

    private void UpdateTime(double current, double total)
    {
        var cur = TimeSpan.FromSeconds(Math.Max(0, current));
        var tot = TimeSpan.FromSeconds(Math.Max(0, total));

        if (_timeCurText is not null) _timeCurText.Content = $"{cur:mm\\:ss}";
        if (_timeRemText is not null) _timeRemText.Content = $"{tot:mm\\:ss}";
    }

    private void RenderFrame(byte[] rgbData)
    {
        if (_framePanel is null) return;
        _framePanel.Clear();

        for (int y = 0; y < _frameHeight; y += 2)
        {
            int x = 0;
            var rowName = $"{_prefix}_fr{_widgetCounter++}";
            var rowXml = $"<StackPanel Name='{rowName}' Direction='Horizontal' Width='{_frameWidth}ch' Height='1ch' BackgroundColor='#000000'>";

            while (x < _frameWidth)
            {
                int topIdx = (y * _frameWidth + x) * 3;
                int botIdx = ((y + 1) * _frameWidth + x) * 3;

                byte tR = rgbData[topIdx], tG = rgbData[topIdx + 1], tB = rgbData[topIdx + 2];
                byte bR = y + 1 < _frameHeight ? rgbData[botIdx] : (byte)0;
                byte bG = y + 1 < _frameHeight ? rgbData[botIdx + 1] : (byte)0;
                byte bB = y + 1 < _frameHeight ? rgbData[botIdx + 2] : (byte)0;

                int segStart = x;
                x++;

                while (x < _frameWidth)
                {
                    int nTopIdx = (y * _frameWidth + x) * 3;
                    int nBotIdx = ((y + 1) * _frameWidth + x) * 3;

                    int dist = Math.Abs(tR - rgbData[nTopIdx]) + Math.Abs(tG - rgbData[nTopIdx + 1]) + Math.Abs(tB - rgbData[nTopIdx + 2]);
                    if (y + 1 < _frameHeight)
                        dist += Math.Abs(bR - rgbData[nBotIdx]) + Math.Abs(bG - rgbData[nBotIdx + 1]) + Math.Abs(bB - rgbData[nBotIdx + 2]);

                    if (dist > 60) break;
                    x++;
                }

                int segLen = x - segStart;
                long fR = 0, fG = 0, fB = 0, bgR = 0, bgG = 0, bgB = 0;
                for (int sx = segStart; sx < x; sx++)
                {
                    int ti = (y * _frameWidth + sx) * 3;
                    fR += rgbData[ti]; fG += rgbData[ti + 1]; fB += rgbData[ti + 2];
                    if (y + 1 < _frameHeight)
                    {
                        int bi = ((y + 1) * _frameWidth + sx) * 3;
                        bgR += rgbData[bi]; bgG += rgbData[bi + 1]; bgB += rgbData[bi + 2];
                    }
                }

                var fg = $"rgb({fR / segLen},{fG / segLen},{fB / segLen})";
                var bg = $"rgb({bgR / segLen},{bgG / segLen},{bgB / segLen})";
                var segName = $"{_prefix}_s{_widgetCounter++}";

                rowXml += $"<Text Name='{segName}' Width='{segLen}ch' Height='1ch' ForegroundColor='{fg}' BackgroundColor='{bg}' AllowWrapping='false'>{new string('▀', segLen)}</Text>";
            }

            rowXml += "</StackPanel>";
            _framePanel.Add(rowXml);
        }
    }

    private double GetDuration(string filePath)
    {
        try
        {
            var ffprobe = _ffmpegPath.Replace("ffmpeg", "ffprobe");
            var psi = new ProcessStartInfo
            {
                FileName = ffprobe,
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            var output = p?.StandardOutput.ReadToEnd()?.Trim() ?? "";
            p?.WaitForExit(5000);
            if (double.TryParse(output, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var dur))
                return dur;
        }
        catch { }
        return 0;
    }

    private void Stop()
    {
        _playing = false;
        _paused = false;
        _generation++;

        // Clear pending frames from old process
        lock (_frameLock)
        {
            _pendingFrame = null;
            _hasNewFrame = false;
        }

        if (_ffmpegProcess is not null && !_ffmpegProcess.HasExited)
        {
            try { _ffmpegProcess.Kill(); } catch { }
            _ffmpegProcess.Dispose();
            _ffmpegProcess = null;
        }

        if (_playBtn is not null) _playBtn.Text = "▶";
        _framePanel?.Clear();
    }

    private void SetStatus(string text)
    {
        if (_statusText is not null) _statusText.Content = text;
    }

    public void Dispose()
    {
        Stop();
    }
}
