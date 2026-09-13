using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>Currencies that only toggle an item state: Mirror of Kalandra, Scroll of Wisdom, Hinekora's Lock.</summary>
public sealed class FlagOperation : CraftOperation
{
    private readonly Func<Item, string?> _refusal;
    private readonly Action<Item> _apply;
    private readonly string _detail;

    public FlagOperation(CraftingEngine engine, string op) : base(engine, op)
    {
        (_refusal, _apply, _detail) = op switch
        {
            "mirror" => ((Func<Item, string?>)(i => i.Rarity == Rarity.Unique ? "Uniques cannot be mirrored." : null),
                         (Action<Item>)(i => i.Mirrored = true), "Item is now Mirrored (this is the copy; both copies are locked)."),
            "identify" => (i => i.Identified ? "Item is already identified." : null, i => i.Identified = true, "Item identified."),
            "lock" => (i => i.Foreseeing ? "Item already foresees its next result." : null, i => i.Foreseeing = true,
                       "The item now foresees its next currency result (use Preview, then decide)."),
            _ => throw new ArgumentException($"Unknown flag operation {op}", nameof(op)),
        };
    }

    public override bool WorksOnCorrupted => Op == "identify";

    public override Applicability? Check(CraftContext ctx) => _refusal(ctx.Item) is { } reason ? Applicability.No(reason) : null;

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex) => new() { Notes = { _detail } };

    public override void Execute(ExecuteContext ctx)
    {
        _apply(ctx.Result);
        ctx.Details.Add(_detail);
    }
}
