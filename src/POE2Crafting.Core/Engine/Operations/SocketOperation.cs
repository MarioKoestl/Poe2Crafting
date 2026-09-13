using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>Artificer's Orb: adds an augment socket up to the base's socket limit (Vaal Orb sockets can exceed it).</summary>
internal sealed class SocketOperation : CraftOperation
{
    public SocketOperation(CraftingEngine engine) : base(engine, CurrencyOps.Socket) { }

    // "Adds an Augment Socket to a Martial Weapon, wand, staff or Armour"
    public override string? DefaultClassTarget => ClassTargets.Socketable;

    private static int Limit(Item item) => item.Base?.SocketLimit ?? 0;

    public override Applicability? Check(CraftContext ctx) =>
        ctx.Item.Sockets >= Limit(ctx.Item) ? Applicability.No($"The item already has the maximum of {Limit(ctx.Item)} augment sockets.") : null;

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex) =>
        new() { Notes = { $"Sockets {ctx.Item.Sockets} → {ctx.Item.Sockets + 1} (limit {Limit(ctx.Item)})." } };

    public override void Execute(ExecuteContext ctx)
    {
        ctx.Result.Sockets++;
        ctx.Details.Add($"Added an augment socket ({ctx.Result.Sockets} sockets).");
    }
}
