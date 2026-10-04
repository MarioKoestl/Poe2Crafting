using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Builds;

/// <summary>Equipment slots in the order a character sheet lists them.</summary>
public static class GearSlots
{
    private static readonly string[] Order =
        { "Weapon", "Offhand", "Weapon2", "Offhand2", "Helm", "BodyArmour", "Gloves", "Boots", "Amulet", "Ring", "Ring2", "Belt" };

    /// <summary>Position of a slot, unknown slots (jewels, flasks, charms) last.</summary>
    public static int IndexOf(string? slot)
    {
        var index = slot == null ? -1 : Array.IndexOf(Order, slot);
        return index < 0 ? Order.Length : index;
    }

    /// <summary>"BodyArmour" → "Body Armour", "Ring2" → "Ring 2".</summary>
    public static string DisplayName(string? slot) => slot switch
    {
        null or "" => "",
        "BodyArmour" => "Body Armour",
        "Weapon2" => "Weapon 2",
        "Offhand2" => "Offhand 2",
        "Ring2" => "Ring 2",
        _ => slot,
    };
}

/// <summary>What kind of piece it is, for grouping the build view.</summary>
public enum GearKind { Equipment, Jewel, Flask }

/// <summary>
/// One piece a sampled character wears. <see cref="Item"/> is the piece as the simulator knows it — null when its base is not in the data
/// (many uniques), in which case only name and base type are known.
/// </summary>
public sealed record GearPiece(string? Slot, GearKind Kind, string Name, string BaseType, Rarity Rarity, Item? Item)
{
    /// <summary>Item class of the piece, or an empty string when its base is unknown.</summary>
    public string ItemClass => Item?.ItemClass ?? "";

    /// <summary>The name to show: the unique's own name, else the base type.</summary>
    public string Title => Name.Length > 0 ? Name : BaseType;

    /// <summary>
    /// Whether this is the piece a build filter names: a unique by its name ("Crown of the Pale King"), a slot value by rarity and
    /// item class ("Rare Gloves").
    /// </summary>
    public bool Matches(string? itemName) =>
        itemName != null && (Name.Equals(itemName, StringComparison.OrdinalIgnoreCase)
                             || $"{Rarity} {ItemClass}".Equals(itemName, StringComparison.OrdinalIgnoreCase));

    public static GearPiece From(PoeItemJson json, GearKind kind, GameData data) => new(
        json.InventoryId, kind, json.Name ?? "", json.BaseType ?? json.TypeLine ?? "",
        Enum.TryParse<Rarity>(json.RarityName, ignoreCase: true, out var rarity) ? rarity : Rarity.Normal,
        json.ToItem(data));
}

/// <summary>A sampled character with the whole build: level, ascendancy, skills and everything worn.</summary>
public sealed record SampledCharacter(CharacterRef Character, string Class, int Level, string? MainSkill,
    IReadOnlyList<string> Skills, IReadOnlyList<GearPiece> Gear)
{
    /// <summary>"Spear Stab \u00b7 Shaman": what this character plays, for every place that asks which build uses something.</summary>
    public string Build => string.Join(" \u00b7 ", new[] { MainSkill, Class }.Where(p => !string.IsNullOrEmpty(p)));

    /// <summary>Equipment, jewels and flasks in slot order.</summary>
    public IEnumerable<GearPiece> InSlotOrder => Gear.OrderBy(g => g.Kind).ThenBy(g => GearSlots.IndexOf(g.Slot)).ThenBy(g => g.Title, StringComparer.OrdinalIgnoreCase);

    /// <summary>The pieces that are what a build filter names (the item the search was about).</summary>
    public IEnumerable<GearPiece> Matching(string? itemName) => Gear.Where(g => g.Matches(itemName));

    /// <param name="mainSkill">The skill poe.ninja shows the DPS of; the first active gem when the search does not name one.</param>
    public static SampledCharacter From(CharacterRef reference, NinjaCharacter character, GameData data, string? mainSkill = null) => new(
        reference, character.Class, character.Level, mainSkill ?? character.SkillNames.FirstOrDefault(), character.SkillNames.ToList(),
        character.Items.Concat(character.Jewels).Concat(character.Flasks)
            .Select(slot => slot.ItemData)
            .Zip(KindsOf(character), (json, kind) => json == null ? null : GearPiece.From(json, kind, data))
            .OfType<GearPiece>().ToList());

    private static IEnumerable<GearKind> KindsOf(NinjaCharacter character) =>
        Enumerable.Repeat(GearKind.Equipment, character.Items.Count)
            .Concat(Enumerable.Repeat(GearKind.Jewel, character.Jewels.Count))
            .Concat(Enumerable.Repeat(GearKind.Flask, character.Flasks.Count));
}
