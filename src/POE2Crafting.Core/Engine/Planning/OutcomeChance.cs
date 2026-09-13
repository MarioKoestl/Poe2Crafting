using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Planning;

/// <summary>Chances of wanted outcomes in a <see cref="StepPreview"/> and a safe execution — shared by the planner and the crafting guides.</summary>
internal static class OutcomeChance
{
    /// <summary>Execution with a manual choice is deterministic apart from what the choice leaves open; a fixed seed keeps planning reproducible.</summary>
    private const int PlanningSeed = 0;

    /// <summary>Chance that the added mod is a wanted one, and the most likely wanted mod.</summary>
    public static (double Chance, ModCandidate? Best) Of(IEnumerable<ModCandidate> additions, Func<ModDef, bool> wanted)
    {
        var matching = additions.Where(a => wanted(a.Mod)).ToList();
        return (matching.Sum(a => a.Probability), matching.MaxBy(a => a.Probability));
    }

    /// <summary>Chance that the removed (or fractured) mod is a wanted one, and the most likely wanted candidate.</summary>
    public static (double Chance, RemovalCandidate? Best) Of(IEnumerable<RemovalCandidate> removals, Func<RemovalCandidate, bool> wanted)
    {
        var matching = removals.Where(wanted).ToList();
        return (matching.Sum(r => r.Probability), matching.MaxBy(r => r.Probability));
    }

    /// <summary>A random source for planning steps.</summary>
    public static Rng PlanningRng() => new(PlanningSeed);

    /// <summary>The item after the action with the chosen outcome, or null when the action or the choice isn't possible (or destroys the item).</summary>
    public static Item? TryExecute(CraftingEngine engine, Item item, CraftAction action, ManualChoice choice)
    {
        try
        {
            var result = engine.Execute(item, action, PlanningRng(), choice);
            return result.Applied && !result.Destroyed ? result.Item : null;
        }
        catch (InvalidChoiceException)
        {
            return null;
        }
    }
}
