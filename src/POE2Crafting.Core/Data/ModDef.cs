using System.Text.Json.Serialization;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Data;

public enum AffixType { Prefix, Suffix, Other }

/// <summary>A modifier definition (one tier of one family, on one or more item-class pages). Loaded from data/mods.json.</summary>
public sealed class ModDef
{
    public string Id { get; init; } = "";
    /// <summary>normal, corrupted, desecrated, corruption_upgrade, essence, perfect_essence, socketable, bonded, ... (<see cref="ModCategories"/>).</summary>
    public string Category { get; init; } = "";
    /// <summary>prefix, suffix, corrupted_implicit, corruption_upgrade, socketable</summary>
    public string Gen { get; init; } = "";
    public string? Family { get; init; }
    public string Name { get; init; } = "";
    public int Level { get; init; }
    public string Text { get; init; } = "";
    public List<string> AltTexts { get; init; } = new();
    public List<string> SpawnTags { get; init; } = new();
    public List<string> ModTags { get; init; } = new();
    /// <summary>poe2db DropChance per ModifiersCalc page (estimate, see KNOWLEDGE_BASE.md 2.6).</summary>
    public Dictionary<string, int> Weights { get; init; } = new();
    public List<double[]> Ranges { get; init; } = new();

    /// <summary>Tier within the family (1 = best), computed at load time.</summary>
    [JsonIgnore] public int Tier { get; internal set; }
    [JsonIgnore] public int TierCount { get; internal set; }

    private string? _statSignature;
    private List<double[]>? _statRanges;

    /// <summary>The stat without numbers (<see cref="ModText.StatSignature"/>), cached: matching and tiers compare it constantly.</summary>
    [JsonIgnore] public string StatSignature => _statSignature ??= ModText.StatSignature(Text);

    /// <summary>
    /// The numbers a value target refers to: the rollable ranges, or for a text without ranges its fixed numbers as one-value ranges
    /// ("+3 to Level of all Melee Skills" → [3, 3]; catalyst quality can still raise it).
    /// </summary>
    [JsonIgnore] public List<double[]> StatRanges => _statRanges ??= Ranges.Count > 0 ? Ranges : ModText.RolledTokens(Text).Select(t => new[] { t.Value, t.Value }).ToList();

    [JsonIgnore] public bool IsPrefix => Gen == "prefix";
    [JsonIgnore] public bool IsSuffix => Gen == "suffix";
    [JsonIgnore] public AffixType AffixType => IsPrefix ? AffixType.Prefix : IsSuffix ? AffixType.Suffix : AffixType.Other;
    [JsonIgnore] public IEnumerable<string> BlockingTags => SpawnTags.Where(t => t.StartsWith("no_", StringComparison.Ordinal));

    public int WeightOn(string? page) => page != null && Weights.TryGetValue(page, out var w) ? w : 0;

    /// <summary>Whether the mod exists on any of the pages (has a weight entry there).</summary>
    public bool IsOnAnyPage(IReadOnlyList<string> pages) => Weights.Keys.Any(pages.Contains);

    /// <summary>Name for display: corruption enchantments only have internal codes as names in the data.</summary>
    [JsonIgnore] public string DisplayName => Category switch
    {
        ModCategories.Corrupted => "Corruption Enchantment",
        ModCategories.CorruptionUpgrade => "Upgraded Corruption Enchantment",
        _ => Name,
    };

    public override string ToString() => $"{Name} T{Tier} ({Text})";
}
