namespace POE2Crafting.Tests;

/// <summary>The mod finder: which bases can have a modifier, and what it takes there.</summary>
public class ModFinderTests
{
    private static ModFinder Finder => TestData.Finder!;

    /// <summary>The one group of a category whose every tier is the given stat (the label has the numbers stripped).</summary>
    private static ModGroup Group(string category, string stat) =>
        Finder.Groups.Single(g => g.Category == category && g.Tiers.All(m => ModText.StatSignature(m.Text) == stat));

    [DataFact]
    public void Projectile_speed_rolls_on_quivers_and_jewels_but_not_on_gloves()
    {
        var group = Group(ModCategories.Normal, "#% increased projectile speed");
        var classes = Finder.Placements(group).Select(p => p.ItemClass).Distinct().ToList();

        Assert.Contains("Quiver", classes);
        Assert.DoesNotContain("Gloves", classes);
        // every quiver base has it, with all its tiers and a chance out of the prefix pool
        var quivers = Finder.Placements(group).Single(p => p.ItemClass == "Quiver");
        Assert.Equal(TestData.Data!.BasesOfClass("Quiver").Count(b => !b.Hidden && !b.IsRuneforged), quivers.Bases.Count);
        Assert.Equal(new int?[] { 1, 2, 3, 4, 5 }, quivers.Tiers.Select(t => t.Tier));
        Assert.InRange(quivers.ChanceLow!.Value, 0.0001, 1);
    }

    [DataFact]
    public void Projectile_speed_on_gloves_needs_the_rune_that_unlocks_marksman_modifiers()
    {
        var group = Group(ModCategories.Marksman, "#% increased projectile speed");
        var placements = Finder.Placements(group);

        Assert.All(placements, p => Assert.Equal("Gloves", p.ItemClass));
        Assert.All(placements, p => Assert.Contains("Kolr's Hunt", p.UnlockingRunes.Select(r => r.Name)));
        // it competes with the base modifiers of the gloves, so its share is far below 100%
        Assert.InRange(placements[0].ChanceHigh!.Value, 0, 0.2);
    }

    [DataFact]
    public void A_rune_effect_is_listed_with_the_bases_it_fits_and_without_a_roll_chance()
    {
        var group = Finder.Groups.Single(g => g.Category == ModCategories.Socketable
                                              && g.Tiers.All(m => m.Text == "Can roll Marksman modifiers"));
        var placements = Finder.Placements(group);

        Assert.All(placements, p => Assert.Equal("Gloves", p.ItemClass));
        Assert.All(placements, p => Assert.Null(p.ChanceLow));
        Assert.All(placements, p => Assert.All(p.Tiers, t => Assert.Null(t.Tier)));
    }

    [DataFact]
    public void Placements_respect_the_blocking_tags_of_a_base()
    {
        // wands and staves of another element block fire spell modifiers, although the modifier is on their page
        var blocked = Finder.Groups.First(g => g.Category == ModCategories.Normal && g.Best.SpawnTags.Contains("no_fire_spell_mods"));
        var bases = Finder.Placements(blocked).SelectMany(p => p.Bases).ToList();

        Assert.NotEmpty(bases);
        Assert.All(bases, b => Assert.DoesNotContain("no_fire_spell_mods", b.Tags));
        Assert.Contains(TestData.Data!.Bases, b => b.Tags.Contains("no_fire_spell_mods") && blocked.Best.IsOnAnyPage(TestData.Pool!.PagesFor(b)));
    }

    [DataFact]
    public void The_item_classes_of_a_search_hit_are_the_classes_of_its_placements()
    {
        foreach (var group in Finder.Groups.Where((_, i) => i % 37 == 0))
        {
            var classes = Finder.Placements(group, includeRuneforged: true).Select(p => p.ItemClass).Distinct().OrderBy(c => c);
            // the summary may list a class whose every base blocks the modifier by tag, never one that has no page for it
            Assert.Subset(group.Classes.ToHashSet(), classes.ToHashSet());
        }
    }

    [DataFact]
    public void Every_modifier_of_the_data_store_belongs_to_exactly_one_group()
    {
        var craftable = Finder.Groups.Where(g => g.Category != ModCategories.Unique).Sum(g => g.Tiers.Count);
        var unique = Finder.Groups.Where(g => g.Category == ModCategories.Unique).Sum(g => g.Tiers.Count);
        Assert.Equal(TestData.Data!.Mods.Count, craftable);
        Assert.Equal(TestData.Data.Uniques.Sum(u => u.Mods.Count), unique);
        Assert.All(Finder.Groups, g => Assert.NotEmpty(g.Label));
    }

    [DataFact]
    public void A_modifier_lists_the_uniques_that_grant_it_themselves()
    {
        // "Thorns can Retaliate against all Hits" exists only on a unique, so it is searchable as a unique modifier
        var group = Finder.Groups.Single(g => g.Category == ModCategories.Unique && g.Label == "Thorns can Retaliate against all Hits");
        var crown = Assert.Single(Finder.UniquesWith(group));
        Assert.Equal(("Crown of the Pale King", "Cultist Crown"), (crown.Unique.Name, crown.Unique.BaseType));
        Assert.Empty(Finder.Placements(group));

        // a craftable stat names the uniques that grant it as well
        var life = Finder.Groups.First(g => g.Category == ModCategories.Normal && g.Label == "+# to maximum Life");
        Assert.Contains(Finder.UniquesWith(life), u => u.Unique.Name == "Crown of the Pale King");
    }

    [DataFact]
    public void A_search_collects_bases_crafting_items_corruptions_and_uniques_for_a_stat()
    {
        var result = Finder.Search("thorns");

        // rolled on bases: base modifiers on body armours, desecrated ones on belts (bones)
        Assert.Contains(result.Classes, c => c.ItemClass == "Body Armour" && c.Mods.Any(g => g.Category == ModCategories.Normal));
        Assert.Contains(result.Classes, c => c.ItemClass == "Belt" && c.Mods.Any(g => g.Category == ModCategories.Desecrated));
        Assert.DoesNotContain(result.Classes.SelectMany(c => c.Mods), g => g.Category is ModCategories.Corrupted or ModCategories.Unique);

        // crafting items: the perfect essence, the runes, and the bones that reach belts
        var hysteria = Assert.Single(result.CraftingItems, s => s.Name == "Essence of Hysteria");
        Assert.Equal(ModSourceKind.Essence, hysteria.Kind);
        Assert.Contains("Body Armour", Assert.Single(hysteria.Effects).Classes);
        Assert.Contains(result.CraftingItems, s => s.Name == "Tempered Rune" && s.Kind == ModSourceKind.Augment);
        var collarbone = Assert.Single(result.CraftingItems, s => s.Name == "Gnawed Collarbone");
        Assert.All(collarbone.Effects, e => Assert.Contains("Belt", e.Classes));
        Assert.DoesNotContain(result.CraftingItems, s => s.Name == "Gnawed Jawbone" && s.Effects.Any(e => e.Classes.Contains("Belt")));

        // corruption: Vaal Orb adds the enchantment, the armour Orb of Sacrifice upgrades it
        Assert.Contains(result.Corruption, s => s.Name == "Vaal Orb");
        Assert.Contains(result.Corruption, s => s.Name == "Kopec's Orb of Sacrifice");
        Assert.DoesNotContain(result.Corruption, s => s.Name == "Kamasa's Orb of Sacrifice");

        // uniques with a matching line of their own
        var crown = Assert.Single(result.Uniques, u => u.Unique.Name == "Crown of the Pale King");
        Assert.All(crown.Lines, l => Assert.Contains("thorns", l.Text, StringComparison.OrdinalIgnoreCase));
    }

    [DataFact]
    public void Every_crafting_item_a_search_names_is_a_known_item()
    {
        foreach (var query in new[] { "thorns", "life", "fire", "speed", "chaos" })
        foreach (var source in Finder.Search(query).Sources)
            Assert.True(TestData.Data!.FindCraftItem(source.Name) != null, $"{query}: {source.Name}");
    }

    [DataFact]
    public void An_empty_search_lists_every_modifier_but_builds_no_overview()
    {
        var result = Finder.Search(" ");
        Assert.Equal(Finder.Groups.Count, result.Groups.Count);
        Assert.Empty(result.Sources);
        Assert.Empty(result.Uniques);
    }
}
