using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Data;

/// <summary>ModDef.Category values (poe2db ModsView categories) used by the simulator.</summary>
public static class ModCategories
{
    public const string Normal = "normal";
    public const string Desecrated = "desecrated";
    public const string Otherworldly = "breach_otherworldly";
    /// <summary>The Genesis Tree (Breach) grants these to rings and belts; poe2db calls them breach_caster / breach_minion.</summary>
    public const string GenesisCaster = "breach_caster", GenesisMinion = "breach_minion";
    public const string Essence = "essence";
    public const string PerfectEssence = "perfect_essence";
    public const string Corrupted = "corrupted";
    public const string CorruptionUpgrade = "corruption_upgrade";
    public const string Socketable = "socketable";
    public const string Bonded = "bonded";
    /// <summary>A modifier a unique item grants itself (data/uniques.json); nothing rolls it.</summary>
    public const string Unique = "unique";

    /// <summary>Jewel crafting with Liquid Emotions (guaranteed crafted modifier per jewel type).</summary>
    public const string Liquid = "liquid";

    /// <summary>Modifiers The Genesis Tree adds to rings and belts (its Caster and Minion branches); the mechanic itself is not simulated.</summary>
    public static readonly string[] GenesisTree = { GenesisCaster, GenesisMinion };

    /// <summary>Categories whose mods are the guaranteed result of an essence, alloy or liquid emotion.</summary>
    public static readonly string[] EssenceResults = { Essence, PerfectEssence, Liquid };

    public const string Berserking = "berserking", Marksman = "marksman", Decay = "decay", Soul = "soul", Chronomancy = "chronomancy", Destruction = "destruction";

    /// <summary>Modifier types a socketed rune unlocks ("Can roll Destruction modifiers", e.g. Thrud's Might); they roll like normal modifiers while the rune is in the item.</summary>
    public static readonly string[] RuneUnlocked = { Berserking, Marksman, Decay, Soul, Chronomancy, Destruction };

    private static readonly System.Text.RegularExpressions.Regex CanRollLine = new(@"Can roll (?<type>\w+) modifiers", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>The rune-unlocked category a rune effect line names ("Can roll Destruction modifiers" → destruction), or null.</summary>
    public static string? UnlockedBy(string runeText) =>
        CanRollLine.Match(runeText) is { Success: true } m && RuneUnlocked.Contains(m.Groups["type"].Value.ToLowerInvariant()) ? m.Groups["type"].Value.ToLowerInvariant() : null;

    /// <summary>Categories unlocked by the item's socketed runes.</summary>
    public static IEnumerable<string> UnlockedFor(Item item) => item.Runes.Select(UnlockedBy).OfType<string>().Distinct();

    /// <summary>
    /// Whether modifiers of this category are drawn at random by weight. The others are granted directly (essences, runes, liquid emotions)
    /// and carry no weights in the data, so a base offers them as soon as they exist on its page.
    /// </summary>
    public static bool IsRolled(string category) => category is not (Essence or PerfectEssence or Liquid or Socketable or Bonded or Unique);

    /// <summary>Name of a category for display.</summary>
    public static string DisplayName(string category) => category switch
    {
        Normal => "Base modifier",
        Desecrated => "Desecrated",
        Otherworldly => "Otherworldly",
        GenesisCaster => "Genesis Tree · Caster",
        GenesisMinion => "Genesis Tree · Minion",
        Essence => "Essence",
        PerfectEssence => "Perfect essence",
        Liquid => "Liquid Emotion",
        Socketable => "Rune or Soul Core",
        Bonded => "Bonded rune effect",
        Unique => "Unique modifier",
        Corrupted => "Corruption enchantment",
        CorruptionUpgrade => "Upgraded corruption enchantment",
        _ => char.ToUpperInvariant(category[0]) + category[1..],
    };

    /// <summary>How a modifier of this category gets onto an item.</summary>
    public static string SourceText(string category) => category switch
    {
        Normal => "Rolls with the currencies that add modifiers: Orb of Transmutation, Augmentation, Regal Orb, Exalted Orb, Chaos Orb and essences.",
        Desecrated => "Added unrevealed by a bone, then revealed at the Well of Souls.",
        Otherworldly => "Added unrevealed by an Altered Collarbone, then revealed at the Well of Souls.",
        GenesisCaster or GenesisMinion => "Granted by The Genesis Tree. The mechanic itself is not simulated.",
        Essence or PerfectEssence => "Guaranteed by the essence or alloy of the same name.",
        Liquid => "Guaranteed by the Liquid Emotion of the same name.",
        Unique => "Granted by the unique item itself; it rolls inside the unique's own range and has no tier.",
        Socketable => "Granted while the rune or soul core sits in a socket.",
        Bonded => "Bonded effect of a rune, for a Shaman's companion. Not simulated.",
        Corrupted => "Added as an enchantment by a Vaal Orb.",
        CorruptionUpgrade => "An Orb of Sacrifice upgrades a corruption enchantment to this one.",
        _ when RuneUnlocked.Contains(category) => "Rolls like a base modifier while a rune that unlocks this modifier type is socketed.",
        _ => "",
    };

    /// <summary>How a mod of this category appears on an item.</summary>
    public static ModKind KindFor(string category) => category switch
    {
        Desecrated => ModKind.Desecrated,
        PerfectEssence or Liquid => ModKind.Crafted,
        Corrupted or CorruptionUpgrade => ModKind.CorruptedImplicit,
        _ => ModKind.Explicit,
    };

    /// <summary>Whether a mod of this category can be a line of this kind on an item (e.g. a crafted line comes from an essence result).</summary>
    public static bool CanAppearAs(string category, ModKind kind) => kind switch
    {
        ModKind.Crafted => EssenceResults.Contains(category),
        ModKind.Desecrated => category == Desecrated,
        ModKind.CorruptedImplicit or ModKind.Enchant => category is Corrupted or CorruptionUpgrade,
        _ => category is not (Corrupted or CorruptionUpgrade or Socketable or Bonded) && !EssenceResults.Contains(category),
    };
}

/// <summary>Mod families the rules refer to by name.</summary>
public static class ModFamilies
{
    /// <summary>"+#% to Maximum Quality" (Essence of the Breach).</summary>
    public const string MaximumQuality = "LocalMaximumQuality";
    /// <summary>"Mark of the Abyssal Lord" (Essence of the Abyss): the next desecration replaces it.</summary>
    public const string AbyssMark = "EssenceAbyss";
}
