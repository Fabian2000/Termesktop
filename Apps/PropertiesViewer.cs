using System.Security;
using TermuiX;
using TermuiX.Widgets;

using Termesktop.Components;

namespace Termesktop.Apps;

public class PropertiesViewer
{
    private static int _instanceCount;

    private readonly TermuiX.TermuiX _termui;
    private readonly int _instanceId;
    private readonly string _prefix;
    private readonly string _path;
    private StackPanel? _content;
    private StackPanel? _scrollPanel;
    private int _btnCounter;

    public PropertiesViewer(TermuiX.TermuiX termui, string path)
    {
        _termui = termui;
        _instanceId = _instanceCount++;
        _prefix = $"prop{_instanceId}";
        _path = path;
    }

    public void BuildContent(Container contentArea, TermuiX.TermuiX termui)
    {
        contentArea.Add($@"
            <StackPanel Name='{_prefix}_scroll' Direction='Vertical'
                Width='100%' Height='100%' ScrollY='true'
                BackgroundColor='Inherit'>
                <StackPanel Name='{_prefix}_content' Direction='Vertical'
                    Width='100%' Height='auto' BackgroundColor='Inherit'
                    PaddingLeft='1ch' PaddingRight='1ch' />
            </StackPanel>");

        _content = termui.GetWidget<StackPanel>($"{_prefix}_content");
        _scrollPanel = termui.GetWidget<StackPanel>($"{_prefix}_scroll");
        if (_content is null) return;

        var isDir = Directory.Exists(_path);
        var isFile = File.Exists(_path);

        if (!isDir && !isFile)
        {
            AddSection("Error");
            AddRow("Status", "Path not found");
            return;
        }

        if (isFile)
            BuildFileProperties();
        else
            BuildDirectoryProperties();
    }

    private void BuildFileProperties()
    {
        var info = new FileInfo(_path);
        var icon = GetFileIcon(info.Extension);

        // Header
        AddHeader($"{icon}  {Esc(info.Name)}");

        // General
        AddSection("General");
        AddRow("Type", GetFileType(info.Extension));
        AddRow("Extension", string.IsNullOrEmpty(info.Extension) ? "(none)" : info.Extension);
        AddRow("Size", FormatSizeDetailed(info.Length));

        // Location
        AddSection("Location");
        AddRow("Folder", info.DirectoryName ?? "");
        AddRow("Full Path", info.FullName);

        // Timestamps
        AddSection("Timestamps");
        AddRow("Created", info.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"));
        AddRow("Modified", info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
        AddRow("Accessed", info.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss"));

        // Permissions
        AddSection("Permissions");
        AddRow("Read Only", info.IsReadOnly ? "Yes" : "No");
        AddRow("Attributes", info.Attributes.ToString());
        try
        {
            AddNamedRow($"{_prefix}_modeText", "Unix Mode", info.UnixFileMode.ToString());
            AddNamedRow($"{_prefix}_octalText", "Octal", Convert.ToString((int)info.UnixFileMode, 8));
            AddChmodButtons(info.FullName);
        }
        catch (Exception ex)
        {
            AddRow("Perm Error", ex.Message);
        }

        // Content analysis
        try
        {
            if (info.Length > 0 && info.Length < 1024 * 1024)
            {
                var bytes = File.ReadAllBytes(_path);
                bool isBinary = bytes.Take(512).Any(b => b == 0);

                AddSection("Content");
                AddRow("Format", isBinary ? "Binary" : "Text");

                if (!isBinary)
                {
                    var text = File.ReadAllText(_path);
                    var lines = text.Split('\n').Length;
                    var words = text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
                    AddRow("Lines", lines.ToString("N0"));
                    AddRow("Words", words.ToString("N0"));
                    AddRow("Characters", text.Length.ToString("N0"));
                    AddRow("Encoding", DetectEncoding(bytes));
                }
                else
                {
                    var magic = string.Join(" ", bytes.Take(16).Select(b => $"{b:X2}"));
                    AddRow("Magic Bytes", magic);
                    AddRow("MIME Guess", GuessMime(bytes));
                }
            }
        }
        catch { }

        // Image metadata
        try
        {
            var imgExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tga", ".tiff" };
            if (imgExts.Contains(info.Extension))
            {
                using var img = SixLabors.ImageSharp.Image.Load(info.FullName);
                AddSection("Image");
                AddRow("Width", $"{img.Width} px");
                AddRow("Height", $"{img.Height} px");
                AddRow("Resolution", $"{img.Width}x{img.Height}");
                AddRow("Bit Depth", $"{img.PixelType.BitsPerPixel} bpp");
                var megapixels = (img.Width * (long)img.Height) / 1_000_000.0;
                AddRow("Megapixels", $"{megapixels:F1} MP");
            }
        }
        catch { }
    }

    private void BuildDirectoryProperties()
    {
        var info = new DirectoryInfo(_path);

        AddHeader($"📁  {Esc(info.Name)}");

        AddSection("General");
        AddRow("Type", "Directory");
        AddRow("Location", info.Parent?.FullName ?? Platform.RootPath);
        AddRow("Full Path", info.FullName);

        AddSection("Timestamps");
        AddRow("Created", info.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"));
        AddRow("Modified", info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
        AddRow("Accessed", info.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss"));

        AddSection("Permissions");
        AddRow("Attributes", info.Attributes.ToString());
        try
        {
            AddNamedRow($"{_prefix}_modeText", "Unix Mode", info.UnixFileMode.ToString());
            AddNamedRow($"{_prefix}_octalText", "Octal", Convert.ToString((int)info.UnixFileMode, 8));
            AddChmodButtons(info.FullName);
        }
        catch { }

        try
        {
            var dirs = info.GetDirectories();
            var files = info.GetFiles();
            var hiddenDirs = dirs.Count(d => (d.Attributes & FileAttributes.Hidden) != 0);
            var hiddenFiles = files.Count(f => (f.Attributes & FileAttributes.Hidden) != 0);

            AddSection("Contents");
            AddRow("Folders", $"{dirs.Length} ({hiddenDirs} hidden)");
            AddRow("Files", $"{files.Length} ({hiddenFiles} hidden)");
            AddRow("Total Items", (dirs.Length + files.Length).ToString("N0"));

            var totalSize = files.Sum(f => f.Length);
            AddRow("Direct Size", FormatSizeDetailed(totalSize));

            if (files.Length > 0)
            {
                var largest = files.OrderByDescending(f => f.Length).First();
                AddRow("Largest File", $"{largest.Name} ({FormatSize(largest.Length)})");

                var newest = files.OrderByDescending(f => f.LastWriteTime).First();
                AddRow("Newest File", $"{newest.Name} ({newest.LastWriteTime:yyyy-MM-dd})");
            }

            // Extension breakdown
            var extGroups = files.GroupBy(f => f.Extension.ToLowerInvariant())
                .OrderByDescending(g => g.Count())
                .Take(5);
            if (extGroups.Any())
            {
                AddSection("File Types");
                foreach (var g in extGroups)
                {
                    var ext = string.IsNullOrEmpty(g.Key) ? "(no ext)" : g.Key;
                    AddRow(ext, $"{g.Count()} files, {FormatSize(g.Sum(f => f.Length))}");
                }
            }
        }
        catch
        {
            AddSection("Contents");
            AddRow("Status", "Access denied");
        }
    }

    // ===== UI Helpers =====

    private void AddHeader(string text)
    {
        _content?.Add($@"
            <StackPanel Direction='Horizontal' Width='100%' Height='2ch'
                BackgroundColor='{Theme.Subtle}' Align='Center' PaddingLeft='1ch'>
                <Text Width='fill' Height='1ch' ForegroundColor='#cccccc'
                    BackgroundColor='Inherit' Style='Bold'>{text}</Text>
            </StackPanel>
            <Line Orientation='Horizontal' Type='Solid' Width='100%'
                ForegroundColor='{Theme.Lighter}' BackgroundColor='Inherit' />");
    }

    private void AddSection(string title)
    {
        _content?.Add($@"
            <Text Width='100%' Height='1ch' ForegroundColor='#aa6666'
                BackgroundColor='Inherit' Style='Bold' MarginTop='0ch'
                PaddingLeft='0ch'>{Esc(title)}</Text>
            <Line Orientation='Horizontal' Type='Dotted' Width='100%'
                ForegroundColor='{Theme.Hover}' BackgroundColor='Inherit' />");
    }

    private void AddRow(string label, string value)
    {
        _content?.Add($@"
            <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                BackgroundColor='Inherit'>
                <Text Width='16ch' Height='1ch' ForegroundColor='#888888'
                    BackgroundColor='Inherit' PaddingLeft='1ch'>{Esc(label)}</Text>
                <Text Width='fill' Height='1ch' ForegroundColor='#cccccc'
                    BackgroundColor='Inherit'>{Esc(value)}</Text>
            </StackPanel>");
    }

    private void AddChmodButtons(string filePath)
    {
        if (_content is null) return;

        if (OperatingSystem.IsWindows()) return;
        UnixFileMode currentMode;
        try { currentMode = File.GetUnixFileMode(filePath); }
        catch { return; }

        // Header: R W X columns
        _content.Add($@"
            <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                BackgroundColor='Inherit'>
                <Text Width='10ch' Height='1ch' BackgroundColor='Inherit' />
                <Text Width='6ch' Height='1ch' ForegroundColor='#888888'
                    BackgroundColor='Inherit' TextAlign='Center'>Read</Text>
                <Text Width='6ch' Height='1ch' ForegroundColor='#888888'
                    BackgroundColor='Inherit' TextAlign='Center'>Write</Text>
                <Text Width='6ch' Height='1ch' ForegroundColor='#888888'
                    BackgroundColor='Inherit' TextAlign='Center'>Exec</Text>
            </StackPanel>");

        // Rows: User, Group, Other
        var groups = new (string label, UnixFileMode r, UnixFileMode w, UnixFileMode x)[]
        {
            ("User", UnixFileMode.UserRead, UnixFileMode.UserWrite, UnixFileMode.UserExecute),
            ("Group", UnixFileMode.GroupRead, UnixFileMode.GroupWrite, UnixFileMode.GroupExecute),
            ("Other", UnixFileMode.OtherRead, UnixFileMode.OtherWrite, UnixFileMode.OtherExecute),
        };

        foreach (var (label, rFlag, wFlag, xFlag) in groups)
        {
            var rName = $"{_prefix}_p{_btnCounter++}";
            var wName = $"{_prefix}_p{_btnCounter++}";
            var xName = $"{_prefix}_p{_btnCounter++}";

            var rSet = (currentMode & rFlag) != 0;
            var wSet = (currentMode & wFlag) != 0;
            var xSet = (currentMode & xFlag) != 0;

            _content.Add($@"
                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='Inherit' Align='Center'>
                    <Text Width='10ch' Height='1ch' ForegroundColor='#aaaaaa'
                        BackgroundColor='Inherit' PaddingLeft='1ch'>{label}</Text>
                    <StackPanel Direction='Horizontal' Width='6ch' Height='1ch'
                        BackgroundColor='Inherit' Justify='Center'>
                        <Checkbox Name='{rName}' Checked='{rSet.ToString().ToLower()}'
                            ForegroundColor='#cccccc' FocusForegroundColor='#ffffff' />
                    </StackPanel>
                    <StackPanel Direction='Horizontal' Width='6ch' Height='1ch'
                        BackgroundColor='Inherit' Justify='Center'>
                        <Checkbox Name='{wName}' Checked='{wSet.ToString().ToLower()}'
                            ForegroundColor='#cccccc' FocusForegroundColor='#ffffff' />
                    </StackPanel>
                    <StackPanel Direction='Horizontal' Width='6ch' Height='1ch'
                        BackgroundColor='Inherit' Justify='Center'>
                        <Checkbox Name='{xName}' Checked='{xSet.ToString().ToLower()}'
                            ForegroundColor='#cccccc' FocusForegroundColor='#ffffff' />
                    </StackPanel>
                </StackPanel>");

            BindPermCheckbox(filePath, rName, rFlag);
            BindPermCheckbox(filePath, wName, wFlag);
            BindPermCheckbox(filePath, xName, xFlag);
        }
    }

    private void BindPermCheckbox(string filePath, string cbName, UnixFileMode flag)
    {
        var cb = _termui.GetWidget<Checkbox>(cbName);
        if (cb is null) return;

        cb.CheckedChanged += (_, isChecked) =>
        {
            try
            {
                if (OperatingSystem.IsWindows()) return;
                var current = File.GetUnixFileMode(filePath);
                var newMode = isChecked ? current | flag : current & ~flag;
                File.SetUnixFileMode(filePath, newMode);
                UpdatePermDisplay();
            }
            catch { }
        };
    }

    private void AddNamedRow(string valueName, string label, string value)
    {
        _content?.Add($@"
            <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                BackgroundColor='Inherit'>
                <Text Width='16ch' Height='1ch' ForegroundColor='#888888'
                    BackgroundColor='Inherit' PaddingLeft='1ch'>{Esc(label)}</Text>
                <Text Name='{valueName}' Width='fill' Height='1ch' ForegroundColor='#cccccc'
                    BackgroundColor='Inherit'>{Esc(value)}</Text>
            </StackPanel>");
    }

    private void UpdatePermDisplay()
    {
        var modeText = _termui.GetWidget<Text>($"{_prefix}_modeText");
        var octalText = _termui.GetWidget<Text>($"{_prefix}_octalText");
        if (modeText is null || octalText is null) return;

        if (OperatingSystem.IsWindows()) return;
        try
        {
            var mode = File.GetUnixFileMode(_path);
            modeText.Content = mode.ToString();
            octalText.Content = Convert.ToString((int)mode, 8);
        }
        catch { }
    }

    private static string Esc(string s) => SecurityElement.Escape(s);

    // ===== Formatting =====

    private static string FormatSizeDetailed(long bytes)
    {
        if (bytes < 1024) return $"{bytes} bytes";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB ({bytes:N0} bytes)";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB ({bytes:N0} bytes)";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB ({bytes:N0} bytes)";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    private static string GetFileIcon(string ext)
    {
        return ext.ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" => "🖼",
            ".mp3" or ".wav" or ".flac" => "🎵",
            ".mp4" or ".mkv" or ".avi" => "🎬",
            ".zip" or ".tar" or ".gz" => "📦",
            ".pdf" => "📕",
            ".cs" or ".js" or ".py" or ".rs" or ".go" => "📜",
            ".txt" or ".md" or ".log" => "📝",
            ".sh" or ".bash" => "⚡",
            ".json" or ".xml" or ".yaml" or ".yml" => "⚙",
            _ => "📄",
        };
    }

    private static string GetFileType(string ext)
    {
        return ext.ToLowerInvariant() switch
        {
            ".txt" => "Text Document", ".md" => "Markdown Document",
            ".cs" => "C# Source Code", ".js" => "JavaScript Source",
            ".ts" => "TypeScript Source", ".py" => "Python Script",
            ".rs" => "Rust Source", ".go" => "Go Source", ".java" => "Java Source",
            ".json" => "JSON Data", ".xml" => "XML Document",
            ".yaml" or ".yml" => "YAML Document",
            ".html" or ".htm" => "HTML Document", ".css" => "CSS Stylesheet",
            ".png" => "PNG Image", ".jpg" or ".jpeg" => "JPEG Image",
            ".gif" => "GIF Image", ".webp" => "WebP Image", ".bmp" => "Bitmap Image",
            ".mp3" => "MP3 Audio", ".wav" => "WAV Audio",
            ".mp4" => "MP4 Video", ".mkv" => "MKV Video",
            ".zip" => "ZIP Archive", ".tar" => "TAR Archive", ".gz" => "GZip Archive",
            ".pdf" => "PDF Document", ".sh" => "Shell Script",
            ".exe" => "Executable", ".dll" => "Dynamic Library", ".so" => "Shared Library",
            _ => string.IsNullOrEmpty(ext) ? "File" : $"{ext.TrimStart('.')} File",
        };
    }

    private static string DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return "UTF-8 (BOM)";
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return "UTF-16 LE";
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return "UTF-16 BE";
        return "UTF-8";
    }

    private static string GuessMime(byte[] bytes)
    {
        if (bytes.Length < 4) return "unknown";
        if (bytes[0] == 0x89 && bytes[1] == 0x50) return "image/png";
        if (bytes[0] == 0xFF && bytes[1] == 0xD8) return "image/jpeg";
        if (bytes[0] == 0x47 && bytes[1] == 0x49) return "image/gif";
        if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46) return "image/webp or audio/wav";
        if (bytes[0] == 0x50 && bytes[1] == 0x4B) return "application/zip";
        if (bytes[0] == 0x7F && bytes[1] == 0x45 && bytes[2] == 0x4C && bytes[3] == 0x46) return "application/x-elf";
        if (bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46) return "application/pdf";
        return "application/octet-stream";
    }
}
