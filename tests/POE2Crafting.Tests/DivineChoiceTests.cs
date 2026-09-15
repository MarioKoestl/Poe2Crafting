namespace POE2Crafting.Tests;

public class DivineChoiceTests
{
    [DataFact]
    public void Divine_preview_lists_the_rerolled_mods_and_applies_chosen_values()
    {
        var item = TestData.NewItem(TestBases.Amulet, Rarity.Rare).WithAffixes(1, 1);
        item.Affixes.First().Fractured = true;
        var preview = TestData.Engine!.Preview(item, TestData.Action("Divine Orb"));

        var index = Assert.Single(preview.ValueRerolls).Index;
        var mod = item.Mods[index];
        Assert.False(mod.Fractured);

        var chosen = mod.Def!.Ranges.Select(r => ModText.Bounds(r).Hi).ToList();
        var result = TestData.Apply(item, "Divine Orb", new ManualChoice { Rerolls = new() { [index] = chosen } });
        Assert.Equal(chosen, result.Item.Mods[index].Values);
    }

    [DataFact]
    public void Divine_rejects_values_outside_the_range()
    {
        var item = TestData.NewItem(TestBases.Amulet, Rarity.Rare).WithAffixes(1, 0);
        var index = item.Mods.FindIndex(m => m.IsAffix);
        var tooHigh = item.Mods[index].Def!.Ranges.Select(r => ModText.Bounds(r).Hi + 1000).ToList();

        Assert.Throws<InvalidChoiceException>(() => TestData.Apply(item, "Divine Orb", new ManualChoice { Rerolls = new() { [index] = tooHigh } }));
    }
}
