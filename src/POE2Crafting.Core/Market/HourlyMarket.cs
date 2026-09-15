namespace POE2Crafting.Core.Market;

/// <summary>
/// Trades of one item pair in one league during one hour (unix time of the hour's start). Ratios are fills as "amount of A per amount of B";
/// stock = the highest amount of each item offered in the order book during the hour.
/// </summary>
public sealed record HourlyMarket(long Hour, string League, string A, string B, long VolumeA, long VolumeB,
    double LowestRatio, double HighestRatio, long StockA, long StockB)
{
    public bool Has(string item) => A == item || B == item;

    public string Other(string item) => A == item ? B : A;

    public long VolumeOf(string item) => A == item ? VolumeA : VolumeB;

    public long StockOf(string item) => A == item ? StockA : StockB;

    /// <summary>What <paramref name="item"/> sold for in the other item this hour; null when nothing was traded.</summary>
    public PairPrice? PriceOf(string item)
    {
        if (VolumeA <= 0 || VolumeB <= 0) return null;
        bool isA = A == item;
        double average = isA ? (double)VolumeB / VolumeA : (double)VolumeA / VolumeB;
        // price of A in B is the inverse of the "A per B" ratio: its lowest price is the highest ratio
        double low = isA ? Inverse(HighestRatio) : LowestRatio, high = isA ? Inverse(LowestRatio) : HighestRatio;
        return new PairPrice(Hour, average, low, high, VolumeOf(item));
    }

    private static double Inverse(double ratio) => ratio > 0 ? 1 / ratio : 0;
}

/// <summary>One hour of fills of an item in a counter item: volume-weighted average, lowest and highest fill (counter per item), items traded.</summary>
public sealed record PairPrice(long Hour, double Average, double Low, double High, long Volume);
