using System.Security;
using TermuiX;
using TermuiX.Widgets;
using Termesktop.Components;
using Termesktop.Apps;

namespace Termesktop.Apps;

public class Downloader
{
    private static int _instanceCount;

    private readonly TermuiX.TermuiX _termui;
    private readonly int _instanceId;
    private readonly string _prefix;

    private Input? _urlInput;
    private Input? _pathInput;
    private ProgressBar? _progressBar;
    private Text? _statusText;
    private Text? _speedText;
    private Button? _downloadBtn;
    private Button? _cancelBtn;
    private StackPanel? _historyList;

    private HttpClient? _httpClient;
    private CancellationTokenSource? _cts;
    private bool _downloading;

    // Thread-safe progress
    private readonly object _progressLock = new();
    private double _progress;
    private string _progressStatus = "";
    private bool _hasProgressUpdate;
    private bool _downloadFinished;
    private readonly List<(string text, string color)> _pendingHistory = [];
    private int _historyCounter;

    public Downloader(TermuiX.TermuiX termui)
    {
        _termui = termui;
        _instanceId = _instanceCount++;
        _prefix = $"dl{_instanceId}";
    }

    public static string Title => "Download";

    public void BuildContent(Container contentArea, TermuiX.TermuiX termui)
    {
        var defaultPath = DesktopSettings.Load().DownloadPath;

        contentArea.Add($@"
            <StackPanel Direction='Vertical' Width='100%' Height='100%' BackgroundColor='Inherit'>

                <!-- URL input -->
                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='{Theme.Subtle}' Align='Center'>
                    <Text Width='5ch' Height='1ch' ForegroundColor='#888888'
                        BackgroundColor='Inherit' PaddingLeft='1ch'>URL</Text>
                    <Input Name='{_prefix}_url' Width='fill' Height='1ch'
                        Placeholder='https://example.com/file.zip'
                        ForegroundColor='#cccccc' FocusForegroundColor='#cccccc'
                        BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                        CursorColor='#cccccc'
                        PaddingLeft='0ch' PaddingRight='0ch' />
                </StackPanel>

                <!-- Save path -->
                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='{Theme.Subtle}' Align='Center'>
                    <Text Width='5ch' Height='1ch' ForegroundColor='#888888'
                        BackgroundColor='Inherit' PaddingLeft='1ch'>Save</Text>
                    <Input Name='{_prefix}_path' Width='fill' Height='1ch'
                        Value='{SecurityElement.Escape(defaultPath)}'
                        ForegroundColor='#cccccc' FocusForegroundColor='#cccccc' BackgroundColor='Inherit'
                        FocusBackgroundColor='Inherit' CursorColor='#cccccc'
                        PaddingLeft='0ch' PaddingRight='0ch' />
                    <Button Name='{_prefix}_browse' Width='4ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#ccaa44' FocusTextColor='#ffcc55'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>📂</Button>
                </StackPanel>

                <Line Orientation='Horizontal' Type='Solid' Width='100%'
                    ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />

                <!-- Controls -->
                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='Inherit' Align='Center'>
                    <Button Name='{_prefix}_download' Width='12ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#88cc88' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>⬇ Download</Button>
                    <Button Name='{_prefix}_cancel' Width='10ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#cc8888' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch' Disabled='true'>Cancel</Button>
                    <Text Name='{_prefix}_speed' Width='fill' Height='1ch'
                        ForegroundColor='#888888' BackgroundColor='Inherit'
                        TextAlign='Right' PaddingRight='1ch' />
                </StackPanel>

                <!-- Progress -->
                <ProgressBar Name='{_prefix}_progress' Width='100%' Value='0'
                    ForegroundColor='#cccccc' BackgroundColor='{Theme.Darker}' />

                <Line Orientation='Horizontal' Type='Solid' Width='100%'
                    ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />

                <!-- Download history -->
                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='Inherit'>
                    <Text Width='fill' Height='1ch' ForegroundColor='#888888'
                        BackgroundColor='Inherit' PaddingLeft='1ch' Style='Bold'>History</Text>
                    <Button Name='{_prefix}_clear' Width='7ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#666666' FocusTextColor='#cccccc'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>Clear</Button>
                </StackPanel>

                <StackPanel Name='{_prefix}_history' Direction='Vertical'
                    Width='100%' Height='fill' ScrollY='true'
                    BackgroundColor='Inherit'>
                    <StackPanel Direction='Vertical' Width='100%' Height='auto'
                        BackgroundColor='Inherit' />
                </StackPanel>

                <!-- Status -->
                <Line Orientation='Horizontal' Type='Solid' Width='100%'
                    ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />
                <Text Name='{_prefix}_status' Width='100%' Height='1ch'
                    ForegroundColor='#666666' BackgroundColor='{Theme.Subtle}'
                    PaddingLeft='1ch'>Ready</Text>

            </StackPanel>");

        _urlInput = termui.GetWidget<Input>($"{_prefix}_url");
        _pathInput = termui.GetWidget<Input>($"{_prefix}_path");
        _progressBar = termui.GetWidget<ProgressBar>($"{_prefix}_progress");
        _statusText = termui.GetWidget<Text>($"{_prefix}_status");
        _speedText = termui.GetWidget<Text>($"{_prefix}_speed");
        _downloadBtn = termui.GetWidget<Button>($"{_prefix}_download");
        _cancelBtn = termui.GetWidget<Button>($"{_prefix}_cancel");
        _historyList = termui.GetWidget<StackPanel>($"{_prefix}_history");

        if (_downloadBtn is not null)
            _downloadBtn.Click += (_, _) => StartDownload();

        if (_cancelBtn is not null)
            _cancelBtn.Click += (_, _) => CancelDownload();

        if (_urlInput is not null)
            _urlInput.EnterPressed += (_, _) => StartDownload();

        var browseBtn = termui.GetWidget<Button>($"{_prefix}_browse");
        if (browseBtn is not null)
            browseBtn.Click += (_, _) =>
            {
                var rootContainer = termui.GetWidget<Container>("rootContainer");
                if (rootContainer is null) return;
                var startPath = _pathInput?.Value?.Trim() ?? defaultPath;
                if (!Directory.Exists(startPath)) startPath = defaultPath;
                var dialog = new FileDialog(_termui, FileDialogMode.Folder, startPath);
                dialog.Show(rootContainer, path =>
                {
                    if (path is not null && _pathInput is not null)
                        _pathInput.Value = path;
                });
            };

        var clearBtn = termui.GetWidget<Button>($"{_prefix}_clear");
        if (clearBtn is not null)
            clearBtn.Click += (_, _) => _historyList?.Clear();
    }

    public void Update()
    {
        if (!_hasProgressUpdate && !_downloadFinished) return;

        lock (_progressLock)
        {
            if (_hasProgressUpdate)
            {
                if (_progressBar is not null) _progressBar.Value = _progress;
                if (_statusText is not null) _statusText.Content = _progressStatus;
                _hasProgressUpdate = false;
            }

            if (_downloadFinished)
            {
                _downloadFinished = false;
                _downloading = false;
                if (_downloadBtn is not null) _downloadBtn.Disabled = false;
                if (_cancelBtn is not null) _cancelBtn.Disabled = true;
            }

            // Add pending history entries
            foreach (var (text, color) in _pendingHistory)
            {
                var name = $"{_prefix}_h{_historyCounter++}";
                _historyList?.Add($@"
                    <Text Name='{name}' Width='100%' Height='1ch'
                        ForegroundColor='{color}' BackgroundColor='Inherit'
                        PaddingLeft='1ch'>{System.Security.SecurityElement.Escape(text)}</Text>");
            }
            _pendingHistory.Clear();
        }
    }

    private void StartDownload()
    {
        if (_downloading) return;

        var url = _urlInput?.Value?.Trim() ?? "";
        var savePath = _pathInput?.Value?.Trim() ?? "";

        if (string.IsNullOrEmpty(url))
        {
            SetStatus("Enter a URL");
            return;
        }

        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            url = "https://" + url;

        if (string.IsNullOrEmpty(savePath) || !Directory.Exists(savePath))
        {
            SetStatus("Save path does not exist. Use 📂 to select a folder.");
            return;
        }

        // Determine filename from URL
        var uri = new Uri(url);
        var fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrEmpty(fileName) || !fileName.Contains('.'))
            fileName = "download";

        var filePath = Path.Combine(savePath, fileName);
        int i = 1;
        var ext = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        while (File.Exists(filePath))
        {
            filePath = Path.Combine(savePath, $"{baseName} ({i++}){ext}");
        }

        _downloading = true;
        if (_downloadBtn is not null) _downloadBtn.Disabled = true;
        if (_cancelBtn is not null) _cancelBtn.Disabled = false;

        _cts = new CancellationTokenSource();
        _ = DownloadAsync(url, filePath, _cts.Token);
    }

    private async Task DownloadAsync(string url, string filePath, CancellationToken ct)
    {
        var startTime = DateTime.Now;
        long totalBytes = 0;

        try
        {
            _httpClient ??= new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(30);

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength ?? -1;
            var fileName = Path.GetFileName(filePath);

            ReportProgress(0, $"Downloading: {fileName}");

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var fileStream = File.Create(filePath);

            var buffer = new byte[65536];
            long downloaded = 0;

            while (true)
            {
                var read = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (read == 0) break;

                await fileStream.WriteAsync(buffer, 0, read, ct);
                downloaded += read;
                totalBytes = downloaded;

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                var speed = elapsed > 0 ? downloaded / elapsed : 0;
                var speedStr = FormatSpeed(speed);

                if (contentLength > 0)
                {
                    var pct = (double)downloaded / contentLength;
                    var eta = speed > 0 ? TimeSpan.FromSeconds((contentLength - downloaded) / speed) : TimeSpan.Zero;
                    ReportProgress(pct, $"{fileName}  {FormatSize(downloaded)}/{FormatSize(contentLength)}  {speedStr}  ETA {eta:mm\\:ss}");
                }
                else
                {
                    ReportProgress(-1, $"{fileName}  {FormatSize(downloaded)}  {speedStr}");
                }
            }

            ReportProgress(1, $"Done: {fileName} ({FormatSize(totalBytes)})");
            AddHistoryEntry(fileName, totalBytes, true);
        }
        catch (OperationCanceledException)
        {
            ReportProgress(0, "Cancelled");
            // Clean up partial file
            try { if (File.Exists(filePath)) File.Delete(filePath); } catch { }
            AddHistoryEntry(Path.GetFileName(filePath), totalBytes, false);
        }
        catch (Exception ex)
        {
            ReportProgress(0, $"Error: {ex.Message}");
            AddHistoryEntry(Path.GetFileName(filePath), 0, false);
        }
        finally
        {
            _cts = null;
            lock (_progressLock)
            {
                _downloadFinished = true;
            }
        }
    }

    private void CancelDownload()
    {
        _cts?.Cancel();
    }

    private void ReportProgress(double progress, string status)
    {
        lock (_progressLock)
        {
            _progress = progress < 0 ? 0 : progress;
            _progressStatus = status;
            _hasProgressUpdate = true;
        }
    }

    private void AddHistoryEntry(string fileName, long size, bool success)
    {
        var icon = success ? "✓" : "✕";
        var color = success ? "#88cc88" : "#cc8888";
        var sizeStr = size > 0 ? $" ({FormatSize(size)})" : "";
        var time = DateTime.Now.ToString("HH:mm");

        lock (_progressLock)
        {
            _pendingHistory.Add(($"{icon} {time}  {fileName}{sizeStr}", color));
        }
    }

    private void SetStatus(string text)
    {
        if (_statusText is not null) _statusText.Content = text;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec < 1024) return $"{bytesPerSec:F0} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024:F1} KB/s";
        return $"{bytesPerSec / (1024 * 1024):F1} MB/s";
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _httpClient?.Dispose();
    }
}
