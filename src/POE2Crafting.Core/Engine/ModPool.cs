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
    private readonly Dictionary<string, List<ModDef>> _byPage;
    private readonly Dictionary<string, Dictionary<string, List<ModDef>>> _categorized;

    /// <summary>Non-normal categories that are browsable in the planner UI.</summary>
    public static readonly string[] BrowsableCategories = { "breach_otherworldly", "desecrated" };

    public ModPool(GameData data)
    {
        _data = data;
        _byPage = new Dictionary<string, List<ModDef>>();
        foreach (var mod in data.Mods.Where(m => m.Category == "normal" && (m.IsPrefix || m.IsSuffix)))
            foreach (var (page, w) in mod.Weights)
                if (w > 0) (_byPage.TryGetValue(page, out var l) ? l : _byPage[page] = new List<ModDef>()).Add(mod);

        _categorized = new Dictionary<string, Dictionary<string, List<ModDef>>>();
        foreach (var mod in data.Mods.Where(m => BrowsableCategories.Contains(m.Category) && (m.IsPrefix || m.IsSuffix)))
        {
            if (!_categorized.TryGetValue(mod.Category, out var catDict))
                _categorized[mod.Category] = catDict = new Dictionary<string, List<ModDef>>();
            foreach (var (page, w) in mod.Weights)
                if (w > 0) (catDict.TryGetValue(page, out var l) ? l : catDict[page] = new List<ModDef>()).Add(mod);
        }
    }

    /// <summary>Pages whose weights apply to this item (its base's page, or all pages of its class as a fallback).</summary>
    public IReadOnlyList<string> PagesFor(Item item)
    {
        if (item.Base?.ModPage is { } p) return new[] { p };
        return _data.PagesOfClass(item.ItemClass).ToList();
    }

    /// <summary>
    /// Candidate mods of the given affix type for the item: right page, level gated by item level and optional minimum modifier level,
    /// not blocked by the base's negative tags, family not already present, and optionally filtered further.
    /// </summary>
    public List<ModCandidate> Candidates(Item item, AffixType type, int minModLevel = 0, Func<ModDef, bool>? filter = null)
    {
        var pages = PagesFor(item);
        var baseTags = item.Base?.Tags ?? new List<string>();
        var result = new Dictionary<string, ModCandidate>();
        foreach (var page in pages)
        {
            if (!_byPage.TryGetValue(page, out var mods)) continue;
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
        var list = result.Values.OrderByDescending(c => c.Weight).ThenBy(c => c.Mod.Family).ThenBy(c => c.Mod.Tier).ToList();
        Normalise(list);
        return list;
    }

    public static void Normalise(List<ModCandidate> list)
    {
        double total = list.Sum(c => (double)c.Weight);
        foreach (var c in list) c.Probability = total > 0 ? c.Weight / total : 0;
    }

    /// <summary>All normal mods that can ever appear on the item's base (ignoring current mods), grouped for browsing.</summary>
    public IEnumerable<ModDef> AllForBase(Item item, AffixType? type = null)
    {
        var pages = PagesFor(item);
        var baseTags = item.Base?.Tags ?? new List<string>();
        var seen = new HashSet<string>();
        foreach (var page in pages)
        {
            if (!_byPage.TryGetValue(page, out var mods)) continue;
            foreach (var mod in mods)
            {
                if (type != null && mod.AffixType != type) continue;
                if (mod.BlockingTags.Any(baseTags.Contains)) continue;
                if (seen.Add(mod.Id)) yield return mod;
            }
        }
    }

    /// <summary>All mods of a non-normal category (desecrated, breach_otherworldly, etc.) that can appear on the item's base.</summary>
    public IEnumerable<ModDef> AllForBaseByCategory(Item item, string category, AffixType? type = null)
    {
        if (!_categorized.TryGetValue(category, out var byPage)) yield break;
        var pages = PagesFor(item);
        var baseTags = item.Base?.Tags ?? new List<string>();
        var seen = new HashSet<string>();
        foreach (var page in pages)
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

    /// <summary>
    /// Compute per-page display tiers for ALL mods visible on the item's base (normal + all browsable categories).
    /// T1 = best (highest level) available on this base. Returns (tiers, tierCounts) dictionaries keyed by mod ID.
    /// This is the single source of truth for display tiers — used by both UI and engine.
    /// </summary>
    public (Dictionary<string, int> Tiers, Dictionary<string, int> TierCounts) ComputeAllDisplayTiers(Item item)
    {
        var tiers = new Dictionary<string, int>();
        var tierCounts = new Dictionary<string, int>();

        // Collect all visible mods: normal + each browsable category
        IEnumerable<ModDef> allMods = AllForBase(item);
        foreach (var cat in BrowsableCategories)
            allMods = allMods.Concat(AllForBaseByCategory(item, cat));

        foreach (var group in allMods
            .Where(m => m.Family != null && (m.IsPrefix || m.IsSuffix))
            .GroupBy(m => (m.Family, m.Gen, m.Category)))
        {
            var levels = group.Select(m => m.Level).Distinct().OrderByDescending(l => l).ToList();
            foreach (var m in group)
            {
                var tier = levels.IndexOf(m.Level) + 1;
                tiers[m.Id] = tier;
                tierCounts[m.Id] = levels.Count;
            }
        }

        return (tiers, tierCounts);
    }

    /// <summary>Per-page display tier for a single mod (convenience for engine use). Falls back to global tier.</summary>
    public int ComputeDisplayTier(ModDef mod, Item item)
    {
        var (tiers, _) = ComputeAllDisplayTiers(item);
        return tiers.TryGetValue(mod.Id, out var t) ? t : mod.Tier;
    }
}
