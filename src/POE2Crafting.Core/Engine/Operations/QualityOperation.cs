using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>
/// Quality currencies (Whetstone, Etcher, Scrap, Bauble; the class group comes from CurrencyDef.QualityTarget) and
/// Vaal Infusers (op vaal_quality): add quality per rarity (config qualityPerUse). Infusers may exceed the maximum by up to
/// infuserOverCap and corrupt the item with a chance that grows with the starting quality above the maximum.
/// </summary>
internal sealed class QualityOperation : CraftOperation
{
    private const string Corrupted = "Item corrupted", NotCorrupted = "Not corrupted";
    private readonly bool _infuser;

    public QualityOperation(CraftingEngine engine, string op, bool infuser) : base(engine, op) => _infuser = infuser;

    private int Cap(Item item) => Engine.MaxQuality(item) + (_infuser ? Assumptions.InfuserOverCap : 0);

    private int Gain(Item item) => Assumptions.QualityPerUse.TryGetValue(item.Rarity, out var q) ? q : 1;

    private int NewQuality(Item item) => Math.Min(Cap(item), item.Quality + Gain(item));

    /// <summary>Infusers only: chance to corrupt, growing with the starting quality above the maximum.</summary>
    private Dictionary<string, double> CorruptionOutcomes(Item item)
    {
        double chance = Math.Clamp((item.Quality - Engine.MaxQuality(item)) * Assumptions.InfuserCorruptChancePerQuality, 0, 1);
        return CraftingEngine.NormaliseOutcomes(new Dictionary<string, double> { [NotCorrupted] = 1 - chance, [Corrupted] = chance });
    }

    public override Applicability? Check(CraftContext ctx)
    {
        if (ctx.Item.Quality >= Cap(ctx.Item)) return Applicability.No($"Quality is already at the maximum of {Cap(ctx.Item)}%.");
        ctx.Notes.Add($"Adds {Gain(ctx.Item)}% quality on a {ctx.Item.Rarity} item (config qualityPerUse: {Assumptions.QualityPerUseNote}).");
        return null;
    }

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex) => new()
    {
        SpecialOutcomes = _infuser ? CorruptionOutcomes(ctx.Item) : new(),
        Notes = { $"Quality {ctx.Item.Quality}% → {NewQuality(ctx.Item)}% (max {Cap(ctx.Item)}%)." },
    };

    public override void Execute(ExecuteContext ctx)
    {
        var item = ctx.Result;
        item.Quality = NewQuality(item);
        ctx.Details.Add($"Quality is now {item.Quality}%.");
        if (_infuser && CraftingEngine.PickOutcome(ctx, CorruptionOutcomes(ctx.Item)) == Corrupted) ctx.Corrupt();
    }
}
