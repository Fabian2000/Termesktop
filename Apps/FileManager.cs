using System.IO.Compression;
using System.Formats.Tar;
using System.Security;
using TermuiX;
using TermuiX.Widgets;
using Termesktop.Components;

namespace Termesktop.Apps;

public class FileManager
{
    private static int _instanceCount;

    private readonly TermuiX.TermuiX _termui;
    private readonly int _instanceId;
    private readonly string _prefix;
    private string _currentPath;

    // Main content
    private StackPanel? _fileList;
    private Container? _scrollContainer;
    private Input? _pathInput;
    private Text? _statusLabel;
    private Container? _rootContainer;
    private int _entryIdx;

    // Popup
    private Container? _popup;
    private Container? _windowRoot;
    private int _popupCounter;

    // Selection state
    private bool _selectMode;
    private readonly HashSet<string> _selectedPaths = new();

    // Drag state
    private string? _dragPath;
    private bool _dragActive;
    private int _dragStartX;
    private int _dragStartY;
    private readonly Dictionary<string, string> _buttonPaths = new();
    private DateTime _lastOpenTime = DateTime.MinValue;
    private string? _lastOpenedPath;

    // Fired when a file/folder is dropped (within same FM or cross-FM)
    public event Action<string, int, int>? OnDragDrop; // sourcePath, screenX, screenY

    // Ordered list of entries for hit-testing drops
    private readonly List<(string path, bool isDir)> _entryList = [];

    private void ToggleSelectMode()
    {
        _selectMode = !_selectMode;
        _selectedPaths.Clear();

        var selBtn = _termui.GetWidget<Button>($"{_prefix}_sel");
        if (selBtn is not null)
            selBtn.Text = _selectMode ? "☑" : "☐";

        UpdateSelectionInfo();
        LoadDirectory(_currentPath);
    }

    private void ToggleSelection(string path, Button? btn = null)
    {
        if (_selectedPaths.Contains(path))
        {
            _selectedPaths.Remove(path);
            if (btn is not null)
            {
                btn.Text = btn.Text.Replace("☑ ", "☐ ");
                btn.BackgroundColor = Color.Parse("Inherit");
            }
        }
        else
        {
            _selectedPaths.Add(path);
            if (btn is not null)
            {
                btn.Text = btn.Text.Replace("☐ ", "☑ ");
                btn.BackgroundColor = Color.Parse(Theme.Hover);
            }
        }

        UpdateSelectionInfo();
    }

    private void SelectAll()
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(_currentPath))
                _selectedPaths.Add(dir);
            foreach (var file in Directory.GetFiles(_currentPath))
                _selectedPaths.Add(file);
        }
        catch { }
        UpdateSelectionInfo();
        LoadDirectory(_currentPath);
    }

    private void UpdateSelectionInfo()
    {
        var info = _termui.GetWidget<Text>($"{_prefix}_selInfo");
        if (info is null) return;

        if (!_selectMode || _selectedPaths.Count == 0)
        {
            info.Width = "0ch";
            info.Content = "";
        }
        else
        {
            info.Width = "auto";
            info.Content = $" {_selectedPaths.Count} selected ";
        }
    }

    private void StartPotentialDrag(string path)
    {
        _dragPath = path;
        _dragActive = false;
        _dragStartX = -1;
        _dragStartY = -1;
    }

    /// <summary>
    /// Returns the path of the folder entry at the given screen coordinates, or null.
    /// Calculates position based on window layout geometry.
    /// </summary>
    public string? GetFolderAtScreen(int screenX, int screenY)
    {
        if (_windowRoot is null || _entryList.Count == 0) return null;

        // Get window position on screen
        int winX = 0, winY = 0;
        var winWidget = (IWidget)_windowRoot;
        int.TryParse(winWidget.PositionX.Replace("ch", ""), out winX);
        int.TryParse(winWidget.PositionY.Replace("ch", ""), out winY);

        // File list starts after: border(1) + title(1) + line(1) + toolbar(1) + line(1) + sidebar header area
        // Content area Y offset within window: title(1) + stackpanel line(1) + toolbar(1) + line(1) = 4
        // Plus border = 5. But with the new StackPanel window layout: title(1) + line(1) + content starts at 2
        // The file list is inside: content > horizontal stackpanel > sidebar(18ch) + line + file container
        // Y offset of file entries: window border(1) + title(1) + line(1) + toolbar(1) + line(1) = 5
        int listStartY = winY + 5;
        // X offset: border(1) + sidebar(18) + line(1) = 20
        int listStartX = winX + 20;

        int relY = screenY - listStartY;
        if (relY < 0 || screenX < listStartX) return null;

        // Account for scroll offset
        // Each entry is 1ch high
        int entryIndex = relY;
        if (entryIndex < 0 || entryIndex >= _entryList.Count) return null;

        var entry = _entryList[entryIndex];
        return entry.isDir ? entry.path : null;
    }

    // Quick-access paths for the sidebar
    private readonly (string Name, string Path, string Icon)[] _quickAccess;

    public FileManager(TermuiX.TermuiX termui, string? startPath = null)
    {
        _termui = termui;
        _instanceId = _instanceCount++;
        _prefix = $"fm{_instanceId}";
        _currentPath = startPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var settings = DesktopSettings.Load();
        _quickAccess = Platform.GetQuickAccess(settings.DesktopFolder, settings.DownloadPath).ToArray();
    }

    public event Action<string>? OnFileOpened;
    public event Action<string>? OnOpenInTerminal;
    public event Action<string>? OnShowProperties;
    public event Action<string, string>? OnRunInTerminal;
    public event Action<string>? OnSetWallpaper;
    public event Action<string>? OnDragStarted;
    public event Action? OnDragCancelled;
    public event Action<List<string>, int, int>? OnMultiDragDrop; // paths, screenX, screenY

    public string CurrentPath => _currentPath;
    public int SelectedCount => _selectedPaths.Count;

    public void Refresh() => LoadDirectory(_currentPath);

    public static string Title => "Files";

    public void BuildContent(Container contentArea, TermuiX.TermuiX termui)
    {
        contentArea.Add($@"
            <StackPanel Direction='Vertical' Width='100%' Height='100%' BackgroundColor='Inherit'>

                <!-- Toolbar -->
                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='{Theme.TitleBar}'>
                    <Button Name='{_prefix}_back' Width='3ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Border}'
                        TextColor='#888888' FocusTextColor='#cccccc'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>←</Button>
                    <Button Name='{_prefix}_fwd' Width='3ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Border}'
                        TextColor='#888888' FocusTextColor='#cccccc'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>→</Button>
                    <Button Name='{_prefix}_up' Width='3ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Border}'
                        TextColor='#888888' FocusTextColor='#cccccc'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>↑</Button>
                    <Line Orientation='Vertical' Type='Solid' Height='1ch'
                        ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />
                    <Button Name='{_prefix}_new' Width='3ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Border}'
                        TextColor='#888888' FocusTextColor='#cccccc'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>+</Button>
                    <Button Name='{_prefix}_sel' Width='3ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Border}'
                        TextColor='#888888' FocusTextColor='#cccccc'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>☐</Button>
                    <Line Orientation='Vertical' Type='Solid' Height='1ch'
                        ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />
                    <Text Name='{_prefix}_selInfo' Width='0ch' Height='1ch'
                        ForegroundColor='#cccccc' BackgroundColor='Inherit' />
                    <Input Name='{_prefix}_path' Width='fill' Height='1ch'
                        ForegroundColor='#cccccc' FocusForegroundColor='#cccccc'
                        BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                        CursorColor='#cccccc'
                        PaddingLeft='0ch' PaddingRight='0ch' />
                </StackPanel>

                <Line Orientation='Horizontal' Type='Solid' Width='100%'
                    ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />

                <!-- Main: Sidebar + Content -->
                <StackPanel Direction='Horizontal' Width='100%' Height='fill'
                    BackgroundColor='Inherit'>

                    <!-- Sidebar -->
                    <StackPanel Direction='Vertical' Width='18ch' Height='100%'
                        BackgroundColor='{Theme.Darker}'>
                        <Text Width='18ch' Height='1ch' PaddingLeft='1ch'
                            ForegroundColor='#666666' BackgroundColor='Inherit'
                            Style='Bold'>Quick Access</Text>
                        <StackPanel Name='{_prefix}_sidebar' Direction='Vertical'
                            Width='18ch' Height='auto' BackgroundColor='Inherit' />
                    </StackPanel>

                    <Line Orientation='Vertical' Type='Solid' Height='100%'
                        ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />

                    <!-- File List -->
                    <Container Name='{_prefix}_scroll' Width='fill' Height='100%' ScrollY='true' BackgroundColor='Inherit'>
                        <StackPanel Name='{_prefix}_list' Direction='Vertical'
                            Width='100%' Height='auto' BackgroundColor='Inherit' />
                    </Container>

                </StackPanel>

                <!-- Statusbar -->
                <Line Orientation='Horizontal' Type='Solid' Width='100%'
                    ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />
                <Text Name='{_prefix}_status' Width='100%' Height='1ch'
                    ForegroundColor='#666666' BackgroundColor='{Theme.Darker}'
                    PaddingLeft='1ch' />

            </StackPanel>");

        _pathInput = termui.GetWidget<Input>($"{_prefix}_path");
        if (_pathInput is not null)
        {
            _pathInput.EnterPressed += (_, text) =>
            {
                if (Directory.Exists(text))
                    NavigateTo(text);
            };
        }
        _fileList = termui.GetWidget<StackPanel>($"{_prefix}_list");
        _scrollContainer = termui.GetWidget<Container>($"{_prefix}_scroll");
        _statusLabel = termui.GetWidget<Text>($"{_prefix}_status");
        _rootContainer = termui.GetWidget<Container>("rootContainer");

        // Find the Window root container for popups
        IWidget? parent = contentArea;
        while (parent?.Parent is not null)
        {
            parent = parent.Parent;
            if (parent is Container c && c.Name?.StartsWith("win_") == true)
            {
                _windowRoot = c;
                break;
            }
        }
        _windowRoot ??= _rootContainer;

        var upBtn = termui.GetWidget<Button>($"{_prefix}_up");
        if (upBtn is not null)
            upBtn.Click += (_, _) => NavigateUp();

        var backBtn = termui.GetWidget<Button>($"{_prefix}_back");
        if (backBtn is not null)
            backBtn.Click += (_, _) => NavigateBack();

        var fwdBtn = termui.GetWidget<Button>($"{_prefix}_fwd");
        if (fwdBtn is not null)
            fwdBtn.Click += (_, _) => NavigateForward();

        var newBtn = termui.GetWidget<Button>($"{_prefix}_new");
        if (newBtn is not null)
            newBtn.Click += (_, _) => ShowNewMenu();

        var selBtn = termui.GetWidget<Button>($"{_prefix}_sel");
        if (selBtn is not null)
            selBtn.Click += (_, _) => ToggleSelectMode();

        _termui.MouseClick += (_, args) =>
        {
            if (args.EventType == MouseEventType.LeftButtonPressed || args.EventType == MouseEventType.RightButtonPressed)
            {
                if (_popup is not null) ClosePopup();
            }

            if (args.EventType == MouseEventType.Moved && _dragPath is not null && !_dragActive)
            {
                if (_dragStartX < 0) { _dragStartX = args.X; _dragStartY = args.Y; return; }
                var dx = Math.Abs(args.X - _dragStartX);
                var dy = Math.Abs(args.Y - _dragStartY);
                if (dx > 2 || dy > 2)
                {
                    _dragActive = true;
                    OnDragStarted?.Invoke(_dragPath);
                }
            }

            if (args.EventType == MouseEventType.LeftButtonReleased && _dragPath is not null)
            {
                var path = _dragPath;
                var wasDrag = _dragActive;
                _dragPath = null;
                _dragActive = false;

                if (wasDrag && _selectMode && _selectedPaths.Count > 0)
                {
                    // Multi-drag drop
                    OnMultiDragDrop?.Invoke(_selectedPaths.ToList(), args.X, args.Y);
                }
                else if (wasDrag)
                {
                    OnDragDrop?.Invoke(path, args.X, args.Y);
                }
                else if (_selectMode && _selectedPaths.Contains(path))
                {
                    // Click on already-selected item without drag = deselect
                    _selectedPaths.Remove(path);
                    UpdateSelectionInfo();
                    LoadDirectory(_currentPath);
                }
                else
                {
                    var now = DateTime.Now;
                    if (path == _lastOpenedPath && (now - _lastOpenTime).TotalMilliseconds < 500)
                        return;
                    _lastOpenTime = now;
                    _lastOpenedPath = path;

                    if (Directory.Exists(path))
                        NavigateTo(path);
                    else if (File.Exists(path))
                        OnFileOpened?.Invoke(path);
                }
            }
        };

        _termui.Shortcut += (_, key) =>
        {
            if (key.Key == ConsoleKey.Escape)
            {
                ClosePopup();
                if (_dragActive)
                {
                    _dragPath = null;
                    _dragActive = false;
                    OnDragCancelled?.Invoke();
                }
            }
        };

        // Sidebar Quick-Access
        var sidebar = termui.GetWidget<StackPanel>($"{_prefix}_sidebar");
        if (sidebar is not null)
            BuildSidebar(sidebar);

        LoadDirectory(_currentPath);
    }

    private void BuildSidebar(StackPanel sidebar)
    {
        int idx = 0;
        foreach (var (name, path, icon) in _quickAccess)
        {
            if (!Directory.Exists(path)) continue;

            var btnName = $"{_prefix}_qa{idx++}";
            var escaped = SecurityElement.Escape(name);
            sidebar.Add($@"
                <Button Name='{btnName}' Width='18ch' Height='1ch'
                    BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                    BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                    TextColor='#aaaaaa' FocusTextColor='#ffffff'
                    PaddingTop='0ch' PaddingBottom='0ch'>{icon} {escaped}</Button>");

            var btn = _termui.GetWidget<Button>(btnName);
            var targetPath = path;
            if (btn is not null)
                btn.Click += (_, _) => NavigateTo(targetPath);
        }
    }

    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();

    private void NavigateTo(string path)
    {
        _backHistory.Push(_currentPath);
        _forwardHistory.Clear();
        LoadDirectory(path);
    }

    private void NavigateUp()
    {
        var parent = Directory.GetParent(_currentPath);
        if (parent is not null)
            NavigateTo(parent.FullName);
    }

    private void NavigateBack()
    {
        if (_backHistory.Count == 0) return;
        _forwardHistory.Push(_currentPath);
        LoadDirectory(_backHistory.Pop());
    }

    private void NavigateForward()
    {
        if (_forwardHistory.Count == 0) return;
        _backHistory.Push(_currentPath);
        LoadDirectory(_forwardHistory.Pop());
    }

    private void LoadDirectory(string path)
    {
        if (_fileList is null || _pathInput is null) return;

        _currentPath = path;
        _pathInput.Value = path;
        _fileList.Clear();
        if (_scrollContainer is not null) ((IWidget)_scrollContainer).ScrollOffsetY = 0;
        _buttonPaths.Clear();
        _entryList.Clear();
        _entryIdx = 0;

        try
        {
            var dirs = Directory.GetDirectories(path)
                .Select(d => new DirectoryInfo(d))
                .OrderBy(d => d.Name)
                .ToList();

            var files = Directory.GetFiles(path)
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.Name)
                .ToList();

            foreach (var dir in dirs)
                AddDirectoryEntry(dir);

            foreach (var file in files)
                AddFileEntry(file);

            if (_statusLabel is not null)
                _statusLabel.Content = $"{dirs.Count} folders, {files.Count} files";
        }
        catch
        {
            _fileList.Add($@"
                <Text Width='100%' Height='1ch' PaddingLeft='1ch'
                    ForegroundColor='#ff6666' BackgroundColor='Inherit'>Access denied</Text>");

            if (_statusLabel is not null)
                _statusLabel.Content = "Access denied";
        }
    }

    private void AddDirectoryEntry(DirectoryInfo dir)
    {
        var name = SecurityElement.Escape(dir.Name);
        var btnName = $"{_prefix}_e{_entryIdx++}";
        var isSelected = _selectedPaths.Contains(dir.FullName);
        var prefix = _selectMode ? (isSelected ? "☑ " : "☐ ") : "";
        var bgColor = isSelected ? Theme.Hover : "Inherit";
        _fileList!.Add($@"
            <Button Name='{btnName}' Width='100%' Height='1ch'
                BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                BackgroundColor='{bgColor}' FocusBackgroundColor='{Theme.Hover}'
                TextColor='#cccccc' FocusTextColor='#ffffff'
                PaddingTop='0ch' PaddingBottom='0ch'>{prefix}📁 {name}</Button>");

        var btn = _termui.GetWidget<Button>(btnName);
        var dirPath = dir.FullName;
        var dirName = dir.Name;
        if (btn is not null)
        {
            _buttonPaths[btnName] = dirPath;
            _entryList.Add((dirPath, true));
            var dirBtn = btn;
            btn.Click += (_, _) =>
            {
                if (_selectMode)
                {
                    if (_selectedPaths.Contains(dirPath))
                        StartPotentialDrag(dirPath); // Drag selected items
                    else
                        ToggleSelection(dirPath, dirBtn);
                }
                else StartPotentialDrag(dirPath);
            };
            btn.RightClick += (_, args) => ShowItemContextMenu(dirPath, dirName, true, args.X, args.Y);
        }
    }

    private void AddFileEntry(FileInfo file)
    {
        var name = SecurityElement.Escape(file.Name);
        var size = FormatSize(file.Length);
        var icon = GetFileIcon(file.Extension);
        var btnName = $"{_prefix}_e{_entryIdx++}";
        var isSelected = _selectedPaths.Contains(file.FullName);
        var prefix = _selectMode ? (isSelected ? "☑ " : "☐ ") : "";
        var bgColor = isSelected ? Theme.Hover : "Inherit";
        _fileList!.Add($@"
            <Button Name='{btnName}' Width='100%' Height='1ch'
                BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                BackgroundColor='{bgColor}' FocusBackgroundColor='{Theme.Hover}'
                TextColor='#888888' FocusTextColor='#cccccc'
                PaddingTop='0ch' PaddingBottom='0ch'>{prefix}{icon} {name}  ({size})</Button>");

        var btn = _termui.GetWidget<Button>(btnName);
        var filePath = file.FullName;
        var fileName = file.Name;
        if (btn is not null)
        {
            _buttonPaths[btnName] = filePath;
            _entryList.Add((filePath, false));
            var fileBtn = btn;
            btn.Click += (_, _) =>
            {
                if (_selectMode)
                {
                    if (_selectedPaths.Contains(filePath))
                        StartPotentialDrag(filePath); // Drag selected items
                    else
                        ToggleSelection(filePath, fileBtn);
                }
                else StartPotentialDrag(filePath);
            };
            btn.RightClick += (_, args) => ShowItemContextMenu(filePath, fileName, false, args.X, args.Y);
        }
    }

    private static string GetFileIcon(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".txt" or ".md" or ".log" => "📝",
            ".cs" or ".js" or ".py" or ".ts" or ".rs" or ".go" or ".java" or ".c" or ".cpp" or ".h" => "📜",
            ".json" or ".xml" or ".yaml" or ".yml" or ".toml" or ".ini" or ".cfg" => "⚙",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" or ".webp" => "🖼",
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" => "🎵",
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" => "🎬",
            ".zip" or ".tar" or ".gz" or ".7z" or ".rar" or ".bz2" => "📦",
            ".pdf" => "📕",
            ".doc" or ".docx" or ".odt" => "📘",
            ".xls" or ".xlsx" or ".ods" or ".csv" => "📊",
            ".sh" or ".bash" or ".zsh" or ".fish" => "⚡",
            ".exe" or ".dll" or ".so" or ".bin" => "⚙",
            ".html" or ".htm" or ".css" => "🌐",
            ".git" or ".gitignore" => "🔀",
            _ => "  ",
        };
    }

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tga", ".tiff"
    };

    private static bool IsImageFile(string path)
    {
        return ImageExtensions.Contains(Path.GetExtension(path));
    }

    private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sh", ".bash", ".zsh", ".fish", ".py", ".rb", ".pl", ".js", ".ts"
    };

    private static bool IsExecutable(string path)
    {
        try
        {
            var ext = Path.GetExtension(path);
            if (ScriptExtensions.Contains(ext)) return true;

            // Windows executables by extension
            if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
                return true;

            // Unix execute permission
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    var mode = File.GetUnixFileMode(path);
                    if ((mode & UnixFileMode.UserExecute) != 0) return true;
                }
                catch { }
            }

            // Magic bytes detection
            using var fs = File.OpenRead(path);
            var header = new byte[4];
            if (fs.Read(header, 0, 4) == 4)
            {
                // ELF (Linux)
                if (header[0] == 0x7F && header[1] == 0x45 && header[2] == 0x4C && header[3] == 0x46)
                    return true;
                // Shebang (#!)
                if (header[0] == 0x23 && header[1] == 0x21)
                    return true;
                // PE (Windows) - MZ header
                if (header[0] == 0x4D && header[1] == 0x5A)
                    return true;
                // Mach-O (macOS) - multiple magic numbers
                if ((header[0] == 0xFE && header[1] == 0xED && header[2] == 0xFA) ||
                    (header[0] == 0xCF && header[1] == 0xFA && header[2] == 0xED) ||
                    (header[0] == 0xCA && header[1] == 0xFE && header[2] == 0xBA && header[3] == 0xBE))
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static bool IsValidFileName(string name, out string error)
    {
        error = "";

        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Name cannot be empty";
            return false;
        }

        if (name.Contains('/') || name.Contains('\\'))
        {
            error = "Name cannot contain path separators";
            return false;
        }

        if (name == "." || name == "..")
        {
            error = "Invalid name";
            return false;
        }

        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in name)
        {
            if (invalid.Contains(c))
            {
                error = $"Invalid character: '{c}'";
                return false;
            }
        }

        if (name.Length > 255)
        {
            error = "Name too long (max 255)";
            return false;
        }

        return true;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    // ===== New File/Folder Menu =====

    private void ShowNewMenu()
    {
        var items = new List<(string label, Action action)>
        {
            ("📄 New File", () => { ClosePopup(); ShowNameDialog("New File", name => CreateFile(name)); }),
            ("📁 New Folder", () => { ClosePopup(); ShowNameDialog("New Folder", name => CreateFolder(name)); }),
            ("💻 Terminal here", () => { ClosePopup(); OnOpenInTerminal?.Invoke(_currentPath); }),
        };

        if (FileClipboard.HasContent)
        {
            var name = Path.GetFileName(FileClipboard.Path!);
            var op = FileClipboard.Operation == ClipboardOperation.Copy ? "Copy" : "Move";
            items.Add(($"📋 Paste ({op}: {name})", () => { ClosePopup(); PasteInto(_currentPath); }));
        }

        if (_selectMode && _selectedPaths.Count > 0)
        {
            items.Add(($"📦 Compress {_selectedPaths.Count} items...", () =>
            {
                ClosePopup();
                ShowCompressSelectedMenu();
            }));
            items.Add(($"🗑 Delete {_selectedPaths.Count} items", () =>
            {
                ClosePopup();
                DeleteSelected();
            }));
        }

        ShowPopup(9, 3, items);
    }

    private void PasteInto(string targetDir)
    {
        if (!FileClipboard.HasContent) return;

        var src = FileClipboard.Path!;
        var dst = Path.Combine(targetDir, Path.GetFileName(src));

        try
        {
            if (FileClipboard.Operation == ClipboardOperation.Copy)
            {
                if (Directory.Exists(src))
                    CopyDirectoryRecursive(src, dst);
                else
                    File.Copy(src, dst, false);
                SetStatus($"Copied: {Path.GetFileName(src)}");
            }
            else
            {
                if (Directory.Exists(src))
                    Directory.Move(src, dst);
                else
                    File.Move(src, dst);
                FileClipboard.Clear();
                SetStatus($"Moved: {Path.GetFileName(src)}");
            }

            LoadDirectory(_currentPath);
        }
        catch (Exception ex)
        {
            SetStatus($"Paste error: {ex.Message}");
        }
    }

    private static void CopyDirectoryRecursive(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(src))
            CopyDirectoryRecursive(dir, Path.Combine(dst, Path.GetFileName(dir)));
    }

    private void SetStatus(string message)
    {
        if (_statusLabel is not null)
            _statusLabel.Content = message;
    }

    private void CreateFile(string name)
    {
        if (!IsValidFileName(name, out var error))
        {
            if (_statusLabel is not null) _statusLabel.Content = error;
            return;
        }

        try
        {
            var path = Path.Combine(_currentPath, name);
            File.WriteAllText(path, "");
            LoadDirectory(_currentPath);
            if (_statusLabel is not null) _statusLabel.Content = $"Created: {name}";
        }
        catch (Exception ex)
        {
            if (_statusLabel is not null) _statusLabel.Content = $"Error: {ex.Message}";
        }
    }

    private void CreateFolder(string name)
    {
        if (!IsValidFileName(name, out var error))
        {
            if (_statusLabel is not null) _statusLabel.Content = error;
            return;
        }

        try
        {
            var path = Path.Combine(_currentPath, name);
            Directory.CreateDirectory(path);
            LoadDirectory(_currentPath);
            if (_statusLabel is not null) _statusLabel.Content = $"Created: {name}/";
        }
        catch (Exception ex)
        {
            if (_statusLabel is not null) _statusLabel.Content = $"Error: {ex.Message}";
        }
    }

    // ===== Item Context Menu (Right-click) =====

    private void ShowItemContextMenu(string path, string name, bool isDir, int screenX, int screenY)
    {
        var items = new List<(string label, Action action)>();

        // If in select mode with selections, show bulk actions
        if (_selectMode && _selectedPaths.Count > 0)
        {
            var count = _selectedPaths.Count;

            items.Add(($"📦 Compress {count} items...", () => { ClosePopup(); ShowCompressSelectedMenu(); }));
            items.Add(($"🗑 Delete {count} items", () => { ClosePopup(); DeleteSelected(); }));
            items.Add(("Select all", () => { ClosePopup(); SelectAll(); }));
            items.Add(("Deselect all", () => { ClosePopup(); _selectedPaths.Clear(); UpdateSelectionInfo(); LoadDirectory(_currentPath); }));

            ShowPopupAtScreen(screenX, screenY, items);
            return;
        }

        if (!isDir)
        {
            items.Add(("Open", () => { ClosePopup(); OnFileOpened?.Invoke(path); }));

            if (IsExecutable(path))
                items.Add(("Run in Terminal", () =>
                {
                    ClosePopup();
                    var dir = Path.GetDirectoryName(path) ?? _currentPath;
                    OnRunInTerminal?.Invoke(path, dir);
                }));

            if (IsImageFile(path))
                items.Add(("Set as Wallpaper", () => { ClosePopup(); OnSetWallpaper?.Invoke(path); }));
        }

        if (isDir)
            items.Add(("Open in Terminal", () => { ClosePopup(); OnOpenInTerminal?.Invoke(path); }));

        // Archive operations
        if (!isDir && IsArchive(path))
        {
            items.Add(("Extract here", () => { ClosePopup(); ExtractArchive(path, _currentPath); }));
            items.Add(("Extract to folder", () => { ClosePopup(); ExtractToFolder(path); }));
        }

        if (isDir || !IsArchive(path))
            items.Add(("Compress...", () => { ClosePopup(); ShowCompressMenu(path, isDir); }));

        items.Add(("Copy", () => { ClosePopup(); FileClipboard.Copy(path); SetStatus($"Copied: {name}"); }));
        items.Add(("Cut", () => { ClosePopup(); FileClipboard.Cut(path); SetStatus($"Cut: {name}"); }));

        if (FileClipboard.HasContent)
        {
            var clipName = Path.GetFileName(FileClipboard.Path!);
            var op = FileClipboard.Operation == ClipboardOperation.Copy ? "Copy" : "Move";
            var pasteTarget = isDir ? path : _currentPath;
            items.Add(($"Paste ({op}: {clipName})", () => { ClosePopup(); PasteInto(pasteTarget); }));
        }

        items.Add(("Rename", () =>
        {
            ClosePopup();
            ShowNameDialog($"Rename: {name}", newName => RenameItem(path, newName), name);
        }));

        items.Add(("Delete", () =>
        {
            ClosePopup();
            ShowConfirmDialog($"Delete '{name}'?", isDir ? "This will delete the folder and all contents." : "This cannot be undone.",
                () => DeleteItem(path, name, isDir));
        }));

        items.Add(("Properties", () => { ClosePopup(); OnShowProperties?.Invoke(path); }));

        // Convert screen coords to window-relative
        int winX = 0, winY = 0;
        if (_windowRoot is not null)
        {
            var bounds = (IWidget)_windowRoot;
            // PositionX/Y are strings like "5ch", parse the number
            int.TryParse(bounds.PositionX.Replace("ch", ""), out winX);
            int.TryParse(bounds.PositionY.Replace("ch", ""), out winY);
            // Account for border
            winX += 1;
            winY += 1;
        }

        ShowPopup(screenX - winX, screenY - winY, items);
    }

    private void RenameItem(string oldPath, string newName)
    {
        if (!IsValidFileName(newName, out var error))
        {
            if (_statusLabel is not null) _statusLabel.Content = error;
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(oldPath)!;
            var newPath = Path.Combine(dir, newName);

            if (Directory.Exists(oldPath))
                Directory.Move(oldPath, newPath);
            else
                File.Move(oldPath, newPath);

            LoadDirectory(_currentPath);
            if (_statusLabel is not null) _statusLabel.Content = $"Renamed to: {newName}";
        }
        catch (Exception ex)
        {
            if (_statusLabel is not null) _statusLabel.Content = $"Error: {ex.Message}";
        }
    }

    private void DeleteItem(string path, string name, bool isDir)
    {
        try
        {
            if (isDir)
                Directory.Delete(path, true);
            else
                File.Delete(path);

            LoadDirectory(_currentPath);
            if (_statusLabel is not null) _statusLabel.Content = $"Deleted: {name}";
        }
        catch (Exception ex)
        {
            if (_statusLabel is not null) _statusLabel.Content = $"Error: {ex.Message}";
        }
    }

    // ===== Popup Helpers =====

    private void ShowPopup(int x, int y, List<(string label, Action action)> items)
    {
        ClosePopup();
        if (_rootContainer is null) return;

        var popupW = 18;
        var popupH = items.Count + 2;

        // Convert window-relative to screen-absolute coordinates
        if (_windowRoot is not null)
        {
            var w = (IWidget)_windowRoot;
            int.TryParse(w.PositionX.Replace("ch", ""), out var wx);
            int.TryParse(w.PositionY.Replace("ch", ""), out var wy);
            x += wx + 1;
            y += wy + 1;
        }

        // Clamp so popup stays on screen
        var screenH = ((IWidget)_rootContainer).ComputedHeight;
        var screenW = ((IWidget)_rootContainer).ComputedWidth;
        if (screenH > 0 && y + popupH > screenH)
            y = Math.Max(0, screenH - popupH);
        if (screenW > 0 && x + popupW > screenW)
            x = Math.Max(0, screenW - popupW);

        _rootContainer.Add($@"
            <Container Name='{_prefix}_popup' Width='{popupW}ch' Height='{popupH}ch'
                PositionX='{x}ch' PositionY='{y}ch'
                BackgroundColor='{Theme.Subtle}' BorderStyle='Single' RoundedCorners='true'
                BorderColor='{Theme.Border}'>
                <Button Name='{_prefix}_popShield' Width='100%' Height='100%'
                    BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                    TextColor='Inherit' FocusTextColor='Inherit'
                    BorderStyle='None' PaddingTop='0ch' PaddingBottom='0ch' />
                <StackPanel Name='{_prefix}_popItems' Direction='Vertical'
                    Width='100%' Height='auto' BackgroundColor='Inherit' />
            </Container>");

        _popup = _termui.GetWidget<Container>($"{_prefix}_popup");
        var list = _termui.GetWidget<StackPanel>($"{_prefix}_popItems");
        if (list is null) return;

        foreach (var (label, action) in items)
        {
            var btnName = $"{_prefix}_pop{_popupCounter++}";
            var escaped = SecurityElement.Escape(label);
            list.Add($@"
                <Button Name='{btnName}' Width='100%' Height='1ch'
                    BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                    BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                    TextColor='#cccccc' FocusTextColor='#ffffff'
                    PaddingTop='0ch' PaddingBottom='0ch'>{escaped}</Button>");

            var btn = _termui.GetWidget<Button>(btnName);
            var act = action;
            if (btn is not null)
                btn.Click += (_, _) => act();
        }
    }

    private void ShowNameDialog(string title, Action<string> onConfirm, string initialValue = "")
    {
        if (_rootContainer is null) return;

        var escaped = SecurityElement.Escape(title);
        var escapedValue = SecurityElement.Escape(initialValue);
        _rootContainer.Add($@"
            <Container Name='{_prefix}_nameDialog' Width='100%' Height='100%'>
                <Button Name='{_prefix}_ndShield' Width='100%' Height='100%'
                    BackgroundColor='#000000' FocusBackgroundColor='#000000'
                    TextColor='#000000' FocusTextColor='#000000'
                    BorderStyle='None' PaddingTop='0ch' PaddingBottom='0ch' />
                <Container Width='35ch' Height='7ch'
                    PositionX='15ch' PositionY='8ch'
                    BackgroundColor='#1a1218' BorderStyle='Single' RoundedCorners='true'
                    BorderColor='{Theme.Lighter}'>
                    <StackPanel Direction='Vertical' Width='100%' Height='100%'
                        BackgroundColor='Inherit' Justify='Center' PaddingLeft='1ch' PaddingRight='1ch'>
                        <Text Width='100%' Height='1ch' ForegroundColor='#cccccc'
                            BackgroundColor='Inherit' Style='Bold'>{escaped}</Text>
                        <Container Width='100%' Height='3ch'
                            BorderStyle='Single' RoundedCorners='true'
                            BackgroundColor='{Theme.Darker}' BorderColor='{Theme.Border}'
                            FocusBorderColor='#5a3030'>
                            <Input Name='{_prefix}_ndInput' Width='100%' Height='1ch'
                                Value='{escapedValue}'
                                ForegroundColor='#cccccc' FocusForegroundColor='#cccccc'
                                BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                                CursorColor='#cccccc'
                                PaddingLeft='0ch' PaddingRight='0ch' />
                        </Container>
                        <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                            BackgroundColor='Inherit' Justify='End'>
                            <Button Name='{_prefix}_ndCancel' Width='8ch' Height='1ch'
                                BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                                TextColor='#888888' FocusTextColor='#cccccc'
                                BorderStyle='None' TextAlign='Center'
                                PaddingTop='0ch' PaddingBottom='0ch'>Cancel</Button>
                            <Button Name='{_prefix}_ndOk' Width='6ch' Height='1ch'
                                BackgroundColor='Inherit' FocusBackgroundColor='#2a2015'
                                TextColor='#cccccc' FocusTextColor='#ffffff'
                                BorderStyle='None' TextAlign='Center'
                                PaddingTop='0ch' PaddingBottom='0ch'>OK</Button>
                        </StackPanel>
                    </StackPanel>
                </Container>
            </Container>");

        var dialog = _termui.GetWidget<Container>($"{_prefix}_nameDialog");
        var input = _termui.GetWidget<Input>($"{_prefix}_ndInput");

        var cancelBtn = _termui.GetWidget<Button>($"{_prefix}_ndCancel");
        if (cancelBtn is not null) cancelBtn.Click += (_, _) =>
        {
            if (dialog is not null) _rootContainer?.Remove(dialog);
        };

        var okBtn = _termui.GetWidget<Button>($"{_prefix}_ndOk");
        if (okBtn is not null) okBtn.Click += (_, _) =>
        {
            var name = input?.Value?.Trim() ?? "";
            if (dialog is not null) _rootContainer?.Remove(dialog);
            if (!string.IsNullOrEmpty(name)) onConfirm(name);
        };

        if (input is not null)
        {
            input.EnterPressed += (_, name) =>
            {
                if (dialog is not null) _rootContainer?.Remove(dialog);
                if (!string.IsNullOrEmpty(name.Trim())) onConfirm(name.Trim());
            };
            _termui.SetFocus(input);
        }
    }

    private void ShowConfirmDialog(string title, string message, Action onConfirm)
    {
        if (_rootContainer is null) return;

        var escapedTitle = SecurityElement.Escape(title);
        var escapedMsg = SecurityElement.Escape(message);

        _rootContainer.Add($@"
            <Container Name='{_prefix}_confirmDialog' Width='100%' Height='100%'>
                <Button Name='{_prefix}_cdShield' Width='100%' Height='100%'
                    BackgroundColor='#000000' FocusBackgroundColor='#000000'
                    TextColor='#000000' FocusTextColor='#000000'
                    BorderStyle='None' PaddingTop='0ch' PaddingBottom='0ch' />
                <Container Width='40ch' Height='7ch'
                    PositionX='15ch' PositionY='8ch'
                    BackgroundColor='#1a1218' BorderStyle='Single' RoundedCorners='true'
                    BorderColor='#5a2020'>
                    <StackPanel Direction='Vertical' Width='100%' Height='100%'
                        BackgroundColor='Inherit' Justify='Center' PaddingLeft='1ch' PaddingRight='1ch'>
                        <Text Width='100%' Height='1ch' ForegroundColor='#ff8888'
                            BackgroundColor='Inherit' Style='Bold'>{escapedTitle}</Text>
                        <Text Width='100%' Height='1ch' ForegroundColor='#888888'
                            BackgroundColor='Inherit'>{escapedMsg}</Text>
                        <Text Width='100%' Height='1ch' BackgroundColor='Inherit' />
                        <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                            BackgroundColor='Inherit' Justify='End'>
                            <Button Name='{_prefix}_cdCancel' Width='10ch' Height='1ch'
                                BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                                TextColor='#888888' FocusTextColor='#cccccc'
                                BorderStyle='None' TextAlign='Center'
                                PaddingTop='0ch' PaddingBottom='0ch'>Cancel</Button>
                            <Button Name='{_prefix}_cdDelete' Width='10ch' Height='1ch'
                                BackgroundColor='Inherit' FocusBackgroundColor='#5a1010'
                                TextColor='#ff5555' FocusTextColor='#ff8888'
                                BorderStyle='None' TextAlign='Center'
                                PaddingTop='0ch' PaddingBottom='0ch'>Delete</Button>
                        </StackPanel>
                    </StackPanel>
                </Container>
            </Container>");

        var dialog = _termui.GetWidget<Container>($"{_prefix}_confirmDialog");

        var cancelBtn = _termui.GetWidget<Button>($"{_prefix}_cdCancel");
        if (cancelBtn is not null) cancelBtn.Click += (_, _) =>
        {
            if (dialog is not null) _rootContainer?.Remove(dialog);
        };

        var deleteBtn = _termui.GetWidget<Button>($"{_prefix}_cdDelete");
        if (deleteBtn is not null) deleteBtn.Click += (_, _) =>
        {
            if (dialog is not null) _rootContainer?.Remove(dialog);
            onConfirm();
        };
    }

    private void ShowPopupAtScreen(int screenX, int screenY, List<(string label, Action action)> items)
    {
        // ShowPopup now uses screen coordinates internally, but expects window-relative input.
        // Since we already have screen coords, subtract window offset so ShowPopup re-adds it.
        int wx = 0, wy = 0;
        if (_windowRoot is not null)
        {
            var b = (IWidget)_windowRoot;
            int.TryParse(b.PositionX.Replace("ch", ""), out wx);
            int.TryParse(b.PositionY.Replace("ch", ""), out wy);
            wx += 1; wy += 1;
        }
        ShowPopup(screenX - wx, screenY - wy, items);
    }

    private void ClosePopup()
    {
        if (_popup is null || _rootContainer is null) return;
        _rootContainer.Remove(_popup);
        _popup = null;
    }

    // ===== Compress Format Selection =====

    private void ShowCompressMenu(string path, bool isDir)
    {
        var baseName = Path.GetFileNameWithoutExtension(path);
        var fullName = Path.GetFileName(path);
        var items = new List<(string label, Action action)>
        {
            ($".zip  ({baseName}.zip)", () => { ClosePopup(); if (isDir) CompressToZip(path); else CompressFileToZip(path); }),
        };

        if (isDir)
        {
            items.Add(($".tar.gz  ({baseName}.tar.gz)", () => { ClosePopup(); CompressToTarGz(path, true); }));
            items.Add(($".tar  ({baseName}.tar)", () => { ClosePopup(); CompressToTar(path, true); }));
        }
        else
        {
            items.Add(($".gz  ({fullName}.gz)", () => { ClosePopup(); CompressToGz(path); }));
        }

        ShowPopup(20, 3, items);
    }

    private void ShowCompressSelectedMenu()
    {
        ShowPopup(20, 3, [
            ($".zip  (archive.zip)", () => { ClosePopup(); CompressSelectedToZip(); }),
            ($".tar.gz  (archive.tar.gz)", () => { ClosePopup(); CompressSelectedToTarGz(); }),
            ($".tar  (archive.tar)", () => { ClosePopup(); CompressSelectedToTar(); }),
        ]);
    }

    // ===== Multi-Selection Operations =====

    private void CompressSelectedToZip()
    {
        if (_selectedPaths.Count == 0) return;

        try
        {
            var zipName = "archive.zip";
            var zipPath = Path.Combine(_currentPath, zipName);
            int i = 1;
            while (File.Exists(zipPath))
            {
                zipName = $"archive ({i++}).zip";
                zipPath = Path.Combine(_currentPath, zipName);
            }

            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (var path in _selectedPaths)
            {
                if (Directory.Exists(path))
                    AddDirectoryToZip(zip, path, Path.GetFileName(path));
                else if (File.Exists(path))
                    zip.CreateEntryFromFile(path, Path.GetFileName(path), CompressionLevel.Optimal);
            }

            ClearSelection();
            SetStatus($"Compressed → {zipName}");
            LoadDirectory(_currentPath);
        }
        catch (Exception ex)
        {
            SetStatus($"Compress error: {ex.Message}");
        }
    }

    private static void AddDirectoryToZip(ZipArchive zip, string dirPath, string entryBase)
    {
        foreach (var file in Directory.GetFiles(dirPath))
            zip.CreateEntryFromFile(file, Path.Combine(entryBase, Path.GetFileName(file)), CompressionLevel.Optimal);
        foreach (var subDir in Directory.GetDirectories(dirPath))
            AddDirectoryToZip(zip, subDir, Path.Combine(entryBase, Path.GetFileName(subDir)));
    }

    private void DeleteSelected()
    {
        if (_selectedPaths.Count == 0) return;

        ShowConfirmDialog(
            $"Delete {_selectedPaths.Count} items?",
            "This cannot be undone.",
            () =>
            {
                int deleted = 0;
                foreach (var path in _selectedPaths.ToList())
                {
                    try
                    {
                        if (Directory.Exists(path))
                            Directory.Delete(path, true);
                        else if (File.Exists(path))
                            File.Delete(path);
                        deleted++;
                    }
                    catch { }
                }

                ClearSelection();
                SetStatus($"Deleted {deleted} items");
                LoadDirectory(_currentPath);
            });
    }

    // ===== Tar/TarGz Compress =====

    private void CompressToGz(string filePath)
    {
        try
        {
            var archivePath = UniqueFilePath(_currentPath, Path.GetFileName(filePath) + ".gz");
            using var input = File.OpenRead(filePath);
            using var output = File.Create(archivePath);
            using var gz = new GZipStream(output, CompressionLevel.Optimal);
            input.CopyTo(gz);

            SetStatus($"Compressed: {Path.GetFileName(archivePath)}");
            LoadDirectory(_currentPath);
        }
        catch (Exception ex) { SetStatus($"Error: {ex.Message}"); }
    }

    private void CompressToTarGz(string path, bool isDir)
    {
        try
        {
            var baseName = Path.GetFileName(path);
            var archiveName = $"{baseName}.tar.gz";
            var archivePath = UniqueFilePath(_currentPath, archiveName);

            using var fileStream = File.Create(archivePath);
            using var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal);
            if (isDir)
                TarFile.CreateFromDirectory(path, gzipStream, includeBaseDirectory: false);
            else
            {
                using var tarWriter = new TarWriter(gzipStream);
                tarWriter.WriteEntry(path, Path.GetFileName(path));
            }

            SetStatus($"Compressed: {Path.GetFileName(archivePath)}");
            LoadDirectory(_currentPath);
        }
        catch (Exception ex) { SetStatus($"Error: {ex.Message}"); }
    }

    private void CompressToTar(string path, bool isDir)
    {
        try
        {
            var baseName = Path.GetFileName(path);
            var archiveName = $"{baseName}.tar";
            var archivePath = UniqueFilePath(_currentPath, archiveName);

            if (isDir)
                TarFile.CreateFromDirectory(path, archivePath, includeBaseDirectory: false);
            else
            {
                using var fileStream = File.Create(archivePath);
                using var tarWriter = new TarWriter(fileStream);
                tarWriter.WriteEntry(path, Path.GetFileName(path));
            }

            SetStatus($"Compressed: {Path.GetFileName(archivePath)}");
            LoadDirectory(_currentPath);
        }
        catch (Exception ex) { SetStatus($"Error: {ex.Message}"); }
    }

    private void CompressSelectedToTarGz()
    {
        if (_selectedPaths.Count == 0) return;
        try
        {
            var archivePath = UniqueFilePath(_currentPath, "archive.tar.gz");
            using var fileStream = File.Create(archivePath);
            using var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal);
            using var tarWriter = new TarWriter(gzipStream);
            foreach (var path in _selectedPaths)
                AddToTar(tarWriter, path, "");

            ClearSelection();
            SetStatus($"Compressed → {Path.GetFileName(archivePath)}");
            LoadDirectory(_currentPath);
        }
        catch (Exception ex) { SetStatus($"Error: {ex.Message}"); }
    }

    private void CompressSelectedToTar()
    {
        if (_selectedPaths.Count == 0) return;
        try
        {
            var archivePath = UniqueFilePath(_currentPath, "archive.tar");
            using var fileStream = File.Create(archivePath);
            using var tarWriter = new TarWriter(fileStream);
            foreach (var path in _selectedPaths)
                AddToTar(tarWriter, path, "");

            ClearSelection();
            SetStatus($"Compressed → {Path.GetFileName(archivePath)}");
            LoadDirectory(_currentPath);
        }
        catch (Exception ex) { SetStatus($"Error: {ex.Message}"); }
    }

    private static void AddToTar(TarWriter tar, string path, string basePath)
    {
        var entryName = string.IsNullOrEmpty(basePath) ? Path.GetFileName(path) : Path.Combine(basePath, Path.GetFileName(path));
        if (File.Exists(path))
        {
            tar.WriteEntry(path, entryName);
        }
        else if (Directory.Exists(path))
        {
            foreach (var file in Directory.GetFiles(path))
                tar.WriteEntry(file, Path.Combine(entryName, Path.GetFileName(file)));
            foreach (var dir in Directory.GetDirectories(path))
                AddToTar(tar, dir, entryName);
        }
    }

    private void ClearSelection()
    {
        _selectedPaths.Clear();
        _selectMode = false;
        var selBtn = _termui.GetWidget<Button>($"{_prefix}_sel");
        if (selBtn is not null) selBtn.Text = "☐";
        UpdateSelectionInfo();
    }

    private static string UniqueFilePath(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        if (!File.Exists(path)) return path;

        var ext = name.Contains(".tar.") ? name[name.IndexOf(".tar.")..] : Path.GetExtension(name);
        var baseName = name[..^ext.Length];
        int i = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(dir, $"{baseName} ({i++}){ext}");
        }
        return path;
    }

    // ===== Archive Operations (native .NET) =====

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".tar", ".gz", ".tgz", ".tar.gz", ".bz2", ".tar.bz2"
    };

    private static bool IsArchive(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        if (name.EndsWith(".tar.gz") || name.EndsWith(".tar.bz2")) return true;
        return ArchiveExtensions.Contains(Path.GetExtension(path));
    }

    private void ExtractArchive(string archivePath, string targetDir)
    {
        try
        {
            var ext = Path.GetExtension(archivePath).ToLowerInvariant();
            var name = Path.GetFileName(archivePath).ToLowerInvariant();

            if (ext == ".zip")
            {
                ZipFile.ExtractToDirectory(archivePath, targetDir, overwriteFiles: true);
                SetStatus($"Extracted: {Path.GetFileName(archivePath)}");
            }
            else if (name.EndsWith(".tar.gz") || ext == ".tgz")
            {
                ExtractTarGz(archivePath, targetDir);
                SetStatus($"Extracted: {Path.GetFileName(archivePath)}");
            }
            else if (ext == ".gz")
            {
                ExtractGz(archivePath, targetDir);
                SetStatus($"Extracted: {Path.GetFileName(archivePath)}");
            }
            else if (ext == ".tar")
            {
                TarFile.ExtractToDirectory(archivePath, targetDir, overwriteFiles: true);
                SetStatus($"Extracted: {Path.GetFileName(archivePath)}");
            }
            else
            {
                SetStatus("Unsupported archive format");
                return;
            }

            LoadDirectory(_currentPath);
        }
        catch (Exception ex)
        {
            SetStatus($"Extract error: {ex.Message}");
        }
    }

    private void ExtractToFolder(string archivePath)
    {
        var folderName = Path.GetFileNameWithoutExtension(archivePath);
        // Handle .tar.gz double extension
        if (folderName.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
            folderName = Path.GetFileNameWithoutExtension(folderName);

        var targetDir = Path.Combine(_currentPath, folderName);
        Directory.CreateDirectory(targetDir);
        ExtractArchive(archivePath, targetDir);
    }

    private static void ExtractTarGz(string archivePath, string targetDir)
    {
        using var fileStream = File.OpenRead(archivePath);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(gzipStream, targetDir, overwriteFiles: true);
    }

    private static void ExtractGz(string archivePath, string targetDir)
    {
        var outputName = Path.GetFileNameWithoutExtension(archivePath);
        var outputPath = Path.Combine(targetDir, outputName);

        using var fileStream = File.OpenRead(archivePath);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var outputStream = File.Create(outputPath);
        gzipStream.CopyTo(outputStream);
    }

    private void CompressToZip(string folderPath)
    {
        try
        {
            var zipName = Path.GetFileName(folderPath) + ".zip";
            var zipPath = Path.Combine(_currentPath, zipName);

            // Avoid overwriting existing
            int i = 1;
            while (File.Exists(zipPath))
            {
                zipName = $"{Path.GetFileName(folderPath)} ({i++}).zip";
                zipPath = Path.Combine(_currentPath, zipName);
            }

            ZipFile.CreateFromDirectory(folderPath, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            SetStatus($"Compressed: {zipName}");
            LoadDirectory(_currentPath);
        }
        catch (Exception ex)
        {
            SetStatus($"Compress error: {ex.Message}");
        }
    }

    private void CompressFileToZip(string filePath)
    {
        try
        {
            var fileName = Path.GetFileName(filePath);
            var zipName = Path.GetFileNameWithoutExtension(filePath) + ".zip";
            var zipPath = Path.Combine(_currentPath, zipName);

            int i = 1;
            while (File.Exists(zipPath))
            {
                zipName = $"{Path.GetFileNameWithoutExtension(filePath)} ({i++}).zip";
                zipPath = Path.Combine(_currentPath, zipName);
            }

            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            zip.CreateEntryFromFile(filePath, fileName, CompressionLevel.Optimal);

            SetStatus($"Compressed: {zipName}");
            LoadDirectory(_currentPath);
        }
        catch (Exception ex)
        {
            SetStatus($"Compress error: {ex.Message}");
        }
    }
}
