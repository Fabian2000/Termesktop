namespace Termesktop.Components;

public enum ClipboardOperation { Copy, Cut }

/// <summary>
/// Shared clipboard for file operations across all FileManager instances.
/// </summary>
public static class FileClipboard
{
    public static string? Path { get; private set; }
    public static ClipboardOperation Operation { get; private set; }
    public static bool HasContent => Path is not null;

    public static void Copy(string path)
    {
        Path = path;
        Operation = ClipboardOperation.Copy;
    }

    public static void Cut(string path)
    {
        Path = path;
        Operation = ClipboardOperation.Cut;
    }

    public static void Clear()
    {
        Path = null;
    }
}
