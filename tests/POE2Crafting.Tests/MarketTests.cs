using POE2Crafting.Core.Market;

namespace POE2Crafting.Tests;

public class MarketTests
{
    private const string Chaos = ExchangeCurrencies.Chaos, Divine = ExchangeCurrencies.Divine, Exalted = ExchangeCurrencies.Exalted;
    private const string Omen = "Metadata/Items/Currency/OmenOnChaosLowestLevelMod";
    private const string League = "Test League";
    private const long Hour = 1_789_466_400;

    private static HourlyMarket Pair(long hour, string a, string b, long volumeA, long volumeB, double lowestAPerB, double highestAPerB) =>
        new(hour, League, a, b, volumeA, volumeB, lowestAPerB, highestAPerB, volumeA * 10, volumeB * 10);

    private static LeagueMarket Market(params HourlyMarket[] markets) =>
        new MarketSnapshot(markets, new MarketSettings()).League(League);

    [Fact]
    public void Digest_ratios_become_amount_of_first_per_second_item()
    {
        const string json = """
            {"next_change_id": 1789470000, "markets": [{"league": "Test League",
              "market_id": "Metadata/Items/Currency/CurrencyRerollRare|Metadata/Items/Currency/CurrencyModValues",
              "market_pair": ["Metadata/Items/Currency/CurrencyRerollRare", "Metadata/Items/Currency/CurrencyModValues"],
              "volume_traded": {"Metadata/Items/Currency/CurrencyRerollRare": 1648939, "Metadata/Items/Currency/CurrencyModValues": 181550},
              "lowest_stock": {"Metadata/Items/Currency/CurrencyRerollRare": 147231, "Metadata/Items/Currency/CurrencyModValues": 50027},
              "highest_stock": {"Metadata/Items/Currency/CurrencyRerollRare": 272076, "Metadata/Items/Currency/CurrencyModValues": 66653},
              "lowest_ratio": {"Metadata/Items/Currency/CurrencyRerollRare": 9, "Metadata/Items/Currency/CurrencyModValues": 1},
              "highest_ratio": {"Metadata/Items/Currency/CurrencyRerollRare": 10, "Metadata/Items/Currency/CurrencyModValues": 1}}]}
            """;
        var digest = ExchangeDigest.Parse(json);
        Assert.True(digest.IsComplete(Hour));

        var market = Assert.Single(digest.ToMarkets(Hour, new StringPool()));
        Assert.Equal((Chaos, Divine, 9.0, 10.0, 272076L), (market.A, market.B, market.LowestRatio, market.HighestRatio, market.StockA));

        // a Divine sells for 9-10 Chaos, a Chaos for 0.1-0.111 Divine
        var divine = market.PriceOf(Divine)!;
        Assert.Equal((9.0, 10.0), (divine.Low, divine.High));
        Assert.Equal(1648939.0 / 181550, divine.Average, 6);
        var chaos = market.PriceOf(Chaos)!;
        Assert.Equal((0.1, 1 / 9.0), (chaos.Low, chaos.High));
    }

    [Fact]
    public void The_hour_in_progress_is_not_complete()
    {
        var digest = ExchangeDigest.Parse("""{"next_change_id":1789466400,"markets":[]}""");
        Assert.False(digest.IsComplete(Hour));
    }

    [Fact]
    public void Sell_and_buy_come_from_the_low_and_high_fills_of_the_latest_hours()
    {
        var market = new MarketSnapshot(new[]
        {
            Pair(Hour - 7200, Chaos, Divine, 5000, 100, 49, 51), // older than the 2 recent hours: ignored
            Pair(Hour - 3600, Chaos, Divine, 900, 100, 8, 10),
            Pair(Hour, Chaos, Divine, 1800, 200, 8.5, 9.5),
        }, new MarketSettings { RecentHours = 2 }).League(League);

        var quote = market.Quote(Divine, Chaos).Fair!;
        Assert.Null(quote.Via);
        Assert.Equal(9.0, quote.Average, 6);                     // 2700 Chaos / 300 Divine
        Assert.Equal((8 * 100 + 8.5 * 200) / 300, quote.Sell, 6); // lowest fills, weighted by volume
        Assert.Equal((10 * 100 + 9.5 * 200) / 300, quote.Buy, 6);
        Assert.Equal(300, quote.Volume);
        Assert.Equal(Hour, quote.LastHour);
    }

    [Fact]
    public void Mistake_fills_far_from_the_average_count_as_the_average()
    {
        // someone sold 1 Divine for 1 Exalted: the "highest Exalted per Divine" is fine, the lowest is a mistake
        var market = Market(Pair(Hour, Divine, Exalted, 10, 4000, 1.0 / 420, 1.0 / 1));
        var quote = market.Quote(Divine, Exalted).Fair!;
        Assert.Equal(400, quote.Average, 6);
        Assert.Equal(400, quote.Sell, 6);
        Assert.Equal(420, quote.Buy, 6);
    }

    [Fact]
    public void Items_without_a_direct_pair_are_priced_through_a_core_currency()
    {
        var market = Market(
            Pair(Hour, Omen, Divine, 100, 50, 1.9, 2.1),       // 1 Omen = 0.5 Divine
            Pair(Hour, Chaos, Divine, 1000, 100, 9.5, 10.5));  // 1 Divine = 10 Chaos
        var routes = market.Quote(Omen, Chaos);

        var quote = Assert.Single(routes.Routes);
        Assert.Equal(Divine, quote.Via);
        Assert.Equal(5, quote.Average, 6);
        Assert.Equal(1 / 2.1 * 9.5, quote.Sell, 6);
        Assert.Equal(1 / 1.9 * 10.5, quote.Buy, 6);
    }

    [Fact]
    public void A_cheaper_indirect_route_is_reported()
    {
        var market = Market(
            Pair(Hour, Chaos, Divine, 10000, 1000, 9.5, 10.5),   // buy 1 Divine directly: 10.5 Chaos
            Pair(Hour, Divine, Exalted, 10, 4000, 1.0 / 400, 1.0 / 400),
            Pair(Hour, Chaos, Exalted, 100, 4500, 1.0 / 45, 1.0 / 45)); // 400 Exalted = 8.9 Chaos
        var routes = market.Quote(Divine, Chaos);

        Assert.Null(routes.Fair!.Via);
        var cheaper = routes.BetterBuyRoute;
        Assert.NotNull(cheaper);
        Assert.Equal(Exalted, cheaper.Via);
        Assert.Equal(400.0 / 45, cheaper.Buy, 6);
        Assert.Null(routes.BetterSellRoute);
    }

    [Fact]
    public void Rows_have_an_hourly_series_change_and_volume()
    {
        var hours = Enumerable.Range(0, 6).Select(i => Hour + i * 3600L).ToList();
        var market = Market(hours.Select((h, i) => Pair(h, Chaos, Divine, 1000 + i * 100, 100, 9, 12)).ToArray());
        var row = market.Row(new ExchangeItem(Divine, "Divine Orb", null, ExchangeCategories.Currency, true), Chaos)!;

        Assert.Equal(hours.Count, row.Series.Count);
        Assert.Equal(10, row.Series[0]!.Value, 6);
        Assert.Equal(15, row.Series[^1]!.Value, 6);
        Assert.Equal((13 + 14 + 15) / 33.0 - 1, row.Change!.Value, 6); // last 3 hours against the first 3
        Assert.Equal(600, row.Volume);
        Assert.Null(market.Row(new ExchangeItem(Chaos, "Chaos Orb", null, ExchangeCategories.Currency, true), Chaos));
    }

    [DataFact]
    public void Exchange_items_resolve_names_and_crafting_categories()
    {
        var catalog = new ExchangeCatalog(TestData.Data!);
        var divine = catalog.Item(Divine);
        Assert.Equal(("Divine Orb", ExchangeCategories.Currency, true), (divine.Name, divine.Category, divine.IsCraftingItem));
        Assert.Equal(ExchangeCategories.Omens, catalog.Item(Omen).Category);
        Assert.Equal("Currency Something New", catalog.Item("Metadata/Items/Currency/CurrencySomethingNew").Name);
    }
}
