namespace POE2Crafting.Core.Data;

/// <summary>
/// Item class groups used by currency targets (currencies.json Target/QualityTarget) and engine rules. The only place that knows which
/// classes a group contains: everything else refers to the group by its key.
/// </summary>
public static class ClassTargets
{
    public const string WeaponOrQuiver = "weapon_or_quiver", MartialWeapon = "martial_weapon", CasterWeapon = "caster_weapon";
    public const string Armour = "armour", Jewellery = "jewellery", RingOrAmulet = "ring_or_amulet", Jewel = "jewel", Flask = "flask";
    /// <summary>Artificer's Orb: "Martial Weapon, wand, staff or Armour".</summary>
    public const string Socketable = "socketable";
    /// <summary>Vaal Orb outcome groups: extra socket vs. extra quality.</summary>
    public const string MartialWeaponOrArmour = "martial_weapon_or_armour", WandOrStaff = "wand_or_staff";
    public const string Equipment = "equipment", EquipmentOrJewel = "equipment_or_jewel";
    /// <summary>Liquid Emotions can only instill notables on this class.</summary>
    public const string InstillClass = "Amulet";

    private sealed record Group(string DisplayName, Func<ItemClassDef, bool> Matches);

    private static readonly Dictionary<string, Group> Groups = new()
    {
        [WeaponOrQuiver] = new("Weapons or Quivers", c => c.Group.EndsWith("Weapon", StringComparison.Ordinal) || c.Name == "Quiver"),
        [MartialWeapon] = new("Martial Weapons", c => c.Group == "Martial Weapon"),
        [CasterWeapon] = new("Wands, Staves or Sceptres", c => c.Group == "Caster Weapon"),
        [Armour] = new("Armour", c => c.Group == "Armour" || c.Name == "Focus"),
        [Jewellery] = new("Amulets, Rings or Belts", c => c.Group == "Jewellery"),
        [RingOrAmulet] = new("Rings or Amulets", c => c.Name is "Ring" or "Amulet"),
        [Jewel] = new("Jewels", c => c.Group == "Jewel"),
        [Flask] = new("Flasks", c => c.Name.EndsWith("Flask", StringComparison.Ordinal)),
        [Socketable] = new("Martial Weapons, Wands, Staves or Armour", c => c.Group is "Martial Weapon" or "Armour" || c.Name is "Wand" or "Staff" or "Focus"),
        [MartialWeaponOrArmour] = new("Martial Weapons or Armour", c => c.Group is "Martial Weapon" or "Armour" || c.Name == "Focus"),
        [WandOrStaff] = new("Wands or Staves", c => c.Name is "Wand" or "Staff"),
        [Equipment] = new("Equipment", c => c.Group is not ("Flask" or "Jewel")),
        [EquipmentOrJewel] = new("Equipment or Jewels", c => c.Group != "Flask"),
    };

    /// <summary>Whether the class belongs to the group; no target = any class, an unknown class or group = no.</summary>
    public static bool Contains(string? target, ItemClassDef? itemClass) =>
        target == null || itemClass != null && Groups.TryGetValue(target, out var group) && group.Matches(itemClass);

    /// <summary>Human readable name of a group.</summary>
    public static string DisplayName(string? target) => target != null && Groups.TryGetValue(target, out var g) ? g.DisplayName : "any item";
}
