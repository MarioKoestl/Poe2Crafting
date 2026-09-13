using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>Currencies that only toggle an item state: Mirror of Kalandra, Scroll of Wisdom, Hinekora's Lock.</summary>
internal sealed class FlagOperation : CraftOperation
{
    private readonly Func<Item, string?> _refusal;
    private readonly Action<ExecuteContext> _apply;
    private readonly string _detail;
    private readonly bool _worksOnCorrupted;

    private FlagOperation(CraftingEngine engine, string op, Func<Item, string?> refusal, Action<ExecuteContext> apply, string detail, bool worksOnCorrupted = false)
        : base(engine, op)
    {
        (_refusal, _apply, _detail, _worksOnCorrupted) = (refusal, apply, detail, worksOnCorrupted);
    }

    public static FlagOperation Mirror(CraftingEngine engine) => new(engine, CurrencyOps.Mirror,
        item => item.Rarity == Rarity.Unique ? "Uniques cannot be mirrored." : null,
        ctx => ctx.Result.Mirrored = true,
        "Item is now Mirrored (this is the copy; both copies are locked).");

    public static FlagOperation Identify(CraftingEngine engine) => new(engine, CurrencyOps.Identify,
        item => item.Identified ? "Item is already identified." : null,
        ctx => ctx.Result.Identified = true,
        "Item identified.", worksOnCorrupted: true);

    /// <summary>Hinekora's Lock: the seed fixes the outcome of every action on this item state, so the preview shows exactly what applying will do.</summary>
    public static FlagOperation Lock(CraftingEngine engine) => new(engine, CurrencyOps.Lock,
        item => item.Foreseeing ? "Item already foresees its next result." : null,
        ctx => (ctx.Result.Foreseeing, ctx.Result.ForeseeSeed) = (true, ctx.Rng.Next(int.MaxValue)),
        "The item now foresees the result of the next currency: the preview shows the exact outcome before you apply it.");

    public override bool WorksOnCorrupted => _worksOnCorrupted;

    public override Applicability? Check(CraftContext ctx) => _refusal(ctx.Item) is { } reason ? Applicability.No(reason) : null;

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex) => new() { Notes = { _detail } };

    public override void Execute(ExecuteContext ctx)
    {
        _apply(ctx);
        ctx.Details.Add(_detail);
    }
}
