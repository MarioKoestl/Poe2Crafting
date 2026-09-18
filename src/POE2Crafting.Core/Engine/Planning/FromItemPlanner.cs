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

    /// <summary>What a search optimises: the most likely path, or the lowest expected material cost (needs prices).</summary>
    private enum Objective { Chance, Cost }

    private readonly GameData _data;
    private readonly CraftingEngine _engine;
    /// <summary>Buy price in Chaos Orbs per consumed item name (null = unknown); null = no cost search.</summary>
    private readonly Func<string, double?>? _prices;

    public FromItemPlanner(CraftingEngine engine, Func<string, double?>? prices = null)
    {
        _engine = engine;
        _data = engine.Data;
        _prices = prices;
    }

    /// <summary>
    /// One candidate step: the action, the outcome to choose, its chances and the item after success.
    /// <paramref name="Uses"/>: how often the action is applied in this step (quality currencies); <paramref name="Materials"/> overrides the consumed items.
    /// </summary>
    private sealed record Move(CraftAction Action, double Success, double Brick, Item Next, string Description, string? Note = null,
        int Uses = 1, Dictionary<string, int>? Materials = null);

    /// <summary>
    /// A search state: the item, its difference to the target, the moves that led here and their combined chance.
    /// <paramref name="ExpectedCost"/>: Chaos Orbs the path costs including retries (each step's materials / its chance, the same rule as the strategy's material list);
    /// <paramref name="Unpriced"/>: expected number of consumed items without a price.
    /// </summary>
    private sealed record Node(Item Item, ItemGoal Goal, List<Move> Path, double Probability, double ExpectedCost = 0, double Unpriced = 0)
    {
        public double Score => Probability * Math.Pow(RemainingChancePerUnit, Goal.Distance) * Math.Pow(StepPenalty, Path.Count);

        /// <summary>Tie-break between equally likely paths: fewer items used (each step's currency and omens).</summary>
        public int Cost => Path.Sum(m => m.Uses + m.Action.Omens.Count);

        /// <summary>Cost with every unpriced item counted as <paramref name="unitCost"/> (the search's cost of one unit of progress).</summary>
        public double EffectiveCost(double unitCost) => ExpectedCost + Unpriced * unitCost;

        /// <summary>Cost search ranking: spent so far plus an estimate for the remaining work.</summary>
        public double CostScore(double unitCost) => EffectiveCost(unitCost) + Goal.Distance * unitCost;
    }

    public PlanResult Plan(Item start, TargetItemSpec target)
    {
        var result = new PlanResult();
        var startGoal = Compare(start, target);
        if (startGoal.RarityImpossible)
        {
            result.Problems.Add($"The item is {start.Rarity}; rarity cannot be lowered to {target.TargetRarity}.");
            return result;
        }
        if (startGoal.FractureImpossible)
        {
            result.Problems.Add("The target wants a different modifier fractured than the one already fractured on the item (only one fracture per item, it can't be undone).");
            return result;
        }
        // no currency raises the item level: a target tier above it can never roll
        var tooHigh = startGoal.Missing.Where(t => t.ResolvedMod?.Level > start.ItemLevel).ToList();
        if (tooHigh.Count > 0)
        {
            result.Problems.AddRange(tooHigh.Select(t =>
                $"{t.DisplayName} ({t.ResolvedMod!.Text}) needs item level {t.ResolvedMod.Level}, but the item has item level {start.ItemLevel} — it can never roll there. Choose a lower tier or start from an item with a higher item level."));
            return result;
        }
        if (startGoal.Reached)
        {
            result.Strategies.Add(new CraftingStrategy { Id = "already-done", Name = "Target already reached!", Description = "The item already matches the target.", OverallProbability = 1.0 });
            return result;
        }

        var seen = new HashSet<string>();
        ItemGoal? closest = null;
        // with prices every tool set also looks for its cheapest path (added when it differs from the most likely one)
        var objectives = _prices != null ? new[] { Objective.Chance, Objective.Cost } : new[] { Objective.Chance };
        foreach (var (name, tools) in Policies)
            foreach (var objective in objectives)
            {
                var (best, nearest) = Search(start, startGoal, target, tools, objective);
                if (best != null)
                {
                    var strategy = ToStrategy(best, objective == Objective.Cost ? $"{name} · cheapest" : name);
                    if (seen.Add(string.Join("|", strategy.Steps.Select(s => $"{s.CurrencyName}#{s.Description}")))) result.Strategies.Add(strategy);
                }
                else if (closest == null || nearest.Goal.Distance < closest.Distance) closest = nearest.Goal;
            }

        result.Strategies.Sort((a, b) => b.OverallProbability.CompareTo(a.OverallProbability));
        if (result.Strategies.Count == 0 && closest != null) result.Problems.AddRange(DescribeUnreached(closest));
        return result;
    }

    /// <summary>
    /// Beam search: the best complete path (most likely, or cheapest expected cost), or null, plus the state closest to the target (for problem reports).
    /// The chance search ranks by <see cref="Node.Score"/> and prunes everything less likely than the best complete path; the cost search ranks by
    /// cost so far + remaining distance × the typical cost of one unit of progress (<see cref="UnitCost"/>) and prunes everything that already costs more.
    /// </summary>
    private (Node? Best, Node Closest) Search(Item start, ItemGoal startGoal, TargetItemSpec target, Tools tools, Objective objective = Objective.Chance)
    {
        var root = new Node(start.Clone(), startGoal, new List<Move>(), 1);
        var beam = new List<Node> { root };
        double unitCost = objective == Objective.Cost ? UnitCost(root, target, tools) : 0;
        var bestValue = new Dictionary<string, double> { [SignatureOf(root.Item)] = Value(root) };
        Node? best = null;
        var closest = root;
        for (int depth = 0; depth < MaxSteps && beam.Count > 0; depth++)
        {
            var children = new List<Node>();
            foreach (var node in beam)
                foreach (var move in Moves(node.Item, node.Goal, target, tools))
                {
                    if (move.Success <= 0) continue;
                    var (cost, unpriced) = MoveCost(move);
                    var goal = Compare(move.Next, target);
                    var child = new Node(move.Next, goal, node.Path.Append(move).ToList(), node.Probability * move.Success,
                        node.ExpectedCost + cost / move.Success, node.Unpriced + unpriced / move.Success);
                    // a longer path only gets less likely and more expensive: nothing worse than the best complete path is worth following
                    if (best != null && WorseThanBest(child, best)) continue;
                    // a move must never leave the item in a state the target cannot come back from (rarity can't be lowered:
                    // e.g. an essence turns a magic item rare, so it is useless for a magic target; the wrong mod fractured)
                    if (goal.Impossible) continue;
                    var signature = SignatureOf(move.Next);
                    // the same state again is only worth following when it is better (complete paths still compete below)
                    if (!goal.Reached && bestValue.TryGetValue(signature, out var known) && known >= Value(child)) continue;
                    bestValue[signature] = Value(child);

                    if (goal.Reached)
                    {
                        if (best == null || IsBetterComplete(child, best)) best = child;
                    }
                    else
                    {
                        children.Add(child);
                        if (goal.Distance < closest.Goal.Distance) closest = child;
                    }
                }
            var survivors = children.Where(c => best == null || !WorseThanBest(c, best));
            beam = (objective == Objective.Cost
                    ? survivors.OrderBy(c => c.CostScore(unitCost)).ThenByDescending(c => c.Probability)
                    : survivors.OrderByDescending(c => c.Score))
                .Take(BeamWidth)
                .ToList();
        }
        return (best, closest);

        // how good a state is for the objective: higher is better
        double Value(Node n) => objective == Objective.Cost ? -n.EffectiveCost(unitCost) : n.Probability;

        bool WorseThanBest(Node n, Node current) => objective == Objective.Cost
            ? n.EffectiveCost(unitCost) > current.EffectiveCost(unitCost) + Tolerance
            : n.Probability < current.Probability - Tolerance;

        bool IsBetterComplete(Node child, Node current) => objective == Objective.Cost
            ? child.EffectiveCost(unitCost) < current.EffectiveCost(unitCost) - Tolerance
              || Math.Abs(child.EffectiveCost(unitCost) - current.EffectiveCost(unitCost)) <= Tolerance && child.Probability > current.Probability + Tolerance
            : child.Probability > current.Probability + Tolerance || child.Cost < current.Cost;
    }

    /// <summary>Chaos Orbs one attempt of the move costs (its materials × prices) and how many of its items have no price.</summary>
    private (double Chaos, int Unpriced) MoveCost(Move move)
    {
        if (_prices == null) return (0, 0);
        double chaos = 0;
        int unpriced = 0;
        foreach (var (name, count) in MaterialsOf(move))
        {
            if (_prices(name) is { } price) chaos += price * count;
            else unpriced += count;
        }
        return (chaos, unpriced);
    }

    /// <summary>The items one attempt of the move consumes (the strategy step shows the same materials).</summary>
    private static Dictionary<string, int> MaterialsOf(Move move) => move.Materials ?? move.Action.ConsumedItems.ToDictionary(n => n, _ => move.Uses);

    /// <summary>
    /// The cost search's estimate of one unit of remaining work (also used for an unpriced item): the median expected cost per unit of progress
    /// of the moves from the start that make progress; 1 Chaos Orb when none of them is priced.
    /// </summary>
    private double UnitCost(Node root, TargetItemSpec target, Tools tools)
    {
        var rates = Moves(root.Item, root.Goal, target, tools)
            .Where(m => m.Success > 0)
            .Select(m => (Move: m, Progress: root.Goal.Distance - Compare(m.Next, target).Distance, Cost: MoveCost(m).Chaos))
            .Where(x => x.Progress > 0 && x.Cost > 0)
            .Select(x => x.Cost / x.Move.Success / x.Progress)
            .Order()
            .ToList();
        return rates.Count > 0 ? Math.Max(rates[rates.Count / 2], 0.01) : 1;
    }

    private static CraftingStrategy ToStrategy(Node node, string name)
    {
        var strategy = new CraftingStrategy { Id = "item-" + name.ToLowerInvariant().Replace(' ', '-'), Name = name };
        foreach (var move in node.Path)
        {
            var step = strategy.NewStep(move.Action, move.Description, move.Success, move.Next, move.Uses, move.Brick);
            step.Materials = MaterialsOf(move);
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
        if (goal.FractureMissing is { } fracture) yield return $"No path found that fractures {fracture.Mod.DisplayText()} (the Fracturing Orb needs enough modifiers on the item).";
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
        foreach (var m in ValueMoves(item, goal, target, tools)) yield return m;
        var additions = new Lazy<List<(CraftAction Action, StepPreview Preview)>>(() => AddOps(item, target).SelectMany(op => Actions(item, op, tools)).ToList());
        if (goal.AffixesDone)
        {
            // Essence of the Breach needs a mod to replace: add a blocker for it first
            if (tools.HasFlag(Tools.Essences) && NeedsMaximumQuality(item, goal, target))
                foreach (var m in BlockerMoves(item, goal, additions.Value)) yield return m;
            yield break;
        }

        if (tools.HasFlag(Tools.Fracture))
        {
            foreach (var m in FractureMoves(item, goal, target)) yield return m;
            if (tools.HasFlag(Tools.Desecration))
                foreach (var m in FracturePlaceholderMoves(item, goal)) yield return m;
        }
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

    private ItemGoal Compare(Item item, TargetItemSpec target) => ItemGoal.Compare(item, target, _engine.Assumptions.DefaultMaxQuality);

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
