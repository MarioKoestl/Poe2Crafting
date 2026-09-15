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

    /// <summary>Non-normal categories that are browsable in the planner / composer UI.</summary>
    public static readonly string[] BrowsableCategories = { ModCategories.Otherworldly, ModCategories.Desecrated };

    /// <summary>Normal plus all browsable categories.</summary>
    public static readonly string[] AllCategories = BrowsableCategories.Prepend(ModCategories.Normal).ToArray();

    public ModPool(GameData data)
    {
        _data = data;
        foreach (var mod in data.Mods.Where(m => ((m.IsPrefix || m.IsSuffix) && AllCategories.Contains(m.Category)) || m.Category == ModCategories.Corrupted))
        {
            if (!_byCategoryPage.TryGetValue(mod.Category, out var byPage))
                _byCategoryPage[mod.Category] = byPage = new Dictionary<string, List<ModDef>>();
            foreach (var (page, w) in mod.Weights)
                if (w > 0) (byPage.TryGetValue(page, out var l) ? l : byPage[page] = new List<ModDef>()).Add(mod);
        }
    }

    /// <summary>Pages whose weights apply to this item (its base's page, or all pages of its class as a fallback).</summary>
    public IReadOnlyList<string> PagesFor(Item item) => _data.PagesFor(item.Base, item.ItemClass);

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
    public IEnumerable<ModDef> AllForBaseByCategory(Item item, string category, AffixType? type = null)
    {
        if (!_byCategoryPage.TryGetValue(category, out var byPage)) yield break;
        var baseTags = item.Base?.Tags ?? new List<string>();
        var seen = new HashSet<string>();
        foreach (var page in PagesFor(item))
        {
            if (!byPage.TryGetValue(page, out var mods)) continue;
            foreach (var mod in mods)
            {
                if (type != null && mod.AffixType != type) continue;
                if (mod.BlockingTags.Any(baseTags.Contains)) continue;
                if (seen.Add(mod.Id)) yield return mod;
            }
        }
    }

    /// <summary>All mods of every browsable category (incl. normal) that can appear on the item's base.</summary>
    public IEnumerable<ModDef> AllBrowsableForBase(Item item) => AllCategories.SelectMany(c => AllForBaseByCategory(item, c));

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

    /// <summary>Weight of a mod on the given pages (highest page weight when a class has several pages).</summary>
    private static int WeightOn(ModDef mod, IReadOnlyList<string> pages) => pages.Select(mod.WeightOn).DefaultIfEmpty(0).Max();

    // ------------------------------------------------------------------ display tiers (single source of truth)

    /// <summary>Tiers of all mods visible on the item's base (normal + browsable categories), cached per base page/tags.</summary>
    private Dictionary<string, ModTiers.Rank> TierTable(Item item)
    {
        var key = string.Join(",", PagesFor(item)) + "|" + string.Join(",", item.Base?.Tags ?? new List<string>());
        return _tierCache.GetOrAdd(key, _ => ModTiers.RankAll(AllBrowsableForBase(item)));
    }

    /// <summary>Per-base display tier for a single mod. Falls back to the global tier for mods outside the base's pool (e.g. imported from another class).</summary>
    public int DisplayTier(ModDef mod, Item item) => TryDisplayTier(mod, item) ?? mod.Tier;

    /// <summary>Per-base display tier, or null for mods without tiers on this base (essence/alloy results, unknown mods).</summary>
    public int? TryDisplayTier(ModDef mod, Item item) => TierTable(item).TryGetValue(mod.Id, out var r) ? r.Tier : null;

    public int DisplayTierCount(ModDef mod, Item item) => TierTable(item).TryGetValue(mod.Id, out var r) ? r.Count : mod.TierCount;
}
