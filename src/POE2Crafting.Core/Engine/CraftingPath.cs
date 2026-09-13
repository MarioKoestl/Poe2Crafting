using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine;

/// <summary>User-defined target item specification for the crafting planner.</summary>
public sealed class TargetItemSpec
{
    public string BaseName { get; set; } = "";
    public string ItemClass { get; set; } = "";
    public int ItemLevel { get; set; } = 82;
    public Rarity TargetRarity { get; set; } = Rarity.Rare;
    public List<TargetMod> TargetMods { get; set; } = new();
}

/// <summary>One desired modifier in the target item.</summary>
public sealed class TargetMod
{
    public string Family { get; set; } = "";
    public int? Tier { get; set; }
    public AffixType AffixType { get; set; }
    /// <summary>Mod category: "normal", "breach_otherworldly", "desecrated", etc.</summary>
    public string Category { get; set; } = ModCategories.Normal;

    /// <summary>Per-page display tier (T1 = best on this base). Set by the UI.</summary>
    public int? DisplayTier { get; set; }

    /// <summary>Resolved ModDef from family + tier lookup.</summary>
    public ModDef? ResolvedMod { get; set; }

    /// <summary>True (default): any tier at least as good as the target counts as a hit. False: only the exact tier.</summary>
    public bool AllowBetterTiers { get; set; } = true;

    /// <summary>Optional minimum rolled value per range of the mod (null entries = any value).</summary>
    public List<double?>? MinValues { get; set; }

    /// <summary>Whether a present mod's rolled values reach the minimum values.</summary>
    public bool ValuesSatisfiedBy(ItemMod mod) =>
        MinValues == null || MinValues.Select((min, i) => min == null || i < mod.Values.Count && mod.Values[i] >= min.Value).All(ok => ok);

    /// <summary>Whether a rolled/present modifier satisfies this target (same family and affix type, and the tier is good enough).</summary>
    public bool Matches(ModDef? mod)
    {
        if (mod == null || mod.Family != Family || mod.AffixType != AffixType) return false;
        if (ResolvedMod == null) return true;
        if (!AllowBetterTiers) return mod.Id == ResolvedMod.Id;
        // one family can hold several stats (e.g. Fire/Physical spell skill levels); tiers of one stat differ by level, higher = better
        return ModText.StatSignature(mod.Text) == ModText.StatSignature(ResolvedMod.Text) && mod.Level >= ResolvedMod.Level;
    }

    public string DisplayName => ResolvedMod != null
        ? $"T{DisplayTier ?? ResolvedMod.Tier} {ResolvedMod.DisplayName}"
        : $"{Family} (T{DisplayTier ?? Tier})";
}

/// <summary>Strategies found for a target, plus reasons when (part of) the target cannot be reached.</summary>
public sealed class PlanResult
{
    public List<CraftingStrategy> Strategies { get; } = new();
    public List<string> Problems { get; } = new();
}

/// <summary>A computed crafting strategy with steps, probabilities, and brick risks.</summary>
public sealed class CraftingStrategy
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<CraftStep> Steps { get; set; } = new();
    public double OverallProbability { get; set; }
    public double ExpectedAttempts => OverallProbability > 0 ? 1.0 / OverallProbability : double.PositiveInfinity;
    public bool HasBrickRisk { get; set; }
    /// <summary>Label of the flowchart's start node.</summary>
    public string StartLabel { get; set; } = "Current Item";
}

/// <summary>One step in a crafting strategy.</summary>
public sealed class CraftStep
{
    public string Id { get; set; } = "";
    public string CurrencyName { get; set; } = "";
    public string Description { get; set; } = "";
    public double SuccessProbability { get; set; }
    public double? BrickProbability { get; set; }
    /// <summary>If this step fails/bricks, where to restart.</summary>
    public string? RestartFromStepId { get; set; }
    public string? RestartLabel { get; set; }
    public CraftStepType Type { get; set; } = CraftStepType.Normal;
    public List<string> Notes { get; set; } = new();
    /// <summary>The item after this step succeeded.</summary>
    public Item? Result { get; set; }
    /// <summary>Expected attempts for this step (1/probability).</summary>
    public double ExpectedAttempts => SuccessProbability > 0 ? 1.0 / SuccessProbability : double.PositiveInfinity;
}

public enum CraftStepType
{
    Normal,
    Brick,
    Checkpoint,
}
