namespace Termesktop.Components;

/// <summary>
/// Cross-platform helpers for paths, shells, and system info.
/// </summary>
public static class Platform
{
    public static string DefaultShell =>
        OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash";

    public static string ShellArgs(string command) =>
        OperatingSystem.IsWindows() ? $"/c \"{command}\"" : $"-c \"{command}\"";

    public static string RootPath =>
        OperatingSystem.IsWindows() ? "C:\\" : "/";

    public static string RootLabel =>
        OperatingSystem.IsWindows() ? "C:\\" : "/";

    public static string RootIcon => "💾";

    /// <summary>
    /// Returns filesystem roots for the sidebar (drives on Windows, / on Unix).
    /// </summary>
    public static List<(string name, string path, string icon)> GetRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            var roots = new List<(string, string, string)>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                    roots.Add(($"{drive.Name.TrimEnd('\\')} {drive.VolumeLabel}", drive.RootDirectory.FullName, "💾"));
            }
            return roots;
        }
        return [("/", "/", "💾")];
    }

    /// <summary>
    /// Quick access paths that exist on the current platform.
    /// </summary>
    public static List<(string name, string path, string icon)> GetQuickAccess(string? desktopFolder = null, string? downloadPath = null)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var result = new List<(string, string, string)> { ("Home", home, "🏠") };

        if (!string.IsNullOrEmpty(desktopFolder) && Directory.Exists(desktopFolder))
            result.Add(("Desktop", desktopFolder, "🖥"));

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(docs)) result.Add(("Documents", docs, "📄"));

        var dl = downloadPath ?? Path.Combine(home, "Downloads");
        if (Directory.Exists(dl)) result.Add(("Downloads", dl, "⬇"));

        var pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (!string.IsNullOrEmpty(pics) && Directory.Exists(pics))
            result.Add(("Pictures", pics, "🖼"));

        result.AddRange(GetRoots());
        return result;
    }

    /// <summary>
    /// Primary drive for disk usage in system monitor.
    /// </summary>
    public static DriveInfo GetPrimaryDrive()
    {
        if (OperatingSystem.IsWindows())
            return new DriveInfo("C");
        return new DriveInfo("/");
    }
}
