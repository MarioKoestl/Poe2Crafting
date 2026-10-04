using System.Text.Json.Serialization;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Data;

/// <summary>
/// A unique item with the modifiers it grants itself (data/uniques.json). These are not part of the craftable pool in mods.json:
/// a unique rolls its own lines inside its own ranges, so they have no tier and no weight.
/// </summary>
public sealed class UniqueItemDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>The base the unique is built on ("Cultist Crown").</summary>
    public string BaseType { get; init; } = "";
    public string? Icon { get; init; }
    /// <summary>"level", "str", "dex", "int".</summary>
    public Dictionary<string, int> Requirements { get; init; } = new();
    /// <summary>Implicit lines as text with their ranges.</summary>
    public List<string> Implicits { get; init; } = new();
    /// <summary>Explicit lines as text with their ranges, e.g. "(50-100)% increased Armour and Energy Shield".</summary>
    public List<string> Mods { get; init; } = new();

    private List<ModDef>? _modDefs, _implicitDefs;

    /// <summary>
    /// The unique's lines as modifier definitions, so an imported item resolves them like any other line: same text, same ranges,
    /// but no tier and no page (nothing rolls them onto an item).
    /// </summary>
    [JsonIgnore] public IReadOnlyList<ModDef> ModDefs => _modDefs ??= Defs(Mods, "mod");

    /// <summary>The unique's implicit lines as definitions (same rule as <see cref="ModDefs"/>).</summary>
    [JsonIgnore] public IReadOnlyList<ModDef> ImplicitDefs => _implicitDefs ??= Defs(Implicits, "implicit");

    private List<ModDef> Defs(List<string> lines, string kind) => lines.Select((text, i) => new ModDef
    {
        Id = $"unique:{Id}:{kind}:{i}",
        Category = ModCategories.Unique,
        Gen = "unique",
        Name = Name,
        Family = $"Unique{Id}{kind}{i}",
        Level = Requirements.GetValueOrDefault("level"),
        Text = text,
        Ranges = ModText.ParseRanges(text),
    }).ToList();

    public override string ToString() => $"{Name} ({BaseType})";
}
