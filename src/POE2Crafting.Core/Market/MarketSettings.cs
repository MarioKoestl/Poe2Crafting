namespace POE2Crafting.Core.Market;

/// <summary>Settings of the market data (appsettings.json section "Market"). The price rules are estimates, see <see cref="LeagueMarket"/>.</summary>
public sealed class MarketSettings
{
    /// <summary>Hours of history loaded and analysed (trend, sparkline, volume).</summary>
    public int HistoryHours { get; set; } = 24;
    /// <summary>The latest hours in which a pair traded that make up its current price.</summary>
    public int RecentHours { get; set; } = 3;
    /// <summary>
    /// ASSUMPTION: an hour's lowest/highest fill further than this factor from the hour's average is a mistake order (e.g. 1 Divine for 1 Exalted)
    /// and is replaced by the average.
    /// </summary>
    public double OutlierFactor { get; set; } = 1.5;
    /// <summary>How often the service checks whether the next hourly digest is published.</summary>
    public int PollMinutes { get; set; } = 5;
    /// <summary>League shown first; null = the league with the most traded pairs.</summary>
    public string? League { get; set; }
}
