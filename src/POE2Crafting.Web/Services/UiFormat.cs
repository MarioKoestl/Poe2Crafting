using System.Globalization;
using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine.Planning;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Web.Services;

/// <summary>Display formatting shared by all components: chances, attempts, CSS classes of steps and rarities, plurals.</summary>
public static class UiFormat
{
    /// <summary>A chance with as many decimals as it needs to stay readable (100%, 12.5%, 0.123%, 0.000012%).</summary>
    public static string Probability(double p) => p switch
    {
        >= 0.999 => "100%",
        0 => "0%",
        >= 0.01 => Percent(p, "0.0"),
        >= 0.0001 => Percent(p, "0.000"),
        _ => Percent(p, "0.000000"),
    };

    /// <summary>A chance in a distribution list (two decimals).</summary>
    public static string Share(double p) => Percent(p, "0.00");

    /// <summary>A CSS length in percent ("12.5%"), always with a dot.</summary>
    public static string CssPercent(double p) => Percent(p, "0.#");

    /// <summary>An SVG coordinate ("12.5"), always with a dot.</summary>
    public static string Coordinate(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Percent(double p, string format) => (p * 100).ToString(format, CultureInfo.InvariantCulture) + "%";

    public static string Attempts(double attempts) => double.IsPositiveInfinity(attempts) ? "∞" : Compact(attempts);

    /// <summary>A count shortened for tables: 7.5, 850, 12.3K, 4.4M.</summary>
    public static string Compact(double value)
    {
        if (value < 10) return value.ToString("0.#", CultureInfo.InvariantCulture);
        if (value < 1000) return value.ToString("N0", CultureInfo.InvariantCulture);
        if (value < 1_000_000) return (value / 1000).ToString("F1", CultureInfo.InvariantCulture) + "K";
        return (value / 1_000_000).ToString("F1", CultureInfo.InvariantCulture) + "M";
    }

    /// <summary>An exchange price with three significant digits for small values: 1,234 · 45.1 · 9.27 · 0.108 · 0.00254.</summary>
    public static string Price(double value)
    {
        if (value >= 1000) return value.ToString("N0", CultureInfo.InvariantCulture);
        if (value >= 10) return value.ToString("0.0", CultureInfo.InvariantCulture);
        if (value >= 1 || value <= 0) return value.ToString("0.00", CultureInfo.InvariantCulture);
        int decimals = Math.Min(8, 2 - (int)Math.Floor(Math.Log10(value)));
        return value.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    /// <summary>A base item stat as the Bases page shows it ("54", "1.45", "25%", "3 s", "58–134"); empty when the base doesn't have it.</summary>
    public static string StatValue(BaseStat stat, BaseItem baseItem)
    {
        if (stat.Text?.Invoke(baseItem) is { } text) return text;
        if (stat.Value(baseItem) is not { } value) return "";
        return stat.Format switch
        {
            BaseStatFormat.Decimal => value.ToString("0.##", CultureInfo.InvariantCulture),
            BaseStatFormat.Percent => value.ToString("0.#", CultureInfo.InvariantCulture) + "%",
            BaseStatFormat.Seconds => value.ToString("0.##", CultureInfo.InvariantCulture) + " s",
            _ => value.ToString("0", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>A change with sign: "+4.2%", "−12.0%".</summary>
    public static string SignedPercent(double p) => (p > 0 ? "+" : p < 0 ? "−" : "") + Percent(Math.Abs(p), "0.0");

    /// <summary>"trend-up" / "trend-down" for a price change (none when unchanged).</summary>
    public static string TrendClass(double change) => change > 0 ? "trend-up" : change < 0 ? "trend-down" : "";

    /// <summary>Local clock time of a unix time (exchange hours): "14:00".</summary>
    public static string ClockTime(long unixSeconds) => DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string ProbabilityClass(double p) => p switch
    {
        >= 0.5 => "prob-high",
        >= 0.1 => "prob-mid",
        >= 0.01 => "prob-low",
        _ => "prob-very-low",
    };

    public static string StepClass(CraftStep step) => step.Type switch
    {
        CraftStepType.Brick => "step-brick",
        CraftStepType.Checkpoint => "step-checkpoint",
        _ => "",
    };

    /// <summary>"rarity-rare"; no rarity (empty slot) gives "rarity-none".</summary>
    public static string RarityCss(Rarity? rarity) => "rarity-" + (rarity?.ToString().ToLowerInvariant() ?? "none");

    /// <summary>"1 step", "3 steps".</summary>
    public static string Plural(int count, string singular, string? plural = null) => $"{count} {(count == 1 ? singular : plural ?? singular + "s")}";
}
