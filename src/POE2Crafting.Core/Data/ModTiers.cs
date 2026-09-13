namespace POE2Crafting.Core.Data;

/// <summary>Tier numbering as in game: T1 = highest level within one family, stat, generation type and category.</summary>
public static class ModTiers
{
    public readonly record struct Rank(int Tier, int Count);

    /// <summary>Mods with the same key are tiers of one another (same family, stat, affix type and category).</summary>
    public static string TierGroupKey(ModDef mod) => $"{mod.Category}|{mod.Gen}|{mod.Family ?? mod.Name}|{mod.StatSignature}";

    /// <summary>Ranks every mod with a family among the given set (e.g. all mods of a base, or all mods globally).</summary>
    public static Dictionary<string, Rank> RankAll(IEnumerable<ModDef> mods)
    {
        var result = new Dictionary<string, Rank>();
        foreach (var group in mods.Where(m => m.Family != null && (m.IsPrefix || m.IsSuffix)).GroupBy(TierGroupKey))
        {
            var levels = group.Select(m => m.Level).Distinct().OrderByDescending(l => l).ToList();
            foreach (var m in group) result[m.Id] = new Rank(levels.IndexOf(m.Level) + 1, levels.Count);
        }
        return result;
    }
}
