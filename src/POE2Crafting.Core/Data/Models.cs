using System.Text.Json;
using System.Text.Json.Serialization;

namespace POE2Crafting.Core.Data;

/// <summary>A base item type (e.g. "Siphoning Wand"). Loaded from data/bases.json.</summary>
public sealed class BaseItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ItemClass { get; set; } = "";
    /// <summary>poe2db ModifiersCalc page whose weights apply (e.g. "Wands", "Body_Armours_int"). Null = fall back to tag filtering.</summary>
    public string? ModPage { get; set; }
    public string? SubType { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? Implicit { get; set; }
    public JsonElement? ImplicitModTypes { get; set; }
    public int? SocketLimit { get; set; }
    public int? Quality { get; set; }
    public bool Hidden { get; set; }
    public Dictionary<string, int> Requirements { get; set; } = new();
    public JsonElement? Weapon { get; set; }
    public JsonElement? Armour { get; set; }
    public JsonElement? Flask { get; set; }
    public JsonElement? Charm { get; set; }

    public bool HasTag(string tag) => Tags.Contains(tag);
    public override string ToString() => Name;
}

/// <summary>A modifier definition (one tier of one family, on one or more item-class pages). Loaded from data/mods.json.</summary>
public sealed class ModDef
{
    public string Id { get; set; } = "";
    /// <summary>normal, corrupted, desecrated, corruption_upgrade, essence, perfect_essence, socketable, bonded, ...</summary>
    public string Category { get; set; } = "";
    /// <summary>prefix, suffix, corrupted_implicit, corruption_upgrade, socketable</summary>
    public string Gen { get; set; } = "";
    public string? Family { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public string Text { get; set; } = "";
    public List<string> AltTexts { get; set; } = new();
    public List<string> SpawnTags { get; set; } = new();
    public List<string> ModTags { get; set; } = new();
    public List<string> AddsNo { get; set; } = new();
    /// <summary>poe2db DropChance per ModifiersCalc page (estimate, see KNOWLEDGE_BASE.md 2.6).</summary>
    public Dictionary<string, int> Weights { get; set; } = new();
    public List<double[]> Ranges { get; set; } = new();
    public string? Code { get; set; }
    public string? Type { get; set; }
    [JsonConverter(typeof(FlexibleBoolConverter))] public bool? IsPerfect { get; set; }
    [JsonConverter(typeof(FlexibleBoolConverter))] public bool? IsAlloy { get; set; }
    [JsonConverter(typeof(FlexibleBoolConverter))] public bool? Removes { get; set; }
    public int? ReqLvl { get; set; }

    /// <summary>Tier within the family (1 = best), computed at load time.</summary>
    [JsonIgnore] public int Tier { get; set; }
    [JsonIgnore] public int TierCount { get; set; }

    [JsonIgnore] public bool IsPrefix => Gen == "prefix";
    [JsonIgnore] public bool IsSuffix => Gen == "suffix";
    [JsonIgnore] public AffixType AffixType => IsPrefix ? AffixType.Prefix : IsSuffix ? AffixType.Suffix : AffixType.Other;
    [JsonIgnore] public IEnumerable<string> BlockingTags => SpawnTags.Where(t => t.StartsWith("no_", StringComparison.Ordinal));

    public int WeightOn(string? page) => page != null && Weights.TryGetValue(page, out var w) ? w : 0;

    /// <summary>Name for display: corruption enchantments only have internal codes as names in the data.</summary>
    [JsonIgnore] public string DisplayName => Category switch
    {
        ModCategories.Corrupted => "Corruption Enchantment",
        ModCategories.CorruptionUpgrade => "Upgraded Corruption Enchantment",
        _ => Name,
    };
    public override string ToString() => $"{Name} T{Tier} ({Text})";
}

public enum AffixType { Prefix, Suffix, Other }

/// <summary>ModDef.Category values (poe2db ModsView categories) used by the simulator.</summary>
public static class ModCategories
{
    public const string Normal = "normal";
    public const string Desecrated = "desecrated";
    public const string Otherworldly = "breach_otherworldly";
    public const string Essence = "essence";
    public const string PerfectEssence = "perfect_essence";
    public const string Corrupted = "corrupted";
    public const string CorruptionUpgrade = "corruption_upgrade";
    public const string Socketable = "socketable";
    public const string Bonded = "bonded";

    /// <summary>Categories whose mods are the guaranteed result of an essence or alloy.</summary>
    public static readonly string[] EssenceResults = { Essence, PerfectEssence };

    /// <summary>How a mod of this category appears on an item.</summary>
    public static Items.ModKind KindFor(string category) => category switch
    {
        Desecrated => Items.ModKind.Desecrated,
        PerfectEssence => Items.ModKind.Crafted,
        Corrupted or CorruptionUpgrade => Items.ModKind.CorruptedImplicit,
        _ => Items.ModKind.Explicit,
    };
}

/// <summary>Tier numbering as in game: T1 = highest level within one family, stat, generation type and category.</summary>
public static class ModTiers
{
    public readonly record struct Rank(int Tier, int Count);

    /// <summary>Mods with the same key are tiers of one another (same family, stat, affix type and category).</summary>
    public static string TierGroupKey(ModDef mod) => $"{mod.Category}|{mod.Gen}|{mod.Family ?? mod.Name}|{Items.ModText.StatSignature(mod.Text)}";

    /// <summary>Ranks every mod with a family among the given set (e.g. all mods of a base, or all mods globally).</summary>
    public static Dictionary<string, Rank> RankAll(IEnumerable<ModDef> mods)
    {
        var result = new Dictionary<string, Rank>();
        foreach (var group in mods.Where(m => m.Family != null && (m.IsPrefix || m.IsSuffix)).GroupBy(TierGroupKey))
        {
            var levels = group.Select(m => m.Level).Distinct().OrderByDescending(l => l).ToList();
            foreach (var m in group) result[m.Id] = new Rank(levels.IndexOf(m.Level) + 1, levels.Count);
        }
        return result;
    }
}

public sealed class CurrencyDef
{
    public string Name { get; set; } = "";
    public string? Slug { get; set; }
    public string? Section { get; set; }
    public string? StackSize { get; set; }
    public int? MinModLevel { get; set; }
    public int? MaxItemLevel { get; set; }
    public List<string> Description { get; set; } = new();
    /// <summary>Engine operation id (transmute, augment, regal, exalt, chaos, alchemy, annul, divine, chance, fracture, vaal, mirror, lock, ...). Null = not simulated.</summary>
    public string? Op { get; set; }
    public List<string>? RarityIn { get; set; }
    public string? RarityOut { get; set; }
    public int? Adds { get; set; }
    public int? Removes { get; set; }
    public int? MinMods { get; set; }
    public bool? RequiresCorrupted { get; set; }
    public string? Target { get; set; }
    public string? QualityTarget { get; set; }
    public string? Element { get; set; }
    public bool? Otherworldly { get; set; }

    /// <summary>For Op == "essence": the essence or alloy this synthetic currency applies (see GameData.AllCurrencies).</summary>
    [JsonIgnore] public EssenceDef? Essence { get; set; }

    /// <summary>For Op == "catalyst": the catalyst this synthetic currency applies.</summary>
    [JsonIgnore] public CatalystDef? Catalyst { get; set; }

    /// <summary>Class group the currency can be used on (Target, or QualityTarget for quality currencies).</summary>
    [JsonIgnore] public string? ClassTarget => Target ?? QualityTarget;

    [JsonIgnore] public string DescriptionText => string.Join(" ", Description);
    public override string ToString() => Name;
}

/// <summary>A crafting item (currency, essence, alloy, catalyst or omen) with what the UI shows about it: kind, icon, description and rule facts.</summary>
public sealed record CraftItemInfo(string Name, string Kind, string? IconUrl, IReadOnlyList<string> Description, IReadOnlyList<string> Facts);

public sealed class GuaranteedMod
{
    public List<string> Targets { get; set; } = new();
    public string Text { get; set; } = "";
}

public sealed class EssenceDef
{
    public string Name { get; set; } = "";
    public string? Slug { get; set; }
    public string? Tier { get; set; }            // Lesser, Normal, Greater, Perfect, Corrupted, Alloy
    public bool RemovesRandomModifier { get; set; }
    public List<string> RarityIn { get; set; } = new();
    public string? RarityOut { get; set; }
    public List<string> Description { get; set; } = new();
    public List<GuaranteedMod> GuaranteedByClass { get; set; } = new();

    /// <summary>Perfect, Corrupted and Alloy variants remove a mod and add a "Crafted" modifier; Lesser/Normal/Greater add a regular explicit mod.</summary>
    [JsonIgnore] public bool AddsCraftedMod => Tier is "Perfect" or "Corrupted" or "Alloy";
    public override string ToString() => Name;
}

public sealed class OmenDef
{
    public string Name { get; set; } = "";
    public string? Slug { get; set; }
    public string? StackSize { get; set; }
    public string Description { get; set; } = "";
    public string? TargetCurrency { get; set; }
    public string? Effect { get; set; }
    public bool Crafting { get; set; }
    public override string ToString() => Name;
}

public sealed class CatalystDef
{
    public string Name { get; set; } = "";
    public string? Slug { get; set; }
    public string? QualityType { get; set; }
    /// <summary>"ring or amulet" or "jewel" in the data.</summary>
    public string? Target { get; set; }
    public List<string> Description { get; set; } = new();

    /// <summary>Class group for ClassMatchesTarget.</summary>
    [JsonIgnore] public string ClassTarget => Target == "jewel" ? "jewel" : "ring_or_amulet";

    /// <summary>The mod tag whose modifiers this catalyst quality enhances.</summary>
    [JsonIgnore] public string? ModTag => QualityTagFor(QualityType);

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

public sealed class ItemClassDef
{
    public string Name { get; set; } = "";
    public string Slot { get; set; } = "";
    public string Group { get; set; } = "";
    public List<string> ModPages { get; set; } = new();
    public int BaseCount { get; set; }
}

public sealed class SimConfig
{
    public int SchemaVersion { get; set; } = 1;
    public string GameVersion { get; set; } = "";
    public SimAssumptions Assumptions { get; set; } = new();
}

public sealed class SimAssumptions
{
    public string WeightsSource { get; set; } = "";
    /// <summary>"weighted" = P(prefix) proportional to total prefix weight vs suffix weight; "equal" = 50/50.</summary>
    public string AffixTypeSelection { get; set; } = "weighted";
    public int MagicMaxPrefixes { get; set; } = 1;
    public int MagicMaxSuffixes { get; set; } = 1;
    public int RareMaxPrefixes { get; set; } = 3;
    public int RareMaxSuffixes { get; set; } = 3;
    public int AlchemyModCount { get; set; } = 4;
    public bool AlchemyOnMagicKeepsExistingMods { get; set; } = true;
    public string WhittlingRule { get; set; } = "lowest_mod_level_then_random";
    public string HomogenisingRule { get; set; } = "shares_any_mod_tag_with_random_existing_mod";
    public string RestrictedOmenWhenNoSlot { get; set; } = "currency_not_applicable";
    /// <summary>Vaal Orb outcome weights by id: no_change, corrupted_implicit, reroll_mods, add_socket_or_quality.</summary>
    public Dictionary<string, double> VaalOutcomes { get; set; } = new();
    public string? VaalOutcomesNote { get; set; }
    /// <summary>"1-3 affixes are randomized": how many mods the reroll outcome replaces (uniform in range).</summary>
    public int VaalRerollMinMods { get; set; } = 1;
    public int VaalRerollMaxMods { get; set; } = 3;
    /// <summary>Quality cap of the wand/staff quality outcome.</summary>
    public int VaalQualityCap { get; set; } = 23;
    public double ArchitectSuccessChance { get; set; } = 0.5;

    /// <summary>Maximum quality when the base does not define one.</summary>
    public int DefaultMaxQuality { get; set; } = 20;
    /// <summary>Quality a catalyst adds per use.</summary>
    public int CatalystQualityPerUse { get; set; } = 5;
    /// <summary>How far Vaal Infusers can exceed the maximum quality.</summary>
    public int InfuserOverCap { get; set; } = 10;
    /// <summary>Infuser corruption chance per point of starting quality above the maximum (0 at or below the maximum).</summary>
    public double InfuserCorruptChancePerQuality { get; set; } = 0.05;
    /// <summary>Omen of Catalysing Exaltation: weight multiplier for matching mods = 1 + quality × this.</summary>
    public double CatalysingWeightBonusPerQuality { get; set; } = 0.05;
    public double ModLevelRequirementFactor { get; set; } = 0.8;
    /// <summary>Quality added by one quality currency / Vaal Infuser per item rarity.</summary>
    public Dictionary<string, int> QualityPerUse { get; set; } = new();
    public string? QualityPerUseNote { get; set; }
    public int ShardsPerOrb { get; set; } = 10;
    public bool OnlyOneCraftedModPerItem { get; set; } = true;
    /// <summary>Number of options offered when revealing a desecrated modifier at the Well of Souls.</summary>
    public int RevealOptionCount { get; set; } = 3;
    /// <summary>Unrevealed modifiers created by Omen of Putrefaction ("up to 6").</summary>
    public int PutrefactionUnrevealedCount { get; set; } = 6;

    public int MaxPrefixes(Items.Rarity r) => r switch { Items.Rarity.Magic => MagicMaxPrefixes, Items.Rarity.Rare => RareMaxPrefixes, _ => 0 };
    public int MaxSuffixes(Items.Rarity r) => r switch { Items.Rarity.Magic => MagicMaxSuffixes, Items.Rarity.Rare => RareMaxSuffixes, _ => 0 };
    public int MaxAffixes(Items.Rarity r, AffixType type) => type == AffixType.Prefix ? MaxPrefixes(r) : type == AffixType.Suffix ? MaxSuffixes(r) : 0;
}

/// <summary>poe2db exports IsPerfect as "1"/"0" strings and IsAlloy as bool; accept both.</summary>
public sealed class FlexibleBoolConverter : JsonConverter<bool?>
{
    public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        JsonTokenType.Null => null,
        JsonTokenType.Number => reader.GetDouble() != 0,
        JsonTokenType.String => reader.GetString() is { } s && (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase)),
        _ => null,
    };

    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue(); else writer.WriteBooleanValue(value.Value);
    }
}
