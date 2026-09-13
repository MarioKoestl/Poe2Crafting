using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine;

/// <summary>
/// What the user wants to apply: a currency (essences/alloys are synthetic currencies with Op "essence"), modified by the omens
/// active in the inventory (any number, as long as they don't contradict each other — see <see cref="OmenEffects.Conflict"/>).
/// </summary>
public sealed class CraftAction
{
    public CurrencyDef Currency { get; init; } = null!;
    public IReadOnlyList<OmenDef> Omens { get; init; } = Array.Empty<OmenDef>();
    public string DisplayName => string.Join(" + ", Omens.Select(o => o.Name).Prepend(Currency.Name));

    public static CraftAction Of(CurrencyDef currency, params OmenDef?[] omens) =>
        new() { Currency = currency, Omens = omens.OfType<OmenDef>().ToList() };
}

/// <summary>Omen effect ids used in data/omens.json.</summary>
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

/// <summary>Result of checking whether an action can be applied to an item.</summary>
public sealed class Applicability
{
    public bool Ok { get; init; }
    public string Reason { get; init; } = "";
    public List<string> Notes { get; init; } = new();
    public static Applicability No(string reason) => new() { Ok = false, Reason = reason };
}

public sealed class RemovalCandidate
{
    public int Index { get; init; }
    public ItemMod Mod { get; init; } = null!;
    public double Probability { get; set; }

    /// <summary>Give every candidate the same probability (removals are uniform).</summary>
    public static List<RemovalCandidate> Uniform(List<RemovalCandidate> list)
    {
        foreach (var r in list) r.Probability = 1.0 / list.Count;
        return list;
    }
}

/// <summary>Everything the UI needs to show before a currency is applied.</summary>
public sealed class StepPreview
{
    public Applicability Applicability { get; set; } = Applicability.No("");
    public int RemoveCount { get; init; }
    public int AddCount { get; init; }
    /// <summary>Distribution of the mod that gets removed (first removal).</summary>
    public List<RemovalCandidate> Removals { get; init; } = new();
    /// <summary>Marginal distribution of the (first) added mod.</summary>
    public List<ModCandidate> Additions { get; init; } = new();
    /// <summary>False when the additions are only informational (e.g. what a desecrated mod could reveal into) and cannot be chosen.</summary>
    public bool AdditionsChoosable { get; init; } = true;
    public string? AdditionLabel { get; init; }
    /// <summary>For special currencies (Orb of Chance, Desecration ...): named outcomes with probabilities.</summary>
    public Dictionary<string, double> SpecialOutcomes { get; init; } = new();
    public List<string> Notes { get; init; } = new();
    public double PrefixProbability { get; init; }
    public double SuffixProbability { get; init; }
    /// <summary>Removal and addition are separate random events (Chaos Orb): a manual choice picks the removal first, then the addition.</summary>
    public bool TwoStepChoice { get; init; }
    /// <summary>Heading for the Removals list (e.g. "Fracture Target" when the list is not a removal).</summary>
    public string? RemovalLabel { get; init; }
}

/// <summary>Manual selection of an outcome instead of rolling.</summary>
public sealed class ManualChoice
{
    /// <summary>Indices (into Item.Mods) of the mods to remove, in order.</summary>
    public List<int> RemoveIndices { get; init; } = new();
    /// <summary>Ids of the mods to add, in order.</summary>
    public List<string> AddModIds { get; init; } = new();
    /// <summary>Optional rolled values per added mod (null = roll randomly within the tier).</summary>
    public List<List<double>?> Values { get; init; } = new();
    public string? SpecialOutcome { get; init; }
    /// <summary>For Divine Orb: values per existing mod index (null entries keep random).</summary>
    public Dictionary<int, List<double>>? Rerolls { get; init; }
}

public sealed class CraftResult
{
    public bool Applied { get; init; }
    public Item Item { get; init; } = null!;
    public string Summary { get; init; } = "";
    public List<string> Details { get; init; } = new();
    public bool Destroyed { get; init; }
}

/// <summary>An action against the item state before it is applied (check / preview).</summary>
public class CraftContext
{
    public Item Item { get; init; } = null!;
    public CurrencyDef Currency { get; init; } = null!;
    public IReadOnlyList<OmenDef> Omens { get; init; } = Array.Empty<OmenDef>();
    /// <summary>Assumptions and hints collected while checking.</summary>
    public List<string> Notes { get; } = new();

    public int MinModLevel => Currency.MinModLevel ?? 0;
    public bool OmenIs(string effect) => Omens.Has(effect);
    public AffixType? RestrictedType => OmenEffects.RestrictedType(Omens);
}

/// <summary>An action being applied: <see cref="Result"/> is a clone of <see cref="CraftContext.Item"/> that operations modify.</summary>
public sealed class ExecuteContext : CraftContext
{
    public Item Result { get; init; } = null!;
    public Rng Rng { get; init; } = null!;
    public ManualChoice? Choice { get; init; }
    public List<string> Details { get; } = new();
    public bool Destroyed { get; set; }
}
