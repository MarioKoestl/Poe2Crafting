using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Data;

/// <summary>
/// A curated crafting sequence (data/guides.json), e.g. the "+4 Melee Skills Amulet": a starting item and steps, each with the currency/omens,
/// the outcome that counts as a hit and an explanation of why the step is done. <see cref="Engine.GuideRunner"/> plays it through the engine,
/// so chances and the item after each step come from the simulator's rules.
/// </summary>
public sealed class CraftingGuide
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Summary { get; init; } = "";
    public List<string> Tags { get; init; } = new();
    /// <summary>Where the technique comes from (video/article title) and a link.</summary>
    public string? Source { get; init; }
    public string? SourceUrl { get; init; }
    /// <summary>The ideas that make the technique work, shown before the steps.</summary>
    public List<string> KeyIdeas { get; init; } = new();
    public GuideItem Start { get; init; } = new();
    public List<GuideStep> Steps { get; init; } = new();
    /// <summary>What to do after the last step (branches the guide doesn't play through).</summary>
    public List<string> NextSteps { get; init; } = new();
}

/// <summary>The starting item of a guide: base, rarity and mods found by text on that base.</summary>
public sealed class GuideItem
{
    public string Base { get; init; } = "";
    public Rarity Rarity { get; init; } = Rarity.Rare;
    public int ItemLevel { get; init; } = Item.DefaultItemLevel;
    public List<GuideMod> Mods { get; init; } = new();
}

public sealed class GuideMod
{
    /// <summary>Part of the mod text ("Level of all Melee Skills"); the highest tier with this text on the base is used.</summary>
    public string Text { get; init; } = "";
    /// <summary>Prefix or Suffix when the text exists on both sides.</summary>
    public AffixType? Affix { get; init; }
    public bool Fractured { get; init; }
}

public sealed class GuideStep
{
    public string Title { get; init; } = "";
    public string Currency { get; init; } = "";
    public List<string> Omens { get; init; } = new();
    /// <summary>How often the currency is applied (catalysts); every use must be possible.</summary>
    public int Uses { get; init; } = 1;
    /// <summary>Why this step is done and what happens.</summary>
    public string Explanation { get; init; } = "";
    public GuideHit Hit { get; init; } = new();
    /// <summary>What to do when the step misses.</summary>
    public string? OnMiss { get; init; }
}

/// <summary>
/// The outcome of a step that counts as a hit. Filters match parts of mod texts (case-insensitive); "*" = any.
/// <see cref="Select"/> picks from the preview's removal list — the removed mod, or for a Fracturing Orb the fractured one.
/// </summary>
public sealed class GuideHit
{
    public string? Select { get; init; }
    public string? Add { get; init; }
    /// <summary>The added mod must have this mod tag (e.g. "defences" for Omen of Catalysing Exaltation).</summary>
    public string? AddTag { get; init; }
    /// <summary>A named outcome must contain this text (e.g. "Unrevealed Prefix" for bones).</summary>
    public string? Outcome { get; init; }
}
