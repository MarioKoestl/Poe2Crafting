using System.Globalization;
using System.Text.RegularExpressions;

namespace POE2Crafting.Core.Items;

/// <summary>
/// "(n)% increased Explicit Fire Modifier magnitudes" (Destruction modifiers, Sovereign Alloy) and "(n)% increased Desecrated Modifier magnitudes":
/// raise the values of the item's other modifiers of that type. "Explicit X" names a mod tag (Fire → fire, Elemental Damage → elemental_damage);
/// magnitude modifiers don't raise each other (ASSUMPTION, see runeModsNote in config.json).
/// </summary>
public static class ModMagnitudes
{
    /// <summary>One magnitude line of an item: the percent and whom it applies to.</summary>
    public sealed record Magnitude(double Percent, string? Tag, bool Desecrated);

    private static readonly Regex Line = new(@"^(?<pct>\d+(?:\.\d+)?)% increased (?:(?<desecrated>Desecrated)|Explicit (?<tag>[A-Za-z ]+?)) Modifier magnitudes$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>The magnitude line's effect, or null when the text is not one.</summary>
    public static Magnitude? Parse(string text)
    {
        if (!text.Contains("Modifier magnitudes", StringComparison.OrdinalIgnoreCase) || Line.Match(text.Trim()) is not { Success: true } m) return null;
        return new Magnitude(double.Parse(m.Groups["pct"].Value, CultureInfo.InvariantCulture),
            m.Groups["tag"].Success ? m.Groups["tag"].Value.Trim().ToLowerInvariant().Replace(' ', '_') : null,
            m.Groups["desecrated"].Success);
    }

    /// <summary>The magnitude lines among the item's affixes.</summary>
    public static IEnumerable<Magnitude> Of(Item item) => item.Affixes.Select(m => Parse(m.DisplayText())).OfType<Magnitude>();

    /// <summary>Total percent the item's magnitude lines add to this modifier (0 for implicits, enchantments and magnitude lines themselves).</summary>
    public static double BonusFor(Item item, ItemMod mod)
    {
        if (!mod.IsAffix || mod.Unrevealed || Parse(mod.DisplayText()) != null) return 0;
        return Of(item).Where(m => m.Desecrated ? mod.Kind == ModKind.Desecrated : m.Tag != null && mod.Def?.ModTags.Contains(m.Tag) == true).Sum(m => m.Percent);
    }
}
