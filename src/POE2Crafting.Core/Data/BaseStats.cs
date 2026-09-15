namespace POE2Crafting.Core.Data;

/// <summary>How a base stat is shown.</summary>
public enum BaseStatFormat { Number, Decimal, Percent, Seconds, Text }

/// <summary>
/// One comparable stat of base items (a column of the Bases page): value for sorting and "best" highlighting, optional text (damage ranges).
/// </summary>
/// <param name="HigherIsBetter">True: the highest value is the best base; false: the lowest (reload time); null: no best (requirements, movement penalty).</param>
public sealed record BaseStat(string Label, string Description, Func<BaseItem, double?> Value, BaseStatFormat Format = BaseStatFormat.Number,
    bool? HigherIsBetter = true, Func<BaseItem, string?>? Text = null);

/// <summary>The stats of base items: defences, weapon damage and DPS, flask values, requirements. Only stats some of the given bases have are listed.</summary>
public static class BaseStats
{
    private static readonly string[] Elements = { "Fire", "Cold", "Lightning" };
    private static readonly string[] NonPhysical = { "Fire", "Cold", "Lightning", "Chaos" };

    private static double? Defence(BaseItem b, string key) => b.Armour?.GetValueOrDefault(key) is > 0 and var v ? v : null;
    private static double? WeaponStat(BaseItem b, string key) => b.Weapon?.GetValueOrDefault(key) is > 0 and var v ? v : null;
    private static double? Requirement(BaseItem b, string key) => b.Requirements.GetValueOrDefault(key) is > 0 and var v ? v : null;

    /// <summary>Average hit of the damage types (e.g. Physical; or Fire+Cold+Lightning).</summary>
    private static double? AverageDamage(BaseItem b, params string[] types)
    {
        double sum = types.Sum(t => ((WeaponStat(b, t + "Min") ?? 0) + (WeaponStat(b, t + "Max") ?? 0)) / 2);
        return sum > 0 ? sum : null;
    }

    private static string? DamageText(BaseItem b, params string[] types)
    {
        var parts = types.Where(t => WeaponStat(b, t + "Max") != null)
            .Select(t => $"{WeaponStat(b, t + "Min") ?? 0:0}–{WeaponStat(b, t + "Max"):0}" + (types.Length > 1 ? $" {t}" : ""));
        return string.Join(", ", parts) is { Length: > 0 } text ? text : null;
    }

    private static double? Dps(BaseItem b, params string[] types) =>
        AverageDamage(b, types) is { } hit && WeaponStat(b, "AttackRateBase") is { } aps ? hit * aps : null;

    /// <summary>Every stat, in column order.</summary>
    public static readonly IReadOnlyList<BaseStat> All = new BaseStat[]
    {
        new("Armour", "Base armour", b => Defence(b, "Armour")),
        new("Evasion", "Base evasion rating", b => Defence(b, "Evasion")),
        new("Energy Shield", "Base energy shield", b => Defence(b, "EnergyShield")),
        new("Ward", "Base ward (Runeforged bases)", b => Defence(b, "Ward")),
        new("Block", "Chance to block", b => Defence(b, "BlockChance"), BaseStatFormat.Percent),
        new("Movement", "Movement speed penalty", b => Defence(b, "MovementPenalty") * 100, BaseStatFormat.Percent, HigherIsBetter: null),
        new("Physical", "Physical damage per hit", b => AverageDamage(b, "Physical"), Text: b => DamageText(b, "Physical")),
        new("Elemental", "Fire, cold and lightning damage per hit", b => AverageDamage(b, Elements), Text: b => DamageText(b, Elements)),
        new("Chaos", "Chaos damage per hit", b => AverageDamage(b, "Chaos"), Text: b => DamageText(b, "Chaos")),
        new("APS", "Attacks per second", b => WeaponStat(b, "AttackRateBase"), BaseStatFormat.Decimal),
        new("Crit", "Critical hit chance", b => WeaponStat(b, "CritChanceBase"), BaseStatFormat.Percent),
        new("pDPS", "Physical damage per second (average hit × attacks per second)", b => Dps(b, "Physical")),
        new("eDPS", "Elemental damage per second", b => Dps(b, Elements)),
        // only for bases with non-physical damage (otherwise it equals pDPS)
        new("DPS", "Total damage per second (physical, elemental and chaos)",
            b => AverageDamage(b, NonPhysical) != null ? Dps(b, ["Physical", .. NonPhysical]) : null),
        new("Reload", "Reload time", b => WeaponStat(b, "ReloadTimeBase"), BaseStatFormat.Seconds, HigherIsBetter: false),
        new("Life", "Life recovered", b => b.Flask?.Life),
        new("Mana", "Mana recovered", b => b.Flask?.Mana),
        new("Duration", "Recovery / effect duration", b => (b.Flask ?? b.Charm)?.Duration, BaseStatFormat.Seconds, HigherIsBetter: null),
        new("Charges", "Charges used per use / maximum charges", b => (b.Flask ?? b.Charm)?.ChargesMax, HigherIsBetter: null,
            Text: b => (b.Flask ?? b.Charm) is { ChargesMax: { } max } f ? $"{f.ChargesUsed}/{max}" : null),
        new("Sockets", "Maximum augment sockets", b => b.SocketLimit),
        new("Level", "Required level", b => Requirement(b, "level"), HigherIsBetter: null),
        new("Str", "Required strength", b => Requirement(b, "str"), HigherIsBetter: null),
        new("Dex", "Required dexterity", b => Requirement(b, "dex"), HigherIsBetter: null),
        new("Int", "Required intelligence", b => Requirement(b, "int"), HigherIsBetter: null),
    };

    /// <summary>The stats that at least one of the bases has.</summary>
    public static IReadOnlyList<BaseStat> For(IReadOnlyCollection<BaseItem> bases) => All.Where(s => bases.Any(b => s.Value(b) != null)).ToList();

    /// <summary>The defence stat named in an armour sub type ("Energy Shield" → Energy Shield; hybrids → their first defence).</summary>
    public static BaseStat? PrimaryOf(string? subType) =>
        subType == null ? null : All.FirstOrDefault(s => subType.Split('/')[0] == s.Label);
}
