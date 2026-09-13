using System.Globalization;
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

    private static string Percent(double p, string format) => (p * 100).ToString(format, CultureInfo.InvariantCulture) + "%";

    public static string Attempts(double attempts)
    {
        if (double.IsPositiveInfinity(attempts)) return "∞";
        if (attempts < 10) return attempts.ToString("0.#", CultureInfo.InvariantCulture);
        if (attempts < 1000) return attempts.ToString("N0", CultureInfo.InvariantCulture);
        if (attempts < 1_000_000) return (attempts / 1000).ToString("F1", CultureInfo.InvariantCulture) + "K";
        return (attempts / 1_000_000).ToString("F1", CultureInfo.InvariantCulture) + "M";
    }

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
