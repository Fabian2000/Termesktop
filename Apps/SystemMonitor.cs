using System.Diagnostics;
using TermuiX;
using TermuiX.Widgets;

using Termesktop.Components;

namespace Termesktop.Apps;

public class SystemMonitor
{
    private static int _instanceCount;

    private readonly TermuiX.TermuiX _termui;
    private readonly int _instanceId;
    private readonly string _prefix;

    private Text? _cpuText;
    private Text? _memText;
    private Text? _diskText;
    private Text? _uptimeText;
    private Text? _hostnameText;
    private ProgressBar? _cpuBar;
    private ProgressBar? _memBar;
    private ProgressBar? _diskBar;
    private Chart? _cpuChart;

    private readonly List<double> _cpuHistory = [];
    private readonly List<double> _memHistory = [];
    private DateTime _lastUpdate = DateTime.MinValue;
    private long _prevIdleTime;
    private long _prevTotalTime;

    public SystemMonitor(TermuiX.TermuiX termui)
    {
        _termui = termui;
        _instanceId = _instanceCount++;
        _prefix = $"mon{_instanceId}";
    }

    public static string Title => "Monitor";

    public void BuildContent(Container contentArea, TermuiX.TermuiX termui)
    {
        contentArea.Add($@"
            <StackPanel Direction='Vertical' Width='100%' Height='100%' BackgroundColor='Inherit'>

                <!-- Header -->
                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='{Theme.Subtle}' PaddingLeft='1ch'>
                    <Text Name='{_prefix}_hostname' Width='fill' Height='1ch'
                        ForegroundColor='#cccccc' BackgroundColor='Inherit' Style='Bold' />
                    <Text Name='{_prefix}_uptime' Width='20ch' Height='1ch'
                        ForegroundColor='#666666' BackgroundColor='Inherit'
                        TextAlign='Right' PaddingRight='1ch' />
                </StackPanel>

                <Line Orientation='Horizontal' Type='Solid' Width='100%'
                    ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />

                <!-- CPU -->
                <StackPanel Direction='Vertical' Width='100%' Height='auto'
                    BackgroundColor='Inherit' PaddingLeft='1ch' PaddingRight='1ch' PaddingTop='0ch'>
                    <Text Name='{_prefix}_cpu' Width='100%' Height='1ch'
                        ForegroundColor='#aa6666' BackgroundColor='Inherit' Style='Bold' />
                    <ProgressBar Name='{_prefix}_cpubar' Width='100%' Value='0'
                        ForegroundColor='#aa4444' BackgroundColor='#1a1010' />
                </StackPanel>

                <!-- Memory -->
                <StackPanel Direction='Vertical' Width='100%' Height='auto'
                    BackgroundColor='Inherit' PaddingLeft='1ch' PaddingRight='1ch' PaddingTop='0ch'>
                    <Text Name='{_prefix}_mem' Width='100%' Height='1ch'
                        ForegroundColor='#66aa66' BackgroundColor='Inherit' Style='Bold' />
                    <ProgressBar Name='{_prefix}_membar' Width='100%' Value='0'
                        ForegroundColor='#44aa44' BackgroundColor='#101a10' />
                </StackPanel>

                <!-- Disk -->
                <StackPanel Direction='Vertical' Width='100%' Height='auto'
                    BackgroundColor='Inherit' PaddingLeft='1ch' PaddingRight='1ch' PaddingTop='0ch'>
                    <Text Name='{_prefix}_disk' Width='100%' Height='1ch'
                        ForegroundColor='#6666aa' BackgroundColor='Inherit' Style='Bold' />
                    <ProgressBar Name='{_prefix}_diskbar' Width='100%' Value='0'
                        ForegroundColor='#4444aa' BackgroundColor='#10101a' />
                </StackPanel>

                <Line Orientation='Horizontal' Type='Solid' Width='100%'
                    ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />

                <!-- CPU History Chart -->
                <Text Width='100%' Height='1ch' PaddingLeft='1ch'
                    ForegroundColor='#666666' BackgroundColor='Inherit'>CPU History</Text>
                <Chart Name='{_prefix}_chart' Width='100%' Height='fill'
                    BackgroundColor='Inherit' />

            </StackPanel>");

        _cpuText = termui.GetWidget<Text>($"{_prefix}_cpu");
        _memText = termui.GetWidget<Text>($"{_prefix}_mem");
        _diskText = termui.GetWidget<Text>($"{_prefix}_disk");
        _uptimeText = termui.GetWidget<Text>($"{_prefix}_uptime");
        _hostnameText = termui.GetWidget<Text>($"{_prefix}_hostname");
        _cpuBar = termui.GetWidget<ProgressBar>($"{_prefix}_cpubar");
        _memBar = termui.GetWidget<ProgressBar>($"{_prefix}_membar");
        _diskBar = termui.GetWidget<ProgressBar>($"{_prefix}_diskbar");
        _cpuChart = termui.GetWidget<Chart>($"{_prefix}_chart");

        if (_hostnameText is not null)
            _hostnameText.Content = $"📊 {Environment.MachineName}";

        if (_cpuChart is not null)
        {
            _cpuChart.AddSeries(new ChartDataSeries
            {
                Label = "CPU %",
                Data = _cpuHistory,
                Color = Color.Parse("#aa4444"),
            });
            _cpuChart.AddSeries(new ChartDataSeries
            {
                Label = "MEM %",
                Data = _memHistory,
                Color = Color.Parse("#44aa44"),
            });
            _cpuChart.MinY = 0;
            _cpuChart.MaxY = 100;
            _cpuChart.ShowAxes = true;
            _cpuChart.ShowLegend = true;
        }

        // Initial fill
        for (int i = 0; i < 30; i++)
        {
            _cpuHistory.Add(0);
            _memHistory.Add(0);
        }

        Refresh();
    }

    public void Update()
    {
        if ((DateTime.Now - _lastUpdate).TotalSeconds < 2) return;
        _lastUpdate = DateTime.Now;
        Refresh();
    }

    private void Refresh()
    {
        RefreshCpu();
        RefreshMemory();
        RefreshDisk();
        RefreshUptime();
    }

    private void RefreshCpu()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return; // /proc not available on Windows
            var lines = File.ReadAllLines("/proc/stat");
            var parts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // user nice system idle iowait irq softirq steal
            long idle = long.Parse(parts[4]) + long.Parse(parts[5]);
            long total = 0;
            for (int i = 1; i < parts.Length; i++)
                total += long.Parse(parts[i]);

            double cpuPercent = 0;
            if (_prevTotalTime > 0)
            {
                var deltaIdle = idle - _prevIdleTime;
                var deltaTotal = total - _prevTotalTime;
                if (deltaTotal > 0)
                    cpuPercent = (1.0 - (double)deltaIdle / deltaTotal) * 100;
            }

            _prevIdleTime = idle;
            _prevTotalTime = total;

            if (_cpuText is not null)
                _cpuText.Content = $"CPU  {cpuPercent:F1}%";
            if (_cpuBar is not null)
                _cpuBar.Value = cpuPercent / 100;

            _cpuHistory.Add(cpuPercent);
            if (_cpuHistory.Count > 30) _cpuHistory.RemoveAt(0);
        }
        catch { }
    }

    private void RefreshMemory()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return;
            var lines = File.ReadAllLines("/proc/meminfo");
            long total = 0, available = 0;

            foreach (var line in lines)
            {
                if (line.StartsWith("MemTotal:"))
                    total = ParseMemValue(line);
                else if (line.StartsWith("MemAvailable:"))
                    available = ParseMemValue(line);
            }

            if (total > 0)
            {
                long used = total - available;
                double percent = (double)used / total * 100;

                if (_memText is not null)
                    _memText.Content = $"MEM  {FormatBytes(used * 1024)} / {FormatBytes(total * 1024)}  ({percent:F1}%)";
                if (_memBar is not null)
                    _memBar.Value = percent / 100;

                _memHistory.Add(percent);
                if (_memHistory.Count > 30) _memHistory.RemoveAt(0);
            }
        }
        catch { }
    }

    private void RefreshDisk()
    {
        try
        {
            var drive = Platform.GetPrimaryDrive();
            if (drive.IsReady)
            {
                var used = drive.TotalSize - drive.AvailableFreeSpace;
                var percent = (double)used / drive.TotalSize * 100;

                if (_diskText is not null)
                    _diskText.Content = $"DISK {FormatBytes(used)} / {FormatBytes(drive.TotalSize)}  ({percent:F1}%)";
                if (_diskBar is not null)
                    _diskBar.Value = percent / 100;
            }
        }
        catch { }
    }

    private void RefreshUptime()
    {
        try
        {
            if (OperatingSystem.IsWindows()) { if (_uptimeText is not null) _uptimeText.Content = $"Up {Environment.TickCount64 / 1000 / 60}m"; return; }
            var uptimeStr = File.ReadAllText("/proc/uptime").Split(' ')[0];
            var seconds = double.Parse(uptimeStr);
            var uptime = TimeSpan.FromSeconds(seconds);

            if (_uptimeText is not null)
                _uptimeText.Content = $"Up {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m";
        }
        catch { }
    }

    private static long ParseMemValue(string line)
    {
        var parts = line.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return 0;
        var num = parts[1].Replace("kB", "").Trim();
        return long.TryParse(num, out var val) ? val : 0;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }
}
