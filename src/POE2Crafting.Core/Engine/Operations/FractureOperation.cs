using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>
/// Fracturing Orb: lock one random mod on a rare with at least 4 mods; an item can only hold one fractured modifier.
/// Desecrated mods (revealed or not) count towards the 4 mods but cannot be fractured, so they raise the chance for the others.
/// </summary>
internal sealed class FractureOperation : CraftOperation
{
    public FractureOperation(CraftingEngine engine) : base(engine, CurrencyOps.Fracture) { }

    private static List<RemovalCandidate> Fracturable(Item item) =>
        RemovalCandidate.UniformOf(item, m => m.IsAffix && !m.Fractured && m.Kind != ModKind.Desecrated);

    public override Applicability? Check(CraftContext ctx)
    {
        int minMods = ctx.Currency.MinMods ?? Assumptions.FractureMinMods;
        if (ctx.Item.AffixCount < minMods) return Applicability.No($"Fracturing Orb needs at least {minMods} prefixes/suffixes (has {ctx.Item.AffixCount}; implicits don't count).");
        if (ctx.Item.Affixes.Any(m => m.Fractured)) return Applicability.No("The item already has a fractured modifier (only one per item).");
        if (Fracturable(ctx.Item).Count == 0) return Applicability.No("No modifier can be fractured (desecrated modifiers cannot be fractured).");
        if (ctx.Item.HasDesecratedMod) ctx.Notes.Add("Desecrated modifiers cannot be fractured: the fracture lands on one of the other modifiers.");
        return null;
    }

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex) => new()
    {
        Removals = Fracturable(ctx.Item),
        RemovalLabel = "Fracture Target",
        Notes = { "One random (non-fractured) modifier becomes fractured and can no longer be removed or changed." },
    };

    public override void Execute(ExecuteContext ctx)
    {
        var chosen = CraftingEngine.ChosenRemovals(ctx).FirstOrDefault();
        var pick = CraftingEngine.Pick(ctx.Rng, Fracturable(ctx.Result), c => c.Probability, chosen != null ? c => ReferenceEquals(c.Mod, chosen) : null,
            "The chosen modifier cannot be fractured.");
        pick.Mod.Fractured = true;
        ctx.Details.Add($"Fractured: {pick.Mod.DisplayText()}");
    }
}
