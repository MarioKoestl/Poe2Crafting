using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>
/// Catalysts (synthetic currencies, Op catalyst): add quality of the catalyst's type to a ring/amulet (Refined: jewel).
/// "Replaces other quality types": the type changes, the quality amount is kept (assumption).
/// </summary>
internal sealed class CatalystOperation : CraftOperation
{
    public CatalystOperation(CraftingEngine engine) : base(engine, CurrencyOps.Catalyst) { }

    public override Applicability? Check(CraftContext ctx)
    {
        var catalyst = ctx.Currency.Catalyst;
        if (catalyst == null) return Applicability.No("Catalyst data missing.");
        var item = ctx.Item;
        int cap = Engine.MaxQuality(item);
        bool sameType = item.QualityType == catalyst.QualityType;
        if (sameType && item.Quality >= cap) return Applicability.No($"{catalyst.QualityType} quality is already at the maximum of {cap}%.");
        ctx.Notes.Add($"Assumption: +{Assumptions.CatalystQualityPerUse}% per catalyst (config catalystQualityPerUse), maximum {cap}%.");
        if (!sameType && item.Quality > 0)
            ctx.Notes.Add($"Replaces the {item.QualityType ?? "current"} quality type; assumption: the {item.Quality}% quality is kept (UNVERIFIED).");
        return null;
    }

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex)
    {
        var catalyst = ctx.Currency.Catalyst!;
        var choice = Choice(ctx.Item, catalyst);
        return new StepPreview
        {
            Quality = choice,
            Notes =
            {
                $"Quality {ctx.Item.Quality}% → {choice.Default}% ({catalyst.QualityType}).",
                ctx.Item.Mods.Any(m => catalyst.Enhances(m.Def)) ? "Whole numbers round down: an enhanced value only rises once the quality is high enough (see the table)."
                    : $"No modifier on the item has the \"{catalyst.ModTag}\" tag yet.",
            },
        };
    }

    /// <summary>The quality after one use: +catalystQualityPerUse when rolled; chosen: any higher value up to the maximum (the current amount when the type changes).</summary>
    private QualityChoice Choice(Item item, CatalystDef catalyst)
    {
        int max = Engine.MaxQuality(item);
        int min = Math.Min(max, item.QualityType == catalyst.QualityType ? item.Quality + 1 : Math.Max(1, item.Quality));
        return new QualityChoice(catalyst.QualityType!, Math.Min(max, item.Quality + Assumptions.CatalystQualityPerUse), min, max);
    }

    public override void Execute(ExecuteContext ctx)
    {
        var item = ctx.Result;
        var choice = Choice(item, ctx.Currency.Catalyst!);
        int quality = ctx.Choice?.Quality ?? choice.Default;
        if (quality < choice.Min || quality > choice.Max)
            throw new InvalidChoiceException($"The quality after the catalyst must be between {choice.Min}% and {choice.Max}%.");
        item.QualityType = choice.Type;
        item.Quality = quality;
        ctx.Details.Add($"Quality is now {item.Quality}% ({item.QualityType}).");
    }
}
