using System.Collections.Concurrent;

namespace POE2Crafting.Core.Market;

/// <summary>The loaded exchange history (all leagues), immutable; the market service replaces it when a new hour arrives.</summary>
public sealed class MarketSnapshot
{
    public static readonly MarketSnapshot Empty = new(Array.Empty<HourlyMarket>(), new MarketSettings());

    /// <summary>Hours with data, oldest first.</summary>
    public IReadOnlyList<long> Hours { get; }
    /// <summary>Leagues of the latest hour, most traded pairs first.</summary>
    public IReadOnlyList<string> Leagues { get; }
    public long? LatestHour => Hours.Count > 0 ? Hours[^1] : null;

    private readonly MarketSettings _settings;
    private readonly ILookup<string, HourlyMarket> _byLeague;
    private readonly ConcurrentDictionary<string, Lazy<LeagueMarket>> _leagues = new();

    public MarketSnapshot(IReadOnlyCollection<HourlyMarket> markets, MarketSettings settings)
    {
        _settings = settings;
        _byLeague = markets.ToLookup(m => m.League);
        Hours = markets.Select(m => m.Hour).Distinct().Order().ToList();
        Leagues = _byLeague
            .OrderByDescending(g => g.Count(m => m.Hour == LatestHour))
            .ThenByDescending(g => g.Count())
            .Select(g => g.Key).ToList();
    }

    /// <summary>The configured league when it has data, otherwise the most traded one.</summary>
    public string? DefaultLeague => _settings.League is { } configured && Leagues.Contains(configured) ? configured : Leagues.FirstOrDefault();

    public LeagueMarket League(string name) =>
        _leagues.GetOrAdd(name, n => new Lazy<LeagueMarket>(() => new LeagueMarket(n, _byLeague[n], Hours, _settings))).Value;
}
