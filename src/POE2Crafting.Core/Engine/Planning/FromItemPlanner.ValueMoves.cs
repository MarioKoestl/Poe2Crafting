using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine.Operations;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Planning;

/// <summary>Moves for value targets (Divine Orb, catalyst quality, Essence of the Breach) and catalyst quality for Omen of Catalysing Exaltation.</summary>
internal sealed partial class FromItemPlanner
{
    /// <summary>Highest catalyst quality considered when looking for the quality a value target needs.</summary>
    private const int HighestConsideredQuality = 100;

    private readonly Dictionary<string, int?> _neededQuality = new();

    private IEnumerable<Move> ValueMoves(Item item, ItemGoal goal, Tools tools)
    {
        if (goal.ValuesUnmet.Count == 0) yield break;
        if (goal.AffixesDone && DivineMove(item, goal) is { } divine) yield return divine;
        foreach (var m in CatalystValueMoves(item, goal)) yield return m;
        if (tools.HasFlag(Tools.Essences) && NeedsMaximumQuality(item, goal))
            foreach (var m in MaximumQualityMoves(item, goal, tools)) yield return m;
    }

    /// <summary>Catalyst currencies whose quality enhances some of the mods (e.g. Reaver Catalyst for "+# to Level of all Melee Skills").</summary>
    private IEnumerable<CurrencyDef> CatalystsFor(IEnumerable<ModDef?> mods)
    {
        var list = mods.ToList();
        return _data.CurrenciesOf(CurrencyOps.Catalyst).Where(c => list.Any(c.Catalyst!.Enhances));
    }

    /// <summary>The lowest catalyst quality at which one catalyst type meets all value targets it enhances, or null when none helps even at 100% (cached per item state).</summary>
    private int? NeededCatalystQuality(Item item, ItemGoal goal)
    {
        var key = SignatureOf(item);
        if (_neededQuality.TryGetValue(key, out var cached)) return cached;

        var catalysts = CatalystsFor(goal.ValuesUnmet.Select(u => u.Mod.Def)).ToList();
        int? needed = null;
        for (int q = Math.Max(1, item.Quality); needed == null && catalysts.Count > 0 && q <= HighestConsideredQuality; q++)
            foreach (var catalyst in catalysts)
            {
                var probe = item.WithQuality(q, catalyst.Catalyst!.QualityType);
                var enhanced = goal.ValuesUnmet.Where(u => probe.QualityEnhances(u.Mod)).ToList();
                if (enhanced.Count > 0 && enhanced.All(u => u.Target.ValuesSatisfiedBy(probe, u.Mod)))
                {
                    needed = q;
                    break;
                }
            }
        return _neededQuality[key] = needed;
    }

    /// <summary>Catalyst quality would meet the value targets, but only above the item's maximum quality (and no "+% to Maximum Quality" mod yet).</summary>
    private bool NeedsMaximumQuality(Item item, ItemGoal goal) =>
        goal.ValuesUnmet.Count > 0 && !item.HasFamily(ModFamilies.MaximumQuality)
        && NeededCatalystQuality(item, goal) is { } needed && needed > _engine.MaxQuality(item);

    /// <summary>Catalysts of the type that enhances unmet value mods, repeated until their values reach the target (within the maximum quality).</summary>
    private IEnumerable<Move> CatalystValueMoves(Item item, ItemGoal goal)
    {
        foreach (var currency in CatalystsFor(goal.ValuesUnmet.Select(u => u.Mod.Def)))
        {
            var enhanced = goal.ValuesUnmet.Where(u => currency.Catalyst!.Enhances(u.Mod.Def)).ToList();
            bool Met(Item i) => enhanced.All(u => u.Target.ValuesSatisfiedBy(i, i.Mods[u.Index]));
            if (Repeat(item, CraftAction.Of(currency), Met) is not { } fix) continue;
            yield return new Move(fix.Action, 1, 0, fix.Next, $"{fix.Uses}× → {fix.Next.QualityText} quality: {string.Join(", ", enhanced.Select(u => fix.Next.EffectiveText(fix.Next.Mods[u.Index])))}",
                Uses: fix.Uses, Note: "Catalyst quality increases the values of matching modifiers; whole numbers round down (e.g. +3 skills need 34% for +4).");
        }
    }

    /// <summary>
    /// The value targets need more catalyst quality than the maximum: an essence adding "+% to Maximum Quality" (Essence of the Breach) on an
    /// unwanted mod's place. The added mod is temporary — the quality stays when it is whittled off later (level 1 = lowest level).
    /// </summary>
    private IEnumerable<Move> MaximumQualityMoves(Item item, ItemGoal goal, Tools tools)
    {
        if (goal.ToRemove.Count == 0 || NeededCatalystQuality(item, goal) is not { } needed) yield break;

        bool RaisesMaximum(CurrencyDef c) => _data.EssenceModsFor(c, item).Any(m => m.Family == ModFamilies.MaximumQuality);
        foreach (var (action, preview) in Actions(item, CurrencyOps.Essence, tools, RaisesMaximum))
        {
            if (preview.Additions.FirstOrDefault(a => a.Mod.Family == ModFamilies.MaximumQuality) is not { } added) continue;
            var choice = new ManualChoice { AddModIds = { added.Mod.Id } };
            if (EssenceRemoval(item, action, preview, goal, choice) is not { } removal || _engine.MaxQuality(removal.Next) < needed) continue;
            yield return new Move(action, added.Probability * removal.Ok, removal.Brick, removal.Next,
                $"{added.Mod.Text} (temporary) so catalyst quality can reach {needed}%",
                Note: $"The value targets need {needed}% catalyst quality. The \"{added.Mod.Text}\" modifier has level {added.Mod.Level} — Omen of Whittling + Chaos Orb removes it again once the quality is applied (assumption: the quality stays).");
        }
    }

    /// <summary>
    /// Omen of Catalysing Exaltation preparation: catalyst quality of a type that a missing target has (up to the maximum),
    /// so the following Exalted Orb with the omen favours it.
    /// </summary>
    private IEnumerable<Move> CatalysingSetupMoves(Item item, ItemGoal goal)
    {
        if (item.Rarity != Rarity.Rare || !_data.Omens.Any(o => o.Effect == OmenEffects.Catalysing)) yield break;
        foreach (var currency in CatalystsFor(goal.Missing.Select(t => t.ResolvedMod)))
        {
            if (item.QualityType == currency.Catalyst!.QualityType && item.Quality > 0) continue;
            var action = CraftAction.Of(currency);
            if (Repeat(item, action, i => !_engine.Check(i, action).Ok) is not { } setup) continue;
            yield return new Move(action, 1, 0, setup.Next, $"{setup.Uses}× → {setup.Next.QualityText} quality for Omen of Catalysing Exaltation", Uses: setup.Uses);
        }
    }

    /// <summary>Divine Orb when only value targets are left: all values are rerolled at once, so every value target must be met together.</summary>
    private Move? DivineMove(Item item, ItemGoal goal)
    {
        var (action, _) = Actions(item, CurrencyOps.Divine, Tools.Basic).FirstOrDefault();
        if (action == null) return null;
        var rerolls = new Dictionary<int, List<double>>();
        double success = 1;
        foreach (var (index, mod, target) in goal.Kept)
        {
            if (!DivineOperation.Rerollable(mod))
            {
                if (goal.ValuesUnmet.Any(u => u.Index == index)) return null; // e.g. a fractured mod keeps its too-low values
                continue;
            }
            var values = mod.Values.ToList();
            for (int i = 0; i < mod.Def!.Ranges.Count && i < values.Count; i++)
                if (target.MinValues?.ElementAtOrDefault(i) is { } min)
                {
                    if (LowestRollReaching(item, mod, i, min) is not { } roll) return null;
                    success *= ModText.ChanceAtLeast(mod.Def.Ranges[i], roll);
                    values[i] = Math.Max(values[i], roll);
                }
            rerolls[index] = values;
        }
        // fixed numbers (no range) are not rerolled: those value targets need catalyst quality instead
        return Simulate(item, action, new ManualChoice { Rerolls = rerolls }) is { } next && goal.ValuesUnmet.All(u => u.Target.ValuesSatisfiedBy(next, next.Mods[u.Index]))
            ? new Move(action, success, 0, next, "rerolls all values until every value target is met", Note: "Divine rerolls every non-fractured modifier, including the ones that already meet their target.")
            : null;
    }

    /// <summary>The lowest roll of the mod's i-th range whose value on the item (with catalyst quality) reaches <paramref name="min"/>, or null.</summary>
    private static double? LowestRollReaching(Item item, ItemMod mod, int i, double min)
    {
        var probe = mod.Clone();
        foreach (var roll in ModText.PossibleRolls(mod.Def!.Ranges[i]))
        {
            probe.Values[i] = roll;
            if (item.EffectiveValues(probe)[i] >= min) return roll;
        }
        return null;
    }
}
