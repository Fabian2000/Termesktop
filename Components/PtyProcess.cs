using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Termesktop.Components;

/// <summary>
/// PTY via `script -qfc "bash -i" /dev/null` — reliable, no AOT/fork issues.
/// For Ctrl+C: walks /proc to find bash's children and kills them directly.
/// </summary>
public class PtyProcess : IDisposable
{
    private Process? _proc;
    private bool _disposed;
    private int? _cachedBashPid;

    public int ChildPid => _proc?.Id ?? -1;
    public bool IsRunning => _proc is not null && !_proc.HasExited;
    public Stream? StdinStream => _proc?.StandardInput.BaseStream;
    public Stream? StdoutStream => _proc?.StandardOutput.BaseStream;

    [DllImport("libc", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPStr)] string path, int flags);
    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    private static extern int ioctl_winsz(int fd, nuint request, ref WinSize ws);
    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinSize { public ushort ws_row, ws_col, ws_xpixel, ws_ypixel; }

    private const int O_RDWR = 2;
    private const int O_NOCTTY = 256;
    private const nuint TIOCSWINSZ = 0x5414;
    private const int SIGTERM = 15;
    private const int SIGINT = 2;
    private const int SIGWINCH = 28;

    public bool Start(string shell, string workingDir, int cols, int rows)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "setsid",
                Arguments = $"-w script -qfc \"{shell} -i\" /dev/null",
                WorkingDirectory = workingDir,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.Environment["TERM"] = "xterm-256color";
            psi.Environment["LANG"] = "en_US.UTF-8";

            _proc = Process.Start(psi);
            if (_proc is null) return false;

            _proc.StandardInput.AutoFlush = true;

            // Set initial size after process spawns (defer to allow /proc to populate)
            _ = Task.Run(async () =>
            {
                await Task.Delay(200);
                SetSize(cols, rows);
            });

            return true;
        }
        catch { return false; }
    }

    public void WriteBytes(byte[] data, int offset, int count)
    {
        try { _proc?.StandardInput.BaseStream.Write(data, offset, count); _proc?.StandardInput.BaseStream.Flush(); }
        catch { }
    }
    public void WriteBytes(byte[] data) => WriteBytes(data, 0, data.Length);
    public void WriteByte(byte b) => WriteBytes([b]);
    public void WriteString(string s) => WriteBytes(System.Text.Encoding.UTF8.GetBytes(s));

    /// <summary>
    /// Walk /proc to find the bash process in our subtree. Cached.
    /// </summary>
    private int? FindBashPid()
    {
        if (_cachedBashPid.HasValue) return _cachedBashPid;
        if (_proc is null) return null;
        var visited = new HashSet<int>();
        var result = WalkForExec(_proc.Id, "bash", visited);
        if (result.HasValue) _cachedBashPid = result;
        return result;
    }

    private static int? WalkForExec(int pid, string execName, HashSet<int> visited)
    {
        if (!visited.Add(pid)) return null;
        try
        {
            var comm = File.ReadAllText($"/proc/{pid}/comm").Trim();
            if (comm == execName) return pid;

            var childrenPath = $"/proc/{pid}/task/{pid}/children";
            if (!File.Exists(childrenPath)) return null;
            var children = File.ReadAllText(childrenPath).Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var child in children)
            {
                if (int.TryParse(child, out var cpid))
                {
                    var r = WalkForExec(cpid, execName, visited);
                    if (r.HasValue) return r;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Find the PTY slave device by walking to bash.
    /// </summary>
    private string? FindPtsPath()
    {
        var bashPid = FindBashPid();
        if (!bashPid.HasValue) return null;
        try
        {
            var target = File.ResolveLinkTarget($"/proc/{bashPid}/fd/0", true)?.FullName;
            if (target is not null && target.StartsWith("/dev/pts/")) return target;
        }
        catch { }
        return null;
    }

    public void SetSize(int cols, int rows)
    {
        var ptsPath = FindPtsPath();
        if (ptsPath is null) return;
        var fd = open(ptsPath, O_RDWR | O_NOCTTY);
        if (fd < 0) return;
        try
        {
            var ws = new WinSize { ws_row = (ushort)rows, ws_col = (ushort)cols };
            ioctl_winsz(fd, TIOCSWINSZ, ref ws);
        }
        finally { close(fd); }

        // SIGWINCH to bash
        var bashPid = FindBashPid();
        if (bashPid.HasValue) kill(bashPid.Value, SIGWINCH);
    }

    /// <summary>
    /// Kill ALL children of bash (the foreground command like sleep, python, etc.).
    /// Bash itself is not touched.
    /// </summary>
    public void SendInterrupt()
    {
        var bashPid = FindBashPid();
        if (!bashPid.HasValue) return;

        try
        {
            var childrenPath = $"/proc/{bashPid}/task/{bashPid}/children";
            if (!File.Exists(childrenPath)) return;
            var children = File.ReadAllText(childrenPath).Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var child in children)
            {
                if (int.TryParse(child, out var cpid))
                    kill(cpid, SIGTERM);
            }
        }
        catch { }
    }

    public bool HasExited() => _proc is null || _proc.HasExited;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_proc is not null && !_proc.HasExited)
                _proc.Kill(true);
            _proc?.Dispose();
        }
        catch { }
        _proc = null;
    }
}
