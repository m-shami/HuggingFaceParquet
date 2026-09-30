using System.Text.Json;
using hfparquet.Models;

namespace hfparquet.Services;

public sealed class RecentRepositoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly object _gate = new();
    private const int MaxEntries = 24;

    public RecentRepositoryStore(IWebHostEnvironment environment)
    {
        var root = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "recent-repos.json");
    }

    public IReadOnlyList<RecentRepositoryEntry> List()
    {
        lock (_gate)
        {
            return ReadUnlocked()
                .OrderByDescending(entry => entry.QueriedUtc)
                .ToArray();
        }
    }

    public void Remember(string repoId, int fileCount)
    {
        if (string.IsNullOrWhiteSpace(repoId)) return;
        lock (_gate)
        {
            var entries = ReadUnlocked()
                .Where(entry => !string.Equals(entry.RepoId, repoId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            entries.Insert(0, new RecentRepositoryEntry
            {
                RepoId = repoId,
                FileCount = Math.Max(0, fileCount),
                QueriedUtc = DateTime.UtcNow
            });
            if (entries.Count > MaxEntries) entries = entries.Take(MaxEntries).ToList();
            WriteUnlocked(entries);
        }
    }

    private List<RecentRepositoryEntry> ReadUnlocked()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<RecentRepositoryEntry>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private void WriteUnlocked(List<RecentRepositoryEntry> entries)
    {
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(entries, JsonOptions));
            File.Move(temp, _path, overwrite: true);
    }
}
