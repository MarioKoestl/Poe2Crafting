using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>Orb of Chance: Normal → Unique or destroyed (Omen of Chance: never destroyed).</summary>
public sealed class ChanceOperation : CraftOperation
{
    private const string Unique = "Upgrade to Unique", Destroyed = "Item destroyed", NoChange = "No change";
    private const double UniqueChance = 0.05;

    public ChanceOperation(CraftingEngine engine) : base(engine, "chance") { }

    public override Applicability? Check(CraftContext ctx)
    {
        ctx.Notes.Add($"Assumption: {UniqueChance:P0} unique chance (UNVERIFIED); the resulting unique is not modelled (item becomes a placeholder unique).");
        return null;
    }

    private static Dictionary<string, double> Outcomes(CraftContext ctx) => ctx.OmenIs(OmenEffects.NoDestroy)
        ? new() { [Unique] = UniqueChance, [NoChange] = 1 - UniqueChance }
        : new() { [Unique] = UniqueChance, [Destroyed] = 1 - UniqueChance };

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex)
    {
        var preview = new StepPreview { SpecialOutcomes = Outcomes(ctx) };
        if (ctx.OmenIs(OmenEffects.NoDestroy)) preview.Notes.Add("Omen of Chance: the item is never destroyed; on failure it stays unchanged.");
        if (ctx.OmenIs(OmenEffects.RandomUniqueOfClass)) preview.Notes.Add("Omen of the Ancients: result is a random unique of the item class.");
        return preview;
    }

    public override void Execute(ExecuteContext ctx)
    {
        switch (CraftingEngine.PickOutcome(ctx, Outcomes(ctx)))
        {
            case Destroyed:
                ctx.Destroyed = true;
                ctx.Details.Add("The Orb of Chance destroyed the item.");
                break;
            case Unique:
                ctx.Result.Rarity = Rarity.Unique;
                ctx.Result.Name = "(random Unique)";
                ctx.Details.Add("Upgraded to a Unique (placeholder, unique mods are not modelled).");
                break;
            default:
                ctx.Details.Add("No change.");
                break;
        }
    }
}
