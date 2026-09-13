namespace POE2Crafting.Tests;

public class GuideTests
{
    private static GuideWalkthrough Run(CraftingGuide guide) => new GuideRunner(TestData.Engine!).Run(guide);

    private static GuideWalkthrough Run(string id) => Run(TestData.Data!.Guides.Single(g => g.Id == id));

    [DataFact]
    public void Every_guide_plays_through_with_the_current_data()
    {
        Assert.NotEmpty(TestData.Data!.Guides);
        foreach (var guide in TestData.Data.Guides)
        {
            var walkthrough = Run(guide);
            Assert.True(walkthrough.Problems.Count == 0, $"{guide.Name}: {string.Join(" / ", walkthrough.Problems)}");
            Assert.Equal(guide.Steps.Count, walkthrough.Strategy.Steps.Count);
            Assert.All(walkthrough.Strategy.Steps, s => Assert.False(string.IsNullOrEmpty(s.Explanation)));
        }
    }

    [DataFact]
    public void Abyss_mark_guide_fractures_the_skills_at_one_in_three()
    {
        var steps = Run("fracture-abyss-mark").Strategy.Steps;
        Assert.Equal(1.0 / 3, steps.Last().SuccessProbability, 6);
        var final = steps.Last().Result!;
        Assert.True(final.Affixes.Single(m => m.Fractured).DisplayText().Contains("Melee Skills"));
        Assert.Single(final.UnrevealedMods);
    }

    [DataFact]
    public void Plus_four_guides_end_with_plus_four_skills_and_without_the_breach_mod()
    {
        foreach (var (id, text) in new[] { ("plus-four-melee-amulet", "+4 to Level of all Melee Skills"), ("plus-four-spell-amulet", "+4 to Level of all Spell Skills") })
        {
            var walkthrough = Run(id);
            var final = walkthrough.Strategy.Steps.Last().Result!;
            Assert.Contains(final.Affixes, m => final.EffectiveText(m) == text);
            Assert.DoesNotContain(final.Affixes, m => m.Def?.Family == ModFamilies.MaximumQuality);
            Assert.Equal(1.0, walkthrough.Strategy.Steps.Last().SuccessProbability, 6);
        }
    }
}
