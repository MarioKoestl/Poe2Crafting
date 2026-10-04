using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine;

/// <summary>How a crafting item puts a modifier on an item; the order is the order of the overview.</summary>
public enum ModSourceKind { Essence, LiquidEmotion, Augment, BondedEffect, UnlockingRune, Bone, Corruption }

/// <summary>What a crafting item adds for a search: the modifier type, the exact lines and the item classes it works on.</summary>
public sealed record SourceEffect(ModGroup Group, IReadOnlyList<ModDef> Mods, IReadOnlyList<string> Classes)
{
    /// <summary>The line itself when the item grants exactly one, otherwise the modifier type (a bone reveals one of its tiers).</summary>
    public string Text => Mods.Count == 1 ? Mods[0].Text : Group.Label;
}

/// <summary>A crafting item — essence, rune, bone, corruption currency — that can add a modifier matching the search.</summary>
public sealed record SourceHit(string Name, ModSourceKind Kind, string KindLabel, IReadOnlyList<SourceEffect> Effects);

/// <summary>The rolled modifiers matching a search that one item class can get.</summary>
public sealed record ClassHits(string ItemClass, string ClassGroup, IReadOnlyList<ModGroup> Mods);

/// <summary>A unique item with the lines of it that match a search.</summary>
public sealed record UniqueHit(UniqueItemDef Unique, IReadOnlyList<ModDef> Lines);

/// <summary>
/// Everything that can put a stat on an item, for one search ("thorns"): the matching modifier types, the item classes that roll them,
/// the crafting items that add them, the corruption results and the uniques that have them.
/// </summary>
public sealed record ModSearchResult(string Query, IReadOnlyList<ModGroup> Groups, IReadOnlyList<ClassHits> Classes,
    IReadOnlyList<SourceHit> Sources, IReadOnlyList<UniqueHit> Uniques)
{
    public IEnumerable<SourceHit> CraftingItems => Sources.Where(s => s.Kind != ModSourceKind.Corruption);
    public IEnumerable<SourceHit> Corruption => Sources.Where(s => s.Kind == ModSourceKind.Corruption);
    public bool IsEmpty => Groups.Count == 0 && Uniques.Count == 0;
}

public sealed partial class ModFinder
{
    /// <summary>
    /// Every modifier type, crafting item and unique that matches the search (<see cref="TextSearch"/>: every word in the stat,
    /// affix name, family or tags). An empty search matches every modifier type but builds no overview.
    /// </summary>
    public ModSearchResult Search(string? query)
    {
        var groups = Groups.Where(g => TextSearch.Matches(query, g.SearchTexts)).ToList();
        if (string.IsNullOrWhiteSpace(query))
            return new ModSearchResult("", groups, Array.Empty<ClassHits>(), Array.Empty<SourceHit>(), Array.Empty<UniqueHit>());

        var craftable = groups.Where(g => g.Category != ModCategories.Unique).ToList();
        return new ModSearchResult(query.Trim(), groups, ClassHitsOf(craftable), SourcesOf(craftable), UniquesMatching(query));
    }

    /// <summary>Modifiers that roll on a base (base, desecrated, otherworldly, Genesis Tree and rune-unlocked ones), per item class.</summary>
    private IReadOnlyList<ClassHits> ClassHitsOf(IEnumerable<ModGroup> groups) => groups
        .Where(g => ModCategories.IsRolled(g.Category) && !IsCorruption(g.Category))
        .SelectMany(g => g.Classes.Select(c => (Class: c, Group: g)))
        .GroupBy(x => x.Class)
        .OrderBy(c => ClassOrder(c.Key))
        .Select(c => new ClassHits(c.Key, _data.FindItemClass(c.Key)?.Group ?? "", c.Select(x => x.Group)
            .OrderBy(g => g.Category == ModCategories.Normal ? 0 : 1).ThenBy(g => g.Affix).ThenBy(g => g.Label, StringComparer.OrdinalIgnoreCase)
            .ToList()))
        .ToList();

    private static bool IsCorruption(string category) => category is ModCategories.Corrupted or ModCategories.CorruptionUpgrade;

    /// <summary>The crafting items that add one of these modifiers, each with what it adds and where.</summary>
    private IReadOnlyList<SourceHit> SourcesOf(IEnumerable<ModGroup> groups) => groups
        .SelectMany(SourcesOf)
        .GroupBy(s => s.Name)
        .Select(s => new SourceHit(s.Key, s.First().Kind, s.First().KindLabel, s.Select(x => x.Effect).ToList()))
        .OrderBy(s => s.Kind).ThenBy(s => s.KindLabel, StringComparer.Ordinal).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private IEnumerable<(string Name, ModSourceKind Kind, string KindLabel, SourceEffect Effect)> SourcesOf(ModGroup group)
    {
        switch (group.Category)
        {
            case ModCategories.Essence or ModCategories.PerfectEssence or ModCategories.Liquid:
                return Granting(group, group.Category == ModCategories.Liquid ? ModSourceKind.LiquidEmotion : ModSourceKind.Essence, KindOf);
            case ModCategories.Socketable:
                return Granting(group, ModSourceKind.Augment, KindOf);
            case ModCategories.Bonded:
                return Granting(group, ModSourceKind.BondedEffect, name => $"{KindOf(name)} · bonded");
            case ModCategories.Desecrated or ModCategories.Otherworldly:
                return Using(group, _data.CurrenciesOf(CurrencyOps.Desecrate)
                    .Where(b => group.Category == ModCategories.Desecrated || b.Otherworldly == true), ModSourceKind.Bone, "Bone");
            case ModCategories.Corrupted:
                return Using(group, _data.CurrenciesOf(CurrencyOps.Vaal).Concat(_data.CurrenciesOf(CurrencyOps.Architect)),
                    ModSourceKind.Corruption, "Enchantment");
            case ModCategories.CorruptionUpgrade:
                return Using(group, _data.CurrenciesOf(CurrencyOps.Sacrifice), ModSourceKind.Corruption, "Upgrade");
            case var category when ModCategories.RuneUnlocked.Contains(category):
                return _runesByCategory[category].Select(rune => (rune, classes: group.Classes
                        .Where(c => _data.AugmentEffectsFor(rune, null, c).Count > 0).ToList()))
                    .Where(x => x.classes.Count > 0)
                    .Select(x => (x.rune.Name, ModSourceKind.UnlockingRune, "Unlocks the modifier",
                        new SourceEffect(group, group.Tiers, x.classes)));
            default:
                return Array.Empty<(string, ModSourceKind, string, SourceEffect)>();
        }
    }

    /// <summary>Items that grant their own line directly: every tier of the group is a different item (Lesser/Greater/Perfect essence).</summary>
    private IEnumerable<(string, ModSourceKind, string, SourceEffect)> Granting(ModGroup group, ModSourceKind kind, Func<string, string> label) =>
        group.Tiers.GroupBy(m => m.Name).Select(m => (m.Key, kind, label(m.Key), new SourceEffect(group, m.ToList(), ClassesOf(m))));

    /// <summary>Currencies that roll the modifier on the item classes they target.</summary>
    private IEnumerable<(string, ModSourceKind, string, SourceEffect)> Using(ModGroup group, IEnumerable<CurrencyDef> currencies,
        ModSourceKind kind, string label) => currencies
        .Where(c => _data.IsAvailable(c.Name))
        .Select(c => (c, classes: group.Classes.Where(itemClass => _data.ClassMatchesTarget(itemClass, c.ClassTarget)).ToList()))
        .Where(x => x.classes.Count > 0)
        .Select(x => (x.c.Name, kind, label, new SourceEffect(group, group.Tiers, x.classes)));

    /// <summary>"Essence", "Alloy", "Liquid Emotion", "Rune", "Soul Core" — as the crafting item popovers name it.</summary>
    private string KindOf(string name) => _data.FindCraftItem(name)?.Kind ?? AugmentDef.KindOf(name);

    /// <summary>Uniques with a line (explicit or implicit) that matches the search by itself.</summary>
    private IReadOnlyList<UniqueHit> UniquesMatching(string query) => _data.Uniques
        .Select(u => new UniqueHit(u, u.ImplicitDefs.Concat(u.ModDefs).Where(m => TextSearch.Matches(query, m.Text)).ToList()))
        .Where(u => u.Lines.Count > 0)
        .OrderBy(u => u.Unique.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();
}
