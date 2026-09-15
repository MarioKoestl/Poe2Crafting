using System.Collections.Concurrent;

namespace POE2Crafting.Core.Market;

/// <summary>
/// Exchange prices of one league from the hourly digests. The API has no live order book, only fills per hour, so prices are estimates:
/// <list type="bullet">
/// <item>average = volume-weighted average fill of the pair's latest <see cref="MarketSettings.RecentHours"/> hours with trades</item>
/// <item>sell ≈ the hours' lowest fills, buy ≈ the highest fills (fills far from the average count as mistake orders, <see cref="MarketSettings.OutlierFactor"/>)</item>
/// <item>indirect routes (item → Divine/Exalted/Chaos → counter) multiply both trades; the best route can beat the direct pair</item>
/// </list>
/// Thread-safe: rows are computed once per reference currency and shared by all users.
/// </summary>
public sealed class LeagueMarket
{
    public string Name { get; }
    /// <summary>Hours with data of the snapshot, oldest first.</summary>
    public IReadOnlyList<long> Hours { get; }

    private readonly MarketSettings _settings;
    /// <summary>item → counter item → that pair's hours, newest first.</summary>
    private readonly Dictionary<string, Dictionary<string, List<HourlyMarket>>> _pairs = new();
    private readonly ConcurrentDictionary<string, Lazy<IReadOnlyList<ItemMarket>>> _rows = new();

    public LeagueMarket(string name, IEnumerable<HourlyMarket> markets, IReadOnlyList<long> hours, MarketSettings settings)
    {
        Name = name;
        Hours = hours;
        _settings = settings;
        foreach (var market in markets.OrderByDescending(m => m.Hour))
        {
            PairList(market.A, market.B).Add(market);
            PairList(market.B, market.A).Add(market);
        }
    }

    private List<HourlyMarket> PairList(string item, string counter)
    {
        if (!_pairs.TryGetValue(item, out var counters)) _pairs[item] = counters = new();
        if (!counters.TryGetValue(counter, out var list)) counters[counter] = list = new();
        return list;
    }

    /// <summary>Every item traded in this league during the history.</summary>
    public IEnumerable<string> Items => _pairs.Keys;

    /// <summary>All routes pricing <paramref name="item"/> in <paramref name="counter"/> (latest hours).</summary>
    public QuoteRoutes Quote(string item, string counter) => Routes(item, counter, hour: null);

    /// <summary>Every traded item except the reference, priced in the reference currency.</summary>
    public IReadOnlyList<ItemMarket> Rows(string reference, ExchangeCatalog catalog) =>
        _rows.GetOrAdd(reference, r => new Lazy<IReadOnlyList<ItemMarket>>(() => BuildRows(r, catalog))).Value;

    private IReadOnlyList<ItemMarket> BuildRows(string reference, ExchangeCatalog catalog) =>
        Items.Select(i => Row(catalog.Item(i), reference)).OfType<ItemMarket>().ToList();

    /// <summary>One item priced in the reference currency; null when it can't be priced (not traded, or the reference itself).</summary>
    public ItemMarket? Row(ExchangeItem item, string reference)
    {
        var quotes = Quote(item.Id, reference);
        if (quotes.IsEmpty) return null;
        var series = Hours.Select(h => Routes(item.Id, reference, h).Fair?.Average).ToList();
        var markets = _pairs[item.Id].Values.SelectMany(l => l).ToList();
        long latest = markets.Max(m => m.Hour);
        return new ItemMarket(item, quotes, series, Change(series),
            markets.Sum(m => m.VolumeOf(item.Id)), markets.Where(m => m.Hour == latest).Sum(m => m.StockOf(item.Id)));
    }

    /// <summary>Average of the last hours against the first hours of the history (each <see cref="MarketSettings.RecentHours"/> hours with trades).</summary>
    private double? Change(IReadOnlyList<double?> series)
    {
        var prices = series.OfType<double>().ToList();
        int n = _settings.RecentHours;
        if (prices.Count < 2 * n) return null;
        double first = prices.Take(n).Average(), last = prices.TakeLast(n).Average();
        return first > 0 ? last / first - 1 : null;
    }

    private QuoteRoutes Routes(string item, string counter, long? hour)
    {
        if (item == counter) return QuoteRoutes.None;
        var routes = new List<ExchangeQuote>();
        if (Direct(item, counter, hour) is { } direct) routes.Add(direct);
        foreach (var bridge in ExchangeCurrencies.Core.Where(b => b != item && b != counter))
            if (Direct(item, bridge, hour) is { } toBridge && Direct(bridge, counter, hour) is { } fromBridge)
                routes.Add(ExchangeQuote.Through(toBridge, fromBridge));
        return routes.Count == 0 ? QuoteRoutes.None : new QuoteRoutes(routes);
    }

    /// <summary>The direct pair: one given hour, or its latest hours with trades.</summary>
    private ExchangeQuote? Direct(string item, string counter, long? hour)
    {
        if (!_pairs.TryGetValue(item, out var counters) || !counters.TryGetValue(counter, out var markets)) return null;
        var prices = markets.Where(m => hour == null || m.Hour == hour).Select(m => m.PriceOf(item)).OfType<PairPrice>();
        return Aggregate(item, counter, (hour == null ? prices.Take(_settings.RecentHours) : prices).ToList());
    }

    /// <summary>Volume-weighted average, sell (low fills) and buy (high fills) of some hours; mistake fills count as the hour's average.</summary>
    internal ExchangeQuote? Aggregate(string item, string counter, IReadOnlyList<PairPrice> hours)
    {
        long volume = hours.Sum(h => h.Volume);
        if (volume <= 0) return null;
        double factor = _settings.OutlierFactor;
        double Weighted(Func<PairPrice, double> value) => hours.Sum(h => value(h) * h.Volume) / volume;
        double sell = Weighted(h => h.Low > 0 && h.Low >= h.Average / factor ? Math.Min(h.Low, h.Average) : h.Average);
        double buy = Weighted(h => h.High > 0 && h.High <= h.Average * factor ? Math.Max(h.High, h.Average) : h.Average);
        return new ExchangeQuote(item, counter, null, Weighted(h => h.Average), sell, buy, volume, hours.Max(h => h.Hour));
    }
}
