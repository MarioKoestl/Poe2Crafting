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
