namespace POE2Crafting.Tests;

public class QualityAndMiscTests
{

    [DataFact]
    public void Without_affixes_keeps_everything_that_is_not_an_editable_affix()
    {
        var item = TestData.NewItem(TestBases.Wand, Rarity.Rare, withImplicit: true).WithAffixes(2, 1);
        item.Quality = 12;
        item.Corrupted = true;
        item.Affixes.First().Fractured = true;
        item.Mods.Add(new ItemMod { Kind = ModKind.Desecrated, Affix = AffixType.Suffix, Unrevealed = true });

        var stripped = item.WithoutAffixes();
        Assert.Equal(12, stripped.Quality);
        Assert.True(stripped.Corrupted);
        Assert.Contains(stripped.Mods, m => m.Kind == ModKind.Implicit);
        Assert.Single(stripped.Affixes, m => m.Unrevealed);
        Assert.Equal(4, item.Affixes.Count()); // the original is untouched
    }

    [DataFact]
    public void Only_one_modifier_can_be_fractured_and_divine_keeps_its_values()
    {
        var item = TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(2, 2);
        var fractured = TestData.Apply(item, "Fracturing Orb").Item;
        var locked = Assert.Single(fractured.Affixes, m => m.Fractured);
        Assert.Contains("only one per item", TestData.Engine!.Check(fractured, TestData.Action("Fracturing Orb")).Reason);

        locked.Values = locked.Def!.Ranges.Select(r => r[0]).ToList();
        for (int seed = 0; seed < 5; seed++)
            Assert.Equal(locked.Values, Assert.Single(TestData.Apply(fractured, "Divine Orb", seed: seed).Item.Affixes, m => m.Fractured).Values);
    }

    [DataFact]
    public void Etcher_adds_quality_per_rarity_up_to_the_maximum()
    {
        var item = TestData.NewItem(TestBases.Wand, Rarity.Rare);
        int perUse = TestData.Data!.Config.Assumptions.QualityPerUse[Rarity.Rare];
        Assert.Equal(perUse, TestData.Apply(item, "Arcanist's Etcher").Item.Quality);

        item.Quality = 20;
        Assert.Contains("maximum", TestData.Engine!.Check(item, TestData.Action("Arcanist's Etcher")).Reason);
        Assert.Contains("can only be used on", TestData.Engine.Check(item, TestData.Action("Blacksmith's Whetstone")).Reason);
    }

    [DataFact]
    public void Infuser_exceeds_the_maximum_and_corrupts_only_above_it()
    {
        var item = TestData.NewItem(TestBases.Wand, Rarity.Rare);
        item.Quality = 20;
        var atMax = TestData.Engine!.Preview(item, TestData.Action("Vaal Arcanist's Infuser"));
        Assert.Equal(1.0, atMax.SpecialOutcomes["Not corrupted"], 6);

        item.Quality = 24;
        var above = TestData.Engine.Preview(item, TestData.Action("Vaal Arcanist's Infuser")).SpecialOutcomes;
        Assert.Equal(0.20, above["Item corrupted"], 6);

        var corrupted = TestData.Apply(item, "Vaal Arcanist's Infuser", choice: new ManualChoice { SpecialOutcome = "Item corrupted" }).Item;
        Assert.True(corrupted.Corrupted);
        Assert.Equal(25, corrupted.Quality);
    }

    [DataFact]
    public void Catalyst_sets_quality_type_and_replaces_other_types()
    {
        var ring = TestData.NewItem(TestBases.Ring, Rarity.Rare);
        var life = TestData.Apply(ring, "Flesh Catalyst").Item;
        Assert.Equal("Life", life.QualityType);
        Assert.Equal(5, life.Quality);
        Assert.Equal("life", life.QualityTag);

        var mana = TestData.Apply(life, "Neural Catalyst").Item;
        Assert.Equal("Mana", mana.QualityType);
        Assert.Equal(10, mana.Quality);

        Assert.Contains("can only be used on", TestData.Engine!.Check(TestData.NewItem(TestBases.Wand), TestData.Action("Flesh Catalyst")).Reason);
    }

    [DataFact]
    public void Catalysing_exaltation_biases_tagged_mods_and_consumes_quality()
    {
        var ring = TestData.NewItem(TestBases.Ring, Rarity.Rare).WithAffixes(1, 1);
        Assert.False(TestData.Engine!.Check(ring, TestData.Action("Exalted Orb", "Omen of Catalysing Exaltation")).Ok);

        ring.QualityType = "Life";
        ring.Quality = 20;
        double LifeShare(string? omen) => TestData.Engine!.Preview(ring, TestData.Action("Exalted Orb", omen)).Additions
            .Where(a => a.Mod.ModTags.Contains("life")).Sum(a => a.Probability);
        Assert.True(LifeShare("Omen of Catalysing Exaltation") > LifeShare(null));

        var result = TestData.Apply(ring, "Exalted Orb", omens: "Omen of Catalysing Exaltation").Item;
        Assert.Equal(0, result.Quality);
        Assert.Null(result.QualityType);
    }

    [DataFact]
    public void Artificers_orb_adds_sockets_up_to_the_base_limit()
    {
        var wand = TestData.NewItem(TestBases.Wand);
        wand.Sockets = 0;
        Assert.Equal(1, TestData.Apply(wand, "Artificer's Orb").Item.Sockets);

        wand.Sockets = wand.Base!.SocketLimit!.Value;
        Assert.Contains("maximum", TestData.Engine!.Check(wand, TestData.Action("Artificer's Orb")).Reason);
    }

    [DataFact]
    public void Extraction_destroys_the_item_and_lists_the_augments()
    {
        var wand = TestData.NewItem(TestBases.Wand);
        Assert.False(TestData.Engine!.Check(wand, TestData.Action("Orb of Extraction")).Ok);
        wand.Runes.Add("+1 to Level of all Spell Skills");
        var result = TestData.Apply(wand, "Orb of Extraction");
        Assert.True(result.Destroyed);
        Assert.Contains("+1 to Level of all Spell Skills", result.Summary);
    }

    [DataFact]
    public void Blazing_flux_turns_cold_resistance_into_an_equivalent_fire_resistance()
    {
        var ring = TestData.NewItem(TestBases.Ring, Rarity.Rare);
        var cold = TestData.Pool!.AllForBase(ring).Where(m => m.Family == "ColdResistance").MaxBy(m => m.Level)!;
        ring.AddMod(cold, values: new() { cold.Ranges[0][1] });

        var result = TestData.Apply(ring, "Blazing Flux").Item;
        var fire = Assert.Single(result.Affixes);
        Assert.Equal("FireResistance", fire.Def!.Family);
        Assert.Equal(cold.Level, fire.Def.Level);
        Assert.Equal(fire.Def.Ranges[0][1], fire.Values[0]);

        Assert.False(TestData.Engine!.Check(result, TestData.Action("Blazing Flux")).Ok);
    }
}
