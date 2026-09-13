using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine;

/// <summary>
/// Finds crafting paths from an existing item to a target item. For each tool set it builds a path greedily: in every state
/// all applicable moves (currency + optional omen + the outcome that makes progress) are evaluated and the move with the best
/// success chance per unit of progress is taken. Probabilities come from <see cref="CraftingEngine.Preview"/> and the next state
/// from <see cref="CraftingEngine.Execute"/> with a manual choice, so the planner uses exactly the simulator's rules.
/// </summary>
public sealed class FromItemPlanner
{
    [Flags]
    private enum Tools { Basic = 1, Omens = 2, Chaos = 4, Essences = 8, Desecration = 16, Fracture = 32 }

    private static readonly (string Name, Tools Tools)[] Policies =
    {
        ("Basic currency", Tools.Basic),
        ("With omens and Chaos", Tools.Basic | Tools.Omens | Tools.Chaos),
        ("With essences and alloys", Tools.Basic | Tools.Omens | Tools.Chaos | Tools.Essences),
        ("With desecration", Tools.Basic | Tools.Omens | Tools.Chaos | Tools.Essences | Tools.Desecration),
        ("Fracture a kept mod first", Tools.Basic | Tools.Omens | Tools.Chaos | Tools.Essences | Tools.Desecration | Tools.Fracture),
    };

    /// <summary>Omen effects whose outcome the planner can reason about.</summary>
    private static readonly string[] PlannableOmenEffects =
    {
        OmenEffects.AddPrefixOnly, OmenEffects.AddSuffixOnly, OmenEffects.RemovePrefixOnly, OmenEffects.RemoveSuffixOnly,
        OmenEffects.RemoveLowestLevel, OmenEffects.RemoveDesecratedOnly,
        OmenEffects.GuaranteeUlaman, OmenEffects.GuaranteeAmanamu, OmenEffects.GuaranteeKurgal,
    };

    private const int MaxSteps = 15;
    private const string StartId = "start";

    private readonly GameData _data;
    private readonly CraftingEngine _engine;

    public FromItemPlanner(GameData data, CraftingEngine engine)
    {
        _data = data;
        _engine = engine;
    }

    /// <summary>One candidate step: the action, the outcome to choose, its chances and the item after success.</summary>
    private sealed record Move(CraftAction Action, double Success, double Brick, Item Next, string Description, bool IsSetup = false, string? Note = null);

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
            result.Strategies.Add(new CraftingStrategy
            {
                Id = "already-done", Name = "Target already reached!", Description = "The item already matches the target.",
                OverallProbability = 1.0,
            });
            return result;
        }

        var seen = new HashSet<string>();
        ItemGoal? closest = null;
        foreach (var (name, tools) in Policies)
        {
            var (strategy, remaining) = BuildGreedy(start, target, name, tools);
            if (remaining.Reached)
            {
                if (seen.Add(string.Join("|", strategy.Steps.Select(s => s.CurrencyName + s.Description)))) result.Strategies.Add(strategy);
            }
            else if (closest == null || remaining.Distance < closest.Distance) closest = remaining;
        }

        result.Strategies.Sort((a, b) => b.OverallProbability.CompareTo(a.OverallProbability));
        if (result.Strategies.Count == 0 && closest != null) result.Problems.AddRange(DescribeUnreached(closest));
        return result;
    }

    private (CraftingStrategy Strategy, ItemGoal Remaining) BuildGreedy(Item start, TargetItemSpec target, string name, Tools tools)
    {
        var strategy = new CraftingStrategy { Id = "item-" + name.ToLowerInvariant().Replace(' ', '-'), Name = name };
        var item = start.Clone();
        var goal = ItemGoal.Compare(item, target);
        while (!goal.Reached && strategy.Steps.Count < MaxSteps)
        {
            // a move must never leave the item in a state the target cannot come back from (rarity can't be lowered:
            // e.g. an essence turns a magic item rare, so it is useless for a magic target)
            var currentGoal = goal;
            var moves = Moves(item, goal, target, tools, firstStep: strategy.Steps.Count == 0)
                .Where(m => m.Success > 0)
                .Select(m => (Move: m, Goal: ItemGoal.Compare(m.Next, target)))
                .Where(x => !x.Goal.RarityImpossible)
                .Select(x => (x.Move, Progress: currentGoal.Distance - x.Goal.Distance))
                .ToList();
            var pick = moves.Select(x => x.Move).FirstOrDefault(m => m.IsSetup)
                       ?? moves.Where(x => x.Progress > 0)
                           .OrderByDescending(x => Math.Pow(x.Move.Success, 1.0 / x.Progress))
                           .ThenByDescending(x => x.Progress)
                           .Select(x => x.Move).FirstOrDefault();
            if (pick == null) break;
            strategy.Steps.Add(ToStep(pick, strategy));
            item = pick.Next;
            goal = ItemGoal.Compare(item, target);
        }

        strategy.OverallProbability = strategy.Steps.Aggregate(1.0, (p, s) => p * s.SuccessProbability);
        strategy.HasBrickRisk = strategy.Steps.Any(s => s.BrickProbability > 0);
        strategy.Description = string.Join(" → ", strategy.Steps.Select(s => s.CurrencyName).Distinct());
        return (strategy, goal);
    }

    private static CraftStep ToStep(Move move, CraftingStrategy strategy)
    {
        var step = new CraftStep
        {
            Id = $"step-{strategy.Steps.Count + 1}",
            CurrencyName = move.Action.DisplayName,
            Description = move.Description,
            SuccessProbability = move.Success,
            BrickProbability = move.Brick > 0 ? move.Brick : null,
            Type = move.Brick > 0.3 ? CraftStepType.Brick : move.Success >= 1 ? CraftStepType.Checkpoint : CraftStepType.Normal,
            RestartFromStepId = move.Success < 1 ? (strategy.Steps.Count > 0 ? strategy.Steps[^1].Id : StartId) : null,
            RestartLabel = move.Brick > 0 ? "Lost a wanted mod → rebuild" : "Retry, or re-plan from the resulting item",
            Notes = { CraftingPathFinder.HitChanceNote(move.Success) },
            Result = move.Next,
        };
        if (move.Brick > 0) step.Notes.Add($"Risk of removing a wanted mod: {move.Brick:P1}");
        if (move.Note != null) step.Notes.Add(move.Note);
        return step;
    }

    private static IEnumerable<string> DescribeUnreached(ItemGoal goal)
    {
        foreach (var t in goal.Missing) yield return $"No path found that adds {t.DisplayName} ({t.ResolvedMod?.Text}).";
        foreach (var (_, mod) in goal.ToRemove) yield return $"No path found that removes {mod.DisplayText()}.";
        if (goal.ValuesUnmet.Count > 0) yield return "Value targets could not be reached.";
        if (goal.RarityGap > 0) yield return "The target rarity could not be reached without adding unwanted modifiers.";
    }

    // ------------------------------------------------------------------ moves

    private IEnumerable<Move> Moves(Item item, ItemGoal goal, TargetItemSpec target, Tools tools, bool firstStep)
    {
        if (goal.Missing.Count == 0 && goal.ToRemove.Count == 0 && goal.RarityGap == 0)
        {
            if (DivineMove(item, goal) is { } divine) yield return divine;
            yield break;
        }
        if (tools.HasFlag(Tools.Fracture) && firstStep)
            foreach (var m in FractureMoves(item, goal)) yield return m;
        foreach (var m in AnnulMoves(item, goal, tools)) yield return m;
        foreach (var m in AddMoves(item, goal, target, tools)) yield return m;
        if (tools.HasFlag(Tools.Chaos))
            foreach (var m in ChaosMoves(item, goal, tools)) yield return m;
        if (tools.HasFlag(Tools.Essences))
            foreach (var m in EssenceMoves(item, goal, tools)) yield return m;
        if (tools.HasFlag(Tools.Desecration))
            foreach (var m in DesecrationMoves(item, goal, tools)) yield return m;
    }

    /// <summary>
    /// Every currency of the op, alone and (with the Omens tool) with each combination of up to two plannable omens that applies to it
    /// and doesn't contradict itself (e.g. Sinistral Necromancy + Omen of the Blackblooded).
    /// </summary>
    private IEnumerable<(CraftAction Action, StepPreview Preview)> Actions(Item item, string op, Tools tools, Func<CurrencyDef, bool>? filter = null)
    {
        var omens = tools.HasFlag(Tools.Omens)
            ? _data.Omens.Where(o => o.Crafting && PlannableOmenEffects.Contains(o.Effect) && CraftingEngine.OpOfOmenTarget(o.TargetCurrency) == op).ToList()
            : new List<OmenDef>();
        var omenSets = omens.Select((a, i) => omens.Skip(i + 1).Select(b => new[] { a, b }).Prepend(new[] { a }))
            .SelectMany(sets => sets)
            .Where(set => OmenEffects.Conflict(set) == null)
            .Prepend(Array.Empty<OmenDef>())
            .ToList();
        foreach (var currency in _data.AllCurrencies.Where(c => c.Op == op && (filter == null || filter(c))))
            foreach (var omenSet in omenSets)
            {
                var action = new CraftAction { Currency = currency, Omens = omenSet };
                var preview = _engine.Preview(item, action);
                if (preview.Applicability.Ok) yield return (action, preview);
            }
    }

    private Item? Simulate(Item item, CraftAction action, ManualChoice choice)
    {
        try
        {
            var result = _engine.Execute(item, action, new Rng(0), choice);
            return result.Applied && !result.Destroyed ? result.Item : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Chance to remove an unwanted mod, chance to lose a kept one, and the best unwanted pick (mods blocking a target first).</summary>
    private static (double Ok, double Brick, RemovalCandidate? Best) Removal(List<RemovalCandidate> removals, ItemGoal goal) =>
        (removals.Where(r => goal.IsToRemove(r.Index)).Sum(r => r.Probability),
         removals.Where(r => goal.IsKept(r.Index)).Sum(r => r.Probability),
         removals.Where(r => goal.IsToRemove(r.Index)).OrderByDescending(r => goal.Blocks(r.Mod)).ThenByDescending(r => r.Probability).FirstOrDefault());

    /// <summary>Chance that the added mod is one of the missing targets, and the most likely such mod.</summary>
    private static (double Ok, ModCandidate? Best) Addition(IEnumerable<ModCandidate> additions, ItemGoal goal)
    {
        var matching = additions.Where(a => goal.Missing.Any(t => t.Matches(a.Mod))).ToList();
        return (matching.Sum(a => a.Probability), matching.MaxBy(a => a.Probability));
    }

    private static string TargetName(ItemGoal goal, ModDef mod) => goal.Missing.FirstOrDefault(t => t.Matches(mod))?.DisplayName ?? mod.DisplayName;

    /// <summary>Worst roll of a mod: the planner assumes minimum values, so value targets are only met by an explicit Divine step.</summary>
    private static List<double> WorstValues(ModDef mod) => mod.Ranges.Select(r => Math.Min(r[0], r[1])).ToList();

    private IEnumerable<Move> AnnulMoves(Item item, ItemGoal goal, Tools tools)
    {
        if (goal.ToRemove.Count == 0) yield break;
        foreach (var (action, preview) in Actions(item, "annul", tools))
        {
            var (ok, brick, best) = Removal(preview.Removals, goal);
            if (best == null || Simulate(item, action, new ManualChoice { RemoveIndices = { best.Index } }) is not { } next) continue;
            yield return new Move(action, ok, brick, next, $"removes {best.Mod.DisplayText()}");
        }
    }

    private IEnumerable<Move> AddMoves(Item item, ItemGoal goal, TargetItemSpec target, Tools tools)
    {
        if (goal.Missing.Count == 0) yield break;
        var ops = item.Rarity switch
        {
            Rarity.Normal => new[] { "transmute" },
            Rarity.Magic => target.TargetRarity == Rarity.Rare ? new[] { "augment", "regal" } : new[] { "augment" },
            Rarity.Rare => new[] { "exalt" },
            _ => Array.Empty<string>(),
        };
        foreach (var op in ops)
        {
            var options = Actions(item, op, tools).Select(x => (x.Action, Hit: Addition(x.Preview.Additions, goal))).ToList();
            var chances = options.Select(o => (o.Action, o.Hit.Ok)).ToList();
            foreach (var (action, (ok, best)) in options)
            {
                if (best == null) continue;
                var choice = new ManualChoice { AddModIds = { best.Mod.Id }, Values = { WorstValues(best.Mod) } };
                if (Simulate(item, action, choice) is not { } next) continue;
                yield return new Move(action, ok, 0, next, $"adds {TargetName(goal, best.Mod)}", Note: VariantNote(action, chances));
            }
        }
    }

    /// <summary>
    /// The other variants (Normal/Greater/Perfect) of the chosen currency with the same omen and their chances, so a step shows why they were not taken
    /// (e.g. a Greater Orb of Augmentation cannot roll any tier of the target when its minimum modifier level is above the best tier's level).
    /// </summary>
    private static string? VariantNote(CraftAction chosen, IEnumerable<(CraftAction Action, double Success)> all)
    {
        var others = all.Where(o => o.Action.Omens.SequenceEqual(chosen.Omens) && o.Action.Currency != chosen.Currency).ToList();
        if (others.Count == 0) return null;
        return "Other variants: " + string.Join("; ", others.Select(o => $"{o.Action.Currency.Name} {o.Success:P2}" +
            (o.Success == 0 && o.Action.Currency.MinModLevel is { } min ? $" (minimum modifier level {min}: no matching tier can roll)" : "")));
    }

    private IEnumerable<Move> ChaosMoves(Item item, ItemGoal goal, Tools tools)
    {
        if (goal.ToRemove.Count == 0 || goal.Missing.Count == 0) yield break;
        // success = the removal hits an unwanted mod AND the following addition hits a target
        var evaluated = Actions(item, "chaos", tools).Select(x => (x.Action, x.Preview, Options: x.Preview.Removals.Where(r => goal.IsToRemove(r.Index))
                .Select(r => (Removal: r, Add: Addition(_engine.Preview(item, x.Action, r.Index).Additions, goal)))
                .Where(o => o.Add.Best != null)
                .ToList()))
            .ToList();
        var chances = evaluated.Select(e => (e.Action, Success: e.Options.Sum(o => o.Removal.Probability * o.Add.Ok))).ToList();
        foreach (var (action, preview, options) in evaluated)
        {
            if (options.Count == 0) continue;
            var best = options.MaxBy(x => x.Removal.Probability * x.Add.Ok);
            var choice = new ManualChoice { RemoveIndices = { best.Removal.Index }, AddModIds = { best.Add.Best!.Mod.Id }, Values = { WorstValues(best.Add.Best.Mod) } };
            if (Simulate(item, action, choice) is not { } next) continue;
            double brick = preview.Removals.Where(r => goal.IsKept(r.Index)).Sum(r => r.Probability);
            yield return new Move(action, chances.First(c => c.Action == action).Success, brick, next,
                $"removes {best.Removal.Mod.DisplayText()}, adds {TargetName(goal, best.Add.Best.Mod)}", Note: VariantNote(action, chances));
        }
    }

    private IEnumerable<Move> EssenceMoves(Item item, ItemGoal goal, Tools tools)
    {
        if (goal.Missing.Count == 0) yield break;
        bool Useful(CurrencyDef c) => c.Essence != null && _data.EssenceModFor(c.Essence, item.Base, item.ItemClass) is { } mod && goal.Missing.Any(t => t.Matches(mod));
        foreach (var (action, preview) in Actions(item, "essence", tools, Useful))
        {
            var mod = preview.Additions[0].Mod;
            var choice = new ManualChoice();
            double success = 1, brick = 0;
            if (preview.Removals.Count > 0)
            {
                (success, brick, var best) = Removal(preview.Removals, goal);
                if (best == null) continue;
                choice.RemoveIndices.Add(best.Index);
            }
            if (Simulate(item, action, choice) is not { } next) continue;
            yield return new Move(action, success, brick, next, $"guarantees {TargetName(goal, mod)}");
        }
    }

    private IEnumerable<Move> DesecrationMoves(Item item, ItemGoal goal, Tools tools)
    {
        var wanted = goal.Missing.Where(t => t.Category is ModCategories.Desecrated or ModCategories.Otherworldly).ToList();
        if (wanted.Count == 0) yield break;
        foreach (var (action, preview) in Actions(item, "desecrate", tools))
            foreach (var (label, typeChance) in preview.SpecialOutcomes)
            {
                var type = label.Contains(nameof(AffixType.Prefix)) ? AffixType.Prefix : AffixType.Suffix;
                if (!wanted.Any(t => t.AffixType == type)) continue;

                var choice = new ManualChoice { SpecialOutcome = label };
                double chance = typeChance, brick = 0;
                if (preview.Removals.Count > 0)
                {
                    var removal = preview.Removals.Where(r => goal.IsToRemove(r.Index) && r.Mod.Affix == type).MaxBy(r => r.Probability);
                    if (removal == null) continue;
                    chance = removal.Probability;
                    brick = preview.Removals.Where(r => goal.IsKept(r.Index)).Sum(r => r.Probability);
                    choice.RemoveIndices.Add(removal.Index);
                }
                if (Simulate(item, action, choice) is not { } desecrated || !desecrated.UnrevealedMods.Any()) continue;

                var index = desecrated.UnrevealedMods.Last().Index;
                var pool = _engine.RevealPool(desecrated, index);
                var matching = pool.Where(c => wanted.Any(t => t.AffixType == type && t.Matches(c.Mod))).ToList();
                if (matching.Count == 0) continue;
                var next = _engine.Reveal(desecrated, index, matching.MaxBy(c => c.Probability)!.Mod.Id, new Rng(0)).Item;
                double reveal = RevealChance(pool.Count, matching.Count, _data.Config.Assumptions.RevealOptionCount);
                var description = $"unrevealed {type.ToString().ToLower()} → reveal {TargetName(goal, matching[0].Mod)}";
                const string revealNote = "Reveal chance assumes equal weights for all desecrated modifiers (no poe2db estimates).";
                yield return new Move(action, chance * reveal, brick, next, description, Note: revealNote);
                yield return new Move(action, chance * (1 - Math.Pow(1 - reveal, 2)), brick, next, description + " (Omen of Abyssal Echoes at the reveal)", Note: revealNote);
            }
    }

    /// <summary>Chance that at least one of <paramref name="good"/> of <paramref name="pool"/> equally weighted mods is among <paramref name="options"/> distinct options.</summary>
    internal static double RevealChance(int pool, int good, int options)
    {
        double none = 1;
        for (int i = 0; i < Math.Min(options, pool); i++) none *= Math.Max(0, pool - good - i) / (double)(pool - i);
        return 1 - none;
    }

    private IEnumerable<Move> FractureMoves(Item item, ItemGoal goal)
    {
        if (goal.ToRemove.Count == 0 || goal.Kept.Count == 0 || goal.Kept.Any(k => k.Mod.Fractured)) yield break;
        foreach (var (action, preview) in Actions(item, "fracture", Tools.Basic))
        {
            var kept = preview.Removals.Where(r => goal.IsKept(r.Index)).ToList();
            var best = kept.MaxBy(r => r.Probability);
            if (best == null || Simulate(item, action, new ManualChoice { RemoveIndices = { best.Index } }) is not { } next) continue;
            yield return new Move(action, kept.Sum(r => r.Probability), 0, next, $"fractures a kept mod (e.g. {best.Mod.DisplayText()})", IsSetup: true,
                Note: "Protects the fractured mod from the following removals.");
        }
    }

    /// <summary>Divine Orb when only value targets are left: all values are rerolled at once, so every value target must be met together.</summary>
    private Move? DivineMove(Item item, ItemGoal goal)
    {
        var (action, _) = Actions(item, "divine", Tools.Basic).FirstOrDefault();
        if (action == null) return null;
        var rerolls = new Dictionary<int, List<double>>();
        double success = 1;
        foreach (var (index, mod, target) in goal.Kept)
        {
            var values = mod.Values.ToList();
            for (int i = 0; i < mod.Def!.Ranges.Count && i < values.Count; i++)
                if (target.MinValues?.ElementAtOrDefault(i) is { } min)
                {
                    success *= ChanceAtLeast(mod.Def.Ranges[i], min);
                    values[i] = Math.Max(values[i], min);
                }
            rerolls[index] = values;
        }
        return Simulate(item, action, new ManualChoice { Rerolls = rerolls }) is { } next
            ? new Move(action, success, 0, next, "rerolls all values until every value target is met", Note: "Divine rerolls every modifier, including the ones that already meet their target.")
            : null;
    }

    /// <summary>Chance that a uniform roll in the range is at least <paramref name="min"/> (integer ranges roll whole numbers).</summary>
    internal static double ChanceAtLeast(double[] range, double min)
    {
        double lo = Math.Min(range[0], range[1]), hi = Math.Max(range[0], range[1]);
        if (min <= lo) return 1;
        if (min > hi) return 0;
        return ModText.IsIntegerRange(lo, hi) ? (hi - Math.Ceiling(min) + 1) / (hi - lo + 1) : (hi - min) / (hi - lo);
    }
}
