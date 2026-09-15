using System.Text.Json.Serialization;
using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine.Planning;
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

/// <summary>What the project selector lists.</summary>
public sealed record ProjectSummary(string Id, string Name, int ItemCount, DateTime UpdatedAt);

/// <summary>
/// Stores projects as one JSON file each in the projects folder (shared by all sessions, file access serialised). The project list is read
/// from disk once and then kept up to date by <see cref="Save"/> and <see cref="Delete"/>.
/// </summary>
public sealed class ProjectStore
{
    private readonly JsonDocumentFolder<CraftingProject> _files;
    private readonly GameData _data;
    private readonly object _lock = new();
    private Dictionary<string, ProjectSummary>? _summaries;

    public ProjectStore(string folder, GameData data, ILogger<ProjectStore> logger)
    {
        _files = new JsonDocumentFolder<CraftingProject>(folder, logger);
        _data = data;
    }

    /// <summary>All projects, most recently changed first.</summary>
    public IReadOnlyList<ProjectSummary> List()
    {
        lock (_lock)
        {
            _summaries ??= _files.ReadAll().ToDictionary(p => p.Id, Summary);
            return _summaries.Values.OrderByDescending(p => p.UpdatedAt).ToList();
        }
    }

    /// <summary>The project with its items bound to the game data, or null if it doesn't exist or can't be read.</summary>
    public CraftingProject? Load(string id)
    {
        lock (_lock)
        {
            var project = _files.Read(id);
            foreach (var entry in project?.Items.SelectMany(i => i.History) ?? Enumerable.Empty<HistoryEntry>()) entry.Item.Bind(_data);
            return project;
        }
    }

    /// <summary>Write the project (atomically).</summary>
    /// <param name="touch">Update the "last changed" time (false for pure selection changes).</param>
    /// <exception cref="IOException">The file could not be written.</exception>
    public void Save(CraftingProject project, bool touch = true)
    {
        if (touch) project.UpdatedAt = DateTime.UtcNow;
        lock (_lock)
        {
            _files.Write(project.Id, project);
            _summaries?.Remove(project.Id);
            _summaries?.Add(project.Id, Summary(project));
        }
    }

    public void Delete(string id)
    {
        lock (_lock)
        {
            _files.Delete(id);
            _summaries?.Remove(id);
        }
    }

    private static ProjectSummary Summary(CraftingProject p) => new(p.Id, p.Name, p.Items.Count, p.UpdatedAt);
}
