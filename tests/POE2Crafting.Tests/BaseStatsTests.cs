namespace POE2Crafting.Tests;

public class BaseStatsTests
{
    private static List<BaseItem> Bases(string itemClass) =>
        TestData.Data!.BasesOfClass(itemClass).Where(b => !b.Hidden && !b.IsRuneforged).ToList();

    private static BaseStat Stat(string label) => BaseStats.All.Single(s => s.Label == label);

    [DataFact]
    public void Best_energy_shield_gloves_come_first_by_their_defence()
    {
        var es = Bases("Gloves").Where(b => b.SubType == "Energy Shield").ToList();
        var primary = BaseStats.PrimaryOf("Energy Shield");
        Assert.Same(Stat("Energy Shield"), primary);

        var best = es.MaxBy(b => primary!.Value(b))!;
        Assert.Equal("Sirenscale Gloves", best.Name);
        Assert.Equal(54, primary!.Value(best));
        Assert.DoesNotContain(Stat("Armour"), BaseStats.For(es));
    }

    [DataFact]
    public void Weapons_list_damage_and_dps()
    {
        var hatchet = TestData.Data!.FindBase("Dull Hatchet")!;
        Assert.Equal("4–10", Stat("Physical").Text!(hatchet));
        Assert.Equal(7 * 1.5, Stat("pDPS").Value(hatchet)!.Value, 6);
        // total DPS only when there is non-physical damage
        Assert.Null(Stat("DPS").Value(hatchet));
        Assert.Contains(Stat("Level"), BaseStats.For(Bases("One Hand Axe")));
    }

    [DataFact]
    public void Hybrid_defence_types_sort_by_their_first_defence()
    {
        Assert.Same(Stat("Armour"), BaseStats.PrimaryOf("Armour/Energy Shield"));
        Assert.Null(BaseStats.PrimaryOf(null));
        Assert.Same(Stat("Life"), BaseStats.PrimaryOf("Life"));
        Assert.Null(BaseStats.PrimaryOf("Radius"));
    }
}
