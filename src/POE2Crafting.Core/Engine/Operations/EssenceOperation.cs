using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>
/// Essences and Alloys (synthetic currencies with Op "essence"). Lesser/Normal/Greater: Magic → Rare with a guaranteed mod.
/// Perfect/Corrupted/Alloy: remove a mod (Sinistral/Dextral Crystallisation restrict it) and add a guaranteed Crafted mod.
/// </summary>
public sealed class EssenceOperation : CraftOperation
{
    public EssenceOperation(CraftingEngine engine) : base(engine, "essence") { }

    // "next Perfect or Corrupted Essence"
    public override bool AcceptsOmen(CraftContext ctx, OmenDef omen) => base.AcceptsOmen(ctx, omen) && ctx.Currency.Essence?.Tier is "Perfect" or "Corrupted";

    private ModDef? GuaranteedMod(CraftContext ctx) =>
        ctx.Currency.Essence is { } e ? Engine.Data.EssenceModFor(e, ctx.Item.Base, ctx.Item.ItemClass) : null;

    /// <summary>A mod of the same family is replaced; if the guaranteed mod's affix type is full, only that type can be removed.</summary>
    private List<RemovalCandidate> Removals(Item item, ModDef mod, IReadOnlyList<OmenDef> omens)
    {
        var list = CraftingEngine.Removable(item, omens);
        if (item.HasFamily(mod.Family)) list = list.Where(r => r.Mod.Def?.Family == mod.Family).ToList();
        else if (IsFull(item, mod)) list = list.Where(r => r.Mod.Affix == mod.AffixType).ToList();
        return RemovalCandidate.Uniform(list);
    }

    private bool IsFull(Item item, ModDef mod) => Engine.FreeSlots(item, mod.AffixType, Rarity.Rare) == 0;

    public override Applicability? Check(CraftContext ctx)
    {
        var essence = ctx.Currency.Essence;
        if (essence == null) return Applicability.No("Essence data missing.");
        var mod = GuaranteedMod(ctx);
        if (mod == null) return Applicability.No($"{essence.Name} has no effect on {ctx.Item.ItemClass}.");
        var item = ctx.Item;
        var type = mod.AffixType.ToString().ToLower();
        if (mod.Level > item.ItemLevel)
            ctx.Notes.Add($"The guaranteed modifier has level {mod.Level} but the item level is {item.ItemLevel}; whether the game blocks this is UNVERIFIED (applied anyway).");

        if (!essence.RemovesRandomModifier)
        {
            if (item.HasFamily(mod.Family)) return Applicability.No($"The item already has a modifier of the same family as \"{mod.Text}\".");
            return IsFull(item, mod) ? Applicability.No($"No free {type} slot.") : null;
        }

        if (Engine.A.OnlyOneCraftedModPerItem && item.Affixes.Any(m => m.Kind == ModKind.Crafted))
            return Applicability.No("The item already has a crafted modifier (config: onlyOneCraftedModPerItem).");
        if (Removals(item, mod, ctx.Omens).Count == 0)
            return Applicability.No($"No removable modifier would make room for the {type} \"{mod.Text}\" (fractured mods or omen restriction).");
        if (item.HasFamily(mod.Family)) ctx.Notes.Add("Assumption: the existing modifier of the same family is the one that gets replaced (UNVERIFIED).");
        else if (IsFull(item, mod)) ctx.Notes.Add($"Assumption: {type}es are full, so only a {type} can be removed (UNVERIFIED).");
        return null;
    }

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex)
    {
        var essence = ctx.Currency.Essence!;
        var mod = GuaranteedMod(ctx)!;
        var removals = essence.RemovesRandomModifier ? Removals(ctx.Item, mod, ctx.Omens) : new List<RemovalCandidate>();
        return new StepPreview
        {
            RemoveCount = removals.Count > 0 ? 1 : 0,
            AddCount = 1,
            Removals = removals,
            Additions = { new ModCandidate { Mod = mod, Weight = 1, Probability = 1 } },
            PrefixProbability = mod.IsPrefix ? 1 : 0,
            SuffixProbability = mod.IsSuffix ? 1 : 0,
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
        var mod = GuaranteedMod(ctx)!;
        if (essence.RemovesRandomModifier)
            Engine.RemoveOne(ctx, Removals(ctx.Result, mod, ctx.Omens), CraftingEngine.ChosenRemovals(ctx).FirstOrDefault());
        CraftingEngine.MakeRare(ctx.Result, ctx.Rng);
        var values = ctx.Choice?.Values.Count > 0 ? ctx.Choice.Values[0] : null;
        var added = ctx.Result.AddMod(mod, essence.AddsCraftedMod ? ModKind.Crafted : ModKind.Explicit, values ?? CraftingEngine.RollValues(mod, ctx.Rng), essence.Name);
        ctx.Details.Add($"Added {Engine.Describe(added, ctx.Result)} (from {essence.Name})");
    }
}
