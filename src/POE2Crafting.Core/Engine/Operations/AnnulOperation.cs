using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>Orb of Annulment: remove a random mod (Greater Annulment: two; Sinistral/Dextral Annulment, Omen of Light restrict the pool).</summary>
internal sealed class AnnulOperation : CraftOperation
{
    public AnnulOperation(CraftingEngine engine) : base(engine, CurrencyOps.Annul) { }

    private static int RemoveCount(CraftContext ctx) => ctx.OmenIs(OmenEffects.RemoveTwo) ? 2 : 1;

    public override Applicability? Check(CraftContext ctx) =>
        CraftingEngine.Removable(ctx.Item, ctx.Omens).Count < RemoveCount(ctx)
            ? Applicability.No("Not enough removable modifiers (fractured mods cannot be removed).")
            : null;

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex) =>
        new() { RemoveCount = RemoveCount(ctx), Removals = CraftingEngine.Removable(ctx.Item, ctx.Omens) };

    public override void Execute(ExecuteContext ctx)
    {
        var chosen = CraftingEngine.ChosenRemovals(ctx);
        for (int i = 0; i < RemoveCount(ctx); i++)
            Engine.RemoveOne(ctx, CraftingEngine.Removable(ctx.Result, ctx.Omens), i < chosen.Count ? chosen[i] : null);
        if (ctx.Result.AffixCount == 0 && ctx.Result.Rarity != Rarity.Normal) ctx.Details.Add("Item has no modifiers left (rarity unchanged).");
    }
}
