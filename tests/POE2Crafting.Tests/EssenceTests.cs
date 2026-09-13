namespace POE2Crafting.Tests;

public class EssenceTests
{
    [DataFact]
    public void Greater_essence_turns_magic_into_rare_with_guaranteed_mod()
    {
        var item = TestData.NewItem(TestBases.Wand, Rarity.Magic);
        Assert.True(TestData.Engine!.Check(item, TestData.Action("Greater Essence of Sorcery")).Ok);
        var result = TestData.Apply(item, "Greater Essence of Sorcery", seed: 7);

        Assert.Equal(Rarity.Rare, result.Item.Rarity);
        var added = Assert.Single(result.Item.Affixes);
        Assert.Equal("(75-89)% increased Spell Damage", added.Def!.Text);
        Assert.Equal(ModKind.Explicit, added.Kind);
        Assert.InRange(added.Values[0], 75, 89);
    }

    [DataFact]
    public void Essence_without_mod_for_the_class_is_not_applicable()
    {
        var check = TestData.Engine!.Check(TestData.NewItem(TestBases.Wand, Rarity.Magic), TestData.Action("Lesser Essence of the Body"));
        Assert.False(check.Ok);
        Assert.Contains("no effect", check.Reason);
    }

    [DataFact]
    public void Perfect_essence_replaces_a_suffix_when_suffixes_are_full_and_adds_a_crafted_mod()
    {
        var item = TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(1, 3);
        var preview = TestData.Engine!.Preview(item, TestData.Action("Perfect Essence of Sorcery")); // wand: +3 spell skills (suffix)
        Assert.True(preview.Applicability.Ok, preview.Applicability.Reason);
        Assert.All(preview.Removals, r => Assert.Equal(AffixType.Suffix, r.Mod.Affix));

        var result = TestData.Apply(item, "Perfect Essence of Sorcery", seed: 3);
        Assert.Equal(3, result.Item.SuffixCount);
        Assert.Equal(1, result.Item.PrefixCount);
        Assert.Contains(result.Item.Mods, m => m.Kind == ModKind.Crafted && m.SourceName == "Perfect Essence of Sorcery");
    }

    [DataFact]
    public void Crystallisation_omen_only_targets_perfect_and_corrupted_essences()
    {
        var check = TestData.Engine!.Check(TestData.NewItem(TestBases.Wand, Rarity.Magic), TestData.Action("Essence of Sorcery", "Omen of Dextral Crystallisation"));
        Assert.False(check.Ok);
        Assert.Contains("does not affect", check.Reason);
    }
}
