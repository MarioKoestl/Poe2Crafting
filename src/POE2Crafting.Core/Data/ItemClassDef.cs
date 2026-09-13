namespace POE2Crafting.Core.Data;

/// <summary>An item class (e.g. "Amulet") with its group and the poe2db mod pages of its bases. Loaded from data/item_classes.json.</summary>
public sealed class ItemClassDef
{
    public string Name { get; init; } = "";
    public string Group { get; init; } = "";
    public List<string> ModPages { get; init; } = new();
    public int BaseCount { get; init; }
}
