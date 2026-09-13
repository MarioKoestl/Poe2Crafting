using System.Text;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Planning;

/// <summary>
/// Entry point of the crafting planner: finds paths from an existing item (composed, imported or crafted in the simulator)
/// to a target item (see <see cref="FromItemPlanner"/>), and renders strategies as flowcharts.
/// </summary>
public sealed class CraftingPathFinder
{
    private readonly CraftingEngine _engine;

    public CraftingPathFinder(CraftingEngine engine) => _engine = engine;

    /// <summary>Find strategies from <paramref name="currentItem"/> to the target. Resolves the target's mods and augment texts for the item's base first.</summary>
    public PlanResult FindPathsFromItem(Item currentItem, TargetItemSpec target)
    {
        if (currentItem.Base == null) return new PlanResult { Problems = { "The current item has no known base." } };
        PrepareTargets(target, currentItem);
        return new FromItemPlanner(_engine).Plan(currentItem, target);
    }

    /// <summary>Resolve family/tier targets to mods of this base (all browsable categories) and recompute their display tiers and augment texts.</summary>
    private void PrepareTargets(TargetItemSpec target, Item item)
    {
        var pool = _engine.Pool;
        var allMods = pool.AllBrowsableForBase(item).ToList();
        foreach (var tm in target.TargetMods)
        {
            if (tm.ResolvedMod == null)
            {
                var candidates = allMods.Where(m => m.Family == tm.Family && m.AffixType == tm.AffixType && m.Category == tm.Category).ToList();
                tm.ResolvedMod = tm.Tier != null ? candidates.FirstOrDefault(m => m.Tier == tm.Tier) : candidates.MinBy(m => m.Level);
            }
            if (tm.ResolvedMod != null) tm.DisplayTier = pool.DisplayTier(tm.ResolvedMod, item);
        }
        foreach (var augment in target.Augments)
            augment.EffectText = _engine.Data.FindCurrency(augment.Name)?.Augment is { } def ? _engine.Data.AugmentEffectText(def, item.Base, item.ItemClass) : null;
    }

    // ------------------------------------------------------------------ Mermaid generation

    /// <summary>Generate a Mermaid flowchart from a strategy.</summary>
    public static string ToMermaid(CraftingStrategy strategy)
    {
        const string start = CraftingStrategy.StartStepId;
        var sb = new StringBuilder();
        sb.AppendLine("flowchart TD");
        sb.AppendLine($"    {start}([\"{EscapeMermaid(strategy.StartLabel)}\"])");

        string prevId = start;
        foreach (var step in strategy.Steps)
        {
            string safeDesc = EscapeMermaid(step.Description);
            string probText = step.SuccessProbability < 1.0 ? $"<br/>{step.SuccessProbability:P1}" : "";
            sb.AppendLine(step.Type switch
            {
                CraftStepType.Brick => $"    {step.Id}{{\"{safeDesc}{probText}\"}}",
                CraftStepType.Checkpoint => $"    {step.Id}([\"{safeDesc}\"])",
                _ => $"    {step.Id}[\"{safeDesc}{probText}\"]",
            });
            sb.AppendLine($"    {prevId} -->|\"{EscapeMermaid(step.CurrencyName)}\"| {step.Id}");
            if (step.RestartFromStepId != null && step.SuccessProbability < 1.0)
                sb.AppendLine($"    {step.Id} -.->|\"{1.0 - step.SuccessProbability:P0} {EscapeMermaid(step.RestartLabel ?? "Retry")}\"| {step.RestartFromStepId}");
            prevId = step.Id;
        }

        sb.AppendLine($"    {prevId} --> finish([\"Target Item\"])");
        sb.AppendLine($"    style {start} fill:#1a1a20,stroke:#af8f4e,color:#d4b462");
        sb.AppendLine("    style finish fill:#1a3a1a,stroke:#6aaa40,color:#6aaa40");
        foreach (var step in strategy.Steps)
        {
            var (fill, stroke) = step.Type switch
            {
                CraftStepType.Brick => ("#3a1a1a", "#cc4444"),
                CraftStepType.Checkpoint => ("#1a1a3a", "#6688cc"),
                _ => ("#1a1a20", "#af8f4e"),
            };
            sb.AppendLine($"    style {step.Id} fill:{fill},stroke:{stroke},color:#c8c4b8");
        }
        return sb.ToString();
    }

    private static string EscapeMermaid(string text) => text.Replace("\"", "'").Replace("\n", "<br/>");
}
