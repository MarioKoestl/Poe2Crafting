using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Planning;

/// <summary>A crafting guide played through the engine: its starting item and the steps as a strategy (chances, item after each step).</summary>
public sealed class GuideWalkthrough
{
    public CraftingGuide Guide { get; init; } = null!;
    public Item? Start { get; init; }
    public CraftingStrategy Strategy { get; init; } = new();
    /// <summary>Why the guide could not be played completely with the current data (e.g. a mod or currency is missing).</summary>
    public List<string> Problems { get; } = new();
}

/// <summary>
/// Plays a <see cref="CraftingGuide"/>: builds the starting item, then applies every step with the outcome the guide calls a hit.
/// The chance of that hit comes from <see cref="CraftingEngine.Preview"/>, so a guide always shows the simulator's current rules and assumptions.
/// </summary>
public sealed class GuideRunner
{
    private readonly GameData _data;
    private readonly CraftingEngine _engine;

    public GuideRunner(CraftingEngine engine)
    {
        _engine = engine;
        _data = engine.Data;
    }

    public GuideWalkthrough Run(CraftingGuide guide)
    {
        var (start, startProblem) = BuildStart(guide);
        var walkthrough = new GuideWalkthrough
        {
            Guide = guide, Start = start,
            Strategy = new CraftingStrategy { Id = "guide-" + guide.Id, Name = guide.Name, Description = guide.Summary, StartLabel = "Starting item" },
        };
        if (startProblem != null) walkthrough.Problems.Add(startProblem);
        if (start == null) return walkthrough;

        var item = start;
        foreach (var step in guide.Steps)
        {
            var (craftStep, problem) = Apply(item, step, walkthrough.Strategy);
            if (craftStep == null)
            {
                walkthrough.Problems.Add(problem!);
                break;
            }
            walkthrough.Strategy.Add(craftStep);
            item = craftStep.Result!;
        }
        // the guide's own summary describes it better than the list of currencies
        walkthrough.Strategy.Description = guide.Summary;
        return walkthrough;
    }

    private (Item? Item, string? Problem) BuildStart(CraftingGuide guide)
    {
        if (_data.FindBase(guide.Start.Base) is not { } baseItem) return (null, $"Base \"{guide.Start.Base}\" is not in the data store.");
        var item = Item.FromBase(baseItem, guide.Start.Rarity, guide.Start.ItemLevel, withImplicit: true);
        foreach (var wanted in guide.Start.Mods)
        {
            var def = _engine.Pool.AllForBase(item, wanted.Affix)
                .Where(m => Contains(m.Text, wanted.Text) && !item.HasFamily(m.Family) && m.Level <= item.ItemLevel)
                .MaxBy(m => m.Level);
            if (def == null) return (null, $"No modifier \"{wanted.Text}\" found for {baseItem.Name}.");
            item.AddMod(def).Fractured = wanted.Fractured;
        }
        return (item, null);
    }

    /// <summary>The guide step applied to the item as a strategy step, or the reason why it can't be played.</summary>
    private (CraftStep? Step, string? Problem) Apply(Item item, GuideStep step, CraftingStrategy strategy)
    {
        string Problem(string reason) => $"Step {strategy.Steps.Count + 1} ({step.Title}): {reason}";
        if (_data.FindCurrency(step.Currency) is not { } currency) return (null, Problem($"\"{step.Currency}\" is not in the data store."));
        var omens = step.Omens.Select(name => _data.FindOmen(name)).ToList();
        if (omens.IndexOf(null) is var missing and >= 0) return (null, Problem($"Omen \"{step.Omens[missing]}\" is not in the data store."));
        var action = CraftAction.Of(currency, omens.ToArray());

        var preview = _engine.Preview(item, action);
        if (!preview.Applicability.Ok) return (null, Problem(preview.Applicability.Reason));
        var (chance, choice, reason) = Hit(item, action, preview, step.Hit);
        if (reason != null) return (null, Problem(reason));

        var next = item;
        int uses = Math.Max(1, step.Uses);
        for (int use = 0; use < uses; use++)
        {
            if (use > 0 && !_engine.Check(next, action).Ok) return (null, Problem($"only {use} of {uses} uses are possible."));
            if (OutcomeChance.TryExecute(_engine, next, action, use == 0 ? choice : new ManualChoice()) is not { } after)
                return (null, Problem("the step cannot be executed with the chosen outcome."));
            next = after;
        }

        var craftStep = strategy.NewStep(action, step.Title, chance, next, uses);
        craftStep.Explanation = step.Explanation;
        craftStep.RestartLabel = step.OnMiss;
        craftStep.Notes.AddRange(preview.Notes.Distinct());
        return (craftStep, null);
    }

    /// <summary>The chance of the guide's hit and the manual choice that produces it; a reason when the hit cannot happen at all.</summary>
    private (double Chance, ManualChoice Choice, string? Reason) Hit(Item item, CraftAction action, StepPreview preview, GuideHit hit)
    {
        double chance = 1;
        string? outcome = null;
        if (hit.Outcome != null)
        {
            var outcomes = preview.SpecialOutcomes.Where(o => Contains(o.Key, hit.Outcome)).ToList();
            if (outcomes.Count == 0) return (0, new ManualChoice(), $"no outcome \"{hit.Outcome}\" is possible.");
            chance *= outcomes.Sum(o => o.Value);
            outcome = outcomes.MaxBy(o => o.Value).Key;
        }
        var choice = new ManualChoice { SpecialOutcome = outcome };

        int? removal = null;
        if (hit.Select != null)
        {
            var (selectChance, best) = OutcomeChance.Of(preview.Removals, r => hit.Select == AnyText || Contains(r.Mod.DisplayText(), hit.Select));
            if (best == null) return (0, choice, $"no modifier \"{hit.Select}\" can be selected.");
            chance *= selectChance;
            choice.RemoveIndices.Add(best.Index);
            removal = best.Index;
        }

        if (hit.Add != null || hit.AddTag != null)
        {
            // when removal and addition are separate random events (Chaos Orb), the addition pool depends on the removed mod
            var additions = removal != null && preview.TwoStepChoice ? _engine.Preview(item, action, removal).Additions : preview.Additions;
            var (addChance, best) = OutcomeChance.Of(additions, m =>
                (hit.Add is null or AnyText || Contains(m.Text, hit.Add)) && (hit.AddTag == null || m.ModTags.Contains(hit.AddTag)));
            if (best == null) return (0, choice, $"no modifier matching \"{hit.Add ?? hit.AddTag}\" can be added.");
            chance *= addChance;
            choice.AddModIds.Add(best.Mod.Id);
        }
        return (chance, choice, null);
    }

    /// <summary>Filter value that matches any modifier.</summary>
    private const string AnyText = "*";

    private static bool Contains(string text, string part) => text.Contains(part, StringComparison.OrdinalIgnoreCase);
}
