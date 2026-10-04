using System.Collections.Concurrent;
using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine;

/// <summary>Computes the pool of modifiers that can roll on an item in its current state. Thread-safe: one instance is shared by all sessions.</summary>
public sealed class ModPool
{
    private readonly GameData _data;
    /// <summary>category -> page -> mods with a positive weight on that page.</summary>
    private readonly Dictionary<string, Dictionary<string, List<ModDef>>> _byCategoryPage = new();
    /// <summary>Display tiers per (pages, base tags) key.</summary>
    private readonly ConcurrentDictionary<string, Dictionary<string, ModTiers.Rank>> _tierCache = new();
    /// <summary>Total affix type weights per (pages, blocking tags, affix type, categories) key.</summary>
    private readonly ConcurrentDictionary<string, int> _weightCache = new();
    /// <summary>Every tag that blocks a modifier ("no_fire_spell_mods"): the only tags that change which mods a base can have.</summary>
    private readonly HashSet<string> _blockingTags;

    /// <summary>
    /// Non-normal categories that are browsable in the planner / composer UI: rune-unlocked ones only roll while their rune is socketed,
    /// Genesis Tree ones only come from that mechanic — none of them are part of a normal addition.
    /// </summary>
    public static readonly string[] BrowsableCategories = new[] { ModCategories.Otherworldly, ModCategories.Desecrated }
        .Concat(ModCategories.RuneUnlocked).Concat(ModCategories.GenesisTree).ToArray();

    /// <summary>Normal plus all browsable categories.</summary>
    public static readonly string[] AllCategories = BrowsableCategories.Prepend(ModCategories.Normal).ToArray();

    public ModPool(GameData data)
    {
        _data = data;
        // every category is indexed: the crafting rules only ask for the ones they roll from, the mod finder asks for all of them
        foreach (var mod in data.Mods)
        {
            if (!_byCategoryPage.TryGetValue(mod.Category, out var byPage))
                _byCategoryPage[mod.Category] = byPage = new Dictionary<string, List<ModDef>>();
            foreach (var page in mod.Weights.Keys)
                if (mod.IsOnPage(page)) (byPage.TryGetValue(page, out var l) ? l : byPage[page] = new List<ModDef>()).Add(mod);
        }
        _blockingTags = data.Mods.SelectMany(m => m.BlockingTags).ToHashSet();
    }

    /// <summary>Pages whose weights apply to this item (its base's page, or all pages of its class as a fallback).</summary>
    public IReadOnlyList<string> PagesFor(Item item) => _data.PagesFor(item.Base, item.ItemClass);

    /// <summary>Pages whose weights apply to a base (questions about a base alone, without an item on it).</summary>
    public IReadOnlyList<string> PagesFor(BaseItem baseItem) => _data.PagesFor(baseItem, baseItem.ItemClass);

    /// <summary>
    /// Candidate mods of the given affix type for the item: right page, level gated by item level and optional minimum modifier level,
    /// not blocked by the base's negative tags, family not already present, and optionally filtered further.
    /// </summary>
    public List<ModCandidate> Candidates(Item item, AffixType type, int minModLevel = 0, Func<ModDef, bool>? filter = null, string category = ModCategories.Normal)
    {
        var pages = PagesFor(item);
        var eligible = AllForBaseByCategory(item, category, type)
            .Where(mod => mod.Level <= item.ItemLevel && !item.HasFamily(mod.Family) && (filter == null || filter(mod)))
            .Select(mod => new ModCandidate { Mod = mod, Weight = WeightOn(mod, pages) })
            .Where(c => c.Weight > 0);
        return ModCandidate.Normalised(AtLeastMinimumLevel(eligible, minModLevel)
            .OrderByDescending(c => c.Weight).ThenBy(c => c.Mod.Family).ThenBy(c => c.Mod.Tier));
    }

    /// <summary>
    /// The minimum modifier level of Greater/Perfect currencies, per modifier type (tier group): tiers below it cannot roll — unless that would exclude
    /// the type entirely, then its highest eligible tier still can (poe2wiki: e.g. the level-47 "Hoarder's" rarity prefix with a Perfect Orb of Augmentation).
    /// </summary>
    private static IEnumerable<ModCandidate> AtLeastMinimumLevel(IEnumerable<ModCandidate> candidates, int minModLevel) =>
        minModLevel <= 0 ? candidates
            : candidates.GroupBy(c => ModTiers.TierGroupKey(c.Mod)).SelectMany(group =>
            {
                var high = group.Where(c => c.Mod.Level >= minModLevel).ToList();
                return high.Count > 0 ? high : group.Where(c => c.Mod.Level == group.Max(x => x.Mod.Level));
            });

    /// <summary>All normal mods that can ever appear on the item's base (ignoring current mods and item level).</summary>
    public IEnumerable<ModDef> AllForBase(Item item, AffixType? type = null) => AllForBaseByCategory(item, ModCategories.Normal, type);

    /// <summary>All mods of a category (normal, desecrated, breach_otherworldly) that can appear on the item's base.</summary>
    public IEnumerable<ModDef> AllForBaseByCategory(Item item, string category, AffixType? type = null) =>
        OnPages(PagesFor(item), item.Base?.Tags, category, type);

    /// <summary>All mods of a category that can appear on a base: same rule as for an item (right page, not blocked by the base's tags).</summary>
    public IEnumerable<ModDef> AllForBaseByCategory(BaseItem baseItem, string category, AffixType? type = null) =>
        OnPages(PagesFor(baseItem), baseItem.Tags, category, type);

    private IEnumerable<ModDef> OnPages(IReadOnlyList<string> pages, IReadOnlyList<string>? baseTags, string category, AffixType? type)
    {
        if (!_byCategoryPage.TryGetValue(category, out var byPage)) yield break;
        var tags = baseTags ?? Array.Empty<string>();
        var seen = new HashSet<string>();
        foreach (var page in pages)
        {
            if (!byPage.TryGetValue(page, out var mods)) continue;
            foreach (var mod in mods)
            {
                if (type != null && mod.AffixType != type) continue;
                if (mod.BlockingTags.Any(tags.Contains)) continue;
                if (seen.Add(mod.Id)) yield return mod;
            }
        }
    }

    /// <summary>All mods of every browsable category (incl. normal) that can appear on the item's base.</summary>
    public IEnumerable<ModDef> AllBrowsableForBase(Item item) => AllCategories.SelectMany(c => AllForBaseByCategory(item, c));

    /// <summary>All mods of every browsable category (incl. normal) that can appear on a base.</summary>
    public IEnumerable<ModDef> AllBrowsableForBase(BaseItem baseItem) => AllCategories.SelectMany(c => AllForBaseByCategory(baseItem, c));

    /// <summary>Corruption enchantments that can be added to the item: its base's pool, excluding mod groups (families) it already has as enchantments.</summary>
    public List<ModCandidate> CorruptionEnchantCandidates(Item item)
    {
        var existing = item.CorruptionEnchants.Select(e => e.Mod.Def?.Family).ToHashSet();
        var pages = PagesFor(item);
        return ModCandidate.Normalised(AllForBaseByCategory(item, ModCategories.Corrupted)
            .Where(m => !existing.Contains(m.Family))
            .Select(m => new ModCandidate { Mod = m, Weight = WeightOn(m, pages) })
            .Where(c => c.Weight > 0));
    }

    /// <summary>Weight of a mod on the pages relevant to the given item.</summary>
    public int WeightForItem(ModDef mod, Item item) => WeightOn(mod, PagesFor(item));

    /// <summary>Weight of a mod on the pages relevant to a base.</summary>
    public int WeightForBase(ModDef mod, BaseItem baseItem) => WeightOn(mod, PagesFor(baseItem));

    /// <summary>Whether a modifier can appear on a base at all: it exists on one of the base's pages and none of its blocking tags is on the base.</summary>
    public bool CanAppearOn(ModDef mod, BaseItem baseItem) =>
        mod.IsOnAnyPage(PagesFor(baseItem)) && !mod.BlockingTags.Any(baseItem.Tags.Contains);

    /// <summary>Weight of a mod on the given pages (highest page weight when a class has several pages).</summary>
    private static int WeightOn(ModDef mod, IReadOnlyList<string> pages) => pages.Select(mod.WeightOn).DefaultIfEmpty(0).Max();

    // ------------------------------------------------------------------ display tiers (single source of truth)

    /// <summary>Tiers of all mods visible on the item's base (normal + browsable categories), cached per base page/tags.</summary>
    private Dictionary<string, ModTiers.Rank> TierTable(Item item) =>
        TierTable(PagesFor(item), item.Base?.Tags, () => AllBrowsableForBase(item));

    private Dictionary<string, ModTiers.Rank> TierTable(BaseItem baseItem) =>
        TierTable(PagesFor(baseItem), baseItem.Tags, () => AllBrowsableForBase(baseItem));

    private Dictionary<string, ModTiers.Rank> TierTable(IReadOnlyList<string> pages, IReadOnlyList<string>? tags, Func<IEnumerable<ModDef>> mods) =>
        _tierCache.GetOrAdd(CacheKey(pages, tags), _ => ModTiers.RankAll(mods()));

    /// <summary>
    /// Cache key of a base's modifier pool: its pages plus the tags that actually block a modifier. Everything else a base is tagged with
    /// (its material, its league) never changes the pool, so all bases of a page share one entry.
    /// </summary>
    private string CacheKey(IReadOnlyList<string> pages, IReadOnlyList<string>? tags) =>
        string.Join(",", pages) + "|" + string.Join(",", (tags ?? Array.Empty<string>()).Where(_blockingTags.Contains).OrderBy(t => t, StringComparer.Ordinal));

    /// <summary>Per-base display tier for a single mod. Falls back to the global tier for mods outside the base's pool (e.g. imported from another class).</summary>
    public int DisplayTier(ModDef mod, Item item) => TryDisplayTier(mod, item) ?? mod.Tier;

    /// <summary>Per-base display tier, or null for mods without tiers on this base (essence/alloy results, unknown mods).</summary>
    public int? TryDisplayTier(ModDef mod, Item item) => TierTable(item).TryGetValue(mod.Id, out var r) ? r.Tier : null;

    public int DisplayTierCount(ModDef mod, Item item) => TierTable(item).TryGetValue(mod.Id, out var r) ? r.Count : mod.TierCount;

    /// <summary>Per-base tier and tier count, or null for mods that have no tiers on that base (rune effects, essence results).</summary>
    public ModTiers.Rank? TryRank(ModDef mod, BaseItem baseItem) => TierTable(baseItem).TryGetValue(mod.Id, out var r) ? r : null;

    /// <summary>
    /// Total weight of one affix type on a base across the given categories: the denominator of a roll chance,
    /// for an empty item at maximum item level, so every tier counts.
    /// </summary>
    public int TotalWeight(BaseItem baseItem, AffixType type, IReadOnlyList<string> categories)
    {
        var key = CacheKey(PagesFor(baseItem), baseItem.Tags) + "|" + type + "|" + string.Join(",", categories);
        return _weightCache.GetOrAdd(key, _ => categories.SelectMany(c => AllForBaseByCategory(baseItem, c, type)).Sum(m => WeightForBase(m, baseItem)));
    }
}
