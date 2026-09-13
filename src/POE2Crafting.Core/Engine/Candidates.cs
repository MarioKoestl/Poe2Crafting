using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine;

/// <summary>One mod that could be added to an item, with its (estimated) weight and normalised probability. Immutable: previews are cached and shared.</summary>
public sealed class ModCandidate
{
    public ModDef Mod { get; init; } = null!;
    public int Weight { get; init; }
    public double Probability { get; init; }

    /// <summary>The candidates with probabilities proportional to their weights.</summary>
    public static List<ModCandidate> Normalised(IEnumerable<ModCandidate> candidates)
    {
        var list = candidates.ToList();
        double total = list.Sum(c => (double)c.Weight);
        return list.Select(c => new ModCandidate { Mod = c.Mod, Weight = c.Weight, Probability = total > 0 ? c.Weight / total : 0 }).ToList();
    }

    public override string ToString() => $"{Mod.Name} T{Mod.Tier} {Mod.Text} ({Probability:P2})";
}

/// <summary>One mod that a removal (or a fracture) can hit, by its index in <see cref="Item.Mods"/>. Immutable like <see cref="ModCandidate"/>.</summary>
public sealed class RemovalCandidate
{
    public int Index { get; init; }
    public ItemMod Mod { get; init; } = null!;
    public double Probability { get; init; }

    /// <summary>The candidates with equal probabilities (removals are uniform).</summary>
    public static List<RemovalCandidate> Uniform(IEnumerable<RemovalCandidate> candidates)
    {
        var list = candidates.ToList();
        return list.Select(r => new RemovalCandidate { Index = r.Index, Mod = r.Mod, Probability = 1.0 / list.Count }).ToList();
    }

    /// <summary>The item's mods matching <paramref name="filter"/>, equally likely.</summary>
    public static List<RemovalCandidate> UniformOf(Item item, Func<ItemMod, bool> filter) =>
        Uniform(item.Mods.Select((m, i) => new RemovalCandidate { Index = i, Mod = m }).Where(r => filter(r.Mod)));
}
