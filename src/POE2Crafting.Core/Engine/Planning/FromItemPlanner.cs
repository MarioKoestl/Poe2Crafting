using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Planning;

/// <summary>
/// Finds crafting paths from an existing item to a target item. For each tool set it runs a beam search: in every state all applicable
/// moves (currency + omens + the outcome that makes progress) are evaluated, and the most promising states are kept. Because states are
/// ranked by path chance × an estimate of the remaining work, the search also takes moves that make no progress on their own but enable
/// better ones: blocker mods, raising the maximum quality with Essence of the Breach, catalyst quality that lifts values (+3 → +4 skills),
/// catalyst quality for Omen of Catalysing Exaltation, and Omen of Whittling to remove a temporary low-level mod again.
/// Probabilities come from <see cref="CraftingEngine.Preview"/> and the next state from <see cref="CraftingEngine.Execute"/> with a manual
/// choice, so the planner uses exactly the simulator's rules.
/// The move generators live in the partial files FromItemPlanner.*Moves.cs, the caches in FromItemPlanner.Cache.cs.
/// </summary>
internal sealed partial class FromItemPlanner
{
    [Flags]
    private enum Tools { Basic = 1, Omens = 2, Chaos = 4, Essences = 8, Desecration = 16, Fracture = 32 }

    private static readonly (string Name, Tools Tools)[] Policies =
    {
        ("Basic currency", Tools.Basic),
        ("With omens and Chaos", Tools.Basic | Tools.Omens | Tools.Chaos),
        ("With essences and alloys", Tools.Basic | Tools.Omens | Tools.Chaos | Tools.Essences),
        ("With desecration", Tools.Basic | Tools.Omens | Tools.Chaos | Tools.Essences | Tools.Desecration),
        ("With fracturing", Tools.Basic | Tools.Omens | Tools.Chaos | Tools.Essences | Tools.Desecration | Tools.Fracture),
    };

    private const int MaxSteps = 15;
    /// <summary>States kept per search depth.</summary>
    private const int BeamWidth = 8;
    /// <summary>Ranking only: assumed chance per unit of remaining work, so a state closer to the target ranks higher at the same path chance.</summary>
    private const double RemainingChancePerUnit = 0.3;
    /// <summary>Ranking only: every step costs a little, so detours without benefit fall behind.</summary>
    private const double StepPenalty = 0.97;
    /// <summary>Chances closer than this count as equal.</summary>
    private const double Tolerance = 1e-9;

    private readonly GameData _data;
    private readonly CraftingEngine _engine;

    public FromItemPlanner(CraftingEngine engine)
    {
        _engine = engine;
        _data = engine.Data;
    }

    /// <summary>
    /// One candidate step: the action, the outcome to choose, its chances and the item after success.
    /// <paramref name="Uses"/>: how often the action is applied in this step (quality currencies); <paramref name="Materials"/> overrides the consumed items.
    /// </summary>
    private sealed record Move(CraftAction Action, double Success, double Brick, Item Next, string Description, string? Note = null,
        int Uses = 1, Dictionary<string, int>? Materials = null);

    /// <summary>A search state: the item, its difference to the target, the moves that led here and their combined chance.</summary>
    private sealed record Node(Item Item, ItemGoal Goal, List<Move> Path, double Probability)
    {
        public double Score => Probability * Math.Pow(RemainingChancePerUnit, Goal.Distance) * Math.Pow(StepPenalty, Path.Count);

        /// <summary>Tie-break between equally likely paths: fewer items used (each step's currency and omens).</summary>
        public int Cost => Path.Sum(m => m.Uses + m.Action.Omens.Count);
    }

    public PlanResult Plan(Item start, TargetItemSpec target)
    {
        var result = new PlanResult();
        var startGoal = ItemGoal.Compare(start, target);
        if (startGoal.RarityImpossible)
        {
            result.Problems.Add($"The item is {start.Rarity}; rarity cannot be lowered to {target.TargetRarity}.");
            return result;
        }
        if (startGoal.Reached)
        {
            result.Strategies.Add(new CraftingStrategy { Id = "already-done", Name = "Target already reached!", Description = "The item already matches the target.", OverallProbability = 1.0 });
            return result;
        }

        var seen = new HashSet<string>();
        ItemGoal? closest = null;
        foreach (var (name, tools) in Policies)
        {
            var (best, nearest) = Search(start, startGoal, target, tools);
            if (best != null)
            {
                var strategy = ToStrategy(best, name);
                if (seen.Add(string.Join("|", strategy.Steps.Select(s => $"{s.CurrencyName}#{s.Description}")))) result.Strategies.Add(strategy);
            }
            else if (closest == null || nearest.Goal.Distance < closest.Distance) closest = nearest.Goal;
        }

        result.Strategies.Sort((a, b) => b.OverallProbability.CompareTo(a.OverallProbability));
        if (result.Strategies.Count == 0 && closest != null) result.Problems.AddRange(DescribeUnreached(closest));
        return result;
    }

    /// <summary>Beam search: the most likely complete path, or null, plus the state closest to the target (for problem reports).</summary>
    private (Node? Best, Node Closest) Search(Item start, ItemGoal startGoal, TargetItemSpec target, Tools tools)
    {
        var root = new Node(start.Clone(), startGoal, new List<Move>(), 1);
        var beam = new List<Node> { root };
        var bestChance = new Dictionary<string, double> { [SignatureOf(root.Item)] = 1 };
        Node? best = null;
        var closest = root;
        for (int depth = 0; depth < MaxSteps && beam.Count > 0; depth++)
        {
            var children = new List<Node>();
            foreach (var node in beam)
                foreach (var move in Moves(node.Item, node.Goal, target, tools))
                {
                    double chance = node.Probability * move.Success;
                    // a longer path can only get less likely: nothing below the best complete path is worth following
                    if (move.Success <= 0 || best != null && chance < best.Probability - Tolerance) continue;
                    // a move must never leave the item in a state the target cannot come back from (rarity can't be lowered:
                    // e.g. an essence turns a magic item rare, so it is useless for a magic target)
                    var goal = ItemGoal.Compare(move.Next, target);
                    if (goal.RarityImpossible) continue;
                    var signature = SignatureOf(move.Next);
                    // the same state again is only worth following when it is more likely (complete paths still compete on cost below)
                    if (!goal.Reached && bestChance.TryGetValue(signature, out var known) && known >= chance) continue;
                    bestChance[signature] = chance;

                    var child = new Node(move.Next, goal, node.Path.Append(move).ToList(), chance);
                    if (goal.Reached)
                    {
                        if (best == null || child.Probability > best.Probability + Tolerance || child.Cost < best.Cost) best = child;
                    }
                    else
                    {
                        children.Add(child);
                        if (goal.Distance < closest.Goal.Distance) closest = child;
                    }
                }
            beam = children.Where(c => best == null || c.Probability > best.Probability - Tolerance)
                .OrderByDescending(c => c.Score)
                .Take(BeamWidth)
                .ToList();
        }
        return (best, closest);
    }

    private static CraftingStrategy ToStrategy(Node node, string name)
    {
        var strategy = new CraftingStrategy { Id = "item-" + name.ToLowerInvariant().Replace(' ', '-'), Name = name };
        foreach (var move in node.Path)
        {
            var step = strategy.NewStep(move.Action, move.Description, move.Success, move.Next, move.Uses, move.Brick);
            if (move.Materials != null) step.Materials = move.Materials;
            step.RestartLabel = move.Brick > 0 ? "Lost a wanted mod → rebuild" : "Retry, or re-plan from the resulting item";
            if (move.Brick > 0) step.Notes.Add($"Risk of removing a wanted mod: {move.Brick:P1}");
            if (move.Note != null) step.Notes.Add(move.Note);
            if (move.Success < 1)
                step.Notes.Add("Hinekora's Lock: apply it first to see this exact result before committing — a miss then costs no currency.");
            strategy.Add(step);
        }
        return strategy;
    }

    private static IEnumerable<string> DescribeUnreached(ItemGoal goal)
    {
        foreach (var t in goal.Missing) yield return $"No path found that adds {t.DisplayName} ({t.ResolvedMod?.Text}).";
        foreach (var (_, mod) in goal.ToRemove) yield return $"No path found that removes {mod.DisplayText()}.";
        if (goal.ValuesUnmet.Count > 0)
            yield return "Value targets could not be reached (Divine Orb rerolls, or catalyst quality of the modifier's type — above the maximum quality only with a \"+% to Maximum Quality\" modifier such as Essence of the Breach).";
        if (goal.RarityGap > 0) yield return "The target rarity could not be reached without adding unwanted modifiers.";
        if (goal.QualityUnmet)
            yield return "The quality target could not be reached (above the maximum quality — add a \"+% to Maximum Quality\" modifier, e.g. Essence of the Breach — or no fitting quality currency/catalyst).";
        if (goal.SocketsMissing > 0) yield return $"{goal.SocketsMissing} more augment socket(s) cannot be added (socket limit of the base).";
        foreach (var a in goal.AugmentsMissing) yield return $"{a.Name} cannot be socketed into this item.";
        if (goal.InstillMissing) yield return "The notable cannot be instilled (amulets only, not corrupted).";
    }

    // ------------------------------------------------------------------ move selection

    private IEnumerable<Move> Moves(Item item, ItemGoal goal, TargetItemSpec target, Tools tools)
    {
        foreach (var m in FinishingMoves(item, goal, target)) yield return m;
        foreach (var m in ValueMoves(item, goal, tools)) yield return m;
        var additions = new Lazy<List<(CraftAction Action, StepPreview Preview)>>(() => AddOps(item, target).SelectMany(op => Actions(item, op, tools)).ToList());
        if (goal.AffixesDone)
        {
            // Essence of the Breach needs a mod to replace: add a blocker for it first
            if (tools.HasFlag(Tools.Essences) && NeedsMaximumQuality(item, goal))
                foreach (var m in BlockerMoves(item, goal, additions.Value)) yield return m;
            yield break;
        }

        if (tools.HasFlag(Tools.Fracture))
            foreach (var m in FractureMoves(item, goal)) yield return m;
        foreach (var m in AnnulMoves(item, goal, tools)) yield return m;
        foreach (var m in AddMoves(item, goal, additions.Value)) yield return m;
        foreach (var m in BlockerMoves(item, goal, additions.Value)) yield return m;
        if (tools.HasFlag(Tools.Omens))
            foreach (var m in CatalysingSetupMoves(item, goal)) yield return m;
        if (tools.HasFlag(Tools.Chaos))
            foreach (var m in ChaosMoves(item, goal, tools)) yield return m;
        if (tools.HasFlag(Tools.Essences))
            foreach (var m in EssenceMoves(item, goal, tools)) yield return m;
        if (tools.HasFlag(Tools.Desecration))
            foreach (var m in DesecrationMoves(item, goal, tools)) yield return m;
    }

    // ------------------------------------------------------------------ shared helpers of the move generators

    /// <summary>Chance to remove an unwanted mod, chance to lose a kept one, and the best unwanted pick (mods blocking a target first).</summary>
    private static (double Ok, double Brick, RemovalCandidate? Best) Removal(List<RemovalCandidate> removals, ItemGoal goal) =>
        (OutcomeChance.Of(removals, r => goal.IsToRemove(r.Index)).Chance,
         BrickChance(removals, goal),
         removals.Where(r => goal.IsToRemove(r.Index)).OrderByDescending(r => goal.Blocks(r.Mod)).ThenByDescending(r => r.Probability).FirstOrDefault());

    /// <summary>Chance that a removal hits a kept mod.</summary>
    private static double BrickChance(IEnumerable<RemovalCandidate> removals, ItemGoal goal) => OutcomeChance.Of(removals, r => goal.IsKept(r.Index)).Chance;

    /// <summary>Chance that the added mod is one of the missing targets, and the most likely such mod.</summary>
    private static (double Ok, ModCandidate? Best) Addition(IEnumerable<ModCandidate> additions, ItemGoal goal) =>
        OutcomeChance.Of(additions, mod => goal.Missing.Any(t => t.Matches(mod)));

    private static string TargetName(ItemGoal goal, ModDef mod) => goal.Missing.FirstOrDefault(t => t.Matches(mod))?.DisplayName ?? mod.DisplayName;

    /// <summary>Worst roll of a mod: the planner assumes minimum values, so value targets are only met by an explicit Divine step.</summary>
    private static List<double> WorstValues(ModDef mod) => mod.Ranges.Select(r => ModText.Bounds(r).Lo).ToList();

    /// <summary>The given notes as one text, or null when there are none.</summary>
    private static string? JoinNotes(params string?[] notes) => notes.Any(n => n != null) ? string.Join(" ", notes.Where(n => n != null)) : null;
}
