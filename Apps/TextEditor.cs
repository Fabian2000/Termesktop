using System.Security;
using TermuiX;
using TermuiX.Widgets;
using Termesktop.Components;

namespace Termesktop.Apps;

public class TextEditor
{
    private static int _instanceCount;

    private readonly TermuiX.TermuiX _termui;
    private readonly int _instanceId;
    private readonly string _prefix;
    private string? _filePath;
    private Input? _editor;
    private Text? _statusLeft;
    private Text? _statusRight;
    private bool _modified;
    private Container? _rootContainer;
    private Container? _windowRoot;

    private Container? _dropdown;
    private int _dropdownBtnCounter;

    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();
    private string _lastSnapshot = "";
    private DateTime _lastSnapshotTime = DateTime.MinValue;

    public TextEditor(TermuiX.TermuiX termui, string? filePath = null)
    {
        _termui = termui;
        _instanceId = _instanceCount++;
        _prefix = $"edit{_instanceId}";
        _filePath = filePath;
    }

    public event Action? OnCloseRequested;
    private string? _pendingContent;

    public static string Title => "Editor";

    /// <summary>
    /// Set content after BuildContent. Applied once the editor widget is ready.
    /// </summary>
    public void SetContent(string content)
    {
        _pendingContent = content;
    }

    public void BuildContent(Container contentArea, TermuiX.TermuiX termui)
    {
        _rootContainer = termui.GetWidget<Container>("rootContainer");

        // Walk up the widget tree to find the enclosing window container for dropdown positioning
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

        contentArea.Add($@"
            <StackPanel Direction='Vertical' Width='100%' Height='100%' BackgroundColor='Inherit'>

                <!-- Menu bar -->
                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='{Theme.Subtle}'>
                    <Button Name='{_prefix}_menuFile' Width='8ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#aaaaaa' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>File</Button>
                    <Button Name='{_prefix}_menuEdit' Width='8ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#aaaaaa' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>Edit</Button>
                    <Button Name='{_prefix}_menuView' Width='8ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#aaaaaa' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>View</Button>
                    <Text Width='fill' BackgroundColor='Inherit' />
                </StackPanel>

                <Line Orientation='Horizontal' Type='Solid' Width='100%'
                    ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />

                <!-- Editor -->
                <Input Name='{_prefix}_editor' Width='100%' Height='fill'
                    Multiline='true' SubmitKey='CtrlEnter'
                    ForegroundColor='#cccccc' FocusForegroundColor='#cccccc'
                    BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                    CursorColor='#cccccc'
                    PaddingLeft='0ch' PaddingRight='0ch'
                    Placeholder='Start typing...' />

                <!-- Statusbar -->
                <Line Orientation='Horizontal' Type='Solid' Width='100%'
                    ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />
                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='{Theme.Subtle}'>
                    <Text Name='{_prefix}_statusL' Width='fill' Height='1ch'
                        ForegroundColor='#666666' BackgroundColor='Inherit'
                        PaddingLeft='1ch' />
                    <Text Name='{_prefix}_statusR' Width='20ch' Height='1ch'
                        ForegroundColor='#666666' BackgroundColor='Inherit'
                        TextAlign='Right' PaddingRight='1ch' />
                </StackPanel>

            </StackPanel>");

        _editor = termui.GetWidget<Input>($"{_prefix}_editor");
        _statusLeft = termui.GetWidget<Text>($"{_prefix}_statusL");
        _statusRight = termui.GetWidget<Text>($"{_prefix}_statusR");

        var menuFile = termui.GetWidget<Button>($"{_prefix}_menuFile");
        if (menuFile is not null)
            menuFile.Click += (_, _) => ShowFileMenu();

        var menuEdit = termui.GetWidget<Button>($"{_prefix}_menuEdit");
        if (menuEdit is not null)
            menuEdit.Click += (_, _) => ShowEditMenu();

        var menuView = termui.GetWidget<Button>($"{_prefix}_menuView");
        if (menuView is not null)
            menuView.Click += (_, _) => ShowViewMenu();

        if (_editor is not null)
        {
            _editor.TextChanged += (_, text) =>
            {
                if (!_modified)
                {
                    _modified = true;
                    UpdateStatus();
                }

                // Take an undo snapshot after 500ms of inactivity
                var now = DateTime.Now;
                if ((now - _lastSnapshotTime).TotalMilliseconds > 500 && text != _lastSnapshot)
                {
                    _undoStack.Push(_lastSnapshot);
                    _redoStack.Clear();
                    _lastSnapshot = text;
                    _lastSnapshotTime = now;
                }
            };
        }

        // Close dropdown when clicking outside
        _termui.MouseClick += (_, args) =>
        {
            if ((args.EventType == MouseEventType.LeftButtonPressed || args.EventType == MouseEventType.RightButtonPressed) && _dropdown is not null)
                CloseDropdown();
        };

        _termui.Shortcut += (_, key) =>
        {
            if (key.Key == ConsoleKey.Escape && _dropdown is not null)
                CloseDropdown();
        };

        if (_pendingContent is not null)
        {
            if (_editor is not null) _editor.Value = _pendingContent;
            _pendingContent = null;
            _modified = false;
            UpdateStatus();
        }
        else if (_filePath is not null)
            LoadFile(_filePath);
        else
            UpdateStatus();
    }

    // ===== Menu Dropdowns =====

    private void ShowFileMenu()
    {
        ShowDropdown(0, [
            ("New", NewFile),
            ("Open", OpenFile),
            ("─", null),
            ("Save", SaveFile),
            ("Save As...", SaveFileAs),
            ("─", null),
            ("Close", CloseEditor),
        ]);
    }

    private void ShowEditMenu()
    {
        ShowDropdown(8, [
            ("Undo", Undo),
            ("Redo", Redo),
        ]);
    }

    private void ShowViewMenu()
    {
        if (_editor is null) return;

        var text = _editor.Value ?? "";
        var chars = text.Length;
        var words = string.IsNullOrWhiteSpace(text) ? 0
            : text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
        var lines = text.Split('\n').Length;

        ShowDropdown(16, [
            ($"Lines:  {lines}", null),
            ($"Words:  {words}", null),
            ($"Chars:  {chars}", null),
        ]);
    }

    private void ShowDropdown(int offsetX, List<(string label, Action? action)> items)
    {
        CloseDropdown();

        if (_rootContainer is null) return;

        var popupW = 24;
        var contentItems = items.Count(i => i.label != "─");
        var separators = items.Count(i => i.label == "─");
        var popupH = contentItems + separators + 2;

        // Convert window-relative to screen-absolute coordinates
        int x = offsetX, y = 3;
        if (_windowRoot is not null)
        {
            var w = (IWidget)_windowRoot;
            int.TryParse(w.PositionX.Replace("ch", ""), out var wx);
            int.TryParse(w.PositionY.Replace("ch", ""), out var wy);
            x += wx + 1;
            y += wy + 1;
        }

        var screenH = ((IWidget)_rootContainer).ComputedHeight;
        var screenW = ((IWidget)_rootContainer).ComputedWidth;
        if (screenH > 0 && y + popupH > screenH)
            y = Math.Max(0, screenH - popupH);
        if (screenW > 0 && x + popupW > screenW)
            x = Math.Max(0, screenW - popupW);

        _rootContainer.Add($@"
            <Container Name='{_prefix}_dropdown' Width='{popupW}ch' Height='{popupH}ch'
                PositionX='{x}ch' PositionY='{y}ch'
                BackgroundColor='{Theme.Subtle}' BorderStyle='Single' RoundedCorners='true'
                BorderColor='{Theme.Border}'>
                <Button Name='{_prefix}_ddShield' Width='100%' Height='100%'
                    BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                    TextColor='Inherit' FocusTextColor='Inherit'
                    BorderStyle='None' PaddingTop='0ch' PaddingBottom='0ch' />
                <StackPanel Name='{_prefix}_ddItems' Direction='Vertical'
                    Width='100%' Height='auto' BackgroundColor='Inherit' />
            </Container>");

        _dropdown = _termui.GetWidget<Container>($"{_prefix}_dropdown");
        var itemList = _termui.GetWidget<StackPanel>($"{_prefix}_ddItems");
        if (itemList is null) return;

        foreach (var (label, action) in items)
        {
            if (label == "─")
            {
                itemList.Add($@"
                    <Line Orientation='Horizontal' Type='Solid' Width='100%'
                        ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />");
                continue;
            }

            var btnName = $"{_prefix}_dd{_dropdownBtnCounter++}";
            var escaped = SecurityElement.Escape(label);
            itemList.Add($@"
                <Button Name='{btnName}' Width='100%' Height='1ch'
                    BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                    BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                    TextColor='#cccccc' FocusTextColor='#ffffff'
                    PaddingTop='0ch' PaddingBottom='0ch'>{escaped}</Button>");

            var btn = _termui.GetWidget<Button>(btnName);
            var act = action;
            if (btn is not null && act is not null)
                btn.Click += (_, _) => { CloseDropdown(); act(); };
        }
    }

    private void CloseDropdown()
    {
        if (_dropdown is null || _rootContainer is null) return;
        _rootContainer.Remove(_dropdown);
        _dropdown = null;
    }

    // ===== File Operations =====

    private void NewFile()
    {
        _filePath = null;
        _modified = false;
        if (_editor is not null) _editor.Value = "";
        _undoStack.Clear();
        _redoStack.Clear();
        _lastSnapshot = "";
        UpdateStatus();
    }

    private void OpenFile()
    {
        if (_rootContainer is null) return;

        var startPath = _filePath is not null
            ? Path.GetDirectoryName(_filePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var dialog = new FileDialog(_termui, FileDialogMode.Open, startPath);
        dialog.Show(_rootContainer, path =>
        {
            if (path is not null)
                LoadFile(path);
        });
    }

    private void SaveFile()
    {
        if (_filePath is null)
        {
            SaveFileAs();
            return;
        }

        DoSave(_filePath);
    }

    private void SaveFileAs()
    {
        if (_rootContainer is null) return;

        var startPath = _filePath is not null
            ? Path.GetDirectoryName(_filePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var dialog = new FileDialog(_termui, FileDialogMode.Save, startPath);
        dialog.Show(_rootContainer, path =>
        {
            if (path is not null)
                DoSave(path);
        });
    }

    private void LoadFile(string path)
    {
        if (_editor is null) return;

        try
        {
            _filePath = path;
            var content = File.Exists(path) ? File.ReadAllText(path) : "";
            _editor.Value = content;
            _modified = false;
            _undoStack.Clear();
            _redoStack.Clear();
            _lastSnapshot = content;
            SetStatus($"Opened: {Path.GetFileName(path)}");
            UpdateStatus();
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
    }

    private void DoSave(string path)
    {
        if (_editor is null) return;

        try
        {
            _filePath = path;
            File.WriteAllText(path, _editor.Value);
            _modified = false;
            SetStatus($"Saved: {Path.GetFileName(path)}");
            UpdateStatus();
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
    }

    private void CloseEditor()
    {
        OnCloseRequested?.Invoke();
    }

    private void Undo()
    {
        if (_editor is null || _undoStack.Count == 0) return;

        // Push current state onto redo stack
        _redoStack.Push(_editor.Value);
        var prev = _undoStack.Pop();
        _lastSnapshot = prev;
        _editor.Value = prev;
    }

    private void Redo()
    {
        if (_editor is null || _redoStack.Count == 0) return;

        // Push current state onto undo stack
        _undoStack.Push(_editor.Value);
        var next = _redoStack.Pop();
        _lastSnapshot = next;
        _editor.Value = next;
    }

    // ===== Status =====

    private void SetStatus(string message)
    {
        if (_statusLeft is not null)
            _statusLeft.Content = message;
    }

    private void UpdateStatus()
    {
        var mod = _modified ? " ●" : "";
        var name = _filePath is not null ? Path.GetFileName(_filePath) : "untitled";
        SetStatus($"{name}{mod}");

        if (_statusRight is not null)
            _statusRight.Content = _filePath is not null ? "UTF-8" : "";
    }
}
