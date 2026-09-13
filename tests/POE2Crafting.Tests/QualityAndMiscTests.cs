using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine;
using POE2Crafting.Core.Items;
using Xunit;

namespace POE2Crafting.Tests;

public class QualityAndMiscTests
{
    private const string Wand = "Siphoning Wand";
    private const string Ring = "Iron Ring";

    private static CraftResult Apply(Item item, string currency, string? omen = null, ManualChoice? choice = null, int seed = 1) =>
        TestData.Engine!.Execute(item, TestData.Action(currency, omen), new Rng(seed), choice);

    [SkippableFact]
    public void Without_affixes_keeps_everything_that_is_not_an_editable_affix()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var item = Item.FromBase(TestData.Data!.FindBase(Wand)!, Rarity.Rare, 82, withImplicit: true).WithAffixes(2, 1);
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

    [SkippableFact]
    public void Etcher_adds_quality_per_rarity_up_to_the_maximum()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var item = TestData.NewItem(Wand, Rarity.Rare);
        int perUse = TestData.Data!.Config.Assumptions.QualityPerUse["Rare"];
        Assert.Equal(perUse, Apply(item, "Arcanist's Etcher").Item.Quality);

        item.Quality = 20;
        Assert.Contains("maximum", TestData.Engine!.Check(item, TestData.Action("Arcanist's Etcher")).Reason);
        Assert.Contains("can only be used on", TestData.Engine.Check(item, TestData.Action("Blacksmith's Whetstone")).Reason);
    }

    [SkippableFact]
    public void Infuser_exceeds_the_maximum_and_corrupts_only_above_it()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var item = TestData.NewItem(Wand, Rarity.Rare);
        item.Quality = 20;
        var atMax = TestData.Engine!.Preview(item, TestData.Action("Vaal Arcanist's Infuser"));
        Assert.Equal(1.0, atMax.SpecialOutcomes["Not corrupted"], 6);

        item.Quality = 24;
        var above = TestData.Engine.Preview(item, TestData.Action("Vaal Arcanist's Infuser")).SpecialOutcomes;
        Assert.Equal(0.20, above["Item corrupted"], 6);

        var corrupted = Apply(item, "Vaal Arcanist's Infuser", choice: new ManualChoice { SpecialOutcome = "Item corrupted" }).Item;
        Assert.True(corrupted.Corrupted);
        Assert.Equal(25, corrupted.Quality);
    }

    [SkippableFact]
    public void Catalyst_sets_quality_type_and_replaces_other_types()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        Skip.If(TestData.Data!.FindBase(Ring) == null, $"{Ring} missing");
        var ring = TestData.NewItem(Ring, Rarity.Rare);
        var life = Apply(ring, "Flesh Catalyst").Item;
        Assert.Equal("Life", life.QualityType);
        Assert.Equal(5, life.Quality);
        Assert.Equal("life", life.QualityTag);

        var mana = Apply(life, "Neural Catalyst").Item;
        Assert.Equal("Mana", mana.QualityType);
        Assert.Equal(10, mana.Quality);

        Assert.Contains("can only be used on", TestData.Engine!.Check(TestData.NewItem(Wand), TestData.Action("Flesh Catalyst")).Reason);
    }

    [SkippableFact]
    public void Catalysing_exaltation_biases_tagged_mods_and_consumes_quality()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        Skip.If(TestData.Data!.FindBase(Ring) == null, $"{Ring} missing");
        var ring = TestData.NewItem(Ring, Rarity.Rare).WithAffixes(1, 1);
        Assert.False(TestData.Engine!.Check(ring, TestData.Action("Exalted Orb", "Omen of Catalysing Exaltation")).Ok);

        ring.QualityType = "Life";
        ring.Quality = 20;
        double LifeShare(string? omen) => TestData.Engine!.Preview(ring, TestData.Action("Exalted Orb", omen)).Additions
            .Where(a => a.Mod.ModTags.Contains("life")).Sum(a => a.Probability);
        Assert.True(LifeShare("Omen of Catalysing Exaltation") > LifeShare(null));

        var result = Apply(ring, "Exalted Orb", "Omen of Catalysing Exaltation").Item;
        Assert.Equal(0, result.Quality);
        Assert.Null(result.QualityType);
    }

    [SkippableFact]
    public void Artificers_orb_adds_sockets_up_to_the_base_limit()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var wand = TestData.NewItem(Wand);
        wand.Sockets = 0;
        Assert.Equal(1, Apply(wand, "Artificer's Orb").Item.Sockets);

        wand.Sockets = wand.Base!.SocketLimit!.Value;
        Assert.Contains("maximum", TestData.Engine!.Check(wand, TestData.Action("Artificer's Orb")).Reason);
    }

    [SkippableFact]
    public void Extraction_destroys_the_item_and_lists_the_augments()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var wand = TestData.NewItem(Wand);
        Assert.False(TestData.Engine!.Check(wand, TestData.Action("Orb of Extraction")).Ok);
        wand.Runes.Add("+1 to Level of all Spell Skills");
        var result = Apply(wand, "Orb of Extraction");
        Assert.True(result.Destroyed);
        Assert.Contains("+1 to Level of all Spell Skills", result.Summary);
    }

    [SkippableFact]
    public void Blazing_flux_turns_cold_resistance_into_an_equivalent_fire_resistance()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        Skip.If(TestData.Data!.FindBase(Ring) == null, $"{Ring} missing");
        var ring = TestData.NewItem(Ring, Rarity.Rare);
        var cold = TestData.Pool!.AllForBase(ring).Where(m => m.Family == "ColdResistance").MaxBy(m => m.Level)!;
        ring.AddMod(cold, values: new() { cold.Ranges[0][1] });

        var result = Apply(ring, "Blazing Flux").Item;
        var fire = Assert.Single(result.Affixes);
        Assert.Equal("FireResistance", fire.Def!.Family);
        Assert.Equal(cold.Level, fire.Def.Level);
        Assert.Equal(fire.Def.Ranges[0][1], fire.Values[0]);

        Assert.False(TestData.Engine!.Check(result, TestData.Action("Blazing Flux")).Ok);
    }

    [Fact]
    public void Rescale_keeps_the_relative_position()
    {
        Assert.Equal(new List<double> { 22 }, ModText.RescaleValues(new[] { 38.0 }, new[] { new[] { 36.0, 40.0 } }, new[] { new[] { 20.0, 23.0 } }));
    }

    [Fact]
    public void Quality_tag_is_read_from_catalyst_and_item_text_names()
    {
        Assert.Equal("life", CatalystDef.QualityTagFor("Life"));
        Assert.Equal("caster", CatalystDef.QualityTagFor("Caster Modifiers"));
        Assert.Equal("defences", CatalystDef.QualityTagFor("Armour , Evasion and Energy Shield"));
        Assert.Null(CatalystDef.QualityTagFor(null));
    }
}
