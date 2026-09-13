using System.Text.Json.Serialization;
using POE2Crafting.Core.Data;

namespace POE2Crafting.Core.Items;

public enum Rarity { Normal, Magic, Rare, Unique }

/// <summary>Kind of an explicit modifier line on an item.</summary>
public enum ModKind { Explicit, Crafted, Desecrated, Implicit, CorruptedImplicit, Rune, Enchant }

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

    public string RangeText() => Def?.Text ?? RawText ?? ModId;

    /// <summary>True when the mod occupies a prefix or suffix slot.</summary>
    [JsonIgnore] public bool IsAffix => Item.IsAffixKind(Kind) && Affix != AffixType.Other;
}

/// <summary>What an unrevealed desecrated mod can become: set by the bone (minimum modifier level, otherworldly) and omens (boss tag).</summary>
public sealed record RevealContext(int MinModLevel = 0, string? RequiredTag = null, bool Otherworldly = false);

/// <summary>A craftable item.</summary>
public sealed class Item
{
    public string BaseName { get; set; } = "";
    public string ItemClass { get; set; } = "";
    public string? Name { get; set; }               // rare / unique name
    public Rarity Rarity { get; set; } = Rarity.Normal;
    public int ItemLevel { get; set; } = 82;
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
    public string? Notes { get; set; }

    [JsonIgnore] public BaseItem? Base { get; set; }

    public IEnumerable<ItemMod> Prefixes => Mods.Where(m => m.Affix == AffixType.Prefix && IsAffixKind(m.Kind));
    public IEnumerable<ItemMod> Suffixes => Mods.Where(m => m.Affix == AffixType.Suffix && IsAffixKind(m.Kind));
    /// <summary>Mods occupying a prefix or suffix slot (explicit, crafted, desecrated).</summary>
    public IEnumerable<ItemMod> Affixes => Mods.Where(m => m.IsAffix);
    public int PrefixCount => Prefixes.Count();
    public int SuffixCount => Suffixes.Count();
    public int AffixCount => PrefixCount + SuffixCount;
    public int CountOf(AffixType type) => type == AffixType.Prefix ? PrefixCount : type == AffixType.Suffix ? SuffixCount : 0;

    public static bool IsAffixKind(ModKind k) => k is ModKind.Explicit or ModKind.Crafted or ModKind.Desecrated;

    public bool HasFamily(string? family) => family != null && Affixes.Any(m => m.Def?.Family == family);

    /// <summary>Family of "+#% to Maximum Quality" (Essence of the Breach).</summary>
    private const string MaximumQualityFamily = "LocalMaximumQuality";

    /// <summary>Maximum quality: the base's value (or the default) plus "+#% to Maximum Quality" modifiers.</summary>
    public int MaxQuality(int defaultMax) =>
        (Base?.Quality ?? defaultMax) + Affixes.Where(m => m.Def?.Family == MaximumQualityFamily)
            .Sum(m => (int)ModText.RolledTokens(m.DisplayText()).Select(t => t.Value).FirstOrDefault());

    /// <summary>Mod tag enhanced by the item's catalyst quality, or null without catalyst quality.</summary>
    public string? QualityTag => Quality > 0 ? CatalystDef.QualityTagFor(QualityType) : null;

    /// <summary>Whether catalyst quality on this item enhances the mod (its tags contain the quality type's tag).</summary>
    public bool QualityEnhances(ItemMod mod) => QualityTag is { } tag && mod.Def?.ModTags.Contains(tag) == true;

    /// <summary>Corruption enchantments (Vaal Orb / Architect's Orb results, upgraded by Orbs of Sacrifice).</summary>
    public IEnumerable<(ItemMod Mod, int Index)> CorruptionEnchants => Mods.Select((m, i) => (m, i)).Where(t => t.m.Kind == ModKind.CorruptedImplicit);

    /// <summary>Items with desecrated modifiers (revealed or not) cannot be desecrated again.</summary>
    public bool HasDesecratedMod => Affixes.Any(m => m.Kind == ModKind.Desecrated);
    public IEnumerable<(ItemMod Mod, int Index)> UnrevealedMods => Mods.Select((m, i) => (m, i)).Where(t => t.m.Unrevealed);

    /// <summary>A fresh item of a base (no mods, sockets at the base's limit).</summary>
    /// <param name="withImplicit">Add the base implicit with mid-range values (for display and composed items).</param>
    public static Item FromBase(BaseItem baseItem, Rarity rarity = Rarity.Normal, int itemLevel = 82, bool withImplicit = false)
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
        Base ??= data.FindBase(BaseName);
        // the game client uses plural class names ("Staves"), the data store singular ones ("Staff")
        if (Base != null) ItemClass = Base.ItemClass;
        foreach (var m in Mods)
        {
            m.Def ??= data.FindMod(m.ModId);
            if (m.Def != null && m.Affix == AffixType.Other && m.Def.AffixType != AffixType.Other) m.Affix = m.Def.AffixType;
        }
    }

    public override string ToString() => $"{(Name != null ? Name + " " : "")}{BaseName} ({Rarity}, ilvl {ItemLevel}, {PrefixCount}P/{SuffixCount}S)";
}
