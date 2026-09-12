using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine;

/// <summary>What the user wants to apply: a currency, optionally modified by an omen (essences/alloys come in stage 2).</summary>
public sealed class CraftAction
{
    public CurrencyDef Currency { get; init; } = null!;
    public OmenDef? Omen { get; init; }
    public EssenceDef? Essence { get; init; }
    public string DisplayName => Essence?.Name ?? Currency.Name + (Omen != null ? $" + {Omen.Name}" : "");
}

/// <summary>Result of checking whether an action can be applied to an item.</summary>
public sealed class Applicability
{
    public bool Ok { get; init; }
    public string Reason { get; init; } = "";
    public List<string> Notes { get; init; } = new();
    public static Applicability Yes(params string[] notes) => new() { Ok = true, Reason = "", Notes = notes.ToList() };
    public static Applicability No(string reason) => new() { Ok = false, Reason = reason };
}

public sealed class RemovalCandidate
{
    public int Index { get; init; }
    public ItemMod Mod { get; init; } = null!;
    public double Probability { get; set; }
}

/// <summary>Everything the UI needs to show before a currency is applied.</summary>
public sealed class StepPreview
{
    public Applicability Applicability { get; init; } = Applicability.No("");
    public int RemoveCount { get; init; }
    public int AddCount { get; init; }
    /// <summary>Distribution of the mod that gets removed (first removal).</summary>
    public List<RemovalCandidate> Removals { get; init; } = new();
    /// <summary>Marginal distribution of the (first) added mod.</summary>
    public List<ModCandidate> Additions { get; init; } = new();
    /// <summary>For special currencies (Orb of Chance, Vaal Orb ...): named outcomes with probabilities.</summary>
    public Dictionary<string, double> SpecialOutcomes { get; init; } = new();
    public List<string> Notes { get; init; } = new();
    public double PrefixProbability { get; init; }
    public double SuffixProbability { get; init; }
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
