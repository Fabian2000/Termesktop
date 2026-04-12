using System.Security;
using TermuiX;
using TermuiX.Widgets;
using Termesktop.Apps;

namespace Termesktop.Components;

public class DesktopIcons
{
    private readonly TermuiX.TermuiX _termui;
    private Container? _desktopArea;
    private Container? _rootContainer;
    private readonly List<IWidget> _iconWidgets = [];
    private Container? _contextMenu;
    private int _iconCounter;
    private int _menuCounter;
    private string? _desktopFolder;
    private DateTime _lastRefresh = DateTime.MinValue;
    private int _lastLayoutHeight;

    // Drag state
    private string? _dragPath;
    private bool _dragActive;
    private int _dragStartX;
    private int _dragStartY;

    // Icon tracking for right-click and drag
    private readonly Dictionary<string, string> _iconPaths = new();
    private readonly Dictionary<string, bool> _iconIsDir = new();

    public event Action<string>? OnFileOpened;
    public event Action<string>? OnOpenInTerminal;
    public event Action<string>? OnShowProperties;
    public event Action? OnOpenSettings;
    public event Action<string>? OnDragStarted;
    public event Action<string, int, int>? OnDragDrop;
    public event Action<string>? OnSetWallpaper;
    public event Action<string, string>? OnRunInTerminal;

    public DesktopIcons(TermuiX.TermuiX termui)
    {
        _termui = termui;
    }

    public void Initialize(Container rootContainer, Container desktopArea)
    {
        _rootContainer = rootContainer;
        _desktopArea = desktopArea;

        // Right-click on desktop
        _termui.MouseClick += (_, args) =>
        {
            // Close existing menu first (both left and right click)
            if ((args.EventType == MouseEventType.LeftButtonPressed || args.EventType == MouseEventType.RightButtonPressed) && _contextMenu is not null)
                CloseContextMenu();

            if (args.EventType == MouseEventType.RightButtonPressed)
            {
                if (!IsClickOnWindow(args.X, args.Y))
                    ShowDesktopContextMenu(args.X, args.Y);
            }

            // Drag tracking
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

                if (wasDrag)
                    OnDragDrop?.Invoke(path, args.X, args.Y);
                else
                {
                    // Click - open
                    if (Directory.Exists(path))
                        OnFileOpened?.Invoke(path);
                    else if (File.Exists(path))
                        OnFileOpened?.Invoke(path);
                }
            }
        };

        _termui.Shortcut += (_, key) =>
        {
            if (key.Key == ConsoleKey.Escape) CloseContextMenu();
        };
    }

    public void ForceRefresh() => RefreshIcons();

    public void Update(DesktopSettings settings)
    {
        var newFolder = settings.DesktopFolder;

        var currentHeight = Console.WindowHeight;
        if (newFolder != _desktopFolder
            || (DateTime.Now - _lastRefresh).TotalSeconds > 2
            || currentHeight != _lastLayoutHeight)
        {
            _desktopFolder = newFolder;
            _lastRefresh = DateTime.Now;
            _lastLayoutHeight = currentHeight;
            RefreshIcons();
        }
    }

    private void RefreshIcons()
    {
        if (_desktopArea is null) return;

        // Remove old icons
        foreach (var widget in _iconWidgets)
            _desktopArea.Remove(widget);
        _iconWidgets.Clear();
        _iconPaths.Clear();
        _iconIsDir.Clear();

        if (_desktopFolder is null || !Directory.Exists(_desktopFolder))
            return;

        try
        {
            var entries = new List<(string icon, string name, string path, bool isDir)>();

            foreach (var dir in Directory.GetDirectories(_desktopFolder)
                .Select(d => new DirectoryInfo(d))
                .Where(d => (d.Attributes & FileAttributes.Hidden) == 0)
                .OrderBy(d => d.Name)
                .Take(20))
            {
                entries.Add(("📁", dir.Name, dir.FullName, true));
            }

            foreach (var file in Directory.GetFiles(_desktopFolder)
                .Select(f => new FileInfo(f))
                .Where(f => (f.Attributes & FileAttributes.Hidden) == 0)
                .OrderBy(f => f.Name)
                .Take(30))
            {
                entries.Add((GetFileIcon(file.Extension), file.Name, file.FullName, false));
            }

            if (entries.Count == 0) return;

            // Layout: columns of icons, top-to-bottom then next column
            // Each icon: 12ch wide, 3ch tall. Calculate rows per column.
            int availHeight = _desktopArea is not null ? ((IWidget)_desktopArea).ComputedHeight : 0;
            if (availHeight <= 0) availHeight = Console.WindowHeight - 8;
            int rowsPerCol = Math.Max(2, (availHeight - 2) / 3);

            int colX = 1;
            int rowY = 1;
            int colCount = 0;

            foreach (var (icon, name, path, isDir) in entries)
            {
                var btnName = $"dIcon_{_iconCounter++}";
                var displayName = name.Length > 10 ? name[..9] + "…" : name;
                var escaped = SecurityElement.Escape(displayName);

                _desktopArea!.Add($@"
                    <Button Name='{btnName}' Width='12ch' Height='3ch'
                        PositionX='{colX}ch' PositionY='{rowY}ch'
                        BorderStyle='None' TextAlign='Center'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#cccccc' FocusTextColor='#ffffff'
                        PaddingTop='0ch' PaddingBottom='0ch'>\n{icon}\n{escaped}</Button>");

                var btn = _termui.GetWidget<Button>(btnName);
                var itemPath = path;
                var itemIsDir = isDir;
                if (btn is not null)
                {
                    _iconWidgets.Add(btn);
                    _iconPaths[btnName] = itemPath;
                    _iconIsDir[btnName] = itemIsDir;

                    btn.Click += (_, _) => StartPotentialDrag(itemPath);
                    btn.RightClick += (_, args) => ShowIconContextMenu(itemPath, name, itemIsDir, args.X, args.Y);
                }

                rowY += 3;
                colCount++;
                if (colCount >= rowsPerCol)
                {
                    colCount = 0;
                    rowY = 1;
                    colX += 13;
                }
            }
        }
        catch { }
    }

    private void StartPotentialDrag(string path)
    {
        _dragPath = path;
        _dragStartX = -1;
        _dragStartY = -1;
        _dragActive = false;
    }

    // ===== Icon Context Menu =====

    private void ShowIconContextMenu(string path, string name, bool isDir, int x, int y)
    {
        CloseContextMenu();
        if (_rootContainer is null) return;

        var items = new List<(string label, Action action)>();

        if (!isDir)
        {
            items.Add(("Open", () => { CloseContextMenu(); OnFileOpened?.Invoke(path); }));

            if (IsExecutable(path))
                items.Add(("Run in Terminal", () =>
                {
                    CloseContextMenu();
                    OnRunInTerminal?.Invoke(path, Path.GetDirectoryName(path) ?? Platform.RootPath);
                }));
        }
        else
        {
            items.Add(("Open in Files", () => { CloseContextMenu(); OnFileOpened?.Invoke(path); }));
            items.Add(("Open in Terminal", () => { CloseContextMenu(); OnOpenInTerminal?.Invoke(path); }));
        }

        items.Add(("Copy", () => { CloseContextMenu(); FileClipboard.Copy(path); }));
        items.Add(("Cut", () => { CloseContextMenu(); FileClipboard.Cut(path); }));

        items.Add(("Rename", () =>
        {
            CloseContextMenu();
            RenameItem(path, name);
        }));

        items.Add(("Delete", () =>
        {
            CloseContextMenu();
            DeleteItem(path, name, isDir);
        }));

        var imgExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
        if (!isDir && imgExts.Contains(Path.GetExtension(path)))
            items.Add(("Set as Wallpaper", () => { CloseContextMenu(); OnSetWallpaper?.Invoke(path); }));

        items.Add(("Properties", () => { CloseContextMenu(); OnShowProperties?.Invoke(path); }));

        BuildPopup(x, y, items);
    }

    // ===== Desktop Context Menu =====

    public event Action? OnOpenTerminal;
    public event Action? OnOpenFiles;
    public event Action? OnOpenWallpaperPicker;

    private void ShowDesktopContextMenu(int x, int y)
    {
        CloseContextMenu();
        if (_rootContainer is null) return;

        var items = new List<(string label, Action action)>();

        if (_desktopFolder is not null && Directory.Exists(_desktopFolder))
        {
            items.Add(("📄 New File", () => { CloseContextMenu(); CreateNewFile(); }));
            items.Add(("📁 New Folder", () => { CloseContextMenu(); CreateNewFolder(); }));

            if (FileClipboard.HasContent)
            {
                var clipName = Path.GetFileName(FileClipboard.Path!);
                items.Add(($"📋 Paste ({clipName})", () => { CloseContextMenu(); PasteToDesktop(); }));
            }

            items.Add(("──", () => { }));
            items.Add(("📁 Open in Files", () => { CloseContextMenu(); OnFileOpened?.Invoke(_desktopFolder); }));
        }

        items.Add(("💻 Terminal", () => { CloseContextMenu(); OnOpenTerminal?.Invoke(); }));
        items.Add(("📁 Files", () => { CloseContextMenu(); OnOpenFiles?.Invoke(); }));
        items.Add(("──", () => { }));
        items.Add(("🖼 Change Wallpaper", () => { CloseContextMenu(); OnOpenWallpaperPicker?.Invoke(); }));
        items.Add(("⚙ Settings", () => { CloseContextMenu(); OnOpenSettings?.Invoke(); }));

        BuildPopup(x, y, items);
    }

    private void BuildPopup(int x, int y, List<(string label, Action action)> items)
    {
        if (_rootContainer is null) return;

        var popW = 22;
        var contentCount = items.Count(i => !i.label.StartsWith("──"));
        var sepCount = items.Count(i => i.label.StartsWith("──"));
        var popH = contentCount + sepCount + 2;

        // Clamp so popup stays on screen
        var screenH = ((IWidget)_rootContainer).ComputedHeight;
        var screenW = ((IWidget)_rootContainer).ComputedWidth;
        if (screenH > 0 && y + popH > screenH)
            y = Math.Max(0, screenH - popH);
        if (screenW > 0 && x + popW > screenW)
            x = Math.Max(0, screenW - popW);

        var menuName = $"deskMenu_{_menuCounter++}";
        _rootContainer.Add($@"
            <Container Name='{menuName}' Width='{popW}ch' Height='{popH}ch'
                PositionX='{x}ch' PositionY='{y}ch'
                BackgroundColor='{Theme.Subtle}' BorderStyle='Single' RoundedCorners='true'
                BorderColor='{Theme.Border}'>
                <Button Name='{menuName}_shield' Width='100%' Height='100%'
                    BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                    TextColor='Inherit' FocusTextColor='Inherit'
                    BorderStyle='None' PaddingTop='0ch' PaddingBottom='0ch' />
                <StackPanel Name='{menuName}_items' Direction='Vertical'
                    Width='100%' Height='auto' BackgroundColor='Inherit' />
            </Container>");

        _contextMenu = _termui.GetWidget<Container>(menuName);
        var list = _termui.GetWidget<StackPanel>($"{menuName}_items");
        if (list is null) return;

        int idx = 0;
        foreach (var (label, action) in items)
        {
            if (label.StartsWith("──"))
            {
                list.Add($@"<Line Orientation='Horizontal' Type='Solid' Width='100%'
                    ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />");
                continue;
            }

            var btnName = $"{menuName}_{idx++}";
            var escaped = SecurityElement.Escape(label);
            list.Add($@"
                <Button Name='{btnName}' Width='100%' Height='1ch'
                    BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                    BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                    TextColor='#cccccc' FocusTextColor='#ffffff'
                    PaddingTop='0ch' PaddingBottom='0ch'>{escaped}</Button>");

            var btn = _termui.GetWidget<Button>(btnName);
            var act = action;
            if (btn is not null) btn.Click += (_, _) => act();
        }
    }

    private void CloseContextMenu()
    {
        if (_contextMenu is null || _rootContainer is null) return;
        _rootContainer.Remove(_contextMenu);
        _contextMenu = null;
    }

    private bool IsClickOnWindow(int x, int y)
    {
        // Check if click is on taskbar area (bottom of screen)
        var termHeight = Console.WindowHeight;
        if (y >= termHeight - 5) return true;

        // Check windows, menus, popups, startmenu
        foreach (var child in ((IWidget)_rootContainer!).Children)
        {
            if (!child.Visible) continue;
            var name = child.Name ?? "";

            // Skip desktop area and wallpaper layer
            if (name == "desktopArea" || name == "wallpaperLayer") continue;

            // Check any named container that's not the background layout
            if (name.StartsWith("win_") || name.StartsWith("startMenu")
                || name.StartsWith("taskbar") || name.StartsWith("deskMenu")
                || name.StartsWith("pinPopup") || name.StartsWith("cmdPopup")
                || name.StartsWith("copyMove") || name.StartsWith("dragInd"))
            {
                int wx = 0, wy = 0;
                int.TryParse(child.PositionX.Replace("ch", ""), out wx);
                int.TryParse(child.PositionY.Replace("ch", ""), out wy);
                if (x >= wx && x < wx + child.ComputedWidth && y >= wy && y < wy + child.ComputedHeight)
                    return true;
            }
        }
        return false;
    }

    // ===== File Operations =====

    private void RenameItem(string oldPath, string oldName)
    {
        // Simple inline rename - use a dialog
        if (_rootContainer is null) return;

        var menuName = $"deskRen_{_menuCounter++}";
        _rootContainer.Add($@"
            <Container Name='{menuName}' Width='100%' Height='100%'>
                <Button Name='{menuName}_shield' Width='100%' Height='100%'
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
                            BackgroundColor='Inherit' Style='Bold'>Rename</Text>
                        <Container Width='100%' Height='3ch'
                            BorderStyle='Single' RoundedCorners='true'
                            BackgroundColor='{Theme.Darker}' BorderColor='{Theme.Border}'>
                            <Input Name='{menuName}_input' Width='100%' Height='1ch'
                                Value='{SecurityElement.Escape(oldName)}'
                                ForegroundColor='#cccccc' FocusForegroundColor='#cccccc'
                                BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                                CursorColor='#cccccc'
                                PaddingLeft='0ch' PaddingRight='0ch' />
                        </Container>
                        <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                            BackgroundColor='Inherit' Justify='End'>
                            <Button Name='{menuName}_cancel' Width='8ch' Height='1ch'
                                BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                                TextColor='#888888' FocusTextColor='#cccccc'
                                BorderStyle='None' TextAlign='Center'
                                PaddingTop='0ch' PaddingBottom='0ch'>Cancel</Button>
                            <Button Name='{menuName}_ok' Width='6ch' Height='1ch'
                                BackgroundColor='Inherit' FocusBackgroundColor='#2a2015'
                                TextColor='#cccccc' FocusTextColor='#ffffff'
                                BorderStyle='None' TextAlign='Center'
                                PaddingTop='0ch' PaddingBottom='0ch'>OK</Button>
                        </StackPanel>
                    </StackPanel>
                </Container>
            </Container>");

        var dialog = _termui.GetWidget<Container>(menuName);
        var input = _termui.GetWidget<Input>($"{menuName}_input");

        Action<string> doRename = (newName) =>
        {
            if (dialog is not null) _rootContainer.Remove(dialog);
            if (string.IsNullOrWhiteSpace(newName) || newName.Contains('/')) return;
            try
            {
                var dir = Path.GetDirectoryName(oldPath)!;
                var newPath = Path.Combine(dir, newName);
                if (Directory.Exists(oldPath)) Directory.Move(oldPath, newPath);
                else File.Move(oldPath, newPath);
                RefreshIcons();
            }
            catch { }
        };

        var cancelBtn = _termui.GetWidget<Button>($"{menuName}_cancel");
        if (cancelBtn is not null) cancelBtn.Click += (_, _) =>
        {
            if (dialog is not null) _rootContainer.Remove(dialog);
        };

        var okBtn = _termui.GetWidget<Button>($"{menuName}_ok");
        if (okBtn is not null) okBtn.Click += (_, _) => doRename(input?.Value?.Trim() ?? "");

        if (input is not null)
        {
            input.EnterPressed += (_, text) => doRename(text.Trim());
            _termui.SetFocus(input);
        }
    }

    private void DeleteItem(string path, string name, bool isDir)
    {
        if (_rootContainer is null) return;

        var menuName = $"deskDel_{_menuCounter++}";
        var escaped = SecurityElement.Escape(name);
        var warning = isDir ? "This will delete the folder and all contents." : "This cannot be undone.";

        _rootContainer.Add($@"
            <Container Name='{menuName}' Width='100%' Height='100%'>
                <Button Name='{menuName}_shield' Width='100%' Height='100%'
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
                            BackgroundColor='Inherit' Style='Bold'>Delete '{escaped}'?</Text>
                        <Text Width='100%' Height='1ch' ForegroundColor='#888888'
                            BackgroundColor='Inherit'>{warning}</Text>
                        <Text Width='100%' Height='1ch' BackgroundColor='Inherit' />
                        <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                            BackgroundColor='Inherit' Justify='End'>
                            <Button Name='{menuName}_cancel' Width='10ch' Height='1ch'
                                BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                                TextColor='#888888' FocusTextColor='#cccccc'
                                BorderStyle='None' TextAlign='Center'
                                PaddingTop='0ch' PaddingBottom='0ch'>Cancel</Button>
                            <Button Name='{menuName}_delete' Width='10ch' Height='1ch'
                                BackgroundColor='Inherit' FocusBackgroundColor='#5a1010'
                                TextColor='#ff5555' FocusTextColor='#ff8888'
                                BorderStyle='None' TextAlign='Center'
                                PaddingTop='0ch' PaddingBottom='0ch'>Delete</Button>
                        </StackPanel>
                    </StackPanel>
                </Container>
            </Container>");

        var dialog = _termui.GetWidget<Container>(menuName);

        var cancelBtn = _termui.GetWidget<Button>($"{menuName}_cancel");
        if (cancelBtn is not null) cancelBtn.Click += (_, _) =>
        {
            if (dialog is not null) _rootContainer.Remove(dialog);
        };

        var deleteBtn = _termui.GetWidget<Button>($"{menuName}_delete");
        if (deleteBtn is not null) deleteBtn.Click += (_, _) =>
        {
            if (dialog is not null) _rootContainer.Remove(dialog);
            try
            {
                if (isDir) Directory.Delete(path, true);
                else File.Delete(path);
                RefreshIcons();
            }
            catch { }
        };
    }

    private void CreateNewFile()
    {
        if (_desktopFolder is null) return;
        var name = "New File.txt";
        var path = Path.Combine(_desktopFolder, name);
        int i = 1;
        while (File.Exists(path)) { name = $"New File ({i++}).txt"; path = Path.Combine(_desktopFolder, name); }
        try { File.WriteAllText(path, ""); RefreshIcons(); } catch { }
    }

    private void CreateNewFolder()
    {
        if (_desktopFolder is null) return;
        var name = "New Folder";
        var path = Path.Combine(_desktopFolder, name);
        int i = 1;
        while (Directory.Exists(path)) { name = $"New Folder ({i++})"; path = Path.Combine(_desktopFolder, name); }
        try { Directory.CreateDirectory(path); RefreshIcons(); } catch { }
    }

    private void PasteToDesktop()
    {
        if (_desktopFolder is null || !FileClipboard.HasContent) return;
        var src = FileClipboard.Path!;
        var dst = Path.Combine(_desktopFolder, Path.GetFileName(src));
        try
        {
            if (FileClipboard.Operation == ClipboardOperation.Copy)
            {
                if (Directory.Exists(src)) CopyDir(src, dst); else File.Copy(src, dst, false);
            }
            else
            {
                if (Directory.Exists(src)) Directory.Move(src, dst); else File.Move(src, dst);
                FileClipboard.Clear();
            }
            RefreshIcons();
        }
        catch { }
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    private static bool IsExecutable(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".sh" or ".bash" or ".py" or ".rb" or ".pl" or ".js" or ".exe" or ".bat") return true;
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(path);
                if ((mode & UnixFileMode.UserExecute) != 0) return true;
            }
            using var fs = File.OpenRead(path);
            var h = new byte[4];
            if (fs.Read(h, 0, 4) == 4)
            {
                if (h[0] == 0x7F && h[1] == 0x45 && h[2] == 0x4C && h[3] == 0x46) return true;
                if (h[0] == 0x23 && h[1] == 0x21) return true;
                if (h[0] == 0x4D && h[1] == 0x5A) return true;
            }
        }
        catch { }
        return false;
    }

    private static string GetFileIcon(string ext)
    {
        return ext.ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" => "🖼",
            ".mp4" or ".mkv" or ".avi" => "🎬",
            ".zip" or ".tar" or ".gz" => "📦",
            ".pdf" => "📕",
            ".cs" or ".js" or ".py" or ".rs" => "📜",
            ".txt" or ".md" or ".log" => "📝",
            ".sh" or ".bash" => "⚡",
            _ => "📄",
        };
    }
}
