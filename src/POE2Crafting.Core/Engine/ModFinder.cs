using System.Collections.Concurrent;
using POE2Crafting.Core.Data;

namespace POE2Crafting.Core.Engine;

/// <summary>One searchable modifier: all tiers of one family and stat within one category (the tiers of a modifier type).</summary>
public sealed record ModGroup(string Key, string Label, string Category, AffixType Affix, IReadOnlyList<ModDef> Tiers)
{
    /// <summary>Item classes whose bases list this modifier on one of their pages.</summary>
    public IReadOnlyList<string> Classes { get; init; } = Array.Empty<string>();

    /// <summary>The best tier: the list is sorted by level, highest first.</summary>
    public ModDef Best => Tiers[0];
    public string Name => Best.Name;

    /// <summary>The crafting item that grants this modifier directly — an essence, a liquid emotion or a rune — or null when it is rolled.</summary>
    public string? GrantedBy => ModCategories.IsRolled(Category) ? null : Name;
    public IReadOnlyList<string> ModTags => Tiers.SelectMany(m => m.ModTags).Distinct().ToList();

    /// <summary>Everything a search should look at: the stat, the affix name, the family and the modifier tags.</summary>
    public IEnumerable<string?> SearchTexts => Tiers.SelectMany(m => new[] { m.Text, m.Name, m.Family }.Concat(m.ModTags)).Append(Label);
}

/// <summary>One tier of a modifier on a base: its tier number on that base (null when the modifier has no tiers there) and its weight.</summary>
public readonly record struct TierOnBase(ModDef Mod, int? Tier, int TierCount, int Weight);

/// <summary>
/// Bases a modifier reaches in the same way: same item class, same tiers, same weights. Rune-unlocked modifiers also name
/// the runes that unlock them on those bases.
/// </summary>
public sealed record ModPlacement(string ItemClass, string ClassGroup, IReadOnlyList<BaseItem> Bases,
    IReadOnlyList<TierOnBase> Tiers, IReadOnlyList<AugmentDef> UnlockingRunes, int CompetingLow, int CompetingHigh)
{
    public int Weight => Tiers.Sum(t => t.Weight);

    /// <summary>Lowest share of this modifier among everything of its affix type that competes with it on these bases; null when it is not rolled.</summary>
    public double? ChanceLow => CompetingHigh > 0 ? (double)Weight / CompetingHigh : null;

    /// <summary>Highest share on these bases: the pool a modifier competes in differs a little from base to base.</summary>
    public double? ChanceHigh => CompetingLow > 0 ? (double)Weight / CompetingLow : null;

    /// <summary>Defence types of the bases, for a short description of the group ("Armour", "Evasion/Energy Shield").</summary>
    public IReadOnlyList<string> SubTypes => Bases.Select(b => b.SubType).OfType<string>().Distinct().ToList();
}

/// <summary>A unique item that grants a modifier itself, with the line as the unique rolls it.</summary>
public sealed record UniqueWithMod(UniqueItemDef Unique, ModDef Mod);

/// <summary>
/// Answers "where can this modifier appear": every modifier type of the data store, and for one of them the bases that can have it
/// together with what it takes there (a roll, a bone, an essence, a socketed rune). Read-only after loading, shared by all sessions.
/// </summary>
public sealed partial class ModFinder
{
    private readonly GameData _data;
    private readonly ModPool _pool;
    private readonly List<BaseItem> _bases;
    private readonly Dictionary<string, int> _classOrder;
    private readonly ILookup<string, AugmentDef> _runesByCategory;
    private readonly ConcurrentDictionary<string, IReadOnlyList<ModPlacement>> _placements = new();
    private readonly ILookup<string, UniqueWithMod> _uniqueModsBySignature;
    private readonly Dictionary<string, ModGroup> _byKey;
    private readonly ILookup<string, string> _classesByPage;

    /// <summary>Every modifier type, sorted by its stat.</summary>
    public IReadOnlyList<ModGroup> Groups { get; }

    public ModFinder(GameData data, ModPool pool)
    {
        _data = data;
        _pool = pool;
        _bases = data.Bases.Where(b => !b.Hidden).ToList();
        _classOrder = data.ItemClasses.Select((c, i) => (c.Name, i)).ToDictionary(c => c.Name, c => c.i);
        _runesByCategory = data.Augments
            .SelectMany(a => a.Effects.Select(e => (Rune: a, Category: ModCategories.UnlockedBy(e.Text))))
            .Where(x => x.Category != null).ToLookup(x => x.Category!, x => x.Rune);

        _classesByPage = ClassesByPage();
        _uniqueModsBySignature = data.Uniques
            .SelectMany(u => u.ModDefs.Select(m => new UniqueWithMod(u, m)))
            .ToLookup(x => x.Mod.StatSignature);
        Groups = data.Mods.GroupBy(ModTiers.TierGroupKey)
            .Select(g => new ModGroup(g.Key, ModTiers.GroupLabel(g), g.First().Category, g.First().AffixType,
                g.OrderByDescending(m => m.Level).ThenBy(m => m.Name, StringComparer.Ordinal).ToList())
            {
                Classes = ClassesOf(g),
            })
            .Concat(UniqueGroups())
            .OrderBy(g => g.Label, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Category, StringComparer.Ordinal).ToList();
        _byKey = Groups.ToDictionary(g => g.Key);
    }

    public ModGroup? Find(string? key) => key != null && _byKey.TryGetValue(key, out var g) ? g : null;

    /// <summary>
    /// The modifiers of unique items, one entry per stat: a unique's line is not on any page, so it never has a placement —
    /// what it has is the list of uniques that grant it.
    /// </summary>
    private IEnumerable<ModGroup> UniqueGroups() => _uniqueModsBySignature
        .Select(g => new ModGroup($"unique|{g.Key}", ModTiers.GroupLabel(g.Select(x => x.Mod)), ModCategories.Unique, AffixType.Other,
            g.Select(x => x.Mod).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList())
        {
            Classes = g.Select(x => _data.FindBase(x.Unique.BaseType)?.ItemClass).OfType<string>().Distinct().OrderBy(ClassOrder).ToList(),
        });

    /// <summary>The uniques that grant this stat themselves, whatever the group's own category is.</summary>
    public IReadOnlyList<UniqueWithMod> UniquesWith(ModGroup group) =>
        _uniqueModsBySignature[group.Best.StatSignature].OrderBy(x => x.Unique.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The item classes that have bases on a modifier page (a base uses its own page, or all pages of its class).</summary>
    private ILookup<string, string> ClassesByPage() => _bases
        .SelectMany(b => _pool.PagesFor(b).Select(page => (page, b.ItemClass)))
        .Distinct().ToLookup(x => x.page, x => x.ItemClass);

    /// <summary>The item classes whose bases list one of these modifiers on one of their pages.</summary>
    private IReadOnlyList<string> ClassesOf(IEnumerable<ModDef> mods) => mods
        .SelectMany(m => m.Weights.Keys.Where(m.IsOnPage)).SelectMany(page => _classesByPage[page])
        .Distinct().OrderBy(ClassOrder).ToList();

    private int ClassOrder(string itemClass) => _classOrder.TryGetValue(itemClass, out var i) ? i : int.MaxValue;

    /// <summary>Where a modifier can appear, grouped by item class and by the tiers and weights the bases share.</summary>
    public IReadOnlyList<ModPlacement> Placements(ModGroup group, bool includeRuneforged = false) =>
        _placements.GetOrAdd(group.Key + (includeRuneforged ? "|runeforged" : ""), _ => Build(group, includeRuneforged));

    private IReadOnlyList<ModPlacement> Build(ModGroup group, bool includeRuneforged)
    {
        var competing = CompetingCategories(group.Category);
        var groups = new Dictionary<string, Variant>();

        foreach (var baseItem in _bases.Where(b => includeRuneforged || !b.IsRuneforged))
        {
            var tiers = group.Tiers.Where(m => _pool.CanAppearOn(m, baseItem)).Select(m =>
            {
                var rank = _pool.TryRank(m, baseItem);
                return new TierOnBase(m, rank?.Tier, rank?.Count ?? 0, _pool.WeightForBase(m, baseItem));
            }).OrderBy(t => t.Tier ?? int.MaxValue).ThenByDescending(t => t.Mod.Level).ToList();
            if (tiers.Count == 0) continue;

            var runes = _runesByCategory[group.Category].Where(r => _data.AugmentEffectsFor(r, baseItem, baseItem.ItemClass).Count > 0).ToList();
            var total = competing.Count == 0 ? 0 : _pool.TotalWeight(baseItem, group.Affix, competing);
            var key = string.Join("|", baseItem.ItemClass, string.Join(",", tiers.Select(t => $"{t.Mod.Id}:{t.Tier}:{t.Weight}")),
                string.Join(",", runes.Select(r => r.Name)));
            if (!groups.TryGetValue(key, out var variant))
                groups[key] = variant = new Variant(tiers, runes);
            variant.Add(baseItem, total);
        }

        return groups.Values.Select(v => new ModPlacement(v.Bases[0].ItemClass, ClassGroupOf(v.Bases[0]), v.Bases,
                v.Tiers, v.Runes, v.CompetingLow, v.CompetingHigh))
            .OrderBy(p => ClassOrder(p.ItemClass)).ThenByDescending(p => p.Weight).ThenByDescending(p => p.Bases.Count)
            .ThenBy(p => p.Bases[0].Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Bases collected while building a placement: they share the tiers and runes, only the pool they compete in can differ.</summary>
    private sealed class Variant(List<TierOnBase> tiers, List<AugmentDef> runes)
    {
        public List<BaseItem> Bases { get; } = new();
        public List<TierOnBase> Tiers { get; } = tiers;
        public List<AugmentDef> Runes { get; } = runes;
        public int CompetingLow { get; private set; } = int.MaxValue;
        public int CompetingHigh { get; private set; }

        public void Add(BaseItem baseItem, int competing)
        {
            Bases.Add(baseItem);
            CompetingLow = Math.Min(CompetingLow, competing);
            CompetingHigh = Math.Max(CompetingHigh, competing);
        }
    }

    private string ClassGroupOf(BaseItem baseItem) => _data.FindItemClass(baseItem.ItemClass)?.Group ?? "";

    /// <summary>
    /// The categories a modifier is drawn against when it rolls: the denominator of its chance. A rune-unlocked modifier joins the
    /// base modifiers of its item, an otherworldly one joins the desecrated pool; modifiers that are granted directly are never drawn.
    /// </summary>
    private static IReadOnlyList<string> CompetingCategories(string category) => category switch
    {
        _ when !ModCategories.IsRolled(category) => Array.Empty<string>(),
        ModCategories.Otherworldly => new[] { ModCategories.Desecrated, ModCategories.Otherworldly },
        _ when ModCategories.RuneUnlocked.Contains(category) => new[] { ModCategories.Normal, category },
        _ => new[] { category },
    };
}
