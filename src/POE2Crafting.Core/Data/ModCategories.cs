using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Data;

/// <summary>ModDef.Category values (poe2db ModsView categories) used by the simulator.</summary>
public static class ModCategories
{
    public const string Normal = "normal";
    public const string Desecrated = "desecrated";
    public const string Otherworldly = "breach_otherworldly";
    public const string Essence = "essence";
    public const string PerfectEssence = "perfect_essence";
    public const string Corrupted = "corrupted";
    public const string CorruptionUpgrade = "corruption_upgrade";
    public const string Socketable = "socketable";
    public const string Bonded = "bonded";

    /// <summary>Jewel crafting with Liquid Emotions (guaranteed crafted modifier per jewel type).</summary>
    public const string Liquid = "liquid";

    /// <summary>Categories whose mods are the guaranteed result of an essence, alloy or liquid emotion.</summary>
    public static readonly string[] EssenceResults = { Essence, PerfectEssence, Liquid };

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
