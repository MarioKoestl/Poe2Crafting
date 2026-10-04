using POE2Crafting.Core.Data;

namespace POE2Crafting.Web.Services;

/// <summary>Display helpers of the modifier finder: category badges and short item class lists.</summary>
public static class ModUi
{
    /// <summary>Badge colour of a modifier category; base modifiers get the plain badge.</summary>
    public static string CategoryCss(string category) => category switch
    {
        ModCategories.Desecrated => "cat-badge-desecrated",
        ModCategories.Otherworldly => "cat-badge-otherworldly",
        ModCategories.GenesisCaster or ModCategories.GenesisMinion => "cat-badge-genesis",
        ModCategories.Essence or ModCategories.PerfectEssence or ModCategories.Liquid => "cat-badge-crafted",
        ModCategories.Unique => "cat-badge-unique",
        ModCategories.Socketable or ModCategories.Bonded => "cat-badge-rune",
        ModCategories.Corrupted or ModCategories.CorruptionUpgrade => "cat-badge-corruption",
        _ when ModCategories.RuneUnlocked.Contains(category) => "cat-badge-rune",
        _ => "",
    };

    /// <summary>Item classes shortened to a readable line ("Gloves, Boots, Helmets and 4 more").</summary>
    public static string ClassSummary(IReadOnlyList<string> classes, int shown = 3) => classes.Count switch
    {
        0 => "no base",
        _ when classes.Count <= shown => string.Join(", ", classes),
        _ => string.Join(", ", classes.Take(shown)) + $" and {classes.Count - shown} more",
    };
}
