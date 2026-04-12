using System.Security;
using TermuiX;
using TermuiX.Widgets;
using Termesktop.Apps;

namespace Termesktop.Components;

public enum FileDialogMode { Open, Save, Folder }

public class FileDialog
{
    private static int _instanceCount;

    private readonly TermuiX.TermuiX _termui;
    private readonly int _instanceId;
    private readonly string _prefix;
    private readonly FileDialogMode _mode;

    private Container? _overlay;
    private Container? _rootContainer;
    private StackPanel? _fileList;
    private Input? _filenameInput;
    private Input? _pathInput;
    private Text? _statusText;
    private string _currentPath;
    private int _entryIdx;
    private int _sidebarIdx;

    private Action<string?>? _callback;

    public FileDialog(TermuiX.TermuiX termui, FileDialogMode mode, string? startPath = null)
    {
        _termui = termui;
        _instanceId = _instanceCount++;
        _prefix = $"fd{_instanceId}";
        _mode = mode;
        _currentPath = startPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public void Show(Container rootContainer, Action<string?> callback)
    {
        _rootContainer = rootContainer;
        _callback = callback;

        var title = _mode switch
        {
            FileDialogMode.Open => "Open File",
            FileDialogMode.Save => "Save File",
            FileDialogMode.Folder => "Select Folder",
            _ => "Browse"
        };
        var confirmLabel = _mode switch
        {
            FileDialogMode.Open => "Open",
            FileDialogMode.Save => "Save",
            FileDialogMode.Folder => "Select",
            _ => "OK"
        };
        var escapedPath = SecurityElement.Escape(_currentPath);

        // Show/hide filename row for Folder mode
        var filenameVisible = _mode != FileDialogMode.Folder ? "true" : "false";

        rootContainer.Add($@"
            <Container Name='{_prefix}_overlay' Width='100%' Height='100%'>

                <Button Name='{_prefix}_shield' Width='100%' Height='100%'
                    BackgroundColor='#000000' FocusBackgroundColor='#000000'
                    TextColor='#000000' FocusTextColor='#000000'
                    BorderStyle='None' PaddingTop='0ch' PaddingBottom='0ch' />

                <Container Name='{_prefix}_dialog' Width='70ch' Height='22ch'
                    PositionX='8ch' PositionY='2ch'
                    BackgroundColor='{Theme.WindowBg}' BorderStyle='Single' RoundedCorners='true'
                    ForegroundColor='{Theme.Border}'>

                    <StackPanel Direction='Vertical' Width='100%' Height='100%'
                        BackgroundColor='Inherit'>

                        <!-- Title bar -->
                        <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                            BackgroundColor='{Theme.TitleBar}' Align='Center'>
                            <Text Width='1ch' Height='1ch' BackgroundColor='Inherit' />
                            <Text Width='fill' Height='1ch'
                                ForegroundColor='#cccccc' BackgroundColor='Inherit'
                                Style='Bold'>{title}</Text>
                            <Button Name='{_prefix}_close' Width='3ch' Height='1ch'
                                BackgroundColor='Inherit' FocusBackgroundColor='#5a2020'
                                TextColor='#888888' FocusTextColor='#ff6666'
                                BorderStyle='None' TextAlign='Center'
                                PaddingTop='0ch' PaddingBottom='0ch'>✕</Button>
                        </StackPanel>

                        <Line Orientation='Horizontal' Type='Solid' Width='100%'
                            ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />

                        <!-- Toolbar: navigation + new folder + path -->
                        <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                            BackgroundColor='{Theme.Subtle}'>
                            <Button Name='{_prefix}_up' Width='3ch' Height='1ch'
                                BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                                TextColor='#888888' FocusTextColor='#cccccc'
                                BorderStyle='None' TextAlign='Center'
                                PaddingTop='0ch' PaddingBottom='0ch'>↑</Button>
                            <Button Name='{_prefix}_newFolder' Width='3ch' Height='1ch'
                                BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                                TextColor='#888888' FocusTextColor='#cccccc'
                                BorderStyle='None' TextAlign='Center'
                                PaddingTop='0ch' PaddingBottom='0ch'
                                Visible='{(_mode != FileDialogMode.Open).ToString().ToLower()}'>+</Button>
                            <Line Orientation='Vertical' Type='Solid' Height='1ch'
                                ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />
                            <Input Name='{_prefix}_path' Width='fill' Height='1ch'
                                Value='{escapedPath}'
                                ForegroundColor='#cccccc' FocusForegroundColor='#cccccc'
                                BackgroundColor='Inherit'
                                FocusBackgroundColor='Inherit' CursorColor='#cccccc'
                                PaddingLeft='0ch' PaddingRight='0ch' />
                        </StackPanel>

                        <Line Orientation='Horizontal' Type='Solid' Width='100%'
                            ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />

                        <!-- Main: Sidebar + File list -->
                        <StackPanel Direction='Horizontal' Width='100%' Height='fill'
                            BackgroundColor='Inherit'>

                            <!-- Sidebar -->
                            <StackPanel Direction='Vertical' Width='16ch' Height='100%'
                                BackgroundColor='{Theme.Darker}'>
                                <Text Width='16ch' Height='1ch' PaddingLeft='1ch'
                                    ForegroundColor='#666666' BackgroundColor='Inherit'
                                    Style='Bold'>Quick Access</Text>
                                <StackPanel Name='{_prefix}_sidebar' Direction='Vertical'
                                    Width='16ch' Height='auto' BackgroundColor='Inherit' />
                            </StackPanel>

                            <Line Orientation='Vertical' Type='Solid' Height='100%'
                                ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />

                            <!-- File list -->
                            <StackPanel Name='{_prefix}_scroll' Direction='Vertical'
                                Width='fill' Height='100%' ScrollY='true'
                                BackgroundColor='Inherit'>
                                <StackPanel Name='{_prefix}_list' Direction='Vertical'
                                    Width='100%' Height='auto' BackgroundColor='Inherit' />
                            </StackPanel>

                        </StackPanel>

                        <Line Orientation='Horizontal' Type='Solid' Width='100%'
                            ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />

                        <!-- Filename input (hidden in Folder mode) -->
                        <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                            BackgroundColor='{Theme.Darker}' Align='Center'
                            Visible='{filenameVisible}'>
                            <Text Width='7ch' Height='1ch' PaddingLeft='1ch'
                                ForegroundColor='#888888' BackgroundColor='Inherit'>Name:</Text>
                            <Input Name='{_prefix}_filename' Width='fill' Height='1ch'
                                ForegroundColor='#cccccc' FocusForegroundColor='#cccccc'
                                BackgroundColor='Inherit'
                                FocusBackgroundColor='Inherit' CursorColor='#cccccc'
                                PaddingLeft='0ch' PaddingRight='0ch' />
                        </StackPanel>

                        <!-- Action buttons -->
                        <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                            BackgroundColor='{Theme.Darker}' Justify='End'>
                            <Text Name='{_prefix}_status' Width='fill' Height='1ch'
                                ForegroundColor='#666666' BackgroundColor='Inherit'
                                PaddingLeft='1ch' />
                            <Button Name='{_prefix}_cancel' Width='10ch' Height='1ch'
                                BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                                TextColor='#888888' FocusTextColor='#cccccc'
                                BorderStyle='None' TextAlign='Center'
                                PaddingTop='0ch' PaddingBottom='0ch'>Cancel</Button>
                            <Button Name='{_prefix}_confirm' Width='10ch' Height='1ch'
                                BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                                TextColor='#cccccc' FocusTextColor='#ffffff'
                                BorderStyle='None' TextAlign='Center'
                                PaddingTop='0ch' PaddingBottom='0ch'>{confirmLabel}</Button>
                        </StackPanel>

                    </StackPanel>
                </Container>

            </Container>");

        _overlay = _termui.GetWidget<Container>($"{_prefix}_overlay");
        _fileList = _termui.GetWidget<StackPanel>($"{_prefix}_list");
        _filenameInput = _termui.GetWidget<Input>($"{_prefix}_filename");
        _pathInput = _termui.GetWidget<Input>($"{_prefix}_path");
        _statusText = _termui.GetWidget<Text>($"{_prefix}_status");

        _termui.GetWidget<Button>($"{_prefix}_close")?.Let(b => b.Click += (_, _) => Cancel());
        _termui.GetWidget<Button>($"{_prefix}_cancel")?.Let(b => b.Click += (_, _) => Cancel());
        _termui.GetWidget<Button>($"{_prefix}_confirm")?.Let(b => b.Click += (_, _) => Confirm());
        _termui.GetWidget<Button>($"{_prefix}_up")?.Let(b => b.Click += (_, _) => NavigateUp());

        _termui.GetWidget<Button>($"{_prefix}_newFolder")?.Let(b => b.Click += (_, _) => ShowNewFolderMenu());

        if (_pathInput is not null)
            _pathInput.EnterPressed += (_, path) =>
            {
                if (Directory.Exists(path)) LoadDirectory(path);
            };

        if (_filenameInput is not null)
            _filenameInput.EnterPressed += (_, _) => Confirm();

        BuildSidebar();
        LoadDirectory(_currentPath);
    }

    private void BuildSidebar()
    {
        var sidebar = _termui.GetWidget<StackPanel>($"{_prefix}_sidebar");
        if (sidebar is null) return;

        var settings = DesktopSettings.Load();
        var quickAccess = Platform.GetQuickAccess(settings.DesktopFolder, settings.DownloadPath);

        foreach (var (name, path, icon) in quickAccess)
        {
            if (!Directory.Exists(path)) continue;

            var btnName = $"{_prefix}_qa{_sidebarIdx++}";
            var escaped = SecurityElement.Escape(name);
            sidebar.Add($@"
                <Button Name='{btnName}' Width='16ch' Height='1ch'
                    BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                    BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                    TextColor='#aaaaaa' FocusTextColor='#ffffff'
                    PaddingTop='0ch' PaddingBottom='0ch'>{icon} {escaped}</Button>");

            var btn = _termui.GetWidget<Button>(btnName);
            var targetPath = path;
            if (btn is not null)
                btn.Click += (_, _) => LoadDirectory(targetPath);
        }
    }

    private int _popupCounter;
    private Container? _popup;

    private void ShowNewFolderMenu()
    {
        ClosePopup();
        if (_rootContainer is null) return;

        // Find dialog position to place popup relative to it
        var dialog = _termui.GetWidget<Container>($"{_prefix}_dialog");
        int dx = 0, dy = 0;
        if (dialog is not null)
        {
            int.TryParse(((IWidget)dialog).PositionX.Replace("ch", ""), out dx);
            int.TryParse(((IWidget)dialog).PositionY.Replace("ch", ""), out dy);
        }

        var popName = $"{_prefix}_pop{_popupCounter++}";
        _rootContainer.Add($@"
            <Container Name='{popName}' Width='16ch' Height='3ch'
                PositionX='{dx + 4}ch' PositionY='{dy + 3}ch'
                BackgroundColor='{Theme.Subtle}' BorderStyle='Single' RoundedCorners='true'
                ForegroundColor='{Theme.Border}'>
                <Button Name='{popName}_shield' Width='100%' Height='100%'
                    BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                    TextColor='Inherit' FocusTextColor='Inherit'
                    BorderStyle='None' PaddingTop='0ch' PaddingBottom='0ch' />
                <Button Name='{popName}_newFolder' Width='100%' Height='1ch'
                    BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                    BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                    TextColor='#cccccc' FocusTextColor='#ffffff'
                    PaddingTop='0ch' PaddingBottom='0ch'>📁 New Folder</Button>
            </Container>");

        _popup = _termui.GetWidget<Container>(popName);

        var newFolderBtn = _termui.GetWidget<Button>($"{popName}_newFolder");
        if (newFolderBtn is not null) newFolderBtn.Click += (_, _) =>
        {
            ClosePopup();
            DoCreateNewFolder();
        };

        // Close on next click anywhere
        void closeHandler(object? s, MouseEventArgs args)
        {
            if (args.EventType == MouseEventType.LeftButtonPressed)
            {
                ClosePopup();
                _termui.MouseClick -= closeHandler;
            }
        }
        _termui.MouseClick += closeHandler;
    }

    private void ClosePopup()
    {
        if (_popup is not null && _rootContainer is not null)
        {
            _rootContainer.Remove(_popup);
            _popup = null;
        }
    }

    private void DoCreateNewFolder()
    {
        // Only allow if current directory actually exists
        if (!Directory.Exists(_currentPath))
        {
            if (_statusText is not null)
                _statusText.Content = "Current directory does not exist";
            return;
        }

        var name = "New Folder";
        var path = Path.Combine(_currentPath, name);
        int i = 1;
        while (Directory.Exists(path))
        {
            name = $"New Folder ({i++})";
            path = Path.Combine(_currentPath, name);
        }

        try
        {
            Directory.CreateDirectory(path);
            LoadDirectory(_currentPath);
            if (_statusText is not null)
                _statusText.Content = $"Created: {name}";
        }
        catch (Exception ex)
        {
            if (_statusText is not null)
                _statusText.Content = $"Error: {ex.Message}";
        }
    }

    private void ShowItemMenu(string path, string name, bool isDir, int screenX, int screenY)
    {
        ClosePopup();
        if (_rootContainer is null) return;

        var dialog = _termui.GetWidget<Container>($"{_prefix}_dialog");
        int dx = 0, dy = 0;
        if (dialog is not null)
        {
            int.TryParse(((IWidget)dialog).PositionX.Replace("ch", ""), out dx);
            int.TryParse(((IWidget)dialog).PositionY.Replace("ch", ""), out dy);
        }

        var items = new List<(string label, Action action)>();

        items.Add(("Rename", () =>
        {
            ClosePopup();
            RenameItem(path, name);
        }));

        items.Add(("Delete", () =>
        {
            ClosePopup();
            DeleteItem(path, name, isDir);
        }));

        var popName = $"{_prefix}_pop{_popupCounter++}";
        var popH = items.Count + 2;

        _rootContainer.Add($@"
            <Container Name='{popName}' Width='14ch' Height='{popH}ch'
                PositionX='{screenX}ch' PositionY='{screenY}ch'
                BackgroundColor='{Theme.Subtle}' BorderStyle='Single' RoundedCorners='true'
                ForegroundColor='{Theme.Border}'>
                <Button Name='{popName}_shield' Width='100%' Height='100%'
                    BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                    TextColor='Inherit' FocusTextColor='Inherit'
                    BorderStyle='None' PaddingTop='0ch' PaddingBottom='0ch' />
                <StackPanel Direction='Vertical' Width='100%' Height='auto'
                    BackgroundColor='Inherit' />
            </Container>");

        _popup = _termui.GetWidget<Container>(popName);

        // Manually add buttons since we can't get the inner stackpanel easily
        // Rebuild with items
        _rootContainer.Remove(_popup!);

        var itemsXml = "";
        int idx = 0;
        foreach (var (label, _) in items)
        {
            var btnName = $"{popName}_{idx++}";
            itemsXml += $@"<Button Name='{btnName}' Width='100%' Height='1ch'
                BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                TextColor='#cccccc' FocusTextColor='#ffffff'
                PaddingTop='0ch' PaddingBottom='0ch'>{SecurityElement.Escape(label)}</Button>";
        }

        _rootContainer.Add($@"
            <Container Name='{popName}' Width='14ch' Height='{popH}ch'
                PositionX='{screenX}ch' PositionY='{screenY}ch'
                BackgroundColor='{Theme.Subtle}' BorderStyle='Single' RoundedCorners='true'
                ForegroundColor='{Theme.Border}'>
                <Button Name='{popName}_shield' Width='100%' Height='100%'
                    BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                    TextColor='Inherit' FocusTextColor='Inherit'
                    BorderStyle='None' PaddingTop='0ch' PaddingBottom='0ch' />
                <StackPanel Direction='Vertical' Width='100%' Height='auto'
                    BackgroundColor='Inherit'>
                    {itemsXml}
                </StackPanel>
            </Container>");

        _popup = _termui.GetWidget<Container>(popName);

        idx = 0;
        foreach (var (_, action) in items)
        {
            var btn = _termui.GetWidget<Button>($"{popName}_{idx++}");
            var act = action;
            if (btn is not null) btn.Click += (_, _) => act();
        }

        void closeHandler(object? s, MouseEventArgs args)
        {
            if (args.EventType == MouseEventType.LeftButtonPressed)
            {
                ClosePopup();
                _termui.MouseClick -= closeHandler;
            }
        }
        _termui.MouseClick += closeHandler;
    }

    private void RenameItem(string oldPath, string oldName)
    {
        // Simple rename: change filename input to show rename prompt
        if (_statusText is not null)
            _statusText.Content = $"Renaming: {oldName}";

        if (_filenameInput is not null)
        {
            _filenameInput.Value = oldName;

            // Temporarily change confirm behavior
            void renameHandler(object? s, string newName)
            {
                _filenameInput.EnterPressed -= renameHandler;
                newName = newName.Trim();
                if (string.IsNullOrEmpty(newName) || newName.Contains('/')) return;

                try
                {
                    var dir = Path.GetDirectoryName(oldPath)!;
                    var newPath = Path.Combine(dir, newName);
                    if (Directory.Exists(oldPath)) Directory.Move(oldPath, newPath);
                    else File.Move(oldPath, newPath);
                    LoadDirectory(_currentPath);
                    if (_statusText is not null) _statusText.Content = $"Renamed: {newName}";
                }
                catch (Exception ex)
                {
                    if (_statusText is not null) _statusText.Content = $"Error: {ex.Message}";
                }
            }

            _filenameInput.EnterPressed += renameHandler;
            _termui.SetFocus(_filenameInput);
        }
    }

    private void DeleteItem(string path, string name, bool isDir)
    {
        try
        {
            if (isDir) Directory.Delete(path, true);
            else File.Delete(path);
            LoadDirectory(_currentPath);
            if (_statusText is not null) _statusText.Content = $"Deleted: {name}";
        }
        catch (Exception ex)
        {
            if (_statusText is not null) _statusText.Content = $"Error: {ex.Message}";
        }
    }

    private void NavigateUp()
    {
        var parent = Directory.GetParent(_currentPath);
        if (parent is not null)
            LoadDirectory(parent.FullName);
    }

    private void LoadDirectory(string path)
    {
        if (_fileList is null) return;

        _currentPath = path;
        if (_pathInput is not null) _pathInput.Value = path;
        _fileList.Clear();
        _entryIdx = 0;

        try
        {
            var dirs = Directory.GetDirectories(path)
                .Select(d => new DirectoryInfo(d))
                .Where(d => (d.Attributes & FileAttributes.Hidden) == 0)
                .OrderBy(d => d.Name);

            var files = Directory.GetFiles(path)
                .Select(f => new FileInfo(f))
                .Where(f => (f.Attributes & FileAttributes.Hidden) == 0)
                .OrderBy(f => f.Name);

            foreach (var dir in dirs)
            {
                var name = SecurityElement.Escape(dir.Name);
                var btnName = $"{_prefix}_e{_entryIdx++}";
                _fileList.Add($@"
                    <Button Name='{btnName}' Width='100%' Height='1ch'
                        BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#cccccc' FocusTextColor='#ffffff'
                        PaddingTop='0ch' PaddingBottom='0ch'>📁 {name}</Button>");

                var btn = _termui.GetWidget<Button>(btnName);
                var dirPath = dir.FullName;
                var dirName = dir.Name;
                if (btn is not null)
                {
                    btn.Click += (_, _) => LoadDirectory(dirPath);
                    btn.RightClick += (_, args) => ShowItemMenu(dirPath, dirName, true, args.X, args.Y);
                }
            }

            // Don't show files in Folder mode
            if (_mode != FileDialogMode.Folder)
            {
                foreach (var file in files)
                {
                    var name = SecurityElement.Escape(file.Name);
                    var btnName = $"{_prefix}_e{_entryIdx++}";
                    _fileList.Add($@"
                        <Button Name='{btnName}' Width='100%' Height='1ch'
                            BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                            BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                            TextColor='#888888' FocusTextColor='#cccccc'
                            PaddingTop='0ch' PaddingBottom='0ch'>  {name}</Button>");

                    var btn = _termui.GetWidget<Button>(btnName);
                    var fileName = file.Name;
                    var filePath = file.FullName;
                    if (btn is not null)
                    {
                        btn.Click += (_, _) =>
                        {
                            if (_filenameInput is not null)
                                _filenameInput.Value = fileName;
                        };
                        btn.RightClick += (_, args) => ShowItemMenu(filePath, fileName, false, args.X, args.Y);
                    }
                }
            }
        }
        catch
        {
            if (_statusText is not null)
                _statusText.Content = "Access denied";
        }
    }

    private void Confirm()
    {
        if (_mode == FileDialogMode.Folder)
        {
            Close();
            _callback?.Invoke(_currentPath);
            return;
        }

        if (_filenameInput is null) return;

        var filename = _filenameInput.Value.Trim();
        if (string.IsNullOrEmpty(filename))
        {
            if (_statusText is not null)
                _statusText.Content = "Enter a filename";
            return;
        }

        if (filename.Contains('/') || filename.Contains('\\') || filename == "." || filename == "..")
        {
            if (_statusText is not null)
                _statusText.Content = "Invalid filename";
            return;
        }

        var invalid = Path.GetInvalidFileNameChars();
        if (filename.Any(c => invalid.Contains(c)))
        {
            if (_statusText is not null)
                _statusText.Content = "Invalid characters in filename";
            return;
        }

        var fullPath = Path.Combine(_currentPath, filename);

        if (_mode == FileDialogMode.Open && !File.Exists(fullPath))
        {
            if (_statusText is not null)
                _statusText.Content = "File not found";
            return;
        }

        Close();
        _callback?.Invoke(fullPath);
    }

    private void Cancel()
    {
        Close();
        _callback?.Invoke(null);
    }

    private void Close()
    {
        if (_overlay is not null && _rootContainer is not null)
        {
            _rootContainer.Remove(_overlay);
            _overlay = null;
        }
    }
}

// Extension helper for nullable widget binding
public static class WidgetExtensions
{
    public static void Let<T>(this T obj, Action<T> action) where T : class
    {
        action(obj);
    }
}
