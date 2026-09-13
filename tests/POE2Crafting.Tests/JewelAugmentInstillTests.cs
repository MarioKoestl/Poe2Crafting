namespace POE2Crafting.Tests;

public class JewelAugmentInstillTests
{

    [DataFact]
    public void Rare_jewels_have_two_prefixes_and_two_suffixes()
    {
        var jewel = TestData.NewItem(TestBases.Jewel, Rarity.Rare).WithAffixes(2, 2);
        Assert.False(TestData.Engine!.Check(jewel, TestData.Action("Exalted Orb")).Ok);
        Assert.Equal(2, TestData.Data!.Config.Assumptions.MaxAffixes(jewel, Rarity.Rare, AffixType.Suffix));
    }

    [DataFact]
    public void Liquid_contempt_adds_a_crafted_mod_that_allows_an_extra_affix()
    {
        var jewel = TestData.NewItem(TestBases.Jewel, Rarity.Rare).WithAffixes(2, 2);
        var preview = TestData.Engine!.Preview(jewel, TestData.Action("Potent Liquid Contempt"));
        Assert.True(preview.Applicability.Ok, preview.Applicability.Reason);
        Assert.Equal(2, preview.Additions.Count);                       // "+1 Suffix allowed" (prefix) or "+1 Prefix allowed" (suffix), 50/50
        Assert.All(preview.Additions, a => Assert.Equal(0.5, a.Probability, 6));

        var suffixAllowed = preview.Additions.Single(a => a.Mod.Text.Contains("Suffix Modifier allowed")).Mod;
        var result = TestData.Apply(jewel, "Potent Liquid Contempt", new ManualChoice { AddModIds = { suffixAllowed.Id } }).Item;
        var crafted = Assert.Single(result.Affixes, m => m.Kind == ModKind.Crafted);
        Assert.Equal(AffixType.Prefix, crafted.Affix);
        Assert.Equal(3, TestData.Data!.Config.Assumptions.MaxAffixes(result, Rarity.Rare, AffixType.Suffix));
        Assert.Equal(4, result.AffixCount);
        Assert.True(TestData.Engine.Check(result, TestData.Action("Exalted Orb", "Omen of Dextral Exaltation")).Ok);
    }

    [DataFact]
    public void Liquid_emotion_has_no_effect_outside_its_jewel_type()
    {
        Assert.Contains("no effect", TestData.Engine!.Check(TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(1, 1), TestData.Action("Liquid Envy")).Reason);
    }

    [DataFact]
    public void Runes_fill_free_sockets_with_the_class_effect_and_replace_when_full()
    {
        var wand = TestData.NewItem(TestBases.Wand, Rarity.Rare);
        wand.Sockets = 1;
        var socketed = TestData.Apply(wand, "Desert Rune").Item;
        Assert.Equal("Gain 8% of Damage as Extra Fire Damage", Assert.Single(socketed.Runes));

        var preview = TestData.Engine!.Preview(socketed, TestData.Action("Glacial Rune"));
        var replace = Assert.Single(preview.SpecialOutcomes).Key;
        Assert.StartsWith("Replace socket 1", replace);
        var replaced = TestData.Apply(socketed, "Glacial Rune").Item;
        Assert.Equal("Gain 8% of Damage as Extra Cold Damage", Assert.Single(replaced.Runes));

        wand.Sockets = 0;
        Assert.Contains("no augment socket", TestData.Engine.Check(wand, TestData.Action("Desert Rune")).Reason);
    }

    [DataFact]
    public void Instill_allocates_a_notable_on_amulets_and_replaces_the_previous_one()
    {
        var engine = TestData.Engine!;
        var data = TestData.Data!;
        var recipe = data.FindInstill("Flamekeeper")!;
        Assert.Equal(new[] { "Guilt", "Ire", "Ire" }, recipe.Emotions);
        Assert.Equal("Diluted Liquid Ire", data.EmotionCurrency("Ire")!.Name);

        var amulet = TestData.NewItem(TestBases.Amulet, Rarity.Rare);
        var once = engine.Instill(amulet, recipe).Item;
        Assert.Equal("Allocates Flamekeeper", once.InstilledNotable!.DisplayText());

        var twice = engine.Instill(once, data.Instills.First(r => r.Notable != "Flamekeeper")).Item;
        Assert.Single(twice.Mods, m => m.Kind == ModKind.Enchant);

        amulet.Corrupted = true;
        Assert.False(engine.CheckInstill(amulet, recipe).Ok);
        Assert.False(engine.CheckInstill(TestData.NewItem(TestBases.Wand), recipe).Ok);
    }
}
