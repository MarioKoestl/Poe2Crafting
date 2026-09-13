using System.Text.Json.Serialization;
using POE2Crafting.Core.Data;

namespace POE2Crafting.Core.Items;

public enum Rarity { Normal, Magic, Rare, Unique }

/// <summary>Kind of a modifier line on an item.</summary>
public enum ModKind { Explicit, Crafted, Desecrated, Implicit, CorruptedImplicit, Rune, Enchant }

public static class ModKindExtensions
{
    /// <summary>Explicit, crafted and desecrated modifiers occupy a prefix or suffix slot; implicits and enchantments don't.</summary>
    public static bool OccupiesSlot(this ModKind kind) => kind is ModKind.Explicit or ModKind.Crafted or ModKind.Desecrated;

    /// <summary>Implicit-like lines shown above the affixes (base implicits, enchantments, corruption enchantments).</summary>
    public static bool IsImplicitLine(this ModKind kind) => kind is ModKind.Implicit or ModKind.CorruptedImplicit or ModKind.Enchant;
}

public static class AffixTypeExtensions
{
    /// <summary>Prefix and suffix, the two slot types.</summary>
    public static readonly AffixType[] Both = { AffixType.Prefix, AffixType.Suffix };

    /// <summary>"prefix" / "suffix" for sentences.</summary>
    public static string Lower(this AffixType type) => type.ToString().ToLowerInvariant();

    public static AffixType Opposite(this AffixType type) => type switch
    {
        AffixType.Prefix => AffixType.Suffix,
        AffixType.Suffix => AffixType.Prefix,
        _ => AffixType.Other,
    };
}

/// <summary>A modifier instance on an item: a ModDef plus rolled values.</summary>
public sealed class ItemMod
{
    public string ModId { get; set; } = "";
    public ModKind Kind { get; set; } = ModKind.Explicit;
    /// <summary>Prefix/Suffix slot; Other for implicits, enchantments and lines not (yet) matched to a mod.</summary>
    public AffixType Affix { get; set; } = AffixType.Other;
    /// <summary>Rolled values, one per range in the ModDef (ModDef.Ranges). Empty = ranges not rolled / unknown.</summary>
    public List<double> Values { get; set; } = new();
    public bool Fractured { get; set; }
    /// <summary>A desecrated mod that still has to be revealed at the Well of Souls (no Def yet).</summary>
    public bool Unrevealed { get; set; }
    /// <summary>Free text for mods that could not be matched to a ModDef (import) – shown as-is.</summary>
    public string? RawText { get; set; }
    public string? SourceName { get; set; }  // e.g. the essence/alloy/bone that created it

    /// <summary>Reveal context of an unrevealed mod, fixed when it was created (bone and omen used).</summary>
    public RevealContext? Reveal { get; set; }

    [JsonIgnore] public ModDef? Def { get; set; }

    public ItemMod Clone()
    {
        var c = (ItemMod)MemberwiseClone();
        c.Values = new List<double>(Values);
        return c;
    }

    /// <summary>Human readable text with rolled values substituted into the ranges, e.g. "(105-119)% increased Spell Damage" -> "112% increased Spell Damage".</summary>
    public string DisplayText()
    {
        if (Unrevealed) return $"Unrevealed Desecrated {Affix}";
        if (Def == null) return RawText ?? ModId;
        return ModText.Render(Def.Text, Values);
    }

    /// <summary>The values of <see cref="ModDef.StatRanges"/>: the rolled values, or the fixed numbers of a text without ranges.</summary>
    [JsonIgnore] public List<double> StatValues => Def == null || Def.Ranges.Count > 0 ? Values : Def.StatRanges.Select(r => r[0]).ToList();

    /// <summary>True when the mod occupies a prefix or suffix slot.</summary>
    [JsonIgnore] public bool IsAffix => Kind.OccupiesSlot() && Affix != AffixType.Other;
}

/// <summary>What an unrevealed desecrated mod can become: set by the bone (minimum modifier level, otherworldly) and omens (boss tag).</summary>
public sealed record RevealContext(int MinModLevel = 0, string? RequiredTag = null, bool Otherworldly = false)
{
    /// <summary>Short description of the restrictions for the UI, e.g. "min. modifier level 40, amanamu modifiers".</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (MinModLevel > 0) parts.Add($"min. modifier level {MinModLevel}");
        if (RequiredTag != null) parts.Add($"{RequiredTag.Replace("_mod", "")} modifiers");
        if (Otherworldly) parts.Add("incl. otherworldly modifiers");
        return string.Join(", ", parts);
    }
}

/// <summary>A craftable item.</summary>
public sealed class Item
{
    /// <summary>Item level of new items when none is given (endgame bases).</summary>
    public const int DefaultItemLevel = 82;

    public string BaseName { get; set; } = "";
    public string ItemClass { get; set; } = "";
    public string? Name { get; set; }               // rare / unique name
    public Rarity Rarity { get; set; } = Rarity.Normal;
    public int ItemLevel { get; set; } = DefaultItemLevel;
    public int Quality { get; set; }
    public string? QualityType { get; set; }        // catalyst type on jewellery
    public int Sockets { get; set; }
    public List<string> Runes { get; set; } = new();
    public List<ItemMod> Mods { get; set; } = new();
    public bool Corrupted { get; set; }
    /// <summary>Corrupted a second time by an Architect's Orb (no further Architect's Orb).</summary>
    public bool TwiceCorrupted { get; set; }
    public bool Mirrored { get; set; }
    public bool Sanctified { get; set; }
    public bool Identified { get; set; } = true;
    public bool Foreseeing { get; set; }            // Hinekora's Lock active
    /// <summary>Hinekora's Lock: fixes the result of every currency action on this item state (see CraftingEngine.Foresee).</summary>
    public int ForeseeSeed { get; set; }
    public string? Notes { get; set; }

    [JsonIgnore] public BaseItem? Base { get; set; }

    /// <summary>Mods occupying a prefix or suffix slot (explicit, crafted, desecrated).</summary>
    [JsonIgnore] public IEnumerable<ItemMod> Affixes => Mods.Where(m => m.IsAffix);
    [JsonIgnore] public IEnumerable<ItemMod> Prefixes => Affixes.Where(m => m.Affix == AffixType.Prefix);
    [JsonIgnore] public IEnumerable<ItemMod> Suffixes => Affixes.Where(m => m.Affix == AffixType.Suffix);
    [JsonIgnore] public int PrefixCount => CountOf(AffixType.Prefix);
    [JsonIgnore] public int SuffixCount => CountOf(AffixType.Suffix);
    [JsonIgnore] public int AffixCount => Affixes.Count();
    public int CountOf(AffixType type) => type == AffixType.Other ? 0 : Affixes.Count(m => m.Affix == type);

    public bool HasFamily(string? family) => family != null && Affixes.Any(m => m.Def?.Family == family);

    /// <summary>"Name Base" for rares and uniques, otherwise the base name.</summary>
    [JsonIgnore] public string Title => Rarity is Rarity.Rare or Rarity.Unique && Name != null ? $"{Name} {BaseName}" : BaseName;

    /// <summary>Maximum quality: the base's value (or the default) plus "+#% to Maximum Quality" modifiers.</summary>
    public int MaxQuality(int defaultMax) =>
        (Base?.Quality ?? defaultMax) + Affixes.Where(m => m.Def?.Family == ModFamilies.MaximumQuality).Sum(m => (int)m.StatValues.FirstOrDefault());

    /// <summary>"20% (Life)", "0%" without quality.</summary>
    [JsonIgnore] public string QualityText => Quality == 0 ? "0%" : $"{Quality}%{(QualityType != null ? $" ({QualityType})" : "")}";

    /// <summary>Mod tag enhanced by the item's catalyst quality, or null without catalyst quality.</summary>
    [JsonIgnore] public string? QualityTag => Quality > 0 ? CatalystDef.QualityTagFor(QualityType) : null;

    /// <summary>Whether catalyst quality on this item enhances the mod (its tags contain the quality type's tag).</summary>
    public bool QualityEnhances(ItemMod mod) => QualityEnhances(mod.Def);

    /// <summary>Whether catalyst quality on this item enhances mods of this definition.</summary>
    public bool QualityEnhances(ModDef? mod) => QualityTag is { } tag && mod?.ModTags.Contains(tag) == true;

    /// <summary>A copy with catalyst quality of another amount/type (e.g. to test which quality a value target needs).</summary>
    public Item WithQuality(int quality, string? qualityType)
    {
        var copy = Clone();
        copy.Quality = quality;
        copy.QualityType = qualityType;
        return copy;
    }

    private double QualityFactor => 1 + Quality / 100.0;

    /// <summary>The mod line as it works on the item: catalyst quality increases the values of matching mods (e.g. +3 skills → +4 at 34%).</summary>
    public string EffectiveText(ItemMod mod) => QualityEnhances(mod) ? ModText.ScaleNumbers(mod.DisplayText(), QualityFactor) : mod.DisplayText();

    /// <summary>The mod's values (<see cref="ItemMod.StatValues"/>) as they work on the item (catalyst quality applied, see <see cref="EffectiveText"/>).</summary>
    public List<double> EffectiveValues(ItemMod mod) => QualityEnhances(mod) ? ModText.ScaleValues(mod.StatValues, QualityFactor) : mod.StatValues;

    private IEnumerable<(ItemMod Mod, int Index)> Indexed(Func<ItemMod, bool> filter) => Mods.Select((m, i) => (m, i)).Where(t => filter(t.m));

    /// <summary>Corruption enchantments (Vaal Orb / Architect's Orb results, upgraded by Orbs of Sacrifice).</summary>
    [JsonIgnore] public IEnumerable<(ItemMod Mod, int Index)> CorruptionEnchants => Indexed(m => m.Kind == ModKind.CorruptedImplicit);

    [JsonIgnore] public IEnumerable<(ItemMod Mod, int Index)> UnrevealedMods => Indexed(m => m.Unrevealed);

    /// <summary>The enchantment "Allocates Notable" instilled with Liquid Emotions (amulets), or null.</summary>
    [JsonIgnore] public ItemMod? InstilledNotable => Mods.FirstOrDefault(m => m.Kind == ModKind.Enchant && InstillRecipe.NotableOf(m.DisplayText()) != null);

    /// <summary>The name of the instilled notable, or null.</summary>
    [JsonIgnore] public string? InstilledNotableName => InstilledNotable is { } enchant ? InstillRecipe.NotableOf(enchant.DisplayText()) : null;

    /// <summary>Items with desecrated modifiers (revealed or not) cannot be desecrated again.</summary>
    [JsonIgnore] public bool HasDesecratedMod => Affixes.Any(m => m.Kind == ModKind.Desecrated);

    /// <summary>A fresh item of a base (no mods, sockets at the base's limit).</summary>
    /// <param name="withImplicit">Add the base implicit with mid-range values (for display and composed items).</param>
    public static Item FromBase(BaseItem baseItem, Rarity rarity = Rarity.Normal, int itemLevel = DefaultItemLevel, bool withImplicit = false)
    {
        var item = new Item
        {
            BaseName = baseItem.Name,
            ItemClass = baseItem.ItemClass,
            Rarity = rarity,
            ItemLevel = itemLevel,
            Base = baseItem,
            Sockets = baseItem.SocketLimit ?? 0,
        };
        if (withImplicit && !string.IsNullOrEmpty(baseItem.Implicit))
            item.Mods.Add(new ItemMod { ModId = "base_implicit", Kind = ModKind.Implicit, RawText = ModText.RenderMid(baseItem.Implicit) });
        return item;
    }

    /// <summary>Add a modifier instance for a definition (values default to the middle of each range).</summary>
    /// <param name="atIndex">Insert position (e.g. replacing a mod in place); null appends.</param>
    public ItemMod AddMod(ModDef def, ModKind kind = ModKind.Explicit, List<double>? values = null, string? source = null, int? atIndex = null)
    {
        var mod = new ItemMod
        {
            ModId = def.Id, Def = def, Affix = def.AffixType, Kind = kind,
            Values = values ?? def.Ranges.Select(ModText.MidValue).ToList(),
            SourceName = source,
        };
        Mods.Insert(atIndex ?? Mods.Count, mod);
        return mod;
    }

    /// <summary>Replace the mod at <paramref name="index"/> in place (same position in the list), e.g. a revealed or upgraded modifier.</summary>
    public ItemMod ReplaceMod(int index, ModDef def, ModKind kind, List<double>? values = null, string? source = null)
    {
        Mods.RemoveAt(index);
        return AddMod(def, kind, values, source, index);
    }

    /// <summary>A copy without its known affixes: implicits, enchantments, runes, quality, flags and unrevealed mods stay (e.g. to re-add edited affixes).</summary>
    public Item WithoutAffixes()
    {
        var copy = Clone();
        copy.Mods.RemoveAll(m => m.IsAffix && m.Def != null && !m.Unrevealed);
        return copy;
    }

    public Item Clone()
    {
        var c = (Item)MemberwiseClone();
        c.Runes = new List<string>(Runes);
        c.Mods = Mods.Select(m => m.Clone()).ToList();
        return c;
    }

    /// <summary>Re-attach ModDef/BaseItem references after deserialisation.</summary>
    public void Bind(GameData data)
    {
        Base = data.FindBase(BaseName) ?? Base;
        // the game client uses plural class names ("Staves"), the data store singular ones ("Staff")
        if (Base != null) ItemClass = Base.ItemClass;
        foreach (var m in Mods)
        {
            m.Def ??= data.FindMod(m.ModId);
            if (m.Def != null && m.Affix == AffixType.Other && m.Def.AffixType != AffixType.Other) m.Affix = m.Def.AffixType;
        }
    }

    public override string ToString() => $"{Title} ({Rarity}, ilvl {ItemLevel}, {PrefixCount}P/{SuffixCount}S)";
}
