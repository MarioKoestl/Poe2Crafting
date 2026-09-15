using System.Collections.Concurrent;
using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine;
using POE2Crafting.Core.Engine.Planning;

namespace POE2Crafting.Web.Services;

/// <summary>
/// All crafting guides of the Guides tab: the curated ones (<see cref="GuideLibrary"/>) and the ones saved from simulator histories
/// (saved-guides/{Id}.json, shared by all sessions). Walkthroughs of saved guides are built once per guide.
/// </summary>
public sealed class GuideCatalog
{
    private readonly GuideLibrary _library;
    private readonly CraftingEngine _engine;
    private readonly JsonDocumentFolder<RecordedGuide> _files;
    private readonly object _lock = new();
    private Dictionary<string, RecordedGuide>? _recorded;
    private readonly ConcurrentDictionary<string, GuideWalkthrough> _walkthroughs = new();

    public GuideCatalog(string folder, GuideLibrary library, CraftingEngine engine, ILogger<GuideCatalog> logger)
    {
        _library = library;
        _engine = engine;
        _files = new JsonDocumentFolder<RecordedGuide>(folder, logger);
    }

    /// <summary>Saved guides (newest first), then the curated guides.</summary>
    public IReadOnlyList<GuideWalkthrough> All() =>
        RecordedSnapshot().OrderByDescending(g => g.CreatedAt).Select(Walkthrough)
            .Concat(_library.Guides.Select(_library.Walkthrough))
            .ToList();

    public GuideWalkthrough? Find(string? id) =>
        id == null ? null
        : _library.Find(id) is { } curated ? _library.Walkthrough(curated)
        : FindRecorded(id) is { } recorded ? Walkthrough(recorded)
        : null;

    /// <summary>The saved guide behind an id, or null for curated or unknown guides.</summary>
    public RecordedGuide? FindRecorded(string? id)
    {
        if (id == null) return null;
        lock (_lock) return Loaded().GetValueOrDefault(id);
    }

    /// <summary>Save a guide recorded in the simulator.</summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    public void Save(RecordedGuide guide)
    {
        lock (_lock)
        {
            _files.Write(guide.Id, guide);
            Loaded()[guide.Id] = guide;
            _walkthroughs.TryRemove(guide.Id, out _);
        }
    }

    public void Delete(string id)
    {
        lock (_lock)
        {
            _files.Delete(id);
            Loaded().Remove(id);
            _walkthroughs.TryRemove(id, out _);
        }
    }

    private GuideWalkthrough Walkthrough(RecordedGuide guide) => _walkthroughs.GetOrAdd(guide.Id, _ => new RecordedGuideRunner(_engine).Run(guide));

    private List<RecordedGuide> RecordedSnapshot()
    {
        lock (_lock) return Loaded().Values.ToList();
    }

    /// <summary>The saved guides, read from disk once with their items bound to the game data (call under the lock).</summary>
    private Dictionary<string, RecordedGuide> Loaded()
    {
        if (_recorded != null) return _recorded;
        _recorded = _files.ReadAll().ToDictionary(g => g.Id);
        foreach (var entry in _recorded.Values.SelectMany(g => g.History)) entry.Item.Bind(_engine.Data);
        return _recorded;
    }
}
