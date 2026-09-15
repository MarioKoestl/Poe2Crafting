using System.Text.Json;
using System.Text.Json.Serialization;

namespace POE2Crafting.Core.Market;

/// <summary>
/// One hourly digest of GGG's public Currency Exchange API (GET https://web.poecdn.com/api/currency-exchange/poe2/&lt;hour&gt;):
/// the trades of every item pair in every league during the hour that starts at the requested unix time.
/// While that hour is not over yet the API answers without markets and <see cref="NextChangeId"/> equals the requested hour.
/// </summary>
public sealed class ExchangeDigest
{
    [JsonPropertyName("next_change_id")] public long NextChangeId { get; set; }
    [JsonPropertyName("markets")] public List<ExchangeDigestMarket> Markets { get; set; } = new();

    public static ExchangeDigest Parse(string json) => JsonSerializer.Deserialize<ExchangeDigest>(json) ?? new ExchangeDigest();

    /// <summary>True when the requested hour is complete (the API names the following hour).</summary>
    public bool IsComplete(long hour) => NextChangeId > hour;

    /// <summary>The traded pairs of this digest as compact hourly records; ids and league names are shared through <paramref name="strings"/>.</summary>
    public List<HourlyMarket> ToMarkets(long hour, StringPool strings) =>
        Markets.Where(m => m.MarketPair.Count == 2).Select(m => m.ToHourly(hour, strings)).ToList();
}

/// <summary>One item pair of a digest; every dictionary is keyed by the two metadata ids of <see cref="MarketPair"/>.</summary>
public sealed class ExchangeDigestMarket
{
    [JsonPropertyName("league")] public string League { get; set; } = "";
    [JsonPropertyName("market_pair")] public List<string> MarketPair { get; set; } = new();
    [JsonPropertyName("volume_traded")] public Dictionary<string, long> VolumeTraded { get; set; } = new();
    [JsonPropertyName("highest_stock")] public Dictionary<string, long> HighestStock { get; set; } = new();
    /// <summary>Fill ratio as amounts of both items ({A: 9, B: 1} = 9 A for 1 B); lowest/highest compare A per B.</summary>
    [JsonPropertyName("lowest_ratio")] public Dictionary<string, double> LowestRatio { get; set; } = new();
    [JsonPropertyName("highest_ratio")] public Dictionary<string, double> HighestRatio { get; set; } = new();

    internal HourlyMarket ToHourly(long hour, StringPool strings)
    {
        string a = MarketPair[0], b = MarketPair[1];
        long Get(Dictionary<string, long> d, string key) => d.GetValueOrDefault(key);
        double Ratio(Dictionary<string, double> d) => d.GetValueOrDefault(b) is var amountB and > 0 ? d.GetValueOrDefault(a) / amountB : 0;
        double low = Ratio(LowestRatio), high = Ratio(HighestRatio);
        return new HourlyMarket(hour, strings.Get(League), strings.Get(a), strings.Get(b),
            Get(VolumeTraded, a), Get(VolumeTraded, b), Math.Min(low, high), Math.Max(low, high), Get(HighestStock, a), Get(HighestStock, b));
    }
}

/// <summary>Shares repeated strings (metadata ids, league names) of many hourly records.</summary>
public sealed class StringPool
{
    private readonly Dictionary<string, string> _strings = new();
    public string Get(string value) => _strings.TryGetValue(value, out var shared) ? shared : _strings[value] = value;
}
