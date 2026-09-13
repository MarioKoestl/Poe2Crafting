using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine;

/// <summary>Seedable random source so crafting sessions can be reproduced.</summary>
public sealed class Rng
{
    private readonly Random _random;

    public Rng(int? seed = null) => _random = new Random(seed ?? Random.Shared.Next());

    /// <summary>A seed derived from a base seed and a text (stable across processes, unlike string.GetHashCode).</summary>
    public static int Derive(int seed, string text)
    {
        unchecked
        {
            uint hash = 2166136261 ^ (uint)seed;
            foreach (char c in text) hash = (hash ^ c) * 16777619;
            return (int)(hash & 0x7FFFFFFF);
        }
    }

    public double NextDouble() => _random.NextDouble();
    public int Next(int maxExclusive) => _random.Next(maxExclusive);

    /// <summary>Pick an index proportionally to the weights (all &gt;= 0, sum &gt; 0).</summary>
    public int PickWeighted(IReadOnlyList<double> weights)
    {
        double total = 0;
        foreach (var w in weights) total += w;
        if (total <= 0) throw new ArgumentException("No candidates with positive weight.", nameof(weights));
        double r = _random.NextDouble() * total;
        for (int i = 0; i < weights.Count; i++)
        {
            r -= weights[i];
            if (r < 0) return i;
        }
        return weights.Count - 1;
    }

    /// <summary>Pick up to <paramref name="count"/> distinct indices, each draw proportional to the remaining weights.</summary>
    public List<int> SampleWeighted(IReadOnlyList<double> weights, int count)
    {
        var remaining = weights.ToList();
        var picked = new List<int>();
        while (picked.Count < count && remaining.Any(w => w > 0))
        {
            int i = PickWeighted(remaining);
            picked.Add(i);
            remaining[i] = 0;
        }
        return picked;
    }

    /// <summary>Roll a value inside a range; integer ranges give integers, fractional ranges keep two decimals (<see cref="ModText.PossibleRolls"/>).</summary>
    public double RollRange(double[] range)
    {
        var (min, max) = ModText.Bounds(range);
        if (ModText.IsIntegerRange(min, max)) return _random.Next((int)min, (int)max + 1);
        return Math.Round(min + _random.NextDouble() * (max - min), 2);
    }
}
