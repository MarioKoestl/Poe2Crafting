using POE2Crafting.Core.Data;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>Orb of Extraction: destroys an equipment item and returns its non socket-bound augments (runes, soul cores).</summary>
internal sealed class ExtractOperation : CraftOperation
{
    public ExtractOperation(CraftingEngine engine) : base(engine, CurrencyOps.Extract) { }

    public override string? DefaultClassTarget => ClassTargets.Equipment;

    // augments can be socketed into corrupted items, so extracting them is assumed to work as well
    public override bool WorksOnCorrupted => true;

    public override Applicability? Check(CraftContext ctx) =>
        ctx.Item.Runes.Count == 0 ? Applicability.No("The item has no socketed augments to extract.") : null;

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex) => new()
    {
        SpecialOutcomes = { ["Item destroyed, augments returned"] = 1 },
        Notes = { $"Returned augments: {string.Join("; ", ctx.Item.Runes)}", "Socket-bound (Bonded) augments are not tracked separately and would be lost." },
    };

    public override void Execute(ExecuteContext ctx)
    {
        ctx.Destroyed = true;
        ctx.Details.Add($"Item destroyed. Returned augments: {string.Join("; ", ctx.Item.Runes)}.");
    }
}
