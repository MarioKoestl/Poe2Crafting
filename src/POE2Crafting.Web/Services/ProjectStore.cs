using System.Text.Json;
using System.Text.Json.Serialization;
using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Web.Services;

/// <summary>A crafting project: several items, each with its own crafting history. Saved automatically as projects/{Id}.json.</summary>
public sealed class CraftingProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ProjectItem> Items { get; set; } = new();
    /// <summary>The item that was open last.</summary>
    public string? SelectedItemId { get; set; }
}

/// <summary>One item of a project with its crafting history (the last entry is the current state).</summary>
public sealed class ProjectItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public List<HistoryEntry> History { get; set; } = new();

    [JsonIgnore] public Item? Current => History.Count > 0 ? History[^1].Item : null;
}

public sealed class HistoryEntry
{
    public string Action { get; init; } = "";
    public Item Item { get; init; } = null!;
    public string Summary { get; init; } = "";
}

/// <summary>What the project selector lists.</summary>
public sealed record ProjectSummary(string Id, string Name, int ItemCount, DateTime UpdatedAt);

/// <summary>
/// Stores projects as one JSON file each in the projects folder (shared by all sessions, file access serialised). The project list is read
/// from disk once and then kept up to date by <see cref="Save"/> and <see cref="Delete"/>.
/// </summary>
public sealed class ProjectStore
{
    private const string Extension = ".json", TempExtension = ".json.tmp";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _folder;
    private readonly GameData _data;
    private readonly ILogger<ProjectStore> _logger;
    private readonly object _lock = new();
    private Dictionary<string, ProjectSummary>? _summaries;

    public ProjectStore(string folder, GameData data, ILogger<ProjectStore> logger)
    {
        _folder = folder;
        _data = data;
        _logger = logger;
        Directory.CreateDirectory(folder);
        // a crash between writing and renaming leaves a temp file behind
        foreach (var stale in Directory.EnumerateFiles(folder, "*" + TempExtension)) TryDelete(stale);
    }

    /// <summary>All projects, most recently changed first.</summary>
    public IReadOnlyList<ProjectSummary> List()
    {
        lock (_lock)
        {
            _summaries ??= Directory.EnumerateFiles(_folder, "*" + Extension)
                .Select(TryRead)
                .OfType<CraftingProject>()
                .ToDictionary(p => p.Id, Summary);
            return _summaries.Values.OrderByDescending(p => p.UpdatedAt).ToList();
        }
    }

    /// <summary>The project with its items bound to the game data, or null if it doesn't exist or can't be read.</summary>
    public CraftingProject? Load(string id)
    {
        lock (_lock)
        {
            var project = TryRead(PathOf(id));
            foreach (var entry in project?.Items.SelectMany(i => i.History) ?? Enumerable.Empty<HistoryEntry>()) entry.Item.Bind(_data);
            return project;
        }
    }

    /// <summary>Write the project (atomically: temp file, flushed, then renamed).</summary>
    /// <param name="touch">Update the "last changed" time (false for pure selection changes).</param>
    /// <exception cref="IOException">The file could not be written.</exception>
    public void Save(CraftingProject project, bool touch = true)
    {
        if (touch) project.UpdatedAt = DateTime.UtcNow;
        var json = JsonSerializer.SerializeToUtf8Bytes(project, JsonOptions);
        lock (_lock)
        {
            var path = PathOf(project.Id);
            try
            {
                using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(json);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(path + ".tmp", path, overwrite: true);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException($"The project file {path} is not writable.", ex);
            }
            _summaries?.Remove(project.Id);
            _summaries?.Add(project.Id, Summary(project));
        }
    }

    public void Delete(string id)
    {
        lock (_lock)
        {
            TryDelete(PathOf(id));
            TryDelete(PathOf(id) + ".tmp");
            _summaries?.Remove(id);
        }
    }

    private static ProjectSummary Summary(CraftingProject p) => new(p.Id, p.Name, p.Items.Count, p.UpdatedAt);

    private string PathOf(string id) => Path.Combine(_folder, Path.GetFileName(id) + Extension);

    /// <summary>A project file, or null when it is missing, locked or not a (valid) project: one bad file never breaks the list.</summary>
    private CraftingProject? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<CraftingProject>(File.ReadAllBytes(path), JsonOptions) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Skipping unreadable project file {Path}", path);
            return null;
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete {Path}", path);
        }
    }
}
