using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine.Operations;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Planning;

/// <summary>Moves with essences/alloys and desecration.</summary>
internal sealed partial class FromItemPlanner
{
    private IEnumerable<Move> EssenceMoves(Item item, ItemGoal goal, Tools tools)
    {
        if (goal.Missing.Count == 0) yield break;
        bool Useful(CurrencyDef c) => _data.EssenceModsFor(c, item).Any(mod => goal.Missing.Any(t => t.Matches(mod)));
        foreach (var (action, preview) in Actions(item, CurrencyOps.Essence, tools, Useful))
        {
            // several possible guaranteed mods (e.g. Liquid Contempt) are equally likely: success = chance of a wanted one
            var (addOk, wanted) = Addition(preview.Additions, goal);
            if (wanted == null) continue;
            var choice = new ManualChoice { AddModIds = { wanted.Mod.Id } };
            if (EssenceRemoval(item, action, preview, goal, choice) is not { } removal) continue;
            yield return new Move(action, addOk * removal.Ok, removal.Brick, removal.Next, $"{(addOk >= 1 ? "guarantees" : "can add")} {TargetName(goal, wanted.Mod)}");
        }
    }

    /// <summary>The removal part of an essence (Perfect/Corrupted/Alloy): the best unwanted mod to lose, its chance, and the resulting item.</summary>
    private (double Ok, double Brick, Item Next)? EssenceRemoval(Item item, CraftAction action, StepPreview preview, ItemGoal goal, ManualChoice choice)
    {
        double ok = 1, brick = 0;
        if (preview.Removals.Count > 0)
        {
            (ok, brick, var best) = Removal(preview.Removals, goal);
            if (best == null) return null;
            choice.RemoveIndices.Add(best.Index);
        }
        return Simulate(item, action, choice) is { } next ? (ok, brick, next) : null;
    }

    private IEnumerable<Move> DesecrationMoves(Item item, ItemGoal goal, Tools tools)
    {
        var wanted = goal.Missing.Where(t => t.Category is ModCategories.Desecrated or ModCategories.Otherworldly).ToList();
        if (wanted.Count == 0) yield break;
        foreach (var (action, preview) in Actions(item, CurrencyOps.Desecrate, tools))
            foreach (var type in AffixTypeExtensions.Both)
            {
                var label = DesecrateOperation.OutcomeName(type);
                if (!preview.SpecialOutcomes.TryGetValue(label, out var typeChance) || !wanted.Any(t => t.AffixType == type)) continue;

                var choice = new ManualChoice { SpecialOutcome = label };
                double chance = typeChance, brick = 0;
                if (preview.Removals.Count > 0)
                {
                    var removal = preview.Removals.Where(r => goal.IsToRemove(r.Index) && r.Mod.Affix == type).MaxBy(r => r.Probability);
                    if (removal == null) continue;
                    chance = removal.Probability;
                    brick = BrickChance(preview.Removals, goal);
                    choice.RemoveIndices.Add(removal.Index);
                }
                if (Simulate(item, action, choice) is not { } desecrated || !desecrated.UnrevealedMods.Any()) continue;
                if (wanted.Any(t => t.Unrevealed && t.AffixType == type))
                {
                    yield return new Move(action, chance, brick, desecrated, $"adds an unrevealed desecrated {type.Lower()} (stays unrevealed)");
                    continue;
                }

                var index = desecrated.UnrevealedMods.Last().Index;
                bool Wanted(ModDef m) => wanted.Any(t => t.AffixType == type && t.Matches(m));
                var matching = _engine.RevealPool(desecrated, index).Where(c => Wanted(c.Mod)).ToList();
                if (matching.Count == 0) continue;
                var next = _engine.Reveal(desecrated, index, matching.MaxBy(c => c.Probability)!.Mod.Id, OutcomeChance.PlanningRng()).Item;
                double reveal = _engine.RevealChance(desecrated, index, Wanted);
                var description = $"unrevealed {type.Lower()} → reveal {TargetName(goal, matching[0].Mod)}";
                const string revealNote = "Reveal chance: the wanted modifier among the offered options (1 exclusive Lich modifier + regular ones — assumption, config revealExclusiveOptionCount; desecrated weights are all equal).";
                yield return new Move(action, chance * reveal, brick, next, description, Note: revealNote);
                yield return new Move(action, chance * (1 - Math.Pow(1 - reveal, 2)), brick, next, description + " (Omen of Abyssal Echoes at the reveal)", Note: revealNote);
            }
    }
}
