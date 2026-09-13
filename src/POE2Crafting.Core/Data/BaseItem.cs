namespace POE2Crafting.Core.Data;

/// <summary>A base item type (e.g. "Siphoning Wand"). Loaded from data/bases.json.</summary>
public sealed class BaseItem
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string ItemClass { get; init; } = "";
    /// <summary>poe2db ModifiersCalc page whose weights apply (e.g. "Wands", "Body_Armours_int"). Null = all pages of the class.</summary>
    public string? ModPage { get; init; }
    public List<string> Tags { get; init; } = new();
    public string? Implicit { get; init; }
    public int? SocketLimit { get; init; }
    public int? Quality { get; init; }
    public bool Hidden { get; init; }

    public override string ToString() => Name;
}
