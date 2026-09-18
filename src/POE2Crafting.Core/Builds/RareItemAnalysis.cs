using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Builds;

/// <summary>A rare item a sampled character wears, parsed into the simulator's item.</summary>
public sealed record SampledItem(CharacterRef Character, string CharacterClass, int CharacterLevel, Item Item);

/// <summary>A modifier (by stat text, numbers as #) seen on rare items of a class: how often, which tiers, which values.</summary>
/// <param name="Tiers">Display tier → items (tiers of the item's own base, global tier for mods outside its pool); crafted and unresolved mods have none.</param>
/// <param name="Values">Rolled value of each occurrence (sorted; the average of the numbers for "Adds # to #" mods).</param>
public sealed record ModDemand(string Text, AffixType Affix, int Count, IReadOnlyDictionary<int, int> Tiers, IReadOnlyList<double> Values, int Desecrated, int Fractured, int Crafted);

/// <summary>What the sampled characters wear as rares of one item class.</summary>
public sealed record ItemClassDemand(string ItemClass, int Characters, IReadOnlyList<SampledItem> Items, IReadOnlyList<NamedCount> Bases, IReadOnlyList<ModDemand> Mods)
{
    public double AverageItemLevel => Items.Count == 0 ? 0 : Items.Average(i => i.Item.ItemLevel);
    public int Fractured => Items.Count(i => i.Item.Mods.Any(m => m.Fractured));
    public int Desecrated => Items.Count(i => i.Item.Mods.Any(m => m.Kind == ModKind.Desecrated));
    public int Corrupted => Items.Count(i => i.Item.Corrupted);

    /// <summary>
    /// The same class restricted to one base ("Mnemonic Ring"): only its items, with characters, modifiers, tiers and facts recomputed — what the
    /// builds that use exactly this base roll on it. Null when no sampled item has that base. <see cref="Bases"/> keeps all bases of the class so
    /// the filter can be switched.
    /// </summary>
    public ItemClassDemand? ForBase(string baseName, ModPool pool)
    {
        var items = Items.Where(i => i.Item.BaseName == baseName).ToList();
        return items.Count == 0 ? null : this with
        {
            Characters = items.Select(i => i.Character).Distinct().Count(),
            Items = items,
            Mods = RareItemAnalysis.ModsOf(items, pool),
        };
    }
}

/// <summary>
/// Rare equipment and jewels of sampled characters grouped by item class (the classes worth crafting for these builds): bases, modifiers with tiers and values.
/// </summary>
public sealed class RareItemAnalysis
{
    public int Characters { get; init; }
    /// <summary>Most worn first.</summary>
    public IReadOnlyList<ItemClassDemand> Classes { get; init; } = Array.Empty<ItemClassDemand>();

    public static RareItemAnalysis Build(IReadOnlyCollection<SampledItem> items, int characters, ModPool pool)
    {
        var classes = items.Where(i => i.Item.Rarity == Rarity.Rare)
            .GroupBy(i => i.Item.ItemClass)
            .Select(g => new ItemClassDemand(g.Key, g.Select(i => i.Character).Distinct().Count(), g.ToList(),
                g.GroupBy(i => i.Item.BaseName).Select(b => new NamedCount(b.Key, b.Count())).OrderByDescending(b => b.Count).ToList(),
                ModsOf(g, pool)))
            .OrderByDescending(c => c.Characters).ThenByDescending(c => c.Items.Count)
            .ToList();
        return new RareItemAnalysis { Characters = characters, Classes = classes };
    }

    /// <summary>Affixes grouped by their stat text ("#% increased Projectile Speed"); most common first.</summary>
    public static List<ModDemand> ModsOf(IEnumerable<SampledItem> items, ModPool pool) =>
        items.SelectMany(i => i.Item.Affixes.Select(mod => (i.Item, Mod: mod)))
            .GroupBy(x => ModText.StatSignature(x.Mod.DisplayText()))
            .Select(g =>
            {
                // crafted (essence/alloy) results have no tiers; mods outside the base's browsable pool (e.g. influence mods) use their global tier
                var tiers = g.Where(x => x.Mod.Def != null && x.Mod.Kind != ModKind.Crafted).Select(x => pool.DisplayTier(x.Mod.Def!, x.Item))
                    .GroupBy(t => t).OrderBy(t => t.Key).ToDictionary(t => t.Key, t => t.Count());
                return new ModDemand(ModText.StripNumbers(g.First().Mod.DisplayText()),
                    g.GroupBy(x => x.Mod.Affix).OrderByDescending(a => a.Count()).First().Key,
                    g.Count(), tiers,
                    g.Select(x => RollValue(x.Mod)).OfType<double>().Order().ToList(),
                    g.Count(x => x.Mod.Kind == ModKind.Desecrated), g.Count(x => x.Mod.Fractured), g.Count(x => x.Mod.Kind == ModKind.Crafted));
            })
            .OrderByDescending(m => m.Count)
            .ToList();

    /// <summary>The mod's number; mods with several numbers ("Adds 4 to 67") count with their average; null when the mod has none.</summary>
    private static double? RollValue(ItemMod mod) => mod.StatValues is { Count: > 0 } values ? values.Average() : null;
}
