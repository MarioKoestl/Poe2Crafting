using POE2Crafting.Core.Engine.Planning;

namespace POE2Crafting.Core.Market;

/// <summary>What one item costs on the Currency Exchange, in Chaos Orbs: <see cref="Buy"/> (what buying costs, used for crafting costs) and the average fill.</summary>
public sealed record ItemPrice(string Name, double Buy, double Average, long LastHour);

/// <summary>The cost of some materials in Chaos Orbs and the materials without a price.</summary>
public sealed record MaterialCost(double Chaos, IReadOnlyList<string> Unpriced)
{
    public bool IsComplete => Unpriced.Count == 0;
}

/// <summary>
/// Exchange prices of one league by item name (the simulator's currency, omen, essence... names), for showing what crafting costs.
/// Costs use the buy price (you buy the materials); Chaos Orb = 1. Immutable, one per market snapshot.
/// </summary>
public sealed class PriceBook
{
    public static readonly PriceBook Empty = new(null, null, new Dictionary<string, ItemPrice>(), 0);

    public string? League { get; }
    /// <summary>Unix hour of the latest trades.</summary>
    public long? LatestHour { get; }
    /// <summary>Chaos Orbs one Divine Orb costs (0 when unknown).</summary>
    public double ChaosPerDivine { get; }
    public bool HasPrices => _byName.Count > 0;

    private readonly IReadOnlyDictionary<string, ItemPrice> _byName;

    private PriceBook(string? league, long? latestHour, IReadOnlyDictionary<string, ItemPrice> byName, double chaosPerDivine)
    {
        League = league;
        LatestHour = latestHour;
        _byName = byName;
        ChaosPerDivine = chaosPerDivine;
    }

    /// <summary>Prices of every item traded in the league, priced in Chaos Orbs (directly or through Divine/Exalted Orbs).</summary>
    public static PriceBook From(LeagueMarket market, ExchangeCatalog catalog, long? latestHour)
    {
        var prices = market.Rows(ExchangeCurrencies.Chaos, catalog)
            .Where(r => r.Quotes.Fair != null)
            .Select(r => new ItemPrice(r.Item.Name, r.Quotes.BestBuy!.Buy, r.Quotes.Fair!.Average, r.Quotes.Fair.LastHour))
            .Append(new ItemPrice(catalog.Item(ExchangeCurrencies.Chaos).Name, 1, 1, latestHour ?? 0))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        double chaosPerDivine = prices.GetValueOrDefault(catalog.Item(ExchangeCurrencies.Divine).Name)?.Average ?? 0;
        return new PriceBook(market.Name, latestHour, prices, chaosPerDivine);
    }

    public ItemPrice? Find(string name) => _byName.GetValueOrDefault(name.Trim());

    /// <summary>Buy cost of the materials (name → count).</summary>
    public MaterialCost Cost(IEnumerable<(string Name, double Count)> materials)
    {
        double chaos = 0;
        var unpriced = new List<string>();
        foreach (var (name, count) in materials)
        {
            if (count <= 0) continue;
            if (Find(name) is { } price) chaos += price.Buy * count;
            else unpriced.Add(name);
        }
        return new MaterialCost(chaos, unpriced.Distinct().ToList());
    }

    /// <summary>Buy cost of one successful run of the materials.</summary>
    public MaterialCost PerRunCost(IEnumerable<StrategyMaterial> materials) => Cost(materials.Select(m => (m.Name, (double)m.PerRun)));

    /// <summary>Expected buy cost including the retries of uncertain steps (infinite for impossible steps).</summary>
    public MaterialCost ExpectedCost(IEnumerable<StrategyMaterial> materials) => Cost(materials.Select(m => (m.Name, m.Expected)));
}
