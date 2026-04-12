using Termesktop;
using TermuiXLib = TermuiX.TermuiX;

var termui = TermuiXLib.Init();

var deinitialized = false;

void SafeDeInit()
{
    if (deinitialized) return;
    deinitialized = true;
    try { TermuiXLib.DeInit(); } catch { }
    Console.ResetColor();
    Console.CursorVisible = true;
    Console.Write("\x1b[?1003l\x1b[?1006l");
}

AppDomain.CurrentDomain.ProcessExit += (_, _) => SafeDeInit();
AppDomain.CurrentDomain.UnhandledException += (_, _) => SafeDeInit();

Desktop? desktop = null;

// Ctrl+C: prevent desktop kill.
// Children run in setsid session (isolated from our process group).
if (OperatingSystem.IsLinux())
{
    [System.Runtime.InteropServices.DllImport("libc")]
    static extern nint signal(int signum, nint handler);
    signal(2, 1); // SIG_IGN
}

try
{
    desktop = new Desktop(termui);
    desktop.Build();

    while (!desktop.ShutdownRequested)
    {
        desktop.Update();
        termui.Render(skipUnchanged: true);
        await Task.Delay(16);
    }
}
finally
{
    SafeDeInit();
}
