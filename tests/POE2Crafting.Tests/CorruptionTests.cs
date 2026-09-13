namespace POE2Crafting.Tests;

public class CorruptionTests
{

    private static Item Corrupt(Item item, string outcomeStartsWith, int seed = 1)
    {
        var outcome = TestData.Engine!.Preview(item, TestData.Action("Vaal Orb")).SpecialOutcomes.Keys.Single(k => k.StartsWith(outcomeStartsWith));
        return TestData.Apply(item, "Vaal Orb", new ManualChoice { SpecialOutcome = outcome }, seed).Item;
    }

    [DataFact]
    public void Vaal_outcomes_sum_to_one_and_depend_on_the_item()
    {
        var engine = TestData.Engine!;
        var wand = engine.Preview(TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(2, 2), TestData.Action("Vaal Orb")).SpecialOutcomes;
        Assert.Equal(1.0, wand.Values.Sum(), 6);
        Assert.Contains(wand.Keys, k => k.StartsWith("Quality"));
        Assert.Equal(4, wand.Count);

        // a normal item has no modifiers to reroll
        var normal = engine.Preview(TestData.NewItem(TestBases.Wand), TestData.Action("Vaal Orb")).SpecialOutcomes;
        Assert.DoesNotContain(normal.Keys, k => k.Contains("rerolled"));

        var forced = engine.Preview(TestData.NewItem(TestBases.Wand), TestData.Action("Vaal Orb", "Omen of Corruption")).SpecialOutcomes;
        Assert.DoesNotContain(forced.Keys, k => k.StartsWith("No change"));
        Assert.Equal(1.0, forced.Values.Sum(), 6);
    }

    [DataFact]
    public void Enchantment_outcome_adds_a_corruption_enchantment_and_corrupts()
    {
        var item = Corrupt(TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(1, 1), "Corruption enchantment");
        Assert.True(item.Corrupted);
        var (enchant, _) = Assert.Single(item.CorruptionEnchants);
        Assert.Equal(ModCategories.Corrupted, enchant.Def!.Category);
        Assert.False(TestData.Engine!.Check(item, TestData.Action("Exalted Orb")).Ok);
    }

    [DataFact]
    public void Socket_outcome_exceeds_the_limit_on_armour()
    {
        var before = TestData.NewItem(TestBases.Body, Rarity.Rare);
        var item = Corrupt(before, "+1 socket");
        Assert.Equal(before.Sockets + 1, item.Sockets);
    }

    [DataFact]
    public void Reroll_outcome_keeps_the_affix_counts()
    {
        for (int seed = 0; seed < 10; seed++)
        {
            var item = Corrupt(TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(3, 3), "1-3 modifiers", seed);
            Assert.Equal(3, item.PrefixCount);
            Assert.Equal(3, item.SuffixCount);
        }
    }

    [DataFact]
    public void Sacrifice_upgrades_the_enchantment_and_removes_a_modifier()
    {
        var corrupted = Corrupt(TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(2, 2), "Corruption enchantment");
        var (before, _) = corrupted.CorruptionEnchants.Single();

        var result = TestData.Engine!.Execute(corrupted, TestData.Action("Yaomac's Orb of Sacrifice"), new Rng(4));
        Assert.True(result.Applied, result.Summary);
        var (after, _) = result.Item.CorruptionEnchants.Single();
        Assert.Equal(ModCategories.CorruptionUpgrade, after.Def!.Category);
        Assert.Equal(before.Def!.Name.Replace("Corruption", "CorruptionUpgrade"), after.Def.Name);
        Assert.Equal(3, result.Item.AffixCount);

        // an upgraded enchantment cannot be upgraded again
        Assert.False(TestData.Engine.Check(result.Item, TestData.Action("Yaomac's Orb of Sacrifice")).Ok);
    }

    [DataFact]
    public void Sacrifice_and_architect_require_a_corrupted_item()
    {
        var item = TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(1, 1);
        Assert.Contains("only be used on Corrupted", TestData.Engine!.Check(item, TestData.Action("Yaomac's Orb of Sacrifice")).Reason);
        Assert.Contains("only be used on Corrupted", TestData.Engine.Check(item, TestData.Action("Architect's Orb")).Reason);
    }

    [DataFact]
    public void Architect_adds_an_enchantment_from_another_group_or_destroys()
    {
        var corrupted = Corrupt(TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(1, 1), "Corruption enchantment");
        var action = TestData.Action("Architect's Orb");
        var outcomes = TestData.Engine!.Preview(corrupted, action).SpecialOutcomes;

        var success = TestData.Engine.Execute(corrupted, action, new Rng(1), new ManualChoice { SpecialOutcome = outcomes.Keys.First(k => k.StartsWith("Second")) });
        Assert.True(success.Item.TwiceCorrupted);
        var families = success.Item.CorruptionEnchants.Select(e => e.Mod.Def!.Family).ToList();
        Assert.Equal(2, families.Count);
        Assert.Equal(2, families.Distinct().Count());
        Assert.Contains("Twice Corrupted", TestData.Engine.Check(success.Item, action).Reason);

        var fail = TestData.Engine.Execute(corrupted, action, new Rng(1), new ManualChoice { SpecialOutcome = "Item destroyed" });
        Assert.True(fail.Destroyed);
    }
}
