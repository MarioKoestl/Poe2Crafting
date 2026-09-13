using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>
/// Architect's Orb: on a Corrupted equipment item or jewel, either adds a second corruption enchantment from a different
/// mod group (Twice Corrupted) or destroys the item (config: architectSuccessChance).
/// </summary>
public sealed class ArchitectOperation : CraftOperation
{
    private const string Success = "Second corruption enchantment (Twice Corrupted)", Destroyed = "Item destroyed";

    public ArchitectOperation(CraftingEngine engine) : base(engine, "architect") { }

    public override bool RequiresCorrupted => true;

    // "Modifies a Corrupted Equipment or Jewel item"
    public override string? DefaultClassTarget => "equipment_or_jewel";

    private Dictionary<string, double> Outcomes => new() { [Success] = Engine.A.ArchitectSuccessChance, [Destroyed] = 1 - Engine.A.ArchitectSuccessChance };

    public override Applicability? Check(CraftContext ctx)
    {
        if (ctx.Item.TwiceCorrupted) return Applicability.No("The item is already Twice Corrupted.");
        if (Engine.Pool.CorruptionEnchantCandidates(ctx.Item).Count == 0) return Applicability.No("No further corruption enchantment can roll on this item.");
        return null;
    }

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex) => new()
    {
        SpecialOutcomes = Outcomes,
        Additions = Engine.Pool.CorruptionEnchantCandidates(ctx.Item),
        AdditionLabel = "Possible second enchantments (on success)",
    };

    public override void Execute(ExecuteContext ctx)
    {
        var chosenEnchant = ctx.Choice?.AddModIds.FirstOrDefault();
        if (chosenEnchant == null && CraftingEngine.PickOutcome(ctx, Outcomes) == Destroyed)
        {
            ctx.Destroyed = true;
            ctx.Details.Add("The Architect's Orb destroyed the item.");
            return;
        }
        Engine.AddCorruptionEnchant(ctx, chosenEnchant);
        ctx.Result.TwiceCorrupted = true;
        ctx.Details.Add("Item is now Twice Corrupted.");
    }
}
