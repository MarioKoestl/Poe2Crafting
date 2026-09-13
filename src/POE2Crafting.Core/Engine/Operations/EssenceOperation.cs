using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>
/// Essences, Alloys and Liquid Emotions on jewels (synthetic currencies with Op essence). Lesser/Normal/Greater: Magic → Rare with a guaranteed mod.
/// Perfect/Corrupted/Alloy/Liquid: remove a mod (Sinistral/Dextral Crystallisation restrict it for Perfect/Corrupted) and add a guaranteed Crafted mod.
/// Some grant one of several mods with equal chance (Potent Liquid Contempt: a prefix or a suffix); only options that fit the item can roll.
/// A mod that exists as prefix and suffix of one family (Essence of the Abyss: Mark of the Abyssal Lord) takes the removed mod's slot instead.
/// </summary>
internal sealed class EssenceOperation : CraftOperation
{
    public EssenceOperation(CraftingEngine engine) : base(engine, CurrencyOps.Essence) { }

    // "next Perfect or Corrupted Essence"
    public override bool AcceptsOmen(CraftContext ctx, OmenDef omen) => base.AcceptsOmen(ctx, omen) && ctx.Currency.Essence?.AcceptsCrystallisation == true;

    private IReadOnlyList<ModDef> GuaranteedMods(CraftContext ctx) => Engine.Data.EssenceModsFor(ctx.Currency, ctx.Item);

    /// <summary>A mod of the same family is replaced; if the guaranteed mod's affix type is full, only that type can be removed.</summary>
    private List<RemovalCandidate> Removals(Item item, ModDef mod, IReadOnlyList<OmenDef> omens)
    {
        var list = CraftingEngine.Removable(item, omens);
        if (item.HasFamily(mod.Family)) return RemovalCandidate.Uniform(list.Where(r => r.Mod.Def?.Family == mod.Family));
        if (IsFull(item, mod)) return RemovalCandidate.Uniform(list.Where(r => r.Mod.Affix == mod.AffixType));
        return list;
    }

    private bool IsFull(Item item, ModDef mod) => Engine.FreeSlots(item, mod.AffixType, Rarity.Rare) == 0;

    /// <summary>One possible result: the added mod, the mods that can be removed for it, and its chance.</summary>
    private sealed record Outcome(ModDef Mod, List<RemovalCandidate> Removals, double Chance);

    /// <summary>The same family as prefix and suffix (Mark of the Abyssal Lord): the added mod takes the slot of the removed one.</summary>
    private static bool TakesRemovedSlot(IReadOnlyList<ModDef> mods) =>
        mods.Count == 2 && mods[0].Family != null && mods[0].Family == mods[1].Family && mods[0].AffixType != mods[1].AffixType;

    /// <summary>All results the essence can have on <paramref name="item"/> (chances sum to 1; empty when it cannot be used).</summary>
    private List<Outcome> Outcomes(CraftContext ctx, Item item)
    {
        var mods = GuaranteedMods(ctx);
        var essence = ctx.Currency.Essence!;
        if (!essence.RemovesRandomModifier)
        {
            var fitting = mods.Where(m => !item.HasFamily(m.Family) && !IsFull(item, m)).ToList();
            return fitting.Select(m => new Outcome(m, new List<RemovalCandidate>(), 1.0 / fitting.Count)).ToList();
        }
        if (TakesRemovedSlot(mods))
        {
            var removable = CraftingEngine.Removable(item, ctx.Omens);
            return mods.Select(m => (Mod: m, Removals: removable.Where(r => r.Mod.Affix == m.AffixType).ToList()))
                .Where(x => x.Removals.Count > 0)
                .Select(x => new Outcome(x.Mod, RemovalCandidate.Uniform(x.Removals), (double)x.Removals.Count / removable.Count))
                .ToList();
        }
        var options = mods.Select(m => (Mod: m, Removals: Removals(item, m, ctx.Omens))).Where(x => x.Removals.Count > 0).ToList();
        return options.Select(x => new Outcome(x.Mod, x.Removals, 1.0 / options.Count)).ToList();
    }

    public override Applicability? Check(CraftContext ctx)
    {
        var essence = ctx.Currency.Essence;
        if (essence == null) return Applicability.No("Essence data missing.");
        var mods = GuaranteedMods(ctx);
        if (mods.Count == 0) return Applicability.No($"{essence.Name} has no effect on {ctx.Item.ItemClass}.");
        var item = ctx.Item;
        if (essence.RemovesRandomModifier && Assumptions.OnlyOneCraftedModPerItem && item.Affixes.Any(m => m.Kind == ModKind.Crafted))
            return Applicability.No("The item already has a crafted modifier (config: onlyOneCraftedModPerItem).");

        var outcomes = Outcomes(ctx, item);
        if (outcomes.Count == 0)
        {
            var mod = mods[0];
            return Applicability.No(!essence.RemovesRandomModifier
                ? item.HasFamily(mod.Family) ? $"The item already has a modifier of the same family as \"{mod.Text}\"." : $"No free {mod.AffixType.Lower()} slot."
                : $"No removable modifier would make room for \"{mod.Text}\" (fractured mods or omen restriction).");
        }
        if (outcomes.Any(o => o.Mod.Level > item.ItemLevel))
            ctx.Notes.Add($"A guaranteed modifier has level {outcomes.Max(o => o.Mod.Level)} but the item level is {item.ItemLevel}; whether the game blocks this is UNVERIFIED (applied anyway).");
        if (TakesRemovedSlot(mods))
            ctx.Notes.Add($"{mods[0].Text} takes the slot of the removed modifier (prefix or suffix).");
        else if (mods.Count > 1)
            ctx.Notes.Add(outcomes.Count < mods.Count
                ? $"Assumption: of {mods.Count} possible modifiers only those that fit the item can roll ({outcomes.Count}); each is equally likely."
                : $"One of {mods.Count} modifiers is added, each equally likely.");
        if (essence.RemovesRandomModifier && !TakesRemovedSlot(mods))
        {
            if (outcomes.Any(o => item.HasFamily(o.Mod.Family))) ctx.Notes.Add("Assumption: the existing modifier of the same family is the one that gets replaced (UNVERIFIED).");
            else if (outcomes.Any(o => IsFull(item, o.Mod))) ctx.Notes.Add("Assumption: when the guaranteed modifier's type is full, only a modifier of that type can be removed (UNVERIFIED).");
        }
        return null;
    }

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex)
    {
        var essence = ctx.Currency.Essence!;
        var outcomes = Outcomes(ctx, ctx.Item);
        var removals = outcomes.SelectMany(o => o.Removals.Select(r => (Removal: r, Chance: r.Probability * o.Chance)))
            .GroupBy(x => x.Removal.Index)
            .Select(g => new RemovalCandidate { Index = g.Key, Mod = g.First().Removal.Mod, Probability = g.Sum(x => x.Chance) })
            .OrderBy(r => r.Index).ToList();
        return new StepPreview
        {
            RemoveCount = removals.Count > 0 ? 1 : 0,
            AddCount = 1,
            Removals = removals,
            Additions = outcomes.Select(o => new ModCandidate { Mod = o.Mod, Weight = 1, Probability = o.Chance }).ToList(),
            PrefixProbability = outcomes.Where(o => o.Mod.IsPrefix).Sum(o => o.Chance),
            SuffixProbability = outcomes.Where(o => o.Mod.IsSuffix).Sum(o => o.Chance),
            Notes =
            {
                essence.AddsCraftedMod
                    ? "The guaranteed modifier is added as a Crafted modifier (shown as \"Crafted\" in the item text)."
                    : "The item becomes Rare and gains the guaranteed modifier; its value is rolled inside the essence's range.",
            },
        };
    }

    public override void Execute(ExecuteContext ctx)
    {
        var essence = ctx.Currency.Essence!;
        var outcomes = Outcomes(ctx, ctx.Result);
        var chosenRemoval = CraftingEngine.ChosenRemovals(ctx).FirstOrDefault();
        var chosenModId = ctx.Choice?.AddModIds.FirstOrDefault();
        Func<Outcome, bool>? isChosen = chosenModId != null ? o => o.Mod.Id == chosenModId
            : chosenRemoval != null ? o => o.Removals.Any(r => ReferenceEquals(r.Mod, chosenRemoval))
            : null;
        var outcome = CraftingEngine.Pick(ctx.Rng, outcomes, o => o.Chance, isChosen, "The chosen outcome is not possible with this essence.");
        if (essence.RemovesRandomModifier) Engine.RemoveOne(ctx, outcome.Removals, chosenRemoval);
        CraftingEngine.MakeRare(ctx.Result, ctx.Rng);
        var added = ctx.Result.AddMod(outcome.Mod, essence.AddsCraftedMod ? ModKind.Crafted : ModKind.Explicit, CraftingEngine.ValuesFor(ctx.Choice, outcome.Mod, ctx.Rng), essence.Name);
        ctx.Details.Add($"Added {Engine.Describe(added, ctx.Result)} (from {essence.Name})");
    }
}
