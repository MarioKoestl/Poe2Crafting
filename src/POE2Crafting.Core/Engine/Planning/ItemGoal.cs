using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine;

/// <summary>
/// The difference between an item and a target: which affixes stay (matching a target mod), which have to go
/// (unwanted, or a too-low tier of a target family), which target mods are missing, which kept mods miss their value targets,
/// and how far the rarity is from the target rarity.
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

    /// <summary>Remaining work: removals + additions + rarity steps + one value reroll if any value target is unmet.</summary>
    public int Distance => ToRemove.Count + Missing.Count + RarityGap + (ValuesUnmet.Count > 0 ? 1 : 0);
    public bool Reached => Distance == 0;

    public bool IsKept(int index) => Kept.Any(k => k.Index == index);
    public bool IsToRemove(int index) => ToRemove.Any(r => r.Index == index);

    /// <summary>The mod occupies the family of a missing target, so the target cannot roll until it is removed.</summary>
    public bool Blocks(ItemMod mod) => Missing.Any(t => t.Family == mod.Def?.Family);

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
                continue;
            }
            open.Remove(match);
            goal.Kept.Add((i, mod, match));
            if (!match.ValuesSatisfiedBy(mod)) goal.ValuesUnmet.Add((i, mod, match));
        }
        goal.Missing.AddRange(open);
        goal.RarityImpossible = item.Rarity > target.TargetRarity;
        goal.RarityGap = Math.Max(0, (int)target.TargetRarity - (int)item.Rarity);
        return goal;
    }
}
