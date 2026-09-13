using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine;

/// <summary>
/// Entry point of the crafting planner: finds paths from an existing item (composed, imported or crafted in the simulator)
/// to a target item (see <see cref="FromItemPlanner"/>), and renders strategies as flowcharts.
/// </summary>
public sealed class CraftingPathFinder
{
    private const string StartId = "start";

    private readonly GameData _data;
    private readonly ModPool _pool;
    private readonly CraftingEngine _engine;

    public CraftingPathFinder(GameData data, ModPool pool)
    {
        _data = data;
        _pool = pool;
        _engine = new CraftingEngine(data, pool);
    }

    /// <summary>Find strategies from <paramref name="currentItem"/> to the target.</summary>
    public PlanResult FindPathsFromItem(Item currentItem, TargetItemSpec target)
    {
        if (currentItem.Base == null) return new PlanResult { Problems = { "The current item has no known base." } };
        PrepareTargets(target, currentItem);
        return new FromItemPlanner(_data, _engine).Plan(currentItem, target);
    }

    /// <summary>Resolve family/tier targets to mods of this base and recompute their display tiers (independent of the UI's values).</summary>
    private void PrepareTargets(TargetItemSpec target, Item item)
    {
        var allMods = _pool.AllForBase(item).ToList();
        foreach (var tm in target.TargetMods)
        {
            if (tm.ResolvedMod == null)
            {
                var candidates = allMods.Where(m => m.Family == tm.Family && m.AffixType == tm.AffixType);
                tm.ResolvedMod = tm.Tier != null ? candidates.FirstOrDefault(m => m.Tier == tm.Tier) : candidates.OrderBy(m => m.Level).FirstOrDefault();
            }
            if (tm.ResolvedMod != null) tm.DisplayTier = _pool.DisplayTier(tm.ResolvedMod, item);
        }
    }

    internal static string HitChanceNote(double p) => $"Hit chance: {p:P2} (1 in {Attempts(p)})";

    private static string Attempts(double p) => p > 0 ? Math.Ceiling(1.0 / p).ToString("N0") : "∞";

    // ------------------------------------------------------------------ Mermaid generation

    /// <summary>Generate a Mermaid flowchart from a strategy.</summary>
    public static string ToMermaid(CraftingStrategy strategy)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("flowchart TD");
        sb.AppendLine($"    {StartId}([\"{EscapeMermaid(strategy.StartLabel)}\"])");

        string prevId = StartId;
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
        sb.AppendLine($"    style {StartId} fill:#1a1a20,stroke:#af8f4e,color:#d4b462");
        sb.AppendLine("    style finish fill:#1a3a1a,stroke:#6aaa40,color:#6aaa40");
        foreach (var step in strategy.Steps)
        {
            var (fill, stroke) = step.Type switch
            {
                CraftStepType.Brick => ("#3a1a1a", "#cc4444"),
                CraftStepType.Checkpoint => ("#1a1a3a", "#6688cc"),
                _ => ("#1a1a20", "#af8f4e")
            };
            sb.AppendLine($"    style {step.Id} fill:{fill},stroke:{stroke},color:#c8c4b8");
        }
        return sb.ToString();
    }

    private static string EscapeMermaid(string text) => text.Replace("\"", "'").Replace("\n", "<br/>");
}
