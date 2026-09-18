using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using POE2Crafting.Core.Builds;
using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Web.Services;

/// <summary>
/// Singleton: builds from poe.ninja (their site API, not documented): league snapshots, build searches (ascendancies, main skills, item usage)
/// and a background analysis of the rare items the top characters of a search wear.
/// Politeness: searches and dictionaries are kept in memory (poe.ninja caches them 30 minutes itself), characters on disk (ninja-cache/, <see cref="CharacterMaxAge"/>),
/// character downloads one at a time with a pause.
/// </summary>
public sealed class NinjaBuildsService(IHttpClientFactory httpClients, GameData data, ModPool pool, string cacheFolder, ILogger<NinjaBuildsService> logger)
{
    public const string HttpClientName = "poe-ninja";
    public static readonly TimeSpan CharacterMaxAge = TimeSpan.FromHours(6);
    private static readonly TimeSpan IndexMaxAge = TimeSpan.FromMinutes(10), PauseBetweenCharacters = TimeSpan.FromMilliseconds(250);

    private (DateTimeOffset At, NinjaIndexState State)? _index;
    private readonly ConcurrentDictionary<string, Lazy<Task<BuildOverview>>> _searches = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> _dictionaries = new();
    private readonly ConcurrentDictionary<string, BuildAnalysisJob> _jobs = new();
    private readonly SemaphoreSlim _characterDownloads = new(1);

    private HttpClient Http => httpClients.CreateClient(HttpClientName);

    /// <summary>League snapshots, current leagues first.</summary>
    public async Task<IReadOnlyList<NinjaSnapshot>> SnapshotsAsync(CancellationToken ct = default)
    {
        if (_index is not { } index || DateTimeOffset.UtcNow - index.At > IndexMaxAge)
        {
            var state = JsonSerializer.Deserialize<NinjaIndexState>(await Http.GetStringAsync("data/index-state", ct)) ?? new NinjaIndexState();
            _index = index = (DateTimeOffset.UtcNow, state);
        }
        return index.State.Snapshots;
    }

    /// <summary>The search of a league snapshot with a filter (cached per snapshot version).</summary>
    public Task<BuildOverview> SearchAsync(NinjaSnapshot snapshot, BuildFilter filter)
    {
        var key = $"{snapshot.Version}/{snapshot.SnapshotName}{filter.Query}";
        var search = _searches.GetOrAdd(key, _ => new Lazy<Task<BuildOverview>>(() => LoadSearch(snapshot, filter)));
        if (search.Value.IsFaulted) _searches.TryRemove(key, out _);
        return search.Value;
    }

    private async Task<BuildOverview> LoadSearch(NinjaSnapshot snapshot, BuildFilter filter)
    {
        var bytes = await Http.GetByteArrayAsync($"builds/{snapshot.Version}/search?overview={Uri.EscapeDataString(snapshot.SnapshotName)}{filter.Query}");
        var search = BuildSearchResult.Parse(bytes);
        // dictionaries first (async), then the synchronous resolution
        var hashes = search.Dictionaries.SelectMany(d => new[] { d.Hash, d.PropertiesHash }).OfType<string>().Distinct().ToList();
        var blobs = (await Task.WhenAll(hashes.Select(async h => (Hash: h, Blob: await Dictionary(h))))).ToDictionary(x => x.Hash, x => x.Blob);
        return BuildOverview.From(search, h => NinjaDictionary.Values(blobs[h]), h => NinjaDictionary.Properties(blobs[h]));
    }

    /// <summary>A dictionary blob by hash (content-addressed: never changes); a failed download is tried again next time.</summary>
    private Task<byte[]> Dictionary(string hash)
    {
        var download = _dictionaries.GetOrAdd(hash, h => new Lazy<Task<byte[]>>(() => Http.GetByteArrayAsync($"builds/dictionary/{h}"))).Value;
        if (download.IsFaulted) _dictionaries.TryRemove(hash, out _);
        return download;
    }

    /// <summary>The running or finished analysis of the top <paramref name="sampleSize"/> characters of a search; started when missing (or failed).</summary>
    public BuildAnalysisJob Analyse(NinjaSnapshot snapshot, BuildFilter filter, int sampleSize)
    {
        var key = $"{snapshot.Version}/{snapshot.SnapshotName}{filter.Query}/{sampleSize}";
        var job = _jobs.AddOrUpdate(key, _ => new BuildAnalysisJob(filter, sampleSize), (_, existing) => existing.Error != null ? new BuildAnalysisJob(filter, sampleSize) : existing);
        if (job.TryStart()) _ = Task.Run(() => RunAnalysis(job, snapshot));
        return job;
    }

    /// <summary>An analysis already started for these settings, or null.</summary>
    public BuildAnalysisJob? ExistingAnalysis(NinjaSnapshot snapshot, BuildFilter filter, int sampleSize) =>
        _jobs.GetValueOrDefault($"{snapshot.Version}/{snapshot.SnapshotName}{filter.Query}/{sampleSize}");

    private async Task RunAnalysis(BuildAnalysisJob job, NinjaSnapshot snapshot)
    {
        try
        {
            var search = await SearchAsync(snapshot, job.Filter);
            var sample = search.Characters.Take(job.SampleSize).ToList();
            var items = new List<SampledItem>();
            int loaded = 0;
            PruneCharacterCache();
            foreach (var (reference, index) in sample.Select((c, i) => (c, i)))
            {
                job.Report($"{index + 1}/{sample.Count} characters");
                if (await CharacterAsync(snapshot, reference) is { } character)
                {
                    loaded++;
                    items.AddRange(character.CraftableItems.Where(i => i.RarityName == "Rare")
                        .Select(i => i.ToItem(data)).OfType<Item>()
                        .Select(item => new SampledItem(reference, character.Class, character.Level, item)));
                }
            }
            job.Complete(RareItemAnalysis.Build(items, loaded, pool));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException)
        {
            logger.LogWarning(ex, "poe.ninja build analysis failed");
            job.Fail($"poe.ninja: {ex.Message}");
        }
    }

    /// <summary>A character from the disk cache (younger than <see cref="CharacterMaxAge"/>) or poe.ninja; null when it can't be loaded (e.g. private or removed).</summary>
    private async Task<NinjaCharacter?> CharacterAsync(NinjaSnapshot snapshot, CharacterRef reference)
    {
        var file = Path.Combine(cacheFolder, "characters", CacheName(snapshot.SnapshotName, reference) + ".json");
        if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < CharacterMaxAge)
            return ParseCharacter(await File.ReadAllTextAsync(file), reference);

        await _characterDownloads.WaitAsync();
        try
        {
            var url = $"builds/{snapshot.Version}/character?account={Uri.EscapeDataString(reference.Account)}&name={Uri.EscapeDataString(reference.Name)}&overview={Uri.EscapeDataString(snapshot.SnapshotName)}";
            using var response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("poe.ninja character {Account}/{Name}: {Status}", reference.Account, reference.Name, response.StatusCode);
                return null;
            }
            var json = await response.Content.ReadAsStringAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, json);
            await Task.Delay(PauseBetweenCharacters);
            return ParseCharacter(json, reference);
        }
        finally
        {
            _characterDownloads.Release();
        }
    }

    /// <summary>Delete cached characters older than <see cref="CharacterMaxAge"/>.</summary>
    private void PruneCharacterCache()
    {
        var folder = Path.Combine(cacheFolder, "characters");
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder, "*.json").Where(f => DateTime.UtcNow - File.GetLastWriteTimeUtc(f) >= CharacterMaxAge))
        {
            try { File.Delete(file); }
            catch (IOException ex) { logger.LogWarning(ex, "Could not delete {File}", file); }
        }
    }

    /// <summary>One broken character response is skipped, not the whole analysis.</summary>
    private NinjaCharacter? ParseCharacter(string json, CharacterRef reference)
    {
        try
        {
            return NinjaCharacter.Parse(json);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Skipping unreadable poe.ninja character {Account}/{Name}", reference.Account, reference.Name);
            return null;
        }
    }

    private static string CacheName(string league, CharacterRef reference) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{league}/{reference.Account}/{reference.Name}")));
}

/// <summary>A background analysis: progress text while running, then the result or an error. <see cref="Changed"/> fires on a background thread.</summary>
public sealed class BuildAnalysisJob(BuildFilter filter, int sampleSize)
{
    private int _started;

    public BuildFilter Filter => filter;
    public int SampleSize => sampleSize;
    public string? Progress { get; private set; }
    public RareItemAnalysis? Result { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public bool IsRunning => _started == 1 && Result == null && Error == null;

    public event Action? Changed;

    internal bool TryStart() => Interlocked.CompareExchange(ref _started, 1, 0) == 0;

    internal void Report(string progress)
    {
        Progress = progress;
        Changed?.Invoke();
    }

    internal void Complete(RareItemAnalysis result)
    {
        Result = result;
        FinishedAt = DateTimeOffset.Now;
        Changed?.Invoke();
    }

    internal void Fail(string error)
    {
        Error = error;
        Changed?.Invoke();
    }
}
