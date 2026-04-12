using System.Security;
using TermuiX;
using TermuiX.Widgets;
using Termesktop.Components;

namespace Termesktop.Apps;

public class Notes
{
    private static int _instanceCount;
    private static readonly string NotesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".termesktop", "notes");

    private readonly TermuiX.TermuiX _termui;
    private readonly int _instanceId;
    private readonly string _prefix;

    private StackPanel? _noteList;
    private Input? _editor;
    private Text? _statusText;
    private Text? _titleText;
    private Container? _rootContainer;
    private Container? _contextMenu;
    private string? _currentNoteId;
    private int _btnCounter;
    private int _menuCounter;
    private DateTime _lastSave = DateTime.MinValue;
    private bool _pendingSave;

    public Notes(TermuiX.TermuiX termui)
    {
        _termui = termui;
        _instanceId = _instanceCount++;
        _prefix = $"notes{_instanceId}";
        Directory.CreateDirectory(NotesDir);
    }

    public static string Title => "Notes";

    public void BuildContent(Container contentArea, TermuiX.TermuiX termui)
    {
        _rootContainer = termui.GetWidget<Container>("rootContainer");

        contentArea.Add($@"
            <StackPanel Direction='Horizontal' Width='100%' Height='100%' BackgroundColor='Inherit'>

                <!-- Note list sidebar -->
                <StackPanel Direction='Vertical' Width='20ch' Height='100%'
                    BackgroundColor='{Theme.Darker}'>
                    <StackPanel Direction='Horizontal' Width='20ch' Height='1ch'
                        BackgroundColor='Inherit'>
                        <Text Width='fill' Height='1ch' PaddingLeft='1ch'
                            ForegroundColor='#888888' BackgroundColor='Inherit'
                            Style='Bold'>Notes</Text>
                        <Button Name='{_prefix}_new' Width='3ch' Height='1ch'
                            BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                            TextColor='#88cc88' FocusTextColor='#ffffff'
                            BorderStyle='None' TextAlign='Center'
                            PaddingTop='0ch' PaddingBottom='0ch'>+</Button>
                    </StackPanel>
                    <Line Orientation='Horizontal' Type='Solid' Width='100%'
                        ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />
                    <StackPanel Name='{_prefix}_list' Direction='Vertical'
                        Width='20ch' Height='fill' ScrollY='true'
                        BackgroundColor='Inherit'>
                        <StackPanel Direction='Vertical' Width='20ch' Height='auto'
                            BackgroundColor='Inherit' />
                    </StackPanel>
                </StackPanel>

                <Line Orientation='Vertical' Type='Solid' Height='100%'
                    ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />

                <!-- Editor area -->
                <StackPanel Direction='Vertical' Width='fill' Height='100%'
                    BackgroundColor='Inherit'>

                    <!-- Note title -->
                    <Text Name='{_prefix}_title' Width='100%' Height='1ch'
                        ForegroundColor='#cccccc' BackgroundColor='{Theme.Subtle}'
                        PaddingLeft='1ch' Style='Bold' />

                    <Line Orientation='Horizontal' Type='Solid' Width='100%'
                        ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />

                    <!-- Editor -->
                    <Input Name='{_prefix}_editor' Width='100%' Height='fill'
                        Multiline='true' SubmitKey='CtrlEnter'
                        ForegroundColor='#cccccc' FocusForegroundColor='#cccccc'
                        BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                        CursorColor='#cccccc'
                        PaddingLeft='0ch' PaddingRight='0ch'
                        Placeholder='Select or create a note...' />

                    <!-- Status -->
                    <Line Orientation='Horizontal' Type='Solid' Width='100%'
                        ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />
                    <Text Name='{_prefix}_status' Width='100%' Height='1ch'
                        ForegroundColor='#666666' BackgroundColor='{Theme.Subtle}'
                        PaddingLeft='1ch'>Select a note or click + to create</Text>
                </StackPanel>

            </StackPanel>");

        _noteList = termui.GetWidget<StackPanel>($"{_prefix}_list");
        _editor = termui.GetWidget<Input>($"{_prefix}_editor");
        _statusText = termui.GetWidget<Text>($"{_prefix}_status");
        _titleText = termui.GetWidget<Text>($"{_prefix}_title");

        var newBtn = termui.GetWidget<Button>($"{_prefix}_new");
        if (newBtn is not null) newBtn.Click += (_, _) => CreateNewNote();

        if (_editor is not null)
        {
            _editor.TextChanged += (_, _) => _pendingSave = true;
        }

        // Close context menu on click
        _termui.MouseClick += (_, args) =>
        {
            if ((args.EventType == MouseEventType.LeftButtonPressed || args.EventType == MouseEventType.RightButtonPressed) && _contextMenu is not null)
                CloseContextMenu();
        };

        LoadNoteList();
    }

    /// <summary>
    /// Call from main loop for auto-save (debounced).
    /// </summary>
    public void Update()
    {
        if (!_pendingSave || _currentNoteId is null || _editor is null) return;

        if ((DateTime.Now - _lastSave).TotalMilliseconds < 500) return;

        _pendingSave = false;
        _lastSave = DateTime.Now;
        SaveNote(_currentNoteId, _editor.Value);
        if (_statusText is not null)
            _statusText.Content = $"Saved at {DateTime.Now:HH:mm:ss}";
    }

    private void LoadNoteList()
    {
        if (_noteList is null) return;
        _noteList.Clear();

        var files = Directory.GetFiles(NotesDir, "*.txt")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();

        foreach (var file in files)
        {
            var noteId = Path.GetFileNameWithoutExtension(file.Name);
            var displayName = GetNoteName(noteId);
            var btnName = $"{_prefix}_n{_btnCounter++}";
            var escaped = SecurityElement.Escape(displayName.Length > 17 ? displayName[..16] + "…" : displayName);
            var isActive = noteId == _currentNoteId;

            _noteList.Add($@"
                <Button Name='{btnName}' Width='20ch' Height='1ch'
                    BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                    BackgroundColor='{(isActive ? Theme.Hover : "Inherit")}' FocusBackgroundColor='{Theme.Hover}'
                    TextColor='{(isActive ? "#ffffff" : "#aaaaaa")}' FocusTextColor='#ffffff'
                    PaddingTop='0ch' PaddingBottom='0ch'>{escaped}</Button>");

            var btn = _termui.GetWidget<Button>(btnName);
            var id = noteId;
            if (btn is not null)
            {
                btn.Click += (_, _) => OpenNote(id);
                btn.RightClick += (_, args) => ShowNoteContextMenu(id, args.X, args.Y);
            }
        }
    }

    private void ShowNoteContextMenu(string noteId, int x, int y)
    {
        CloseContextMenu();
        if (_rootContainer is null) return;

        var popW = 14;
        var popH = 4;
        var screenH = ((IWidget)_rootContainer).ComputedHeight;
        var screenW = ((IWidget)_rootContainer).ComputedWidth;
        if (screenH > 0 && y + popH > screenH)
            y = Math.Max(0, screenH - popH);
        if (screenW > 0 && x + popW > screenW)
            x = Math.Max(0, screenW - popW);

        var menuName = $"{_prefix}_ctx{_menuCounter++}";
        _rootContainer.Add($@"
            <Container Name='{menuName}' Width='{popW}ch' Height='{popH}ch'
                PositionX='{x}ch' PositionY='{y}ch'
                BackgroundColor='{Theme.Subtle}' BorderStyle='Single' RoundedCorners='true'
                ForegroundColor='{Theme.Border}'>
                <Button Name='{menuName}_shield' Width='100%' Height='100%'
                    BackgroundColor='Inherit' FocusBackgroundColor='Inherit'
                    TextColor='Inherit' FocusTextColor='Inherit'
                    BorderStyle='None' PaddingTop='0ch' PaddingBottom='0ch' />
                <StackPanel Direction='Vertical' Width='100%' Height='auto'
                    BackgroundColor='Inherit'>
                    <Button Name='{menuName}_rename' Width='100%' Height='1ch'
                        BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#cccccc' FocusTextColor='#ffffff'
                        PaddingTop='0ch' PaddingBottom='0ch'>Rename</Button>
                    <Button Name='{menuName}_delete' Width='100%' Height='1ch'
                        BorderStyle='None' TextAlign='Left' PaddingLeft='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='#3a1010'
                        TextColor='#cc8888' FocusTextColor='#ff8888'
                        PaddingTop='0ch' PaddingBottom='0ch'>Delete</Button>
                </StackPanel>
            </Container>");

        _contextMenu = _termui.GetWidget<Container>(menuName);

        var renameBtn = _termui.GetWidget<Button>($"{menuName}_rename");
        if (renameBtn is not null) renameBtn.Click += (_, _) =>
        {
            CloseContextMenu();
            RenameNote(noteId);
        };

        var deleteBtn = _termui.GetWidget<Button>($"{menuName}_delete");
        if (deleteBtn is not null) deleteBtn.Click += (_, _) =>
        {
            CloseContextMenu();
            DeleteNote(noteId);
        };
    }

    private void CloseContextMenu()
    {
        if (_contextMenu is not null && _rootContainer is not null)
        {
            _rootContainer.Remove(_contextMenu);
            _contextMenu = null;
        }
    }

    private void CreateNewNote()
    {
        var id = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var path = Path.Combine(NotesDir, $"{id}.txt");
        File.WriteAllText(path, "");

        // Also create a name file
        File.WriteAllText(Path.Combine(NotesDir, $"{id}.name"), "New Note");

        OpenNote(id);
        LoadNoteList();
    }

    private void OpenNote(string noteId)
    {
        // Save current before switching
        if (_currentNoteId is not null && _editor is not null)
            SaveNote(_currentNoteId, _editor.Value);

        _currentNoteId = noteId;
        var path = Path.Combine(NotesDir, $"{noteId}.txt");

        if (_editor is not null && File.Exists(path))
        {
            _editor.Value = File.ReadAllText(path);
            _termui.SetFocus(_editor);
        }

        if (_titleText is not null)
            _titleText.Content = $"📓 {GetNoteName(noteId)}";

        if (_statusText is not null)
            _statusText.Content = "Auto-saves while typing";

        _pendingSave = false;
        LoadNoteList();
    }

    private void RenameNote(string noteId)
    {
        if (_rootContainer is null) return;

        var currentName = GetNoteName(noteId);
        var menuName = $"{_prefix}_ren{_menuCounter++}";

        _rootContainer.Add($@"
            <Container Name='{menuName}' Width='100%' Height='100%'>
                <Button Name='{menuName}_shield' Width='100%' Height='100%'
                    BackgroundColor='#000000' FocusBackgroundColor='#000000'
                    TextColor='#000000' FocusTextColor='#000000'
                    BorderStyle='None' PaddingTop='0ch' PaddingBottom='0ch' />
                <Container Width='35ch' Height='7ch'
                    PositionX='15ch' PositionY='8ch'
                    BackgroundColor='{Theme.WindowBg}' BorderStyle='Single' RoundedCorners='true'
                    ForegroundColor='{Theme.Border}'>
                    <StackPanel Direction='Vertical' Width='100%' Height='100%'
                        BackgroundColor='Inherit' Justify='Center' PaddingLeft='1ch' PaddingRight='1ch'>
                        <Text Width='100%' Height='1ch' ForegroundColor='#cccccc'
                            BackgroundColor='Inherit' Style='Bold'>Rename Note</Text>
                        <Container Width='100%' Height='3ch'
                            BorderStyle='Single' RoundedCorners='true'
                            BackgroundColor='{Theme.Darker}' ForegroundColor='{Theme.Border}'>
                            <Input Name='{menuName}_input' Width='100%' Height='1ch'
                                Value='{SecurityElement.Escape(currentName)}'
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
                                BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
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
            newName = newName.Trim();
            if (string.IsNullOrEmpty(newName)) return;

            File.WriteAllText(Path.Combine(NotesDir, $"{noteId}.name"), newName);
            if (_titleText is not null && _currentNoteId == noteId)
                _titleText.Content = $"📓 {newName}";
            LoadNoteList();
        };

        _termui.GetWidget<Button>($"{menuName}_cancel")?.Let(b => b.Click += (_, _) =>
        {
            if (dialog is not null) _rootContainer.Remove(dialog);
        });

        _termui.GetWidget<Button>($"{menuName}_ok")?.Let(b => b.Click += (_, _) =>
            doRename(input?.Value ?? ""));

        if (input is not null)
        {
            input.EnterPressed += (_, text) => doRename(text);
            _termui.SetFocus(input);
        }
    }

    private void DeleteNote(string noteId)
    {
        if (_rootContainer is null) return;

        var name = GetNoteName(noteId);
        var menuName = $"{_prefix}_del{_menuCounter++}";

        _rootContainer.Add($@"
            <Container Name='{menuName}' Width='100%' Height='100%'>
                <Button Name='{menuName}_shield' Width='100%' Height='100%'
                    BackgroundColor='#000000' FocusBackgroundColor='#000000'
                    TextColor='#000000' FocusTextColor='#000000'
                    BorderStyle='None' PaddingTop='0ch' PaddingBottom='0ch' />
                <Container Width='35ch' Height='6ch'
                    PositionX='15ch' PositionY='8ch'
                    BackgroundColor='{Theme.WindowBg}' BorderStyle='Single' RoundedCorners='true'
                    ForegroundColor='#5a2020'>
                    <StackPanel Direction='Vertical' Width='100%' Height='100%'
                        BackgroundColor='Inherit' Justify='Center' PaddingLeft='1ch' PaddingRight='1ch'>
                        <Text Width='100%' Height='1ch' ForegroundColor='#ff8888'
                            BackgroundColor='Inherit' Style='Bold'>Delete note?</Text>
                        <Text Width='100%' Height='1ch' ForegroundColor='#888888'
                            BackgroundColor='Inherit'>{SecurityElement.Escape(name)}</Text>
                        <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                            BackgroundColor='Inherit' Justify='End'>
                            <Button Name='{menuName}_cancel' Width='8ch' Height='1ch'
                                BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                                TextColor='#888888' FocusTextColor='#cccccc'
                                BorderStyle='None' TextAlign='Center'
                                PaddingTop='0ch' PaddingBottom='0ch'>Cancel</Button>
                            <Button Name='{menuName}_delete' Width='8ch' Height='1ch'
                                BackgroundColor='Inherit' FocusBackgroundColor='#5a1010'
                                TextColor='#ff5555' FocusTextColor='#ff8888'
                                BorderStyle='None' TextAlign='Center'
                                PaddingTop='0ch' PaddingBottom='0ch'>Delete</Button>
                        </StackPanel>
                    </StackPanel>
                </Container>
            </Container>");

        var dialog = _termui.GetWidget<Container>(menuName);

        _termui.GetWidget<Button>($"{menuName}_cancel")?.Let(b => b.Click += (_, _) =>
        {
            if (dialog is not null) _rootContainer.Remove(dialog);
        });

        _termui.GetWidget<Button>($"{menuName}_delete")?.Let(b => b.Click += (_, _) =>
        {
            if (dialog is not null) _rootContainer.Remove(dialog);

            try
            {
                File.Delete(Path.Combine(NotesDir, $"{noteId}.txt"));
                File.Delete(Path.Combine(NotesDir, $"{noteId}.name"));
            }
            catch { }

            if (_currentNoteId == noteId)
            {
                _currentNoteId = null;
                if (_editor is not null) _editor.Value = "";
                if (_titleText is not null) _titleText.Content = "";
            }
            LoadNoteList();
            if (_statusText is not null) _statusText.Content = $"Deleted: {name}";
        });
    }

    private void SaveNote(string noteId, string content)
    {
        var path = Path.Combine(NotesDir, $"{noteId}.txt");
        try { File.WriteAllText(path, content); } catch { }
    }

    private static string GetNoteName(string noteId)
    {
        var nameFile = Path.Combine(NotesDir, $"{noteId}.name");
        if (File.Exists(nameFile))
        {
            var name = File.ReadAllText(nameFile).Trim();
            if (!string.IsNullOrEmpty(name)) return name;
        }
        // Fallback: parse date from ID
        if (noteId.Length >= 8)
            return $"{noteId[..4]}-{noteId[4..6]}-{noteId[6..8]}";
        return noteId;
    }
}
