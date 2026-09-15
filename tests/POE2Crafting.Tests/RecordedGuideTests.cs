namespace POE2Crafting.Tests;

public class RecordedGuideTests
{
    private static HistoryEntry Entry(string action, Item item) => new() { Action = action, Item = item, Summary = action };

    /// <summary>Start (rare amulet with 1P/1S) → Exalted Orb → Chaos Orb → manual edit.</summary>
    private static (RecordedGuide Guide, Item Start, Item Exalted) Record()
    {
        var start = TestData.NewItem(TestBases.Amulet, Rarity.Rare, withImplicit: true).WithAffixes(1, 1);
        var exalted = TestData.Apply(start, "Exalted Orb", seed: 3).Item;
        var chaos = TestData.Apply(exalted, "Chaos Orb", seed: 4).Item;
        var guide = new RecordedGuide
        {
            Name = "Test craft",
            History = { Entry("Composed", start), Entry("Exalted Orb", exalted), Entry("Chaos Orb", chaos), Entry("Edited", chaos.Clone()) },
        };
        return (guide, start, exalted);
    }

    [DataFact]
    public void Recorded_history_becomes_a_guide_with_its_path_base_and_materials()
    {
        var (guide, start, _) = Record();
        var walkthrough = new RecordedGuideRunner(TestData.Engine!).Run(guide);

        Assert.Same(start, walkthrough.Start);
        Assert.Contains(start.BaseName, walkthrough.Guide.Tags);
        Assert.Equal(3, walkthrough.Strategy.Steps.Count);
        Assert.Same(guide.History[^1].Item, walkthrough.Strategy.Steps[^1].Result);

        var materials = walkthrough.Strategy.Steps.Materials().ToDictionary(m => m.Name, m => m.PerRun);
        Assert.Equal(1, materials["Exalted Orb"]);
        Assert.Equal(1, materials["Chaos Orb"]);
        Assert.Equal(2, materials.Count);

        // the manual edit is kept in the path but reported
        Assert.Single(walkthrough.Problems);
        Assert.Contains("Edited", walkthrough.Problems[0]);
    }

    [DataFact]
    public void Step_chance_is_the_chance_of_the_added_mod_at_its_tier_or_better()
    {
        var (guide, start, exalted) = Record();
        var step = new RecordedGuideRunner(TestData.Engine!).Run(guide).Strategy.Steps[0];

        var added = exalted.Affixes.Single(m => !start.Affixes.Any(s => s.ModId == m.ModId)).Def!;
        var expected = TestData.Engine!.Preview(start, TestData.Action("Exalted Orb")).Additions
            .Where(a => ModTiers.IsSameOrBetterTier(a.Mod, added)).Sum(a => a.Probability);
        Assert.InRange(step.SuccessProbability, 1e-9, 0.999);
        Assert.Equal(expected, step.SuccessProbability, 9);
    }
}
