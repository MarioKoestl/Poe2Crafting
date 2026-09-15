using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine.Operations;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Planning;

/// <summary>Deterministic finishing work: quality, augment sockets, augments, instill.</summary>
internal sealed partial class FromItemPlanner
{
    /// <summary>Upper bound for repeated certain actions (quality currencies, catalysts) in one step.</summary>
    private const int MaxRepeatedUses = 40;

    private IEnumerable<Move> FinishingMoves(Item item, ItemGoal goal, TargetItemSpec target)
    {
        if (goal.QualityUnmet && QualityMove(item, target) is { } quality) yield return quality;
        if (goal.SocketsMissing > 0 && SingleUse(item, _data.CurrenciesOf(CurrencyOps.Socket), null) is { } socket)
            yield return socket with { Description = $"adds an augment socket ({socket.Next.Sockets})" };
        if (goal.AugmentsMissing.Count > 0 && item.Runes.Count < item.Sockets)
        {
            var wanted = goal.AugmentsMissing[0];
            if (_data.FindCurrency(wanted.Name) is { Augment: not null } currency
                && SingleUse(item, new[] { currency }, new ManualChoice { SpecialOutcome = AugmentOperation.FreeSocket }) is { } augment)
                yield return augment with { Description = $"socket {wanted.Name}: {wanted.EffectText}" };
        }
        if (goal.InstillMissing && InstillMove(item, target) is { } instill) yield return instill;
    }

    /// <summary>The first of the currencies that applies to the item, used once with a guaranteed result.</summary>
    private Move? SingleUse(Item item, IEnumerable<CurrencyDef> currencies, ManualChoice? choice)
    {
        foreach (var currency in currencies)
        {
            var action = CraftAction.Of(currency);
            if (_engine.Check(item, action).Ok && Simulate(item, action, choice ?? new ManualChoice()) is { } next)
                return new Move(action, 1, 0, next, currency.Name);
        }
        return null;
    }

    /// <summary>A certain action (quality currency, catalyst) applied repeatedly until <paramref name="done"/>; null when it can't get there.</summary>
    private (CraftAction Action, Item Next, int Uses)? Repeat(Item item, CraftAction action, Func<Item, bool> done)
    {
        var next = item;
        int uses = 0;
        while (!done(next) && uses < MaxRepeatedUses && _engine.Check(next, action).Ok && Simulate(next, action, new ManualChoice()) is { } after)
        {
            next = after;
            uses++;
        }
        return uses > 0 && done(next) ? (action, next, uses) : null;
    }

    /// <summary>
    /// Quality: repeated uses of the base's quality currency, or of the catalyst of the wanted type (jewellery), until the target is met.
    /// Cheapest on a Normal item (most quality per use); the step is certain.
    /// </summary>
    private Move? QualityMove(Item item, TargetItemSpec target)
    {
        var currencies = target.QualityType != null
            ? _data.CurrenciesOf(CurrencyOps.Catalyst).Where(c => c.Catalyst!.QualityType == target.QualityType)
            : _data.CurrenciesOf(CurrencyOps.Quality);
        foreach (var currency in currencies)
        {
            if (Repeat(item, CraftAction.Of(currency), i => ItemGoal.QualityMet(i, target)) is not { } fix) continue;
            return new Move(fix.Action, 1, 0, fix.Next, $"{fix.Uses}× → quality {fix.Next.QualityText}", Uses: fix.Uses,
                Note: item.Rarity != Rarity.Normal ? "Quality currencies add more per use on Normal items — cheaper before upgrading the rarity." : null);
        }
        return null;
    }

    private Move? InstillMove(Item item, TargetItemSpec target)
    {
        if (_data.FindInstill(target.InstillNotable!) is not { } recipe || !_engine.CheckInstill(item, recipe).Ok) return null;
        var result = _engine.Instill(item, recipe);
        var action = CraftAction.Of(new CurrencyDef { Name = recipe.ActionName });
        return new Move(action, 1, 0, result.Item, $"instill {recipe.Notable} with {string.Join(" → ", recipe.Emotions)}", Materials: _data.InstillMaterials(recipe),
            Note: string.Join("; ", recipe.Effects));
    }
}
