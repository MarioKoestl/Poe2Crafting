using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>Fracturing Orb: lock one random non-fractured mod on a rare with at least 4 mods.</summary>
public sealed class FractureOperation : CraftOperation
{
    public FractureOperation(CraftingEngine engine) : base(engine, "fracture") { }

    private static List<RemovalCandidate> Fracturable(Item item) =>
        RemovalCandidate.Uniform(item.Mods.Select((m, i) => new RemovalCandidate { Index = i, Mod = m }).Where(r => r.Mod.IsAffix && !r.Mod.Fractured).ToList());

    public override Applicability? Check(CraftContext ctx)
    {
        int minMods = ctx.Currency.MinMods ?? 4;
        if (ctx.Item.AffixCount < minMods) return Applicability.No($"Fracturing Orb needs at least {minMods} modifiers.");
        if (Fracturable(ctx.Item).Count == 0) return Applicability.No("All modifiers are already fractured.");
        if (ctx.Item.Affixes.Any(m => m.Fractured)) ctx.Notes.Add("Assumption: an item can hold more than one fractured modifier (UNVERIFIED).");
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
        var candidates = Fracturable(ctx.Result);
        var chosen = CraftingEngine.ChosenRemovals(ctx).FirstOrDefault();
        var pick = chosen != null
            ? candidates.FirstOrDefault(c => ReferenceEquals(c.Mod, chosen)) ?? throw new InvalidOperationException("The chosen modifier cannot be fractured.")
            : candidates[ctx.Rng.Next(candidates.Count)];
        pick.Mod.Fractured = true;
        ctx.Details.Add($"Fractured: {pick.Mod.DisplayText()}");
    }
}
