using Termesktop;
using System.Text.Json;

namespace Termesktop.Components;

public record AppEntry(string Id, string Name, string Icon);

public class AppRegistry
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".termesktop");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "pinned.json");

    public static readonly AppEntry[] AllApps =
    [
        new("Files", "Files", "📁"),
        new("Terminal", "Terminal", "💻"),
        new("Editor", "Editor", "📝"),
        new("Image", "Image", "🖼"),
        new("Video", "Video", "🎬"),
        new("Download", "Download", "⬇"),
        new("Calc", "Calc", "🧮"),
        new("Notes", "Notes", "📓"),
        new("Clock", "Clock", "🕐"),
        new("Markdown", "Markdown", "📑"),
        new("Monitor", "Monitor", "📊"),
        new("Tasks", "Tasks", "📋"),
        new("Settings", "Settings", "⚙"),
    ];

    private readonly List<string> _pinnedIds;

    public AppRegistry()
    {
        _pinnedIds = LoadPinned();
    }

    public IReadOnlyList<AppEntry> GetPinned()
    {
        return _pinnedIds
            .Select(id => AllApps.FirstOrDefault(a => a.Id == id))
            .Where(a => a is not null)
            .ToList()!;
    }

    public IReadOnlyList<AppEntry> GetAll() => AllApps;

    public bool IsPinned(string appId) => _pinnedIds.Contains(appId);

    public void Pin(string appId)
    {
        if (!_pinnedIds.Contains(appId))
        {
            _pinnedIds.Add(appId);
            Save();
        }
    }

    public void Unpin(string appId)
    {
        if (_pinnedIds.Remove(appId))
            Save();
    }

    public void TogglePin(string appId)
    {
        if (IsPinned(appId))
            Unpin(appId);
        else
            Pin(appId);
    }

    public int GetPinnedIndex(string appId) => _pinnedIds.IndexOf(appId);

    public void MovePinned(string appId, int delta)
    {
        var idx = _pinnedIds.IndexOf(appId);
        if (idx < 0) return;

        var newIdx = idx + delta;
        if (newIdx < 0 || newIdx >= _pinnedIds.Count) return;

        _pinnedIds.RemoveAt(idx);
        _pinnedIds.Insert(newIdx, appId);
        Save();
    }

    private void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(_pinnedIds, AppJsonContext.Default.ListString));
    }

    private static List<string> LoadPinned()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize(json, AppJsonContext.Default.ListString) ?? DefaultPinned();
            }
        }
        catch { }

        return DefaultPinned();
    }

    private static List<string> DefaultPinned()
    {
        return ["Files", "Terminal", "Editor", "Settings"];
    }
}
