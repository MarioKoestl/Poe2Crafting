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
    public AffixType Affix { get; set; }
    /// <summary>Rolled values, one per range in the ModDef (ModDef.Ranges). Empty = ranges not rolled / unknown.</summary>
    public List<double> Values { get; set; } = new();
    public bool Fractured { get; set; }
    public bool Unrevealed { get; set; }
    /// <summary>Free text for mods that could not be matched to a ModDef (import) – shown as-is.</summary>
    public string? RawText { get; set; }
    public string? SourceName { get; set; }  // e.g. the essence/alloy/rune that created it

    [JsonIgnore] public ModDef? Def { get; set; }

    public ItemMod Clone() => new()
    {
        ModId = ModId, Kind = Kind, Affix = Affix, Values = new List<double>(Values), Fractured = Fractured,
        Unrevealed = Unrevealed, RawText = RawText, SourceName = SourceName, Def = Def,
    };

    /// <summary>Human readable text with rolled values substituted into the ranges, e.g. "(105-119)% increased Spell Damage" -> "112% increased Spell Damage".</summary>
    public string DisplayText()
    {
        if (Def == null) return RawText ?? ModId;
        if (Unrevealed) return "Unrevealed Desecrated Modifier";
        return ModText.Render(Def.Text, Values);
    }

    public string RangeText() => Def?.Text ?? RawText ?? ModId;

    /// <summary>True when the mod occupies a prefix or suffix slot.</summary>
    [JsonIgnore] public bool IsAffix => Item.IsAffixKind(Kind) && Affix != AffixType.Other;
}

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
    public List<string> Implicits { get; set; } = new();
    public List<ItemMod> Mods { get; set; } = new();
    public bool Corrupted { get; set; }
    public bool Mirrored { get; set; }
    public bool Sanctified { get; set; }
    public bool Identified { get; set; } = true;
    public bool Desecrated { get; set; }            // item has been desecrated once already
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

    public Item Clone()
    {
        var c = (Item)MemberwiseClone();
        c.Runes = new List<string>(Runes);
        c.Implicits = new List<string>(Implicits);
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

/// <summary>Helpers for rendering mod texts with rolled values.</summary>
public static class ModText
{
    static readonly System.Text.RegularExpressions.Regex RangeRx = new(@"\((-?\d+(?:\.\d+)?)\s*-\s*(-?\d+(?:\.\d+)?)\)", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Replace each "(a-b)" range with the corresponding rolled value. Missing values keep the range text.</summary>
    public static string Render(string template, IReadOnlyList<double> values)
    {
        int i = 0;
        return RangeRx.Replace(template, m =>
        {
            var idx = i++;
            if (idx < values.Count)
            {
                var v = values[idx];
                return v == Math.Floor(v) ? ((long)v).ToString() : v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            }
            return m.Value;
        });
    }

    static readonly System.Text.RegularExpressions.Regex NumberOrRangeRx = new(@"\(-?\d+(?:\.\d+)?\s*-\s*-?\d+(?:\.\d+)?\)|\d+(?:\.\d+)?", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// The stat a mod grants with all numbers removed, e.g. "+(5-6) to Level of all Physical Spell Skills" -> "+# to level of all physical spell skills".
    /// Tiers are numbered per family AND stat: one family can hold several stats (Physical/Fire/... Spell Skill levels).
    /// </summary>
    public static string StatSignature(string template) =>
        System.Text.RegularExpressions.Regex.Replace(NumberOrRangeRx.Replace(template, "#"), @"\s+", " ").Trim().ToLowerInvariant();

    public static List<double[]> ParseRanges(string template) =>
        RangeRx.Matches(template).Select(m => new[]
        {
            double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
        }).ToList();
}
