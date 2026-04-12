using System.Diagnostics;
using System.Security;
using TermuiX;
using TermuiX.Widgets;

using Termesktop.Components;

namespace Termesktop.Apps;

public class TaskManager
{
    private static int _instanceCount;

    private readonly TermuiX.TermuiX _termui;
    private readonly int _instanceId;
    private readonly string _prefix;

    private StackPanel? _processList;
    private StackPanel? _scrollContainer;
    private Text? _statusText;
    private DateTime _lastRefresh = DateTime.MinValue;
    private int _entryIdx;

    // Sort
    private string _sortBy = "mem";
    private bool _sortDesc = true;

    public TaskManager(TermuiX.TermuiX termui)
    {
        _termui = termui;
        _instanceId = _instanceCount++;
        _prefix = $"tm{_instanceId}";
    }

    public static string Title => "Tasks";

    public void BuildContent(Container contentArea, TermuiX.TermuiX termui)
    {
        contentArea.Add($@"
            <StackPanel Direction='Vertical' Width='100%' Height='100%' BackgroundColor='Inherit'>

                <!-- Header -->
                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='{Theme.Subtle}'>
                    <Button Name='{_prefix}_sortName' Width='30ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#aaaaaa' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                        PaddingTop='0ch' PaddingBottom='0ch'>Name</Button>
                    <Button Name='{_prefix}_sortPid' Width='10ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#aaaaaa' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Right'
                        PaddingTop='0ch' PaddingBottom='0ch'>PID</Button>
                    <Button Name='{_prefix}_sortMem' Width='12ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#aaaaaa' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Right'
                        PaddingTop='0ch' PaddingBottom='0ch'>Memory</Button>
                    <Text Width='fill' Height='1ch' BackgroundColor='Inherit' />
                </StackPanel>

                <Line Orientation='Horizontal' Type='Solid' Width='100%'
                    ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />

                <!-- Process list -->
                <StackPanel Name='{_prefix}_scroll' Direction='Vertical'
                    Width='100%' Height='fill' ScrollY='true'
                    BackgroundColor='Inherit'>
                    <StackPanel Name='{_prefix}_list' Direction='Vertical'
                        Width='100%' Height='auto' BackgroundColor='Inherit' />
                </StackPanel>

                <!-- Status bar -->
                <Line Orientation='Horizontal' Type='Solid' Width='100%'
                    ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />
                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='{Theme.Subtle}'>
                    <Text Name='{_prefix}_status' Width='fill' Height='1ch'
                        ForegroundColor='#666666' BackgroundColor='Inherit'
                        PaddingLeft='1ch' />
                    <Button Name='{_prefix}_kill' Width='12ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='#3a1010'
                        TextColor='#666666' FocusTextColor='#ff5555'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>End Task</Button>
                    <Button Name='{_prefix}_refresh' Width='10ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#888888' FocusTextColor='#cccccc'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>Refresh</Button>
                </StackPanel>

            </StackPanel>");

        _processList = termui.GetWidget<StackPanel>($"{_prefix}_list");
        _scrollContainer = termui.GetWidget<StackPanel>($"{_prefix}_scroll");
        _statusText = termui.GetWidget<Text>($"{_prefix}_status");

        // Sort buttons
        var sortName = termui.GetWidget<Button>($"{_prefix}_sortName");
        if (sortName is not null) sortName.Click += (_, _) => SetSort("name");

        var sortPid = termui.GetWidget<Button>($"{_prefix}_sortPid");
        if (sortPid is not null) sortPid.Click += (_, _) => SetSort("pid");

        var sortMem = termui.GetWidget<Button>($"{_prefix}_sortMem");
        if (sortMem is not null) sortMem.Click += (_, _) => SetSort("mem");

        // Kill button
        var killBtn = termui.GetWidget<Button>($"{_prefix}_kill");
        if (killBtn is not null) killBtn.Click += (_, _) => KillSelected();

        // Refresh button
        var refreshBtn = termui.GetWidget<Button>($"{_prefix}_refresh");
        if (refreshBtn is not null) refreshBtn.Click += (_, _) => RefreshProcesses();

        RefreshProcesses();
    }

    private int? _selectedPid;

    private void SetSort(string column)
    {
        if (_sortBy == column)
            _sortDesc = !_sortDesc;
        else
        {
            _sortBy = column;
            _sortDesc = column is "cpu" or "mem";
        }
        RefreshProcesses();
    }

    public void Update()
    {
        if ((DateTime.Now - _lastRefresh).TotalSeconds < 3) return;
        _lastRefresh = DateTime.Now;
        RefreshProcesses();
    }

    private void RefreshProcesses()
    {
        if (_processList is null) return;

        _processList.Clear();
        _entryIdx = 0;
        _lastRefresh = DateTime.Now;

        try
        {
            var all = Process.GetProcesses()
                .Select(p =>
                {
                    try
                    {
                        return new ProcessInfo
                        {
                            Pid = p.Id,
                            Name = p.ProcessName,
                            Memory = p.WorkingSet64,
                        };
                    }
                    catch { return null; }
                })
                .Where(p => p is not null)
                .Cast<ProcessInfo>()
                .ToList();

            IEnumerable<ProcessInfo> sorted = _sortBy switch
            {
                "name" => _sortDesc ? all.OrderByDescending(p => p.Name) : all.OrderBy(p => p.Name),
                "pid" => _sortDesc ? all.OrderByDescending(p => p.Pid) : all.OrderBy(p => p.Pid),
                _ => _sortDesc ? all.OrderByDescending(p => p.Memory) : all.OrderBy(p => p.Memory),
            };

            var list = sorted.ToList();

            foreach (var proc in list)
            {
                var name = SecurityElement.Escape(
                    proc.Name.Length > 22 ? proc.Name[..22] + ".." : proc.Name);
                var mem = FormatBytes(proc.Memory);
                var btnName = $"{_prefix}_p{_entryIdx++}";

                var textColor = _selectedPid == proc.Pid ? "#ffffff" : "#aaaaaa";
                var bgColor = _selectedPid == proc.Pid ? "#2a1515" : "Inherit";

                _processList.Add($@"
                    <Button Name='{btnName}' Width='100%' Height='1ch'
                        BorderStyle='None' TextAlign='Left'
                        BackgroundColor='{bgColor}' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='{textColor}' FocusTextColor='#ffffff'
                        PaddingTop='0ch' PaddingBottom='0ch'> {name,-28} {proc.Pid,8}   {mem,10}</Button>");

                var btn = _termui.GetWidget<Button>(btnName);
                var pid = proc.Pid;
                if (btn is not null)
                    btn.Click += (_, _) =>
                    {
                        _selectedPid = pid;
                        if (_statusText is not null)
                            _statusText.Content = $"Selected: {proc.Name} (PID {pid})";
                    };
            }

            if (_statusText is not null && _selectedPid is null)
                _statusText.Content = $"{list.Count} processes";
        }
        catch (Exception ex)
        {
            if (_statusText is not null)
                _statusText.Content = $"Error: {ex.Message}";
        }
    }

    private void KillSelected()
    {
        if (_selectedPid is null)
        {
            if (_statusText is not null)
                _statusText.Content = "Select a process first";
            return;
        }

        try
        {
            var proc = Process.GetProcessById(_selectedPid.Value);
            var name = proc.ProcessName;
            proc.Kill();
            _selectedPid = null;

            if (_statusText is not null)
                _statusText.Content = $"Killed: {name}";

            RefreshProcesses();
        }
        catch (Exception ex)
        {
            if (_statusText is not null)
                _statusText.Content = $"Error: {ex.Message}";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F0} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    private class ProcessInfo
    {
        public int Pid { get; init; }
        public string Name { get; init; } = "";
        public long Memory { get; init; }
        public double Cpu { get; init; }
    }
}
