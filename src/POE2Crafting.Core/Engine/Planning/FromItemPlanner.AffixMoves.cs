using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Planning;

/// <summary>Moves with basic currency: annulment, additions (incl. blockers), Chaos Orbs (incl. Whittling) and fracturing.</summary>
internal sealed partial class FromItemPlanner
{
    private const string WhittlingNote = "Omen of Whittling removes the lowest-level modifier (an unrevealed desecrated modifier counts as level 1 — reveal it first).";

    private IEnumerable<Move> AnnulMoves(Item item, ItemGoal goal, Tools tools)
    {
        if (goal.ToRemove.Count == 0) yield break;
        foreach (var (action, preview) in Actions(item, CurrencyOps.Annul, tools))
        {
            var (ok, brick, best) = Removal(preview.Removals, goal);
            if (best == null || Simulate(item, action, new ManualChoice { RemoveIndices = { best.Index } }) is not { } next) continue;
            yield return new Move(action, ok, brick, next, $"removes {best.Mod.DisplayText()}");
        }
    }

    /// <summary>Currency ops that add one mod to the item without leaving the target rarity.</summary>
    private static string[] AddOps(Item item, TargetItemSpec target) => item.Rarity switch
    {
        Rarity.Normal => new[] { CurrencyOps.Transmute },
        Rarity.Magic => target.TargetRarity == Rarity.Rare ? new[] { CurrencyOps.Augment, CurrencyOps.Regal } : new[] { CurrencyOps.Augment },
        Rarity.Rare => new[] { CurrencyOps.Exalt },
        _ => Array.Empty<string>(),
    };

    private IEnumerable<Move> AddMoves(Item item, ItemGoal goal, List<(CraftAction Action, StepPreview Preview)> actions)
    {
        if (goal.Missing.Count == 0) yield break;
        var options = actions.Select(x => (x.Action, Hit: Addition(x.Preview.Additions, goal))).ToList();
        var chances = options.Select(o => (o.Action, o.Hit.Ok)).ToList();
        foreach (var (action, (ok, best)) in options)
        {
            if (best == null) continue;
            var choice = new ManualChoice { AddModIds = { best.Mod.Id }, Values = { WorstValues(best.Mod) } };
            if (Simulate(item, action, choice) is not { } next) continue;
            var catalysing = action.Omens.Has(OmenEffects.Catalysing)
                ? $"Catalysing Exaltation consumes the {item.Quality}% {item.QualityType} quality to favour {item.QualityTag} modifiers (strength is an assumption, config catalysingWeightBonusPerQuality)."
                : null;
            yield return new Move(action, ok, 0, next, $"adds {TargetName(goal, best.Mod)}",
                Note: JoinNotes(catalysing, VariantNote(action, chances.Where(c => c.Action.Currency.Op == action.Currency.Op))));
        }
    }

    /// <summary>
    /// The states when an unwanted mod is added, one per affix type: the chance that the random mod is a non-target of that type, and the item with
    /// the most likely such mod (worst values) standing in for it. Later steps may depend on the type, so the planner never assumes one.
    /// </summary>
    private IEnumerable<(AffixType Type, double Chance, Item Next)> WithJunk(Item item, CraftAction action, IEnumerable<ModCandidate> additions, ItemGoal goal, Func<ManualChoice> choice)
    {
        foreach (var group in additions.Where(a => !goal.Missing.Any(t => t.Matches(a.Mod))).GroupBy(a => a.Mod.AffixType))
        {
            var junk = group.MaxBy(a => a.Probability)!;
            var c = choice();
            c.AddModIds.Add(junk.Mod.Id);
            c.Values.Add(WorstValues(junk.Mod));
            if (Simulate(item, action, c) is { } next) yield return (group.Key, group.Sum(a => a.Probability), next);
        }
    }

    /// <summary>
    /// Blocker mods: an addition of a certain affix type (an omen restricts it, or the other type is full) whose result doesn't matter.
    /// It fills a slot so later additions/removals of that type are forced to the other side, or gives a later removal (Essence of the Breach,
    /// Perfect Essences, Chaos) a harmless mod to take. The most likely unwanted mod stands in for the random result.
    /// </summary>
    private IEnumerable<Move> BlockerMoves(Item item, ItemGoal goal, List<(CraftAction Action, StepPreview Preview)> actions)
    {
        foreach (var type in AffixTypeExtensions.Both)
        {
            var (action, preview) = actions.FirstOrDefault(x =>
                (type == AffixType.Prefix ? x.Preview.PrefixProbability : x.Preview.SuffixProbability) >= 1
                && x.Action.Omens.All(o => OmenEffects.RestrictedType(o) != null));
            if (action == null) continue;
            // a target hit instead of a blocker is no loss: the step is certain
            foreach (var (_, _, next) in WithJunk(item, action, preview.Additions, goal, () => new ManualChoice()))
                yield return new Move(action, 1, 0, next, $"adds any {type.Lower()} as a blocker (e.g. {next.Affixes.Last().DisplayText()})",
                    Note: "Blocker: the exact modifier doesn't matter — it only occupies the slot until a later step needs it or removes it.");
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
        if (goal.ToRemove.Count == 0) yield break;
        var actions = Actions(item, CurrencyOps.Chaos, tools).ToList();
        if (goal.Missing.Count == 0)
        {
            foreach (var m in ReplacingChaosMoves(item, goal, actions)) yield return m;
            yield break;
        }
        // success = the removal hits an unwanted mod AND the following addition hits a target
        var evaluated = actions.Select(x =>
            {
                var options = x.Preview.Removals.Where(r => goal.IsToRemove(r.Index))
                    .Select(r => (Removal: r, Add: Addition(Preview(item, x.Action, r.Index).Additions, goal)))
                    .Where(o => o.Add.Best != null)
                    .ToList();
                return (x.Action, x.Preview, Options: options, Success: options.Sum(o => o.Removal.Probability * o.Add.Ok));
            })
            .ToList();
        var chances = evaluated.Select(e => (e.Action, e.Success)).ToList();
        foreach (var (action, preview, options, success) in evaluated)
        {
            if (options.Count == 0) continue;
            var best = options.MaxBy(x => x.Removal.Probability * x.Add.Ok);
            var choice = new ManualChoice { RemoveIndices = { best.Removal.Index }, AddModIds = { best.Add.Best!.Mod.Id }, Values = { WorstValues(best.Add.Best.Mod) } };
            if (Simulate(item, action, choice) is not { } next) continue;
            yield return new Move(action, success, BrickChance(preview.Removals, goal), next,
                $"removes {best.Removal.Mod.DisplayText()}, adds {TargetName(goal, best.Add.Best.Mod)}",
                Note: JoinNotes(VariantNote(action, chances), action.Omens.Has(OmenEffects.RemoveLowestLevel) ? WhittlingNote : null));
        }
    }

    /// <summary>Nothing to add: a Chaos Orb that surely hits an unwanted mod (Omen of Whittling on a temporary low-level mod) swaps it for another one.</summary>
    private IEnumerable<Move> ReplacingChaosMoves(Item item, ItemGoal goal, List<(CraftAction Action, StepPreview Preview)> actions)
    {
        foreach (var (action, preview) in actions)
        {
            var (ok, brick, best) = Removal(preview.Removals, goal);
            if (best == null || ok < 1) continue;
            var additions = Preview(item, action, best.Index).Additions;
            foreach (var (type, chance, next) in WithJunk(item, action, additions, goal, () => new ManualChoice { RemoveIndices = { best.Index } }))
                yield return new Move(action, ok * chance, brick, next, $"removes {best.Mod.DisplayText()}, the new random modifier is a {type.Lower()}",
                    Note: JoinNotes(action.Omens.Has(OmenEffects.RemoveLowestLevel) ? WhittlingNote : null,
                        chance < 1 ? $"Counted as a hit only when the added modifier is a {type.Lower()} ({chance:P0}); otherwise re-plan." : null));
        }
    }

    private IEnumerable<Move> FractureMoves(Item item, ItemGoal goal)
    {
        if (goal.ToRemove.Count == 0 || goal.Kept.Count == 0) yield break; // an already fractured item is refused by the engine
        foreach (var (action, preview) in Actions(item, CurrencyOps.Fracture, Tools.Basic))
        {
            var (chance, best) = OutcomeChance.Of(preview.Removals, r => goal.IsKept(r.Index));
            if (best == null || Simulate(item, action, new ManualChoice { RemoveIndices = { best.Index } }) is not { } next) continue;
            yield return new Move(action, chance, 0, next, $"fractures a kept mod (e.g. {best.Mod.DisplayText()})",
                Note: "Protects the fractured mod from the following removals.");
        }
    }
}
