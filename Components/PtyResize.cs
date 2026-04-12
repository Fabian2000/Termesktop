using System.Runtime.InteropServices;

namespace Termesktop.Components;

/// <summary>
/// Resize a PTY by finding the slave device of a script process's child
/// and calling ioctl(TIOCSWINSZ). Linux only.
/// </summary>
public static class PtyResize
{
    [StructLayout(LayoutKind.Sequential)]
    private struct WinSize
    {
        public ushort ws_row;
        public ushort ws_col;
        public ushort ws_xpixel;
        public ushort ws_ypixel;
    }

    private const uint TIOCSWINSZ = 0x5414;
    private const int O_RDWR = 2;
    private const int O_NOCTTY = 256;
    private const int SIGWINCH = 28;

    [DllImport("libc", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPStr)] string path, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, uint request, ref WinSize ws);

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcgetpgrp(int fd);

    private const int SIGINT = 2;

    /// <summary>
    /// Send a signal to the foreground process group of the PTY.
    /// This is how real terminal emulators deliver Ctrl+C — only the foreground app
    /// gets the signal, not the shell behind it.
    /// </summary>
    public static void SendSignalToForeground(int scriptPid, int signal)
    {
        if (!OperatingSystem.IsLinux()) return;
        try
        {
            var ptsPath = FindPtsForScript(scriptPid);
            if (ptsPath is null) return;

            var fd = open(ptsPath, O_RDWR | O_NOCTTY);
            if (fd < 0) return;

            try
            {
                var pgid = tcgetpgrp(fd);
                if (pgid > 0)
                    kill(-pgid, signal); // Negative = entire process group
            }
            finally { close(fd); }
        }
        catch { }
    }

    /// <summary>Send SIGINT to the foreground process group of the PTY.</summary>
    public static void SendInterrupt(int scriptPid) => SendSignalToForeground(scriptPid, SIGINT);

    private static string? FindPtsForScript(int pid)
    {
        // Walk the process tree to find a child connected to a /dev/pts/ device
        try
        {
            var visited = new HashSet<string>();
            return FindPtsRecursive(pid.ToString(), visited);
        }
        catch { return null; }
    }

    private static string? FindPtsRecursive(string pid, HashSet<string> visited)
    {
        if (!visited.Add(pid)) return null;

        // Check this process's fds for a PTS
        var pts = FindPtsPath(pid);
        if (pts is not null) return pts;

        // Check children
        var childrenPath = $"/proc/{pid}/task/{pid}/children";
        if (!File.Exists(childrenPath)) return null;
        var children = File.ReadAllText(childrenPath).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var child in children)
        {
            var result = FindPtsRecursive(child, visited);
            if (result is not null) return result;
        }
        return null;
    }

    /// <summary>
    /// Resize the PTY used by a script process's child to the given dimensions.
    /// Sends SIGWINCH so TUI apps (htop, vim) re-read the size.
    /// </summary>
    public static void Resize(int scriptPid, int cols, int rows)
    {
        if (!OperatingSystem.IsLinux()) return;

        try
        {
            // Find child PIDs of the script process (bash -i)
            var childrenPath = $"/proc/{scriptPid}/task/{scriptPid}/children";
            if (!File.Exists(childrenPath)) return;

            var children = File.ReadAllText(childrenPath).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (children.Length == 0) return;

            // The first child is typically bash. Find its PTY slave.
            var childPid = children[0];
            var ptsPath = FindPtsPath(childPid);
            if (ptsPath is null) return;

            // Open the PTY slave and set window size
            var fd = open(ptsPath, O_RDWR | O_NOCTTY);
            if (fd < 0) return;

            try
            {
                var ws = new WinSize
                {
                    ws_row = (ushort)rows,
                    ws_col = (ushort)cols,
                };
                ioctl(fd, TIOCSWINSZ, ref ws);
            }
            finally
            {
                close(fd);
            }

            // Send SIGWINCH to the child process group so apps re-read the size
            if (int.TryParse(childPid, out var pid))
            {
                kill(-pid, SIGWINCH); // Negative PID = process group
                kill(pid, SIGWINCH);  // Also direct to child
            }
        }
        catch { }
    }

    private static string? FindPtsPath(string pid)
    {
        try
        {
            // /proc/{pid}/fd/0 is a symlink to the PTY slave (e.g. /dev/pts/5)
            var link = $"/proc/{pid}/fd/0";
            var target = File.ResolveLinkTarget(link, true)?.FullName;
            if (target is not null && target.StartsWith("/dev/pts/"))
                return target;

            // Try fd/1 as fallback
            link = $"/proc/{pid}/fd/1";
            target = File.ResolveLinkTarget(link, true)?.FullName;
            if (target is not null && target.StartsWith("/dev/pts/"))
                return target;
        }
        catch { }
        return null;
    }
}
