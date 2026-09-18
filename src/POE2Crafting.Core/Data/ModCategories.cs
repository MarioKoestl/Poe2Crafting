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
