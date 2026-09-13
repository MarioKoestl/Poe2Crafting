using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>
/// Catalysts (synthetic currencies, Op "catalyst"): add quality of the catalyst's type to a ring/amulet (Refined: jewel).
/// "Replaces other quality types": the type changes, the quality amount is kept (assumption).
/// </summary>
public sealed class CatalystOperation : CraftOperation
{
    public CatalystOperation(CraftingEngine engine) : base(engine, "catalyst") { }

    private int Cap(Item item) => item.MaxQuality(Engine.A.DefaultMaxQuality);

    public override Applicability? Check(CraftContext ctx)
    {
        var catalyst = ctx.Currency.Catalyst;
        if (catalyst == null) return Applicability.No("Catalyst data missing.");
        var item = ctx.Item;
        bool sameType = item.QualityType == catalyst.QualityType;
        if (sameType && item.Quality >= Cap(item)) return Applicability.No($"{catalyst.QualityType} quality is already at the maximum of {Cap(item)}%.");
        ctx.Notes.Add($"Assumption: +{Engine.A.CatalystQualityPerUse}% per catalyst (config catalystQualityPerUse), maximum {Cap(item)}%.");
        if (!sameType && item.Quality > 0)
            ctx.Notes.Add($"Replaces the {item.QualityType ?? "current"} quality type; assumption: the {item.Quality}% quality is kept (UNVERIFIED).");
        return null;
    }

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex)
    {
        var catalyst = ctx.Currency.Catalyst!;
        var enhanced = ctx.Item.Mods.Where(m => m.Def?.ModTags.Contains(catalyst.ModTag!) == true).Select(m => m.DisplayText()).ToList();
        return new StepPreview
        {
            Notes =
            {
                $"Quality {ctx.Item.Quality}% → {NewQuality(ctx.Item)}% ({catalyst.QualityType}).",
                enhanced.Count > 0 ? $"Enhanced modifiers: {string.Join("; ", enhanced)}" : $"No modifier on the item has the \"{catalyst.ModTag}\" tag yet.",
            },
        };
    }

    private int NewQuality(Item item) => Math.Min(Cap(item), item.Quality + Engine.A.CatalystQualityPerUse);

    public override void Execute(ExecuteContext ctx)
    {
        var item = ctx.Result;
        item.QualityType = ctx.Currency.Catalyst!.QualityType;
        item.Quality = NewQuality(item);
        ctx.Details.Add($"Quality is now {item.Quality}% ({item.QualityType}).");
    }
}
