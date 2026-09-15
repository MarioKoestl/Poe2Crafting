using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Planning;

/// <summary>Strategies found for a target, plus reasons when (part of) the target cannot be reached.</summary>
public sealed class PlanResult
{
    public List<CraftingStrategy> Strategies { get; } = new();
    public List<string> Problems { get; } = new();
}

/// <summary>A sequence of crafting steps with chances (a planner result or a played crafting guide).</summary>
public sealed class CraftingStrategy
{
    /// <summary>Id of the start node that restarts refer to (flowchart).</summary>
    public const string StartStepId = "start";

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; set; } = "";
    public List<CraftStep> Steps { get; } = new();
    public double OverallProbability { get; set; }
    public double ExpectedAttempts => OverallProbability > 0 ? 1.0 / OverallProbability : double.PositiveInfinity;
    public bool HasBrickRisk => Steps.Any(s => s.BrickProbability > 0);
    /// <summary>Label of the flowchart's start node.</summary>
    public string StartLabel { get; init; } = "Current Item";

    /// <summary>Append a step and update the overall chance (product of the step chances) and the description (currencies in order).</summary>
    public void Add(CraftStep step)
    {
        Steps.Add(step);
        OverallProbability = Steps.Aggregate(1.0, (p, s) => p * s.SuccessProbability);
        Description = string.Join(" → ", Steps.Select(s => s.CurrencyName).Distinct());
    }

    /// <summary>The next step of this strategy for an action: id, chance, type, restart link, consumed materials and the hit-chance note.</summary>
    /// <param name="uses">How often the action is applied in the step (e.g. catalysts).</param>
    /// <param name="brick">Chance to lose a wanted mod.</param>
    public CraftStep NewStep(CraftAction action, string description, double chance, Item result, int uses = 1, double brick = 0)
    {
        var step = new CraftStep
        {
            Id = $"step-{Steps.Count + 1}",
            CurrencyName = action.DisplayName,
            Description = description,
            SuccessProbability = chance,
            BrickProbability = brick > 0 ? brick : null,
            Type = brick > CraftStep.BrickThreshold ? CraftStepType.Brick : chance >= 1 ? CraftStepType.Checkpoint : CraftStepType.Normal,
            RestartFromStepId = chance < 1 ? (Steps.Count > 0 ? Steps[^1].Id : StartStepId) : null,
            Result = result,
            Materials = (action.Currency.Consumed ? action.Omens.Select(o => o.Name).Prepend(action.Currency.Name) : action.Omens.Select(o => o.Name))
                .ToDictionary(n => n, _ => uses),
        };
        step.Notes.Add(CraftStep.HitChanceNote(chance));
        return step;
    }
}

/// <summary>One step in a crafting strategy.</summary>
public sealed class CraftStep
{
    /// <summary>Steps that lose a wanted mod more often than this are marked as brick steps.</summary>
    public const double BrickThreshold = 0.3;

    public string Id { get; init; } = "";
    public string CurrencyName { get; init; } = "";
    public string Description { get; set; } = "";
    /// <summary>Why the step is done and what happens (crafting guides).</summary>
    public string? Explanation { get; set; }
    public double SuccessProbability { get; init; }
    public double? BrickProbability { get; init; }
    /// <summary>If this step fails, the step to restart from (<see cref="CraftingStrategy.StartStepId"/> for the start).</summary>
    public string? RestartFromStepId { get; init; }
    public string? RestartLabel { get; set; }
    public CraftStepType Type { get; init; } = CraftStepType.Normal;
    public List<string> Notes { get; } = new();
    /// <summary>The item after this step succeeded.</summary>
    public Item? Result { get; init; }
    /// <summary>Currencies, omens, augments or liquid emotions one attempt of this step consumes (name → count).</summary>
    public Dictionary<string, int> Materials { get; set; } = new();
    /// <summary>Expected attempts for this step (1/probability).</summary>
    public double ExpectedAttempts => SuccessProbability > 0 ? 1.0 / SuccessProbability : double.PositiveInfinity;

    /// <summary>"Hit chance: 25.00% (1 in 4)"; rounding noise (0.9999999) must not turn "1 in 1" into "1 in 2".</summary>
    public static string HitChanceNote(double p) => $"Hit chance: {p:P2} (1 in {(p > 0 ? Math.Ceiling(1.0 / p - 1e-9).ToString("N0") : "∞")})";
}

public enum CraftStepType
{
    Normal,
    Brick,
    Checkpoint,
}

/// <summary>An item consumed by a strategy: per successful run, and expected including retries of the uncertain steps.</summary>
public sealed record StrategyMaterial(string Name, int PerRun, double Expected);

public static class CraftingStrategyExtensions
{
    /// <summary>Consumed items over all steps; a step with chance p takes 1/p attempts on average.</summary>
    public static IEnumerable<StrategyMaterial> Materials(this IEnumerable<CraftStep> steps) =>
        steps.SelectMany(s => s.Materials.Select(m => (m.Key, m.Value, Expected: m.Value / Math.Max(s.SuccessProbability, 1e-9))))
            .GroupBy(x => x.Key)
            .Select(g => new StrategyMaterial(g.Key, g.Sum(x => x.Value), g.Sum(x => x.Expected)))
            .OrderByDescending(m => m.Expected);
}
