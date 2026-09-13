namespace POE2Crafting.Core.Data;

/// <summary>An omen (data/omens.json); <see cref="Effect"/> is one of the Engine.OmenEffects ids.</summary>
public sealed class OmenDef
{
    public string Name { get; init; } = "";
    public string? Slug { get; init; }
    public string? StackSize { get; init; }
    public string Description { get; init; } = "";
    /// <summary>The currency the omen modifies as named in the data ("Chaos Orb", "Exalted Orb", "Essence", "Desecration", ...).</summary>
    public string? TargetCurrency { get; init; }
    public string? Effect { get; init; }
    public bool Crafting { get; init; }
    public override string ToString() => Name;
}
