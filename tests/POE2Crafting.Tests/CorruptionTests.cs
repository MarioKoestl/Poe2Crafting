using POE2Crafting.Core.Engine;
using POE2Crafting.Core.Items;
using Xunit;

namespace POE2Crafting.Tests;

public class CorruptionTests
{
    private const string Wand = "Siphoning Wand";
    private const string Body = "Vile Robe";

    private static Item Corrupt(Item item, string outcomeStartsWith, int seed = 1)
    {
        var action = TestData.Action("Vaal Orb");
        var outcome = TestData.Engine!.Preview(item, action).SpecialOutcomes.Keys.Single(k => k.StartsWith(outcomeStartsWith));
        return TestData.Engine.Execute(item, action, new Rng(seed), new ManualChoice { SpecialOutcome = outcome }).Item;
    }

    [SkippableFact]
    public void Vaal_outcomes_sum_to_one_and_depend_on_the_item()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var engine = TestData.Engine!;
        var wand = engine.Preview(TestData.NewItem(Wand, Rarity.Rare).WithAffixes(2, 2), TestData.Action("Vaal Orb")).SpecialOutcomes;
        Assert.Equal(1.0, wand.Values.Sum(), 6);
        Assert.Contains(wand.Keys, k => k.StartsWith("Quality"));
        Assert.Equal(4, wand.Count);

        // a normal item has no modifiers to reroll
        var normal = engine.Preview(TestData.NewItem(Wand), TestData.Action("Vaal Orb")).SpecialOutcomes;
        Assert.DoesNotContain(normal.Keys, k => k.Contains("rerolled"));

        var forced = engine.Preview(TestData.NewItem(Wand), TestData.Action("Vaal Orb", "Omen of Corruption")).SpecialOutcomes;
        Assert.DoesNotContain(forced.Keys, k => k.StartsWith("No change"));
        Assert.Equal(1.0, forced.Values.Sum(), 6);
    }

    [SkippableFact]
    public void Enchantment_outcome_adds_a_corruption_enchantment_and_corrupts()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var item = Corrupt(TestData.NewItem(Wand, Rarity.Rare).WithAffixes(1, 1), "Corruption enchantment");
        Assert.True(item.Corrupted);
        var (enchant, _) = Assert.Single(item.CorruptionEnchants);
        Assert.Equal("corrupted", enchant.Def!.Category);
        Assert.False(TestData.Engine!.Check(item, TestData.Action("Exalted Orb")).Ok);
    }

    [SkippableFact]
    public void Socket_outcome_exceeds_the_limit_on_armour()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        Skip.If(TestData.Data!.FindBase(Body) == null, $"{Body} not in data");
        var before = TestData.NewItem(Body, Rarity.Rare);
        var item = Corrupt(before, "+1 socket");
        Assert.Equal(before.Sockets + 1, item.Sockets);
    }

    [SkippableFact]
    public void Reroll_outcome_keeps_the_affix_counts()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        for (int seed = 0; seed < 10; seed++)
        {
            var item = Corrupt(TestData.NewItem(Wand, Rarity.Rare).WithAffixes(3, 3), "1-3 modifiers", seed);
            Assert.Equal(3, item.PrefixCount);
            Assert.Equal(3, item.SuffixCount);
        }
    }

    [SkippableFact]
    public void Sacrifice_upgrades_the_enchantment_and_removes_a_modifier()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var corrupted = Corrupt(TestData.NewItem(Wand, Rarity.Rare).WithAffixes(2, 2), "Corruption enchantment");
        var (before, _) = corrupted.CorruptionEnchants.Single();

        var result = TestData.Engine!.Execute(corrupted, TestData.Action("Yaomac's Orb of Sacrifice"), new Rng(4));
        Assert.True(result.Applied, result.Summary);
        var (after, _) = result.Item.CorruptionEnchants.Single();
        Assert.Equal("corruption_upgrade", after.Def!.Category);
        Assert.Equal(before.Def!.Name.Replace("Corruption", "CorruptionUpgrade"), after.Def.Name);
        Assert.Equal(3, result.Item.AffixCount);

        // an upgraded enchantment cannot be upgraded again
        Assert.False(TestData.Engine.Check(result.Item, TestData.Action("Yaomac's Orb of Sacrifice")).Ok);
    }

    [SkippableFact]
    public void Sacrifice_and_architect_require_a_corrupted_item()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var item = TestData.NewItem(Wand, Rarity.Rare).WithAffixes(1, 1);
        Assert.Contains("only be used on Corrupted", TestData.Engine!.Check(item, TestData.Action("Yaomac's Orb of Sacrifice")).Reason);
        Assert.Contains("only be used on Corrupted", TestData.Engine.Check(item, TestData.Action("Architect's Orb")).Reason);
    }

    [SkippableFact]
    public void Architect_adds_an_enchantment_from_another_group_or_destroys()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var corrupted = Corrupt(TestData.NewItem(Wand, Rarity.Rare).WithAffixes(1, 1), "Corruption enchantment");
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

    [SkippableFact]
    public void Enchant_line_on_corrupted_item_is_imported_as_corruption_enchantment()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var text = @"Item Class: Wands
Rarity: Rare
Grim Bane
Siphoning Wand
--------
Item Level: 80
--------
25(20-30)% increased Spell Damage (enchant)
--------
Corrupted";
        var item = ItemParser.Parse(text, TestData.Data);
        var (enchant, _) = Assert.Single(item.CorruptionEnchants);
        Assert.Equal("CorruptionSpellDamageOnWeapon1", enchant.Def!.Name);
        Assert.Contains("(enchant)", ItemParser.ToText(item));
    }
}
