using System.Text.Json.Serialization;

namespace POE2Crafting.Core.Data;

/// <summary>An essence, alloy or (synthetic) liquid emotion. Loaded from data/essences.json and data/alloys.json.</summary>
public sealed class EssenceDef
{
    public string Name { get; init; } = "";
    public string? Slug { get; init; }
    /// <summary>Lesser, Normal, Greater, Perfect, Corrupted, Alloy or Liquid (<see cref="EssenceTiers"/>).</summary>
    public string? Tier { get; init; }
    public bool RemovesRandomModifier { get; init; }
    public List<string> RarityIn { get; init; } = new();
    public List<string> Description { get; init; } = new();
    /// <summary>Liquid emotions come from currencies.json and keep its stack size.</summary>
    [JsonIgnore] public string? StackSize { get; init; }

    /// <summary>Perfect, Corrupted, Alloy and Liquid variants remove a mod and add a "Crafted" modifier; Lesser/Normal/Greater add a regular explicit mod.</summary>
    [JsonIgnore] public bool AddsCraftedMod => Tier is EssenceTiers.Perfect or EssenceTiers.Corrupted or EssenceTiers.Alloy or EssenceTiers.Liquid;

    /// <summary>Crystallisation omens only apply to Perfect and Corrupted essences.</summary>
    [JsonIgnore] public bool AcceptsCrystallisation => Tier is EssenceTiers.Perfect or EssenceTiers.Corrupted;

    public override string ToString() => Name;
}

public static class EssenceTiers
{
    public const string Perfect = "Perfect", Corrupted = "Corrupted", Alloy = "Alloy";
    /// <summary>The essence-like Liquid Emotions used on jewels (built from currencies.json, see GameData).</summary>
    public const string Liquid = "Liquid";
}
