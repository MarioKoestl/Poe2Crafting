namespace POE2Crafting.Tests;

/// <summary>
/// A random step of the golden path carries a retry plan: which state to go back to when it misses, and what to apply to get there.
/// The plan is written by hand, so it never shows up as a step of the path — only under the step, and in the expected materials.
/// </summary>
public class RetryPlanTests
{
    /// <summary>Recorded history: start, a safe step, and a random one that carries a plan.</summary>
    private static RecordedGuide Recorded(bool withPlan)
    {
        var start = TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(1, 1);
        var safe = TestData.Apply(start, "Exalted Orb", seed: 1).Item;
        var random = TestData.Apply(safe, "Exalted Orb", seed: 2).Item;

        HistoryEntry E(string action, Item item) => new() { Action = action, Item = item, Summary = action };
        var guide = new RecordedGuide { Name = "retry" };
        guide.History.Add(E("Composed", start));
        guide.History.Add(E("Exalted Orb", safe));
        guide.History.Add(E("Exalted Orb", random));
        if (withPlan)
        {
            var step = guide.History[2];
            step.RetryFromId = guide.History[1].Id;      // go back to the state after step 1
            step.RetrySteps.Add(new RetryStep { Action = "Orb of Annulment + Omen of Light", Note = "take the wrong mod off again" });
            step.RetrySteps.Add(new RetryStep { Action = "Chaos Orb" });
        }
        foreach (var entry in guide.History) entry.Item.Bind(TestData.Data!);
        return guide;
    }

    [DataFact]
    public void Without_a_plan_a_step_carries_no_retry()
    {
        var walkthrough = new RecordedGuideRunner(TestData.Engine!).Run(Recorded(withPlan: false));
        Assert.Equal(2, walkthrough.Strategy.Steps.Count);
        Assert.All(walkthrough.Strategy.Steps, s => Assert.Null(s.Retry));
    }

    /// <summary>The plan hangs off its own step and never becomes a step of the golden path.</summary>
    [DataFact]
    public void The_plan_hangs_under_the_random_step_and_keeps_the_path_straight()
    {
        var walkthrough = new RecordedGuideRunner(TestData.Engine!).Run(Recorded(withPlan: true));
        var steps = walkthrough.Strategy.Steps;

        Assert.Equal(2, steps.Count);
        Assert.Null(steps[0].Retry);

        var retry = Assert.IsType<CraftingStrategy>(steps[1].Retry);
        Assert.Equal(new[] { "Orb of Annulment + Omen of Light", "Chaos Orb" }, retry.Steps.Select(s => s.CurrencyName));
        Assert.Equal("take the wrong mod off again", retry.Steps[0].Explanation);
        Assert.Equal("step 1", steps[1].RetryBackLabel);
        Assert.Empty(walkthrough.Problems);
    }

    /// <summary>The plan costs nothing on a perfect run, but every missed attempt of the step pays for one run of it.</summary>
    [DataFact]
    public void Plan_materials_count_per_missed_attempt()
    {
        var steps = new RecordedGuideRunner(TestData.Engine!).Run(Recorded(withPlan: true)).Strategy.Steps;
        var random = steps[1];
        var annul = Assert.Single(steps.Materials(), m => m.Name == "Orb of Annulment");

        double misses = 1 / random.SuccessProbability - 1;
        Assert.Equal(0, annul.PerRun);                  // a run without a miss never uses it
        Assert.Equal(misses, annul.Expected, 6);
        Assert.True(misses > 0, "the step has to be able to miss for the plan to cost anything");

        // the omen is consumed per attempt as well
        Assert.Contains(steps.Materials(), m => m.Name == "Omen of Light" && m.Expected > 0);
    }

    /// <summary>A plan whose currency is unknown (renamed in the data) still shows up, it just carries no materials.</summary>
    [DataFact]
    public void An_unknown_action_in_a_plan_does_not_break_the_guide()
    {
        var recorded = Recorded(withPlan: true);
        recorded.History[2].RetrySteps.Add(new RetryStep { Action = "Orb of Nothing" });
        var steps = new RecordedGuideRunner(TestData.Engine!).Run(recorded).Strategy.Steps;
        Assert.Equal(3, steps[1].Retry!.Steps.Count);
        Assert.Contains(steps[1].Retry!.Steps, s => s.CurrencyName == "Orb of Nothing");
    }
}
