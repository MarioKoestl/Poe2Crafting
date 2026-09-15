namespace POE2Crafting.Core.Market;

/// <summary>
/// Price of one item in a counter item over one route (direct, or through <see cref="Via"/>): average fill, what selling it gets
/// (the low fills) and what buying it costs (the high fills), all in counter items per item.
/// </summary>
/// <param name="Volume">Items traded in the hours the price is based on (first leg of an indirect route).</param>
/// <param name="LastHour">Oldest "latest trade" of the route's legs (unix hour).</param>
public sealed record ExchangeQuote(string Item, string Counter, string? Via, double Average, double Sell, double Buy, long Volume, long LastHour)
{
    /// <summary>Buy − sell relative to the average: what a round trip loses.</summary>
    public double Spread => Average > 0 ? (Buy - Sell) / Average : 0;

    /// <summary>Two trades in a row: item → <see cref="Via"/> → counter.</summary>
    public static ExchangeQuote Through(ExchangeQuote toBridge, ExchangeQuote fromBridge) =>
        new(toBridge.Item, fromBridge.Counter, toBridge.Counter, toBridge.Average * fromBridge.Average,
            toBridge.Sell * fromBridge.Sell, toBridge.Buy * fromBridge.Buy, toBridge.Volume, Math.Min(toBridge.LastHour, fromBridge.LastHour));
}

/// <summary>All routes that price an item in a counter item.</summary>
public sealed record QuoteRoutes(IReadOnlyList<ExchangeQuote> Routes)
{
    public static readonly QuoteRoutes None = new(Array.Empty<ExchangeQuote>());

    public bool IsEmpty => Routes.Count == 0;

    /// <summary>The market price: the route with the most items traded (usually the direct pair).</summary>
    public ExchangeQuote? Fair => Routes.MaxBy(r => r.Volume);

    /// <summary>The route that pays the most counter items when selling.</summary>
    public ExchangeQuote? BestSell => Routes.MaxBy(r => r.Sell);

    /// <summary>The route that costs the fewest counter items when buying.</summary>
    public ExchangeQuote? BestBuy => Routes.MinBy(r => r.Buy);

    /// <summary>Advantage an other route needs over the market route to be worth two trades.</summary>
    public const double MinimumRouteAdvantage = 0.01;

    /// <summary>A route that pays noticeably more than the market route when selling, or null.</summary>
    public ExchangeQuote? BetterSellRoute => BestSell is { } best && Fair is { } fair && best != fair && best.Sell > fair.Sell * (1 + MinimumRouteAdvantage) ? best : null;

    /// <summary>A route that costs noticeably less than the market route when buying, or null.</summary>
    public ExchangeQuote? BetterBuyRoute => BestBuy is { } best && Fair is { } fair && best != fair && best.Buy < fair.Buy * (1 - MinimumRouteAdvantage) ? best : null;
}

/// <summary>One row of the Market page: an item priced in the reference currency with history and liquidity.</summary>
/// <param name="Series">Market price per hour of the analysed history (oldest first; null = no trade that hour).</param>
/// <param name="Change">Price change from the first to the last hours of the history; null when there are too few hours.</param>
/// <param name="Volume">Items traded in all pairs during the history.</param>
/// <param name="Stock">Items offered in the order books (latest hour, all pairs).</param>
public sealed record ItemMarket(ExchangeItem Item, QuoteRoutes Quotes, IReadOnlyList<double?> Series, double? Change, long Volume, long Stock)
{
    /// <summary>Value of the traded volume in the reference currency.</summary>
    public double VolumeValue => Volume * (Quotes.Fair?.Average ?? 0);
}
