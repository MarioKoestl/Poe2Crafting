using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Builds;

/// <summary>
/// A character from poe.ninja (GET /poe2/api/builds/&lt;version&gt;/character?account=..&amp;name=..&amp;overview=..): only the parts the crafting analysis uses.
/// Items are GGG's official item JSON.
/// </summary>
public sealed class NinjaCharacter
{
    public string Account { get; set; } = "";
    public string Name { get; set; } = "";
    public string League { get; set; } = "";
    public int Level { get; set; }
    public string Class { get; set; } = "";
    public List<NinjaItemSlot> Items { get; set; } = new();
    public List<NinjaItemSlot> Jewels { get; set; } = new();
    public List<NinjaItemSlot> Flasks { get; set; } = new();
    public List<NinjaSkill> Skills { get; set; } = new();

    public static NinjaCharacter Parse(string json) => JsonSerializer.Deserialize<NinjaCharacter>(json, JsonOptions) ?? new NinjaCharacter();

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Equipment and jewels (flasks and charms are not crafted).</summary>
    [JsonIgnore] public IEnumerable<PoeItemJson> CraftableItems => Items.Concat(Jewels).Select(s => s.ItemData).OfType<PoeItemJson>();

    /// <summary>Everything the character wears, flasks and charms included: what the build view shows.</summary>
    [JsonIgnore] public IEnumerable<PoeItemJson> AllItems => Items.Concat(Jewels).Concat(Flasks).Select(s => s.ItemData).OfType<PoeItemJson>();

    /// <summary>The active skill gems, without their support gems.</summary>
    [JsonIgnore] public IEnumerable<string> SkillNames =>
        Skills.SelectMany(s => s.AllGems).Where(g => !g.Support).Select(g => g.Name).Where(n => n.Length > 0).Distinct();
}

public sealed class NinjaItemSlot
{
    public PoeItemJson? ItemData { get; set; }
}

/// <summary>One skill of a character: the active gem plus the gems socketed with it.</summary>
public sealed class NinjaSkill
{
    public List<NinjaGem> AllGems { get; set; } = new();
}

public sealed class NinjaGem
{
    public string Name { get; set; } = "";
    public PoeItemJson? ItemData { get; set; }

    /// <summary>Support gems sit in the same list as the skill they support.</summary>
    [JsonIgnore] public bool Support => ItemData?.Support == true;
}

/// <summary>GGG's item JSON (the fields needed to rebuild the in-game item text).</summary>
public sealed class PoeItemJson
{
    public string? Rarity { get; set; }
    public int FrameType { get; set; }
    public string? Name { get; set; }
    public string? TypeLine { get; set; }
    public string? BaseType { get; set; }
    /// <summary>Equipment slot ("Helm", "BodyArmour", "Weapon", "Ring2"); empty for jewels, flasks and gems.</summary>
    public string? InventoryId { get; set; }
    /// <summary>True for support gems.</summary>
    public bool Support { get; set; }
    public int Ilvl { get; set; }
    public bool Corrupted { get; set; }
    public bool DoubleCorrupted { get; set; }
    public bool Identified { get; set; } = true;
    public int? QualityProperty { get; set; }
    public List<string>? ImplicitMods { get; set; }
    public List<string>? ExplicitMods { get; set; }
    public List<string>? FracturedMods { get; set; }
    public List<string>? DesecratedMods { get; set; }
    public List<string>? CraftedMods { get; set; }
    public List<string>? EnchantMods { get; set; }

    /// <summary>"Rare", "Unique", ... (rarity field; older data only has the frame type).</summary>
    [JsonIgnore] public string RarityName => Rarity ?? FrameType switch { 0 => "Normal", 1 => "Magic", 2 => "Rare", 3 => "Unique", _ => "Normal" };

    /// <summary>"[Resistances|Chaos Resistance]" → "Chaos Resistance", "[Projectile]" → "Projectile".</summary>
    private static readonly Regex Markup = new(@"\[(?:[^\]|]*\|)?([^\]]*)\]", RegexOptions.Compiled);

    public static string StripMarkup(string text) => Markup.Replace(text, "$1");

    /// <summary>
    /// The in-game item text (Ctrl+C format) so <see cref="ItemParser"/> resolves the modifiers against the base like an imported item:
    /// header, quality, item level, enchants, implicits, then fractured/explicit/crafted/desecrated lines with their markers, corruption.
    /// Rune and bonded lines are left out (augments, not crafted).
    /// </summary>
    public string ToItemText()
    {
        var text = new StringBuilder();
        text.AppendLine($"Rarity: {RarityName}");
        if (RarityName is "Rare" or "Unique" && !string.IsNullOrEmpty(Name)) text.AppendLine(Name);
        text.AppendLine(BaseType ?? TypeLine ?? "");
        void Section(IEnumerable<string> lines)
        {
            var list = lines.ToList();
            if (list.Count == 0) return;
            text.AppendLine(ItemTextFormat.Separator);
            foreach (var line in list) text.AppendLine(line);
        }
        IEnumerable<string> Lines(List<string>? mods, string? marker) =>
            (mods ?? new()).SelectMany(m => StripMarkup(m).Split('\n')).Select(l => marker == null ? l : $"{l} ({marker})");

        if (QualityProperty is > 0 and var quality) Section(new[] { $"Quality: +{quality}%" });
        Section(new[] { $"Item Level: {Ilvl}" });
        Section(Lines(EnchantMods, ItemTextFormat.Marker(ModKind.Enchant)));
        Section(Lines(ImplicitMods, ItemTextFormat.Marker(ModKind.Implicit)));
        Section(Lines(FracturedMods, ItemTextFormat.FracturedMarker)
            .Concat(Lines(ExplicitMods, null))
            .Concat(Lines(CraftedMods, ItemTextFormat.Marker(ModKind.Crafted)))
            .Concat(Lines(DesecratedMods, ItemTextFormat.Marker(ModKind.Desecrated))));
        if (DoubleCorrupted) Section(new[] { "Twice Corrupted" });
        else if (Corrupted) Section(new[] { "Corrupted" });
        return text.ToString();
    }

    /// <summary>The item as the simulator knows it, or null when its base is not in the data (e.g. uniques of unknown bases).</summary>
    public Item? ToItem(GameData data)
    {
        if (data.FindBase(BaseType ?? "") == null) return null;
        try
        {
            return ItemParser.Parse(ToItemText(), data);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
