using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>
/// Quality currencies (Whetstone, Etcher, Scrap, Bauble; the class group comes from CurrencyDef.QualityTarget) and
/// Vaal Infusers (op "vaal_quality"): add quality per rarity (config qualityPerUse). Infusers may exceed the maximum by up to
/// infuserOverCap and corrupt the item with a chance that grows with the starting quality above the maximum.
/// </summary>
public sealed class QualityOperation : CraftOperation
{
    private const string Corrupted = "Item corrupted", NotCorrupted = "Not corrupted";
    private readonly bool _infuser;

    public QualityOperation(CraftingEngine engine, string op, bool infuser) : base(engine, op) => _infuser = infuser;

    private int Cap(Item item) => item.MaxQuality(Engine.A.DefaultMaxQuality) + (_infuser ? Engine.A.InfuserOverCap : 0);

    private int Gain(Item item) => Engine.A.QualityPerUse.TryGetValue(item.Rarity.ToString(), out var q) ? q : 1;

    private double CorruptChance(Item item) =>
        _infuser ? Math.Clamp((item.Quality - item.MaxQuality(Engine.A.DefaultMaxQuality)) * Engine.A.InfuserCorruptChancePerQuality, 0, 1) : 0;

    private Dictionary<string, double> Outcomes(Item item) =>
        CraftingEngine.NormaliseOutcomes(new Dictionary<string, double> { [NotCorrupted] = 1 - CorruptChance(item), [Corrupted] = CorruptChance(item) });

    public override Applicability? Check(CraftContext ctx)
    {
        if (ctx.Item.Quality >= Cap(ctx.Item)) return Applicability.No($"Quality is already at the maximum of {Cap(ctx.Item)}%.");
        ctx.Notes.Add($"Adds {Gain(ctx.Item)}% quality on a {ctx.Item.Rarity} item (config qualityPerUse: {Engine.A.QualityPerUseNote}).");
        return null;
    }

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex) => new()
    {
        SpecialOutcomes = Outcomes(ctx.Item),
        Notes = { $"Quality {ctx.Item.Quality}% → {Math.Min(Cap(ctx.Item), ctx.Item.Quality + Gain(ctx.Item))}% (max {Cap(ctx.Item)}%)." },
    };

    public override void Execute(ExecuteContext ctx)
    {
        var item = ctx.Result;
        item.Quality = Math.Min(Cap(item), item.Quality + Gain(item));
        ctx.Details.Add($"Quality is now {item.Quality}%.");
        if (CraftingEngine.PickOutcome(ctx, Outcomes(ctx.Item)) == Corrupted)
        {
            item.Corrupted = true;
            ctx.Details.Add("Item is now Corrupted.");
        }
    }
}
