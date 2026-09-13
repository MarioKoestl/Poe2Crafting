namespace POE2Crafting.Core.Engine.Operations;

/// <summary>Artificer's Orb: adds an augment socket up to the base's socket limit (Vaal Orb sockets can exceed it).</summary>
public sealed class SocketOperation : CraftOperation
{
    public SocketOperation(CraftingEngine engine) : base(engine, "socket") { }

    // "Adds an Augment Socket to a Martial Weapon, wand, staff or Armour"
    public override string? DefaultClassTarget => "socketable";

    public override Applicability? Check(CraftContext ctx)
    {
        int limit = ctx.Item.Base?.SocketLimit ?? 0;
        return ctx.Item.Sockets >= limit ? Applicability.No($"The item already has the maximum of {limit} augment sockets.") : null;
    }

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex) =>
        new() { Notes = { $"Sockets {ctx.Item.Sockets} → {ctx.Item.Sockets + 1} (limit {ctx.Item.Base?.SocketLimit ?? 0})." } };

    public override void Execute(ExecuteContext ctx)
    {
        ctx.Result.Sockets++;
        ctx.Details.Add($"Added an augment socket ({ctx.Result.Sockets} sockets).");
    }
}
