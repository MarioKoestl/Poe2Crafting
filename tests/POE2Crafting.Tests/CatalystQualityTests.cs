namespace POE2Crafting.Tests;

public class CatalystQualityTests
{
    private static Item SpellAmulet()
    {
        var amulet = TestData.NewItem(TestBases.Amulet, Rarity.Rare);
        amulet.AddMod(TestData.BestMod(amulet, "Level of all Spell Skills"));
        return amulet;
    }

    [DataFact]
    public void Catalyst_quality_can_be_chosen_up_to_the_maximum()
    {
        var amulet = SpellAmulet();
        var quality = TestData.Engine!.Preview(amulet, TestData.Action("Sibilant Catalyst")).Quality!;
        Assert.Equal(TestData.Data!.Config.Assumptions.CatalystQualityPerUse, quality.Default);
        Assert.Equal(1, quality.Min);

        var result = TestData.Apply(amulet, "Sibilant Catalyst", new ManualChoice { Quality = quality.Max });
        Assert.Equal(quality.Max, result.Item.Quality);
        Assert.Throws<InvalidChoiceException>(() => TestData.Apply(amulet, "Sibilant Catalyst", new ManualChoice { Quality = quality.Max + 1 }));
    }

    [DataFact]
    public void Quality_effects_show_when_plus_three_skills_becomes_plus_four()
    {
        var amulet = SpellAmulet();
        var quality = TestData.Engine!.Preview(amulet, TestData.Action("Sibilant Catalyst")).Quality!;

        // the amulet's maximum is 20%: +4 needs more maximum quality (Essence of the Breach), the step is still shown
        var effect = Assert.Single(QualityEffects.For(amulet, quality.Type, 20, quality.Max, TestData.Data!.HighestReachableQuality));
        Assert.Equal("+3 to Level of all Spell Skills", effect.Now);
        Assert.Equal("+3 to Level of all Spell Skills", effect.At);
        Assert.Equal(34, effect.NextStepQuality);
        Assert.Equal("+4 to Level of all Spell Skills", effect.NextStepText);
    }
}
