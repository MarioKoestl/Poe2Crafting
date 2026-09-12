using System.Collections.Concurrent;
using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine;

/// <summary>One mod that could be added to an item, with its (estimated) weight and normalised probability.</summary>
public sealed class ModCandidate
{
    public ModDef Mod { get; init; } = null!;
    public int Weight { get; init; }
    public double Probability { get; set; }
    public override string ToString() => $"{Mod.Name} T{Mod.Tier} {Mod.Text} ({Probability:P2})";
}

/// <summary>Computes the pool of modifiers that can roll on an item in its current state.</summary>
public sealed class ModPool
{
    private readonly GameData _data;
    /// <summary>category -> page -> mods with a positive weight on that page.</summary>
    private readonly Dictionary<string, Dictionary<string, List<ModDef>>> _byCategoryPage = new();
    /// <summary>Display tiers per (pages, base tags) key; ModPool is a singleton shared by all sessions.</summary>
    private readonly ConcurrentDictionary<string, DisplayTierTable> _tierCache = new();

    public const string NormalCategory = "normal";

    /// <summary>Non-normal categories that are browsable in the planner / composer UI.</summary>
    public static readonly string[] BrowsableCategories = { "breach_otherworldly", "desecrated" };

    public ModPool(GameData data)
    {
        _data = data;
        foreach (var mod in data.Mods.Where(m => (m.IsPrefix || m.IsSuffix) && (m.Category == NormalCategory || BrowsableCategories.Contains(m.Category))))
        {
            if (!_byCategoryPage.TryGetValue(mod.Category, out var byPage))
                _byCategoryPage[mod.Category] = byPage = new Dictionary<string, List<ModDef>>();
            foreach (var (page, w) in mod.Weights)
                if (w > 0) (byPage.TryGetValue(page, out var l) ? l : byPage[page] = new List<ModDef>()).Add(mod);
        }
    }

    public GameData Data => _data;

    /// <summary>Pages whose weights apply to this item (its base's page, or all pages of its class as a fallback).</summary>
    public IReadOnlyList<string> PagesFor(Item item) => _data.PagesFor(item.Base, item.ItemClass);

    /// <summary>
    /// Candidate mods of the given affix type for the item: right page, level gated by item level and optional minimum modifier level,
    /// not blocked by the base's negative tags, family not already present, and optionally filtered further.
    /// </summary>
    public List<ModCandidate> Candidates(Item item, AffixType type, int minModLevel = 0, Func<ModDef, bool>? filter = null)
    {
        var result = new Dictionary<string, ModCandidate>();
        if (_byCategoryPage.TryGetValue(NormalCategory, out var byPage))
        {
            var baseTags = item.Base?.Tags ?? new List<string>();
            foreach (var page in PagesFor(item))
            {
                if (!byPage.TryGetValue(page, out var mods)) continue;
                foreach (var mod in mods)
                {
                    if (mod.AffixType != type) continue;
                    if (mod.Level > item.ItemLevel || mod.Level < minModLevel) continue;
                    if (mod.BlockingTags.Any(baseTags.Contains)) continue;
                    if (item.HasFamily(mod.Family)) continue;
                    if (filter != null && !filter(mod)) continue;
                    var w = mod.WeightOn(page);
                    if (w <= 0) continue;
                    if (!result.TryGetValue(mod.Id, out var existing) || existing.Weight < w)
                        result[mod.Id] = new ModCandidate { Mod = mod, Weight = w };
                }
            }
        }
        var list = result.Values.OrderByDescending(c => c.Weight).ThenBy(c => c.Mod.Family).ThenBy(c => c.Mod.Tier).ToList();
        Normalise(list);
        return list;
    }

    public static void Normalise(List<ModCandidate> list)
    {
        double total = list.Sum(c => (double)c.Weight);
        foreach (var c in list) c.Probability = total > 0 ? c.Weight / total : 0;
    }

    /// <summary>All normal mods that can ever appear on the item's base (ignoring current mods and item level).</summary>
    public IEnumerable<ModDef> AllForBase(Item item, AffixType? type = null) => AllForBaseByCategory(item, NormalCategory, type);

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

    /// <summary>Weight of a mod on the pages relevant to the given item.</summary>
    public int WeightForItem(ModDef mod, Item item)
    {
        foreach (var page in PagesFor(item))
        {
            var w = mod.WeightOn(page);
            if (w > 0) return w;
        }
        return 0;
    }

    // ------------------------------------------------------------------ display tiers (single source of truth)

    private sealed record DisplayTierTable(Dictionary<string, int> Tiers, Dictionary<string, int> Counts);

    private DisplayTierTable TierTable(Item item)
    {
        var key = string.Join(",", PagesFor(item)) + "|" + string.Join(",", item.Base?.Tags ?? new List<string>());
        return _tierCache.GetOrAdd(key, _ =>
        {
            var tiers = new Dictionary<string, int>();
            var counts = new Dictionary<string, int>();
            var allMods = BrowsableCategories.Aggregate(AllForBase(item), (acc, cat) => acc.Concat(AllForBaseByCategory(item, cat)));
            foreach (var group in allMods.Where(m => m.Family != null).GroupBy(m => (m.Family, m.Gen, m.Category, ModText.StatSignature(m.Text))))
            {
                var levels = group.Select(m => m.Level).Distinct().OrderByDescending(l => l).ToList();
                foreach (var m in group)
                {
                    tiers[m.Id] = levels.IndexOf(m.Level) + 1;
                    counts[m.Id] = levels.Count;
                }
            }
            return new DisplayTierTable(tiers, counts);
        });
    }

    /// <summary>
    /// Per-base display tiers for ALL mods visible on the item's base (normal + all browsable categories).
    /// T1 = best (highest level) available on this base. Cached per base page/tags, so calling it in loops is cheap.
    /// </summary>
    public (IReadOnlyDictionary<string, int> Tiers, IReadOnlyDictionary<string, int> TierCounts) ComputeAllDisplayTiers(Item item)
    {
        var t = TierTable(item);
        return (t.Tiers, t.Counts);
    }

    /// <summary>Per-base display tier for a single mod. Falls back to the global tier for mods outside the base's pool (e.g. imported from another class).</summary>
    public int DisplayTier(ModDef mod, Item item) => TierTable(item).Tiers.TryGetValue(mod.Id, out var t) ? t : mod.Tier;

    /// <summary>Per-base display tier, or null for mods without tiers on this base (essence/alloy results, unknown mods).</summary>
    public int? TryDisplayTier(ModDef mod, Item item) => TierTable(item).Tiers.TryGetValue(mod.Id, out var t) ? t : null;

    public int DisplayTierCount(ModDef mod, Item item) => TierTable(item).Counts.TryGetValue(mod.Id, out var t) ? t : mod.TierCount;
}
