using System.Text.Json.Serialization;

namespace POE2Crafting.Core.Data;

/// <summary>A catalyst (data/catalysts.json): adds quality of its type to a ring/amulet (Refined: jewel) that enhances mods with its tag.</summary>
public sealed class CatalystDef
{
    public string Name { get; init; } = "";
    public string? Slug { get; init; }
    public string? QualityType { get; init; }
    /// <summary>"ring or amulet" or "jewel" in the data.</summary>
    public string? Target { get; init; }
    public List<string> Description { get; init; } = new();

    /// <summary>Class group for ClassMatchesTarget.</summary>
    [JsonIgnore] public string ClassTarget => Target == "jewel" ? ClassTargets.Jewel : ClassTargets.RingOrAmulet;

    /// <summary>The mod tag whose modifiers this catalyst quality enhances.</summary>
    [JsonIgnore] public string? ModTag => QualityTagFor(QualityType);

    /// <summary>Whether this catalyst's quality enhances the mod.</summary>
    public bool Enhances(ModDef? mod) => ModTag is { } tag && mod?.ModTags.Contains(tag) == true;

    /// <summary>
    /// Mod tag for a catalyst quality type as stored on items: the catalyst's type ("Life") or the item text's
    /// "Quality: +20% (Life Modifiers)" → life; defence variants → defences.
    /// </summary>
    public static string? QualityTagFor(string? qualityType)
    {
        var t = qualityType?.Replace("Modifiers", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (string.IsNullOrEmpty(t)) return null;
        return t.Contains("Armour", StringComparison.OrdinalIgnoreCase) ? "defences" : t.ToLowerInvariant();
    }
}
