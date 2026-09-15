using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Planning;

/// <summary>The target item of the planner: rarity, wanted modifiers and finishing work. The base is always the current item's.</summary>
public sealed class TargetItemSpec
{
    public Rarity TargetRarity { get; set; } = Rarity.Rare;
    public List<TargetMod> TargetMods { get; set; } = new();

    /// <summary>Minimum quality (null = any).</summary>
    public int? MinQuality { get; set; }
    /// <summary>Catalyst quality type for jewellery, e.g. "Life" (null = any / normal quality).</summary>
    public string? QualityType { get; set; }
    /// <summary>Augments (runes, soul cores, idols) that should be socketed, one entry per socket.</summary>
    public List<AugmentTarget> Augments { get; set; } = new();
    /// <summary>Notable that should be instilled on the amulet (null = none).</summary>
    public string? InstillNotable { get; set; }
}

/// <summary>A wanted socketed augment; <see cref="EffectText"/> is resolved for the item's class by the path finder.</summary>
public sealed class AugmentTarget
{
    public string Name { get; set; } = "";
    public string? EffectText { get; set; }
}

/// <summary>One desired modifier in the target item.</summary>
public sealed class TargetMod
{
    public string Family { get; set; } = "";
    public int? Tier { get; set; }
    public AffixType AffixType { get; set; }
    /// <summary>Mod category: "normal", "breach_otherworldly", "desecrated", etc.</summary>
    public string Category { get; set; } = ModCategories.Normal;

    /// <summary>Per-base display tier (T1 = best on this base); the path finder recomputes it for the current item's base.</summary>
    public int? DisplayTier { get; set; }

    /// <summary>Resolved ModDef from family + tier lookup.</summary>
    public ModDef? ResolvedMod { get; set; }

    /// <summary>True (default): any tier at least as good as the target counts as a hit. False: only the exact tier.</summary>
    public bool AllowBetterTiers { get; set; } = true;

    /// <summary>An unrevealed desecrated modifier of <see cref="AffixType"/> is wanted (whatever it reveals into).</summary>
    public bool Unrevealed { get; set; }

    public static TargetMod UnrevealedOf(AffixType type) => new() { Unrevealed = true, AffixType = type, Category = ModCategories.Desecrated, AllowBetterTiers = false };

    /// <summary>Optional minimum value per <see cref="ModDef.StatRanges"/> entry of the mod (null entries = any value).</summary>
    public List<double?>? MinValues { get; set; }

    /// <summary>Whether a present mod's values on the item reach the minimum values (catalyst quality counts, e.g. +3 skills at 34% = +4).</summary>
    public bool ValuesSatisfiedBy(Item item, ItemMod mod)
    {
        if (MinValues == null) return true;
        var values = item.EffectiveValues(mod);
        return MinValues.Select((min, i) => min == null || i < values.Count && values[i] >= min.Value).All(ok => ok);
    }

    /// <summary>Whether a modifier on an item satisfies this target: an unrevealed one of the wanted type, or a known mod (see <see cref="Matches(ModDef?)"/>).</summary>
    public bool Matches(ItemMod mod) => Unrevealed ? mod.Unrevealed && mod.Affix == AffixType : !mod.Unrevealed && Matches(mod.Def);

    /// <summary>Whether a rolled/present modifier satisfies this target (same family and affix type, and the tier is good enough).</summary>
    public bool Matches(ModDef? mod)
    {
        if (Unrevealed || mod == null || mod.Family != Family || mod.AffixType != AffixType) return false;
        if (ResolvedMod == null) return true;
        if (!AllowBetterTiers) return mod.Id == ResolvedMod.Id;
        // one family can hold several stats (e.g. Fire/Physical spell skill levels); tiers of one stat differ by level, higher = better
        return mod.StatSignature == ResolvedMod.StatSignature && mod.Level >= ResolvedMod.Level;
    }

    public string DisplayName => Unrevealed ? $"Unrevealed Desecrated {AffixType}"
        : ResolvedMod != null
        ? $"T{DisplayTier ?? ResolvedMod.Tier} {ResolvedMod.DisplayName}"
        : $"{Family} (T{DisplayTier ?? Tier})";
}
