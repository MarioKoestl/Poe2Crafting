using System.Net;
using System.Text.Json;
using POE2Crafting.Core.Market;

namespace POE2Crafting.Web.Services;

/// <summary>
/// Singleton + hosted service: keeps the Currency Exchange history of the last <see cref="MarketSettings.HistoryHours"/> hours up to date.
/// GGG publishes one digest per completed hour (public API, no login); on start the missing hours are downloaded, afterwards the service
/// checks every <see cref="MarketSettings.PollMinutes"/> minutes for the next one. Downloaded hours are kept as compact JSON files
/// (market-cache/{hour}.json) so a restart doesn't download them again. <see cref="Changed"/> fires (on a background thread) for every new snapshot.
/// </summary>
public sealed class MarketDataService(IHttpClientFactory httpClients, MarketSettings settings, string cacheFolder, ILogger<MarketDataService> logger)
    : BackgroundService
{
    public const string HttpClientName = "currency-exchange";
    private const int HourSeconds = 3600;

    private readonly Dictionary<long, List<HourlyMarket>> _hours = new();
    private readonly StringPool _strings = new();
    private readonly SemaphoreSlim _wake = new(0);

    public MarketSnapshot Snapshot { get; private set; } = MarketSnapshot.Empty;
    public MarketSettings Settings => settings;
    public bool IsLoading { get; private set; } = true;
    /// <summary>"12/24 hours" while downloading.</summary>
    public string? Progress { get; private set; }
    public DateTimeOffset? LastCheck { get; private set; }
    public string? LastError { get; private set; }

    public event Action? Changed;

    /// <summary>Check for new hours now instead of waiting for the next poll.</summary>
    public void CheckNow()
    {
        if (_wake.CurrentCount == 0) _wake.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Run(LoadCache, stoppingToken);
        Publish();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DownloadMissingHours(stoppingToken);
                LastError = null;
            }
            catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or JsonException) && !stoppingToken.IsCancellationRequested)
            {
                LastError = $"Exchange API: {ex.Message}";
                logger.LogWarning(ex, "Currency Exchange download failed");
            }
            LastCheck = DateTimeOffset.Now;
            IsLoading = false;
            Progress = null;
            PruneOldHours();
            Publish();
            await _wake.WaitAsync(TimeSpan.FromMinutes(settings.PollMinutes), stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        }
    }

    /// <summary>Oldest hour of the history window and the latest complete hour.</summary>
    private (long First, long Last) Window()
    {
        long current = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / HourSeconds * HourSeconds;
        return (current - settings.HistoryHours * HourSeconds, current - HourSeconds);
    }

    private async Task DownloadMissingHours(CancellationToken ct)
    {
        var (first, last) = Window();
        var missing = new List<long>();
        for (long hour = first; hour <= last; hour += HourSeconds)
            if (!_hours.ContainsKey(hour)) missing.Add(hour);

        var http = httpClients.CreateClient(HttpClientName);
        for (int i = 0; i < missing.Count; i++)
        {
            long hour = missing[i];
            Progress = $"{i + 1}/{missing.Count} hours";
            Changed?.Invoke();
            using var response = await http.GetAsync(hour.ToString(), ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                LastError = $"Exchange API rate limit, retrying in {response.Headers.RetryAfter?.Delta?.TotalSeconds ?? settings.PollMinutes * 60:0} s";
                return;
            }
            var body = await response.Content.ReadAsStringAsync(ct);
            // the hour in progress answers 404 with next_change_id = the requested hour
            var digest = ExchangeDigest.Parse(body);
            if (!digest.IsComplete(hour))
            {
                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound) continue;
                response.EnsureSuccessStatusCode();
            }
            var markets = digest.ToMarkets(hour, _strings);
            lock (_hours) _hours[hour] = markets;
            await SaveHour(hour, markets, ct);
            // a long first download shows partial data along the way
            if (i % 6 == 5) Publish();
        }
    }

    private void Publish()
    {
        List<HourlyMarket> all;
        lock (_hours) all = _hours.Values.SelectMany(h => h).ToList();
        Snapshot = new MarketSnapshot(all, settings);
        Changed?.Invoke();
    }

    private void PruneOldHours()
    {
        var (first, _) = Window();
        lock (_hours)
            foreach (var hour in _hours.Keys.Where(h => h < first).ToList())
            {
                _hours.Remove(hour);
                TryDelete(CachePath(hour));
            }
    }

    private string CachePath(long hour) => Path.Combine(cacheFolder, $"{hour}.json");

    private void LoadCache()
    {
        if (!Directory.Exists(cacheFolder)) return;
        var (first, _) = Window();
        foreach (var file in Directory.EnumerateFiles(cacheFolder, "*.json"))
        {
            if (!long.TryParse(Path.GetFileNameWithoutExtension(file), out var hour)) continue;
            if (hour < first)
            {
                TryDelete(file);
                continue;
            }
            try
            {
                var markets = JsonSerializer.Deserialize<List<HourlyMarket>>(File.ReadAllText(file)) ?? new();
                _hours[hour] = markets.Select(m => m with { League = _strings.Get(m.League), A = _strings.Get(m.A), B = _strings.Get(m.B) }).ToList();
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Skipping broken market cache file {File}", file);
            }
        }
    }

    private async Task SaveHour(long hour, List<HourlyMarket> markets, CancellationToken ct)
    {
        Directory.CreateDirectory(cacheFolder);
        var temp = CachePath(hour) + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(markets), ct);
        File.Move(temp, CachePath(hour), overwrite: true);
    }

    private void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (IOException ex) { logger.LogWarning(ex, "Could not delete {File}", file); }
    }
}
