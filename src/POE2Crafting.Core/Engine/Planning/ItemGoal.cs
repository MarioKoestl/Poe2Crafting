using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Planning;

/// <summary>
/// The difference between an item and a target: which affixes stay (matching a target mod), which have to go
/// (unwanted, or a too-low tier of a target family), which target mods are missing, which kept mods miss their value targets,
/// how far the rarity is from the target rarity, and the deterministic finishing work (quality, sockets, augments, instill).
/// </summary>
public sealed class ItemGoal
{
    public List<(int Index, ItemMod Mod, TargetMod Target)> Kept { get; } = new();
    public List<(int Index, ItemMod Mod)> ToRemove { get; } = new();
    public List<TargetMod> Missing { get; } = new();
    public List<(int Index, ItemMod Mod, TargetMod Target)> ValuesUnmet { get; } = new();
    public int RarityGap { get; private set; }
    /// <summary>The item is already above the target rarity (rarity cannot be lowered).</summary>
    public bool RarityImpossible { get; private set; }
    public bool QualityUnmet { get; private set; }
    /// <summary>Augment sockets that still have to be added for the wanted augments.</summary>
    public int SocketsMissing { get; private set; }
    public List<AugmentTarget> AugmentsMissing { get; } = new();
    public bool InstillMissing { get; private set; }

    private readonly HashSet<int> _keptIndices = new();
    private readonly HashSet<int> _removeIndices = new();

    /// <summary>Everything about prefixes and suffixes is done (only values, quality and finishing work may be left).</summary>
    public bool AffixesDone => Missing.Count == 0 && ToRemove.Count == 0 && RarityGap == 0;

    /// <summary>Remaining work: removals + additions + rarity steps + one value reroll if any value target is unmet + finishing steps.</summary>
    public int Distance => ToRemove.Count + Missing.Count + RarityGap + (ValuesUnmet.Count > 0 ? 1 : 0)
                           + (QualityUnmet ? 1 : 0) + SocketsMissing + AugmentsMissing.Count + (InstillMissing ? 1 : 0);
    public bool Reached => Distance == 0;

    public bool IsKept(int index) => _keptIndices.Contains(index);
    public bool IsToRemove(int index) => _removeIndices.Contains(index);

    /// <summary>The mod occupies the family of a missing target, so the target cannot roll until it is removed.</summary>
    public bool Blocks(ItemMod mod) => Missing.Any(t => t.Family == mod.Def?.Family);

    /// <summary>Whether the item's quality meets the target (amount and, for catalysts, type).</summary>
    public static bool QualityMet(Item item, TargetItemSpec target) =>
        item.Quality >= (target.MinQuality ?? 0) && (target.QualityType == null || item.QualityType == target.QualityType && item.Quality > 0);

    public static ItemGoal Compare(Item item, TargetItemSpec target)
    {
        var goal = new ItemGoal();
        var open = target.TargetMods.Where(t => t.ResolvedMod != null).ToList();
        for (int i = 0; i < item.Mods.Count; i++)
        {
            var mod = item.Mods[i];
            if (!mod.IsAffix) continue;
            var match = open.FirstOrDefault(t => t.Matches(mod.Def));
            if (match == null)
            {
                goal.ToRemove.Add((i, mod));
                goal._removeIndices.Add(i);
                continue;
            }
            open.Remove(match);
            goal.Kept.Add((i, mod, match));
            goal._keptIndices.Add(i);
            if (!match.ValuesSatisfiedBy(item, mod)) goal.ValuesUnmet.Add((i, mod, match));
        }
        goal.Missing.AddRange(open);
        goal.RarityImpossible = item.Rarity > target.TargetRarity;
        goal.RarityGap = Math.Max(0, (int)target.TargetRarity - (int)item.Rarity);
        goal.QualityUnmet = !QualityMet(item, target);

        var socketed = item.Runes.ToList();
        foreach (var augment in target.Augments)
            if (!socketed.Remove(augment.EffectText ?? augment.Name)) goal.AugmentsMissing.Add(augment);
        goal.SocketsMissing = Math.Max(0, target.Augments.Count - item.Sockets);
        goal.InstillMissing = target.InstillNotable != null && item.InstilledNotableName != target.InstillNotable;
        return goal;
    }
}
