using TermuiX;
using TermuiX.Widgets;
using Termesktop.Components;
using Termesktop.Apps;

namespace Termesktop;

public class Desktop
{
    public const string Version = "0.1.0";
    private bool _shutdownRequested;
    public bool ShutdownRequested => _shutdownRequested;

    // Set from SIGINT signal handler (signal-safe: just a volatile bool)
    public static volatile bool SigintPending;

    /// <summary>
    /// Called from Console.CancelKeyPress — forwards Ctrl+C to focused terminal.
    /// </summary>
    public void HandleCtrlC()
    {
        foreach (var term in _activeTerminals)
        {
            if (term.IsFocused)
            {
                term.SendInterrupt();
                return;
            }
        }
    }

    private readonly TermuiX.TermuiX _termui;
    private readonly Taskbar _taskbar;
    private readonly BigClock _bigClock;
    private readonly WindowManager _windowManager;
    private readonly StartMenu _startMenu;
    private readonly DesktopIcons _desktopIcons;
    private readonly Wallpaper _wallpaper;

    private readonly List<SystemMonitor> _activeMonitors = [];
    private readonly List<Terminal> _activeTerminals = [];
    private readonly List<TaskManager> _activeTaskManagers = [];
    private readonly List<ImageViewer> _activeImageViewers = [];
    private readonly List<SettingsApp> _activeSettings = [];
    private readonly List<Downloader> _activeDownloaders = [];

    // Drag & Drop between FileManagers
    private readonly List<(FileManager fm, Window win)> _activeFileManagers = [];
    private string? _dragSourcePath;
    private FileManager? _dragSourceFm;
    private Text? _dragIndicator;
    private int _dragIndicatorCounter;
    private int _lastDropX;
    private int _lastDropY;

    // Windows queued for closing on the next frame to avoid mutation during iteration
    private readonly List<Window> _pendingClose = [];

    private Container? _rootContainer;
    private DesktopSettings _settings;

    public Desktop(TermuiX.TermuiX termui)
    {
        _termui = termui;
        _taskbar = new Taskbar(termui);
        _bigClock = new BigClock(termui);
        _windowManager = new WindowManager(termui);
        _startMenu = new StartMenu(termui);
        _desktopIcons = new DesktopIcons(termui);
        _wallpaper = new Wallpaper(termui);
        _settings = DesktopSettings.Load();
    }

    public void Build()
    {
        _termui.RegisterComponent("Taskbar", _ => _taskbar.BuildXml());
        _termui.RegisterComponent("BigClock", _ => _bigClock.BuildXml());

        _termui.LoadXml($@"
            <Container Name='rootContainer' Width='100%' Height='100%'
                BackgroundColor='{_settings.BackgroundColor}'>

                <StackPanel Direction='Vertical' Width='100%' Height='100%'
                    BackgroundColor='Inherit'>

                    <Container Name='desktopArea' Width='100%' Height='fill'
                        BackgroundColor='Inherit'>
                        <!-- Wallpaper (lowest Z) -->
                        <Container Name='wallpaperLayer' Width='100%' Height='100%'
                            BackgroundColor='Inherit' />
                        <!-- Clock: positioned absolutely, centered via code -->
                        <BigClock />
                        <!-- Icons overlay at top-left, added dynamically -->
                    </Container>

                    <Container Width='100%' Height='auto'
                        BackgroundColor='Inherit'
                        PaddingLeft='1ch' PaddingRight='1ch'>
                        <Taskbar />
                    </Container>

                </StackPanel>

            </Container>");

        _taskbar.Initialize();
        _bigClock.Initialize();

        _rootContainer = _termui.GetWidget<Container>("rootContainer");
        if (_rootContainer is not null)
        {
            _windowManager.Initialize(_rootContainer, _taskbar);
            _startMenu.Build(_rootContainer, 6, _taskbar);

            var desktopArea = _termui.GetWidget<Container>("desktopArea");
            if (desktopArea is not null)
            {
                var wallpaperLayer = _termui.GetWidget<Container>("wallpaperLayer");
                if (wallpaperLayer is not null)
                    _wallpaper.Initialize(wallpaperLayer);
                _desktopIcons.Initialize(_rootContainer, desktopArea);
                _desktopIcons.OnFileOpened += (path) =>
                {
                    if (Directory.Exists(path))
                        OpenFileManager(path);
                    else
                        OpenFileByType(path);
                };
                _desktopIcons.OnOpenInTerminal += (path) => OpenTerminal(path);
                _desktopIcons.OnOpenTerminal += () => OpenTerminal();
                _desktopIcons.OnOpenFiles += () => OpenFileManager(null);
                _desktopIcons.OnOpenSettings += () => OpenSettings();
                _desktopIcons.OnShowProperties += (path) => OpenProperties(path);
                _desktopIcons.OnSetWallpaper += (path) => SetWallpaper(path);
                _desktopIcons.OnOpenWallpaperPicker += () => OpenWallpaperPicker();
                _desktopIcons.OnRunInTerminal += (cmd, dir) => RunInTerminal(cmd, dir);
                _desktopIcons.OnDragStarted += (path) =>
                {
                    _dragSourcePath = path;
                    _dragSourceFm = null;
                };
                _desktopIcons.OnDragDrop += (sourcePath, x, y) =>
                {
                    RemoveDragIndicator();
                    _dragSourcePath = null;
                    _lastDropX = x;
                    _lastDropY = y;

                    // Check FM windows
                    foreach (var (fm, win) in _activeFileManagers)
                    {
                        if (!win.IsVisible || !win.HitTest(x, y)) continue;
                        ShowCopyMoveDialog(sourcePath, fm.CurrentPath);
                        return;
                    }

                    // Drop on desktop = same folder, ignore
                };
            }
        }

        // Ctrl+D sends interrupt to the focused terminal instance
        _termui.Shortcut += (_, key) =>
        {
            if (key.Key == ConsoleKey.Escape && _copyMovePopup is not null)
            {
                CloseCopyMovePopup();
                return;
            }

            // Forward Ctrl+key combos to focused terminal for readline
            if (key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                foreach (var term in _activeTerminals)
                {
                    if (!term.IsFocused) continue;

                    switch (key.Key)
                    {
                        // Note: Ctrl+C is routed to focused widget by TermuiX (copy), not here.
                        // Use Ctrl+D to send interrupt to terminal processes.
                        case ConsoleKey.D:
                            term.SendInterrupt();
                            return;
                        // Readline keybindings
                        case ConsoleKey.P: term.SendRawChar('\x10'); return; // History back (arrow up)
                        case ConsoleKey.N: term.SendRawChar('\x0e'); return; // History forward (arrow down)
                        case ConsoleKey.F: term.SendRawChar('\x06'); return; // Cursor forward (arrow right)
                        case ConsoleKey.B: term.SendRawChar('\x02'); return; // Cursor back (arrow left)
                        case ConsoleKey.E: term.SendRawChar('\x05'); return; // End of line
                        case ConsoleKey.K: term.SendRawChar('\x0b'); return; // Kill to end
                        case ConsoleKey.U: term.SendRawChar('\x15'); return; // Kill to start
                        case ConsoleKey.W: term.SendRawChar('\x17'); return; // Kill word back
                        case ConsoleKey.L: term.SendRawChar('\x0c'); return; // Clear screen
                        case ConsoleKey.R: term.SendRawChar('\x12'); return; // Reverse search
                        case ConsoleKey.Z: term.SendRawChar('\x1a'); return; // Suspend
                    }
                    return;
                }
            }
        };

        // Drag & Drop: indicator + drop logic + popup close
        _termui.MouseClick += (_, args) =>
        {
            if (args.EventType == MouseEventType.Moved && _dragSourcePath is not null)
            {
                UpdateDragIndicator(args.X, args.Y);
            }

            if (args.EventType == MouseEventType.LeftButtonReleased && _dragSourcePath is not null)
            {
                RemoveDragIndicator();
            }

            if ((args.EventType == MouseEventType.LeftButtonPressed || args.EventType == MouseEventType.RightButtonPressed) && _copyMovePopup is not null)
            {
                CloseCopyMovePopup();
            }
        };

        _taskbar.OnAppClicked += (appType) => OpenApp(appType, null);
        _taskbar.OnNewInstance += (path) => OpenApp("Files", path);
        _taskbar.OnCloseAll += (appType) => _windowManager.CloseAllByType(appType);
        _taskbar.OnBringToFront += (window) => _windowManager.BringWindowToFront(window);
        _taskbar.OnStartClicked += () => _startMenu.Toggle();

        _startMenu.OnAppClicked += (appId) => OpenApp(appId, null);
        _startMenu.OnShutdown += () => _shutdownRequested = true;

        // Apply saved settings on startup
        ApplySettings();
    }

    public void Update()
    {
        // Handle Ctrl+C from real terminal — forward to focused terminal's foreground process
        if (SigintPending)
        {
            SigintPending = false;
            HandleCtrlC();
        }

        // Process pending window closes on the main thread to avoid cross-thread mutations
        lock (_pendingClose)
        {
            foreach (var win in _pendingClose)
                win.Close();
            _pendingClose.Clear();
        }

        _taskbar.Update();
        _bigClock.Update();
        _startMenu.Update();

        foreach (var mon in _activeMonitors)
            mon.Update();

        foreach (var term in _activeTerminals)
            term.Update();

        foreach (var tm in _activeTaskManagers)
            tm.Update();

        foreach (var s in _activeSettings)
            s.Update();

        foreach (var dl in _activeDownloaders)
            dl.Update();

        foreach (var cl in _activeClocks)
            cl.Update();

        foreach (var n in _activeNotes)
            n.Update();

        foreach (var iv in _activeImageViewers)
            iv.Update();

        _wallpaper.Update(_settings.WallpaperPath);
        CenterClock();
        foreach (var vp in _activeVideoPlayers)
            vp.Update();

        _desktopIcons.Update(_settings);

    }

    private void OpenApp(string appType, string? param)
    {
        switch (appType)
        {
            case "Files":
                OpenFileManager(param);
                break;
            case "Terminal":
                OpenTerminal();
                break;
            case "Editor":
                OpenTextEditor(param);
                break;
            case "Markdown":
                OpenMarkdown(param);
                break;
            case "Monitor":
                OpenSystemMonitor();
                break;
            case "Image":
                OpenImageViewer(param);
                break;
            case "Video":
                OpenVideoPlayer(param);
                break;
            case "Tasks":
                OpenTaskManager();
                break;
            case "Download":
                OpenDownloader();
                break;
            case "Calc":
                OpenCalculator();
                break;
            case "Notes":
                OpenNotes();
                break;
            case "Clock":
                OpenClock();
                break;
            case "Settings":
                OpenSettings();
                break;
        }
    }

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tga", ".tiff"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".flv", ".wmv", ".m4v", ".3gp"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".cs", ".js", ".ts", ".py", ".rs", ".go", ".java",
        ".c", ".cpp", ".h", ".json", ".xml", ".yaml", ".yml", ".toml", ".ini",
        ".cfg", ".sh", ".bash", ".html", ".htm", ".css", ".csv"
    };

    private static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".mdown", ".mkd"
    };

    private void OpenFileByType(string path)
    {
        var ext = Path.GetExtension(path);

        if (ImageExtensions.Contains(ext))
        {
            OpenImageViewer(path);
            return;
        }

        if (VideoExtensions.Contains(ext))
        {
            OpenVideoPlayer(path);
            return;
        }

        if (MarkdownExtensions.Contains(ext))
        {
            OpenMarkdown(path);
            return;
        }

        // Try to detect if file is text or binary
        if (TextExtensions.Contains(ext) || IsTextFile(path))
        {
            OpenTextEditor(path);
            return;
        }

        // Binary: open in hex viewer mode
        OpenHexViewer(path);
    }

    private static bool IsTextFile(string path)
    {
        try
        {
            var buffer = new byte[512];
            using var fs = File.OpenRead(path);
            int read = fs.Read(buffer, 0, buffer.Length);

            for (int i = 0; i < read; i++)
            {
                var b = buffer[i];
                // NUL bytes = binary
                if (b == 0) return false;
            }
            return true;
        }
        catch { return false; }
    }

    private void OpenHexViewer(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var sb = new System.Text.StringBuilder();
            var maxBytes = Math.Min(bytes.Length, 8192);

            for (int i = 0; i < maxBytes; i += 16)
            {
                sb.Append($"{i:X8}  ");

                for (int j = 0; j < 16; j++)
                {
                    if (i + j < maxBytes)
                        sb.Append($"{bytes[i + j]:X2} ");
                    else
                        sb.Append("   ");
                    if (j == 7) sb.Append(' ');
                }

                sb.Append(" |");
                for (int j = 0; j < 16 && i + j < maxBytes; j++)
                {
                    var b = bytes[i + j];
                    sb.Append(b is >= 32 and < 127 ? (char)b : '.');
                }
                sb.Append("|\n");
            }

            if (bytes.Length > maxBytes)
                sb.Append($"\n... truncated ({bytes.Length} bytes total)");

            var editor = new TextEditor(_termui);
            editor.SetContent(sb.ToString());
            var window = _windowManager.OpenWindow("Hex: " + Path.GetFileName(path), 80, 24, editor.BuildContent);
            _taskbar.RegisterWindow("Editor", window);
            editor.OnCloseRequested += () => window.Close();
        }
        catch { }
    }

    private void OpenFileManager(string? startPath)
    {
        var fm = new FileManager(_termui, startPath);
        var window = _windowManager.OpenWindow("Files", 70, 22, fm.BuildContent);
        _taskbar.RegisterWindow("Files", window);
        _activeFileManagers.Add((fm, window));
        window.Closed += (_, _) => _activeFileManagers.RemoveAll(x => x.fm == fm);

        fm.OnFileOpened += (path) => OpenFileByType(path);
        fm.OnOpenInTerminal += (path) => OpenTerminal(path);
        fm.OnShowProperties += (path) => OpenProperties(path);
        fm.OnRunInTerminal += (cmd, dir) => RunInTerminal(cmd, dir);
        fm.OnSetWallpaper += (path) => SetWallpaper(path);

        fm.OnDragStarted += (path) =>
        {
            _dragSourcePath = path;
            _dragSourceFm = fm;
        };
        fm.OnDragCancelled += () =>
        {
            _dragSourcePath = null;
            _dragSourceFm = null;
            RemoveDragIndicator();
        };
        fm.OnDragDrop += (sourcePath, x, y) => HandleFileDrop(fm, sourcePath, x, y);
        fm.OnMultiDragDrop += (paths, x, y) => HandleMultiFileDrop(fm, paths, x, y);
    }

    private static readonly Dictionary<string, string> ScriptInterpreters = new(StringComparer.OrdinalIgnoreCase)
    {
        [".sh"] = "bash", [".bash"] = "bash", [".zsh"] = "zsh", [".fish"] = "fish",
        [".py"] = "python3", [".rb"] = "ruby", [".pl"] = "perl",
        [".js"] = "node", [".ts"] = "npx ts-node",
    };

    private void RunInTerminal(string command, string workingDir)
    {
        var term = new Terminal(_termui, workingDir);
        var window = _windowManager.OpenWindow("Terminal", 80, 24, term.BuildContent);
        _taskbar.RegisterWindow("Terminal", window);
        _activeTerminals.Add(term);
        window.Closed += (_, _) =>
        {
            term.Dispose();
            _activeTerminals.Remove(term);
        };
        term.OnProcessExited += () =>
        {
            lock (_pendingClose) { _pendingClose.Add(window); }
        };

        // Use interpreter for scripts without execute permission
        var ext = Path.GetExtension(command);
        if (ScriptInterpreters.TryGetValue(ext, out var interpreter))
            term.SendCommand($"{interpreter} \"{command}\"");
        else
            term.SendCommand(command);
    }

    private void OpenTerminal(string? startPath = null)
    {
        var term = new Terminal(_termui, startPath);
        var window = _windowManager.OpenWindow("Terminal", 80, 24, term.BuildContent);
        _taskbar.RegisterWindow("Terminal", window);
        _activeTerminals.Add(term);
        window.Closed += (_, _) =>
        {
            term.Dispose();
            _activeTerminals.Remove(term);
        };
        term.OnProcessExited += () =>
        {
            lock (_pendingClose) { _pendingClose.Add(window); }
        };
    }

    private void OpenTextEditor(string? filePath)
    {
        var editor = new TextEditor(_termui, filePath);
        var window = _windowManager.OpenWindow("Editor", 70, 22, editor.BuildContent);
        _taskbar.RegisterWindow("Editor", window);
        editor.OnCloseRequested += () => window.Close();
    }

    private void OpenMarkdown(string? filePath)
    {
        var md = new MarkdownApp(_termui, filePath);
        var window = _windowManager.OpenWindow("Markdown", 75, 24, md.BuildContent);
        _taskbar.RegisterWindow("Markdown", window);
        md.OnCloseRequested += () => window.Close();
    }

    private void OpenSystemMonitor()
    {
        var monitor = new SystemMonitor(_termui);
        var window = _windowManager.OpenWindow("Monitor", 60, 22, monitor.BuildContent);
        _taskbar.RegisterWindow("Monitor", window);
        _activeMonitors.Add(monitor);

        window.Closed += (_, _) => _activeMonitors.Remove(monitor);
    }

    private readonly List<VideoPlayer> _activeVideoPlayers = [];

    private void OpenVideoPlayer(string? filePath)
    {
        var player = new VideoPlayer(_termui, filePath);
        var window = _windowManager.OpenWindow("Video", 80, 24, player.BuildContent);
        _taskbar.RegisterWindow("Video", window);
        _activeVideoPlayers.Add(player);
        window.Closed += (_, _) =>
        {
            player.Dispose();
            _activeVideoPlayers.Remove(player);
        };
    }

    private void OpenImageViewer(string? filePath)
    {
        var viewer = new ImageViewer(_termui, filePath);
        var window = _windowManager.OpenWindow("Image", 70, 24, viewer.BuildContent);
        _taskbar.RegisterWindow("Image", window);
        _activeImageViewers.Add(viewer);
        window.Closed += (_, _) => _activeImageViewers.Remove(viewer);
    }

    private void OpenProperties(string path)
    {
        var name = Path.GetFileName(path);
        var viewer = new PropertiesViewer(_termui, path);
        _windowManager.OpenWindow($"Properties: {name}", 55, 30, viewer.BuildContent);
    }

    private void OpenTaskManager()
    {
        var tm = new TaskManager(_termui);
        var window = _windowManager.OpenWindow("Tasks", 65, 24, tm.BuildContent);
        _taskbar.RegisterWindow("Tasks", window);
        _activeTaskManagers.Add(tm);
        window.Closed += (_, _) => _activeTaskManagers.Remove(tm);
    }

    private void OpenDownloader()
    {
        var dl = new Downloader(_termui);
        var window = _windowManager.OpenWindow("Download", 70, 20, dl.BuildContent);
        _taskbar.RegisterWindow("Download", window);
        _activeDownloaders.Add(dl);
        window.Closed += (_, _) =>
        {
            dl.Dispose();
            _activeDownloaders.Remove(dl);
        };
    }

    private readonly List<StopWatch> _activeClocks = [];
    private readonly List<Notes> _activeNotes = [];

    private void OpenCalculator()
    {
        var calc = new Calculator(_termui);
        _windowManager.OpenWindow("Calculator", 30, 16, calc.BuildContent);
        _taskbar.RegisterWindow("Calc", _windowManager.Windows[^1]);
    }

    private void OpenNotes()
    {
        var notes = new Notes(_termui);
        var window = _windowManager.OpenWindow("Notes", 55, 18, notes.BuildContent);
        _taskbar.RegisterWindow("Notes", window);
        _activeNotes.Add(notes);
        window.Closed += (_, _) => _activeNotes.Remove(notes);
    }

    private void OpenClock()
    {
        var clock = new StopWatch(_termui);
        var window = _windowManager.OpenWindow("Clock", 45, 18, clock.BuildContent);
        _taskbar.RegisterWindow("Clock", window);
        _activeClocks.Add(clock);
        window.Closed += (_, _) => _activeClocks.Remove(clock);
    }

    private void OpenSettings()
    {
        var settings = new SettingsApp(_termui);
        var window = _windowManager.OpenWindow("Settings", 70, 22, settings.BuildContent);
        _taskbar.RegisterWindow("Settings", window);
        _activeSettings.Add(settings);
        window.Closed += (_, _) => _activeSettings.Remove(settings);

        settings.OnSettingsChanged += (newSettings) =>
        {
            _settings = newSettings;
            ApplySettings();
        };
    }

    private void UpdateDragIndicator(int x, int y)
    {
        if (_dragSourcePath is null || _rootContainer is null) return;

        if (_dragIndicator is not null)
        {
            _dragIndicator.PositionX = $"{x + 2}ch";
            _dragIndicator.PositionY = $"{y}ch";
            return;
        }

        // Check if multi-drag from a FileManager with selections
        string label;
        foreach (var (fm, _) in _activeFileManagers)
        {
            if (fm.CurrentPath is not null && fm.SelectedCount > 1)
            {
                label = $" 📦 {fm.SelectedCount} items ";
                goto createIndicator;
            }
        }
        {
            var name = Path.GetFileName(_dragSourcePath);
            var isDir = Directory.Exists(_dragSourcePath);
            var icon = isDir ? "📁" : "📄";
            label = $" {icon} {name} ";
        }
        createIndicator:
        var widgetName = $"dragInd_{_dragIndicatorCounter++}";

        _rootContainer.Add($@"
            <Text Name='{widgetName}' Height='1ch'
                PositionX='{x + 2}ch' PositionY='{y}ch'
                ForegroundColor='#ffffff' BackgroundColor='{Theme.Hover}'
                Style='Bold'>{System.Security.SecurityElement.Escape(label)}</Text>");

        _dragIndicator = _termui.GetWidget<Text>(widgetName);
    }

    private void RemoveDragIndicator()
    {
        if (_dragIndicator is not null && _rootContainer is not null)
        {
            _rootContainer.Remove(_dragIndicator);
            _dragIndicator = null;
        }
    }

    private void HandleFileDrop(FileManager sourceFm, string sourcePath, int x, int y)
    {
        RemoveDragIndicator();
        _dragSourcePath = null;
        _dragSourceFm = null;
        _lastDropX = x;
        _lastDropY = y;

        // Check all FileManager windows for drop target
        bool hitAnyWindow = false;
        foreach (var (fm, win) in _activeFileManagers)
        {
            if (!win.IsVisible || !win.HitTest(x, y)) continue;
            hitAnyWindow = true;

            var folderTarget = fm.GetFolderAtScreen(x, y);

            string? targetDir = null;
            if (folderTarget is not null && folderTarget != sourcePath)
                targetDir = folderTarget;
            else if (fm != sourceFm)
                targetDir = fm.CurrentPath;

            if (targetDir is null) continue;
            if (sourcePath == targetDir) continue;
            if (targetDir.StartsWith(sourcePath + Path.DirectorySeparatorChar)) continue;

            ShowCopyMoveDialog(sourcePath, targetDir);
            return;
        }

        // Drop on desktop area (not on any window) - use desktop folder if set
        if (!hitAnyWindow && _settings.DesktopFolder is not null && Directory.Exists(_settings.DesktopFolder))
        {
            if (sourcePath != _settings.DesktopFolder
                && !sourcePath.StartsWith(_settings.DesktopFolder + Path.DirectorySeparatorChar))
            {
                ShowCopyMoveDialog(sourcePath, _settings.DesktopFolder);
            }
        }
    }

    private void HandleMultiFileDrop(FileManager sourceFm, List<string> paths, int x, int y)
    {
        RemoveDragIndicator();
        _dragSourcePath = null;
        _lastDropX = x;
        _lastDropY = y;

        // Find target FM
        foreach (var (fm, win) in _activeFileManagers)
        {
            if (!win.IsVisible || !win.HitTest(x, y)) continue;

            var folderTarget = fm.GetFolderAtScreen(x, y);
            string? targetDir = folderTarget ?? (fm != sourceFm ? fm.CurrentPath : null);

            if (targetDir is null) continue;

            ShowMultiCopyMoveDialog(paths, targetDir);
            return;
        }

        // Desktop folder
        if (_settings.DesktopFolder is not null && Directory.Exists(_settings.DesktopFolder))
            ShowMultiCopyMoveDialog(paths, _settings.DesktopFolder);
    }

    private void ShowMultiCopyMoveDialog(List<string> sourcePaths, string targetDir)
    {
        CloseCopyMovePopup();
        if (_rootContainer is null) return;

        var popupName = $"cmdPopup_{_copyMoveCounter++}";

        _rootContainer.Add($@"
            <Container Name='{popupName}' Width='14ch' Height='5ch'
                PositionX='{_lastDropX}ch' PositionY='{_lastDropY}ch'
                BackgroundColor='{Theme.Subtle}' BorderStyle='Single' RoundedCorners='true'
                BorderColor='{Theme.Lighter}'>
                <Button Name='{popupName}_shield' Width='100%' Height='100%'
                    BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                    TextColor='Inherit' FocusTextColor='Inherit'
                    BorderStyle='None' PaddingTop='0ch' PaddingBottom='0ch' />
                <StackPanel Direction='Vertical' Width='100%' Height='auto'
                    BackgroundColor='Inherit'>
                    <Button Name='{popupName}_move' Width='100%' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#cccc88' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                        PaddingTop='0ch' PaddingBottom='0ch'>Move {sourcePaths.Count}</Button>
                    <Button Name='{popupName}_copy' Width='100%' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#88cccc' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                        PaddingTop='0ch' PaddingBottom='0ch'>Copy {sourcePaths.Count}</Button>
                    <Button Name='{popupName}_cancel' Width='100%' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#888888' FocusTextColor='#cccccc'
                        BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                        PaddingTop='0ch' PaddingBottom='0ch'>Cancel</Button>
                </StackPanel>
            </Container>");

        _copyMovePopup = _termui.GetWidget<Container>(popupName);

        var moveBtn = _termui.GetWidget<Button>($"{popupName}_move");
        if (moveBtn is not null) moveBtn.Click += (_, _) =>
        {
            CloseCopyMovePopup();
            foreach (var src in sourcePaths)
            {
                try
                {
                    var dst = Path.Combine(targetDir, Path.GetFileName(src));
                    if (Directory.Exists(src)) Directory.Move(src, dst);
                    else File.Move(src, dst);
                }
                catch { }
            }
            RefreshAllFileManagers();
        };

        var copyBtn = _termui.GetWidget<Button>($"{popupName}_copy");
        if (copyBtn is not null) copyBtn.Click += (_, _) =>
        {
            CloseCopyMovePopup();
            foreach (var src in sourcePaths)
            {
                try
                {
                    var dst = Path.Combine(targetDir, Path.GetFileName(src));
                    if (Directory.Exists(src)) CopyDirectory(src, dst);
                    else File.Copy(src, dst, false);
                }
                catch { }
            }
            RefreshAllFileManagers();
        };

        var cancelBtn = _termui.GetWidget<Button>($"{popupName}_cancel");
        if (cancelBtn is not null) cancelBtn.Click += (_, _) => CloseCopyMovePopup();
    }

    private Container? _copyMovePopup;
    private int _copyMoveCounter;

    private void ShowCopyMoveDialog(string sourcePath, string targetDir)
    {
        CloseCopyMovePopup();
        if (_rootContainer is null) return;

        var src = sourcePath;
        var dst = Path.Combine(targetDir, Path.GetFileName(sourcePath));
        var popupName = $"cmdPopup_{_copyMoveCounter++}";

        _rootContainer.Add($@"
            <Container Name='{popupName}' Width='12ch' Height='5ch'
                PositionX='{_lastDropX}ch' PositionY='{_lastDropY}ch'
                BackgroundColor='#1e1015' BorderStyle='Single' RoundedCorners='true'
                BorderColor='#4a2a2a'>
                <Button Name='{popupName}_shield' Width='100%' Height='100%'
                    BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                    TextColor='Inherit' FocusTextColor='Inherit'
                    BorderStyle='None' PaddingTop='0ch' PaddingBottom='0ch' />
                <StackPanel Direction='Vertical' Width='100%' Height='auto'
                    BackgroundColor='Inherit'>
                    <Button Name='{popupName}_move' Width='100%' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='#2a2015'
                        TextColor='#cccc88' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                        PaddingTop='0ch' PaddingBottom='0ch'>Move</Button>
                    <Button Name='{popupName}_copy' Width='100%' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='#15202a'
                        TextColor='#88cccc' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                        PaddingTop='0ch' PaddingBottom='0ch'>Copy</Button>
                    <Button Name='{popupName}_cancel' Width='100%' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='#2a1515'
                        TextColor='#888888' FocusTextColor='#cccccc'
                        BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                        PaddingTop='0ch' PaddingBottom='0ch'>Cancel</Button>
                </StackPanel>
            </Container>");

        _copyMovePopup = _termui.GetWidget<Container>(popupName);

        var moveBtn = _termui.GetWidget<Button>($"{popupName}_move");
        if (moveBtn is not null) moveBtn.Click += (_, _) =>
        {
            CloseCopyMovePopup();
            try
            {
                if (Directory.Exists(src))
                    Directory.Move(src, dst);
                else
                    File.Move(src, dst);
            }
            catch { }
            RefreshAllFileManagers();
        };

        var copyBtn = _termui.GetWidget<Button>($"{popupName}_copy");
        if (copyBtn is not null) copyBtn.Click += (_, _) =>
        {
            CloseCopyMovePopup();
            try
            {
                if (Directory.Exists(src))
                    CopyDirectory(src, dst);
                else
                    File.Copy(src, dst, false);
            }
            catch { }
            RefreshAllFileManagers();
        };

        var cancelBtn = _termui.GetWidget<Button>($"{popupName}_cancel");
        if (cancelBtn is not null) cancelBtn.Click += (_, _) => CloseCopyMovePopup();
    }

    private void CloseCopyMovePopup()
    {
        if (_copyMovePopup is not null && _rootContainer is not null)
        {
            _rootContainer.Remove(_copyMovePopup);
            _copyMovePopup = null;
        }
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(src))
            CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
    }

    private void RefreshAllFileManagers()
    {
        foreach (var (fm, _) in _activeFileManagers)
            fm.Refresh();
        _desktopIcons.ForceRefresh();
    }

    private void OpenWallpaperPicker()
    {
        if (_rootContainer is null) return;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dialog = new FileDialog(_termui, FileDialogMode.Open, home);
        dialog.Show(_rootContainer, path =>
        {
            if (path is not null && ImageExtensions.Contains(Path.GetExtension(path)))
                SetWallpaper(path);
        });
    }

    private void CenterClock()
    {
        var clockPanel = _termui.GetWidget<StackPanel>("clockPanel");
        var desktopArea = _termui.GetWidget<Container>("desktopArea");
        if (clockPanel is null || desktopArea is null) return;

        var areaW = ((IWidget)desktopArea).ComputedWidth;
        var areaH = ((IWidget)desktopArea).ComputedHeight;
        var clockW = ((IWidget)clockPanel).ComputedWidth;
        var clockH = ((IWidget)clockPanel).ComputedHeight;

        if (areaW <= 0 || clockW <= 0) return;

        var x = Math.Max(0, (areaW - clockW) / 2);
        var y = Math.Max(0, (areaH - clockH) / 2);

        clockPanel.PositionX = $"{x}ch";
        clockPanel.PositionY = $"{y}ch";
    }

    private void SetWallpaper(string path)
    {
        _settings.WallpaperPath = path;
        _settings.Save();
        // Wallpaper.Update will pick up the change on next frame
    }

    private void ApplySettings()
    {
        Theme.Apply(_settings);

        if (_rootContainer is not null)
            _rootContainer.BackgroundColor = Color.Parse(_settings.BackgroundColor);

        var clockText = _termui.GetWidget<Text>("clockText");
        if (clockText is not null)
        {
            clockText.ForegroundColor = Color.Parse(_settings.ClockColor);
            clockText.Visible = _settings.ShowClock;
        }

        var dateText = _termui.GetWidget<Text>("dateText");
        if (dateText is not null)
        {
            dateText.ForegroundColor = Color.Parse(_settings.DateColor);
            dateText.Visible = _settings.ShowDate;
        }

        _windowManager.ApplyThemeToAll(
            _settings.WindowBackgroundColor,
            _settings.WindowTitleBarColor,
            _settings.WindowBorderColor);

        _taskbar.ApplyTheme();
        _startMenu.ApplyTheme();
    }
}
