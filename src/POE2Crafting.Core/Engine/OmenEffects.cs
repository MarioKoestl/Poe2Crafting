using POE2Crafting.Core.Data;

namespace POE2Crafting.Core.Engine;

/// <summary>Omen effect ids used in data/omens.json, and the rules about combining omens.</summary>
public static class OmenEffects
{
    public const string AddPrefixOnly = "add_prefix_only", AddSuffixOnly = "add_suffix_only";
    public const string RemovePrefixOnly = "remove_prefix_only", RemoveSuffixOnly = "remove_suffix_only";
    public const string MaxPrefixes = "max_prefixes", MaxSuffixes = "max_suffixes";
    public const string AddTwo = "add_two", RemoveTwo = "remove_two";
    public const string Homogenising = "homogenising", Catalysing = "catalysing";
    public const string RemoveLowestLevel = "remove_lowest_level", RemoveDesecratedOnly = "remove_desecrated_only";
    public const string ImplicitsOnly = "implicits_only", Sanctify = "sanctify";
    public const string NoDestroy = "no_destroy", RandomUniqueOfClass = "random_unique_of_class";
    public const string GuaranteeUlaman = "guarantee_ulaman", GuaranteeAmanamu = "guarantee_amanamu", GuaranteeKurgal = "guarantee_kurgal";
    public const string Putrefaction = "putrefaction", RerollRevealOnce = "reroll_reveal_once";
    public const string ForceChange = "force_change";

    /// <summary>No active omen.</summary>
    public static readonly IReadOnlyList<OmenDef> None = Array.Empty<OmenDef>();

    /// <summary>The affix type an omen restricts the next addition/removal to (Sinistral = prefix, Dextral = suffix).</summary>
    public static AffixType? RestrictedType(OmenDef? omen) => omen?.Effect switch
    {
        AddPrefixOnly or RemovePrefixOnly or MaxPrefixes => AffixType.Prefix,
        AddSuffixOnly or RemoveSuffixOnly or MaxSuffixes => AffixType.Suffix,
        _ => null,
    };

    /// <summary>The affix type the active omens restrict to (they cannot contradict each other, see <see cref="Conflict"/>).</summary>
    public static AffixType? RestrictedType(IEnumerable<OmenDef> omens) => omens.Select(RestrictedType).FirstOrDefault(t => t != null);

    /// <summary>The active omen that restricts the affix type, or null.</summary>
    public static OmenDef? RestrictingOmen(IEnumerable<OmenDef> omens) => omens.FirstOrDefault(o => RestrictedType(o) != null);

    public static bool Has(this IEnumerable<OmenDef> omens, string effect) => omens.Any(o => o.Effect == effect);

    public static OmenDef? WithEffect(this IEnumerable<OmenDef> omens, string effect) => omens.FirstOrDefault(o => o.Effect == effect);

    /// <summary>Effects of which at most one can be active at once (one guaranteed boss).</summary>
    private static readonly string[][] ExclusiveGroups = { new[] { GuaranteeUlaman, GuaranteeAmanamu, GuaranteeKurgal } };

    /// <summary>
    /// Why the omens can't be active together, or null. ASSUMPTION (not verified in game): the same omen twice, omens restricting to
    /// different affix types (Sinistral + Dextral), two boss omens, and Putrefaction with a prefix/suffix restriction contradict each other.
    /// </summary>
    public static string? Conflict(IReadOnlyList<OmenDef> omens)
    {
        if (omens.GroupBy(o => o.Name).FirstOrDefault(g => g.Count() > 1) is { } twice)
            return $"{twice.Key} is selected twice.";
        var restricting = omens.Where(o => RestrictedType(o) != null).ToList();
        if (restricting.Select(RestrictedType).Distinct().Count() > 1)
            return $"{restricting[0].Name} and {restricting.First(o => RestrictedType(o) != RestrictedType(restricting[0])).Name} contradict each other (prefix vs. suffix).";
        foreach (var group in ExclusiveGroups)
            if (omens.Where(o => group.Contains(o.Effect)).ToList() is { Count: > 1 } both)
                return $"{both[0].Name} and {both[1].Name} cannot be combined (only one guaranteed modifier type).";
        if (omens.Has(Putrefaction) && restricting.Count > 0)
            return $"{omens.WithEffect(Putrefaction)!.Name} replaces all modifiers and cannot be combined with {restricting[0].Name}.";
        return null;
    }
}
