namespace POE2Crafting.Tests;

public class ChaosChoiceTests
{
    [DataFact]
    public void Choosing_only_the_added_mod_rolls_a_compatible_removal_instead_of_throwing()
    {
        var item = TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(3, 3);

        // with 3P/3S, a prefix can only be added if a prefix is removed
        var prefix = TestData.Pool!.Candidates(item, AffixType.Prefix).First().Mod;
        for (int seed = 0; seed < 20; seed++)
        {
            var result = TestData.Apply(item, "Chaos Orb", new ManualChoice { AddModIds = { prefix.Id } }, seed);
            Assert.Contains(result.Item.Affixes, m => m.ModId == prefix.Id);
            Assert.Equal(3, result.Item.PrefixCount);
        }
    }

    [DataFact]
    public void Preview_after_a_fixed_removal_only_offers_that_affix_type()
    {
        var item = TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(3, 3);
        int suffixIndex = item.Mods.FindIndex(m => m.Affix == AffixType.Suffix);

        var preview = TestData.Engine!.Preview(item, TestData.Action("Chaos Orb"), suffixIndex);
        Assert.True(preview.TwoStepChoice);
        Assert.All(preview.Additions, a => Assert.Equal(AffixType.Suffix, a.Mod.AffixType));
        Assert.Equal(1.0, preview.Additions.Sum(a => a.Probability), 6);
    }

    [DataFact]
    public void Impossible_choice_throws_an_invalid_choice_exception()
    {
        var item = TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(1, 1);
        var foreign = TestData.BestMod(TestData.NewItem(TestBases.Amulet), "to Spirit");
        Assert.Throws<InvalidChoiceException>(() => TestData.Apply(item, "Exalted Orb", new ManualChoice { AddModIds = { foreign.Id } }));
    }
}

public class WhittlingTests
{
    private static ModDef Mod(string name, string text) => TestData.Data!.Mods.First(m => m.Name == name && m.Text.Contains(text));

    [DataFact]
    public void Whittling_removes_the_lowest_level_modifier_for_sure()
    {
        // Gold Amulet from a real craft: of the Sorcerer (level 75), Countess' Spirit (54), of Ephij (82)
        var item = TestData.NewItem(TestBases.Amulet, Rarity.Rare, withImplicit: true);
        item.AddMod(Mod("of the Sorcerer", "Spell Skills"));
        var spirit = item.AddMod(Mod("Countess'", "Spirit"));
        item.AddMod(Mod("of Ephij", "Lightning Resistance"));

        var preview = TestData.Engine!.Preview(item, TestData.Action("Chaos Orb", "Omen of Whittling"));
        var removal = Assert.Single(preview.Removals);
        Assert.Same(spirit, removal.Mod);
        Assert.Equal(1.0, removal.Probability);
        Assert.Contains("level 54", preview.RemovalLabel);

        var after = TestData.Apply(item, "Chaos Orb", omens: "Omen of Whittling").Item;
        Assert.DoesNotContain(after.Affixes, m => m.ModId == spirit.ModId);
    }
}

public class ChosenValuesTests
{
    [DataFact]
    public void Chosen_addition_gets_the_entered_values_inside_its_range()
    {
        var amulet = TestData.NewItem(TestBases.Amulet, Rarity.Magic).WithAffixes(0, 1);
        var es = TestData.Pool!.AllForBase(amulet, AffixType.Prefix).Where(m => m.Text.Contains("increased maximum Energy Shield")).MaxBy(m => m.Level)!;
        var best = es.Ranges.Select(r => ModText.Bounds(r).Hi).ToList();

        var regal = TestData.Apply(amulet, "Regal Orb", new ManualChoice { AddModIds = { es.Id }, Values = { best } }).Item;
        Assert.Equal(best, regal.Affixes.Single(m => m.ModId == es.Id).Values);

        var tooHigh = best.Select(v => v + 100).ToList();
        Assert.Throws<InvalidChoiceException>(() => TestData.Apply(amulet, "Regal Orb", new ManualChoice { AddModIds = { es.Id }, Values = { tooHigh } }));
    }
}

