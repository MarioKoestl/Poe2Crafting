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

    /// <summary>Creates a virtual Normal item matching this spec for probability calculations.</summary>
    public Item ToBaseItem(GameData data)
    {
        var baseItem = data.FindBase(BaseName);
        return new Item
        {
            BaseName = BaseName,
            ItemClass = ItemClass,
            Rarity = Rarity.Normal,
            ItemLevel = ItemLevel,
            Base = baseItem,
            Sockets = baseItem?.SocketLimit ?? 0,
        };
    }

    public int PrefixCount => TargetMods.Count(m => m.AffixType == AffixType.Prefix);
    public int SuffixCount => TargetMods.Count(m => m.AffixType == AffixType.Suffix);
    public int TotalMods => TargetMods.Count;
}

/// <summary>One desired modifier in the target item.</summary>
public sealed class TargetMod
{
    public string Family { get; set; } = "";
    public int? Tier { get; set; }
    public AffixType AffixType { get; set; }
    /// <summary>Mod category: "normal", "breach_otherworldly", "desecrated", etc.</summary>
    public string Category { get; set; } = "normal";

    /// <summary>Per-page display tier (T1 = best on this base). Set by the UI.</summary>
    public int? DisplayTier { get; set; }

    /// <summary>Resolved ModDef from family + tier lookup.</summary>
    public ModDef? ResolvedMod { get; set; }

    /// <summary>True (default): any tier at least as good as the target counts as a hit. False: only the exact tier.</summary>
    public bool AllowBetterTiers { get; set; } = true;

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
        ? $"T{DisplayTier ?? ResolvedMod.Tier} {ResolvedMod.Name}"
        : $"{Family} (T{DisplayTier ?? Tier})";
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
    public List<string> Warnings { get; set; } = new();
    /// <summary>Label of the flowchart's start node.</summary>
    public string StartLabel { get; set; } = "Normal Base Item";
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
    /// <summary>Currency variants (Normal/Greater/Perfect) with their probabilities for this step.</summary>
    public List<CurrencyVariant> Variants { get; set; } = new();
    /// <summary>Expected attempts for this step (1/probability).</summary>
    public double ExpectedAttempts => SuccessProbability > 0 ? 1.0 / SuccessProbability : double.PositiveInfinity;
}

/// <summary>A currency variant (Normal/Greater/Perfect) with its probability impact.</summary>
public sealed class CurrencyVariant
{
    public string Name { get; set; } = "";
    public int MinModLevel { get; set; }
    public double Probability { get; set; }
    public bool IsRecommended { get; set; }
}

public enum CraftStepType
{
    Normal,
    Brick,
    Checkpoint,
    Decision
}
