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
    public override string ToString() => $"{Name} T{Tier} ({Text})";
}

public enum AffixType { Prefix, Suffix, Other }

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

    [JsonIgnore] public string DescriptionText => string.Join(" ", Description);
    public override string ToString() => Name;
}

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
    public string? QualityType { get; set; }
    public string? Target { get; set; }
    public List<string> Description { get; set; } = new();
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
    public Dictionary<string, double> VaalOutcomes { get; set; } = new();
    public string? VaalOutcomesNote { get; set; }
    public double ModLevelRequirementFactor { get; set; } = 0.8;
    public Dictionary<string, int> QualityPerUse { get; set; } = new();
    public int ShardsPerOrb { get; set; } = 10;
    public bool OnlyOneCraftedModPerItem { get; set; } = true;
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
