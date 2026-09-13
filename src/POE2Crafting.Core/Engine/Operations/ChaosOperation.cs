namespace POE2Crafting.Core.Engine.Operations;

/// <summary>Chaos Orb (incl. Greater/Perfect): remove one random mod, then add one (Whittling, Sinistral/Dextral Erasure restrict the removal).</summary>
public sealed class ChaosOperation : CraftOperation
{
    public ChaosOperation(CraftingEngine engine) : base(engine, "chaos") { }

    public override Applicability? Check(CraftContext ctx) =>
        CraftingEngine.Removable(ctx.Item, ctx.Omens).Count == 0 ? Applicability.No("No modifier can be removed (all fractured or omen restriction).") : null;

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex)
    {
        var removals = CraftingEngine.Removable(ctx.Item, ctx.Omens);
        var considered = forcedRemovalIndex is { } fi ? removals.Where(r => r.Index == fi).ToList() : removals;
        // marginal distribution of the added mod over the (considered) removal outcomes
        var acc = new Dictionary<string, ModCandidate>();
        double pPrefix = 0, weightSum = considered.Sum(r => r.Probability);
        foreach (var r in considered)
        {
            var (additions, pP) = Engine.AdditionsAfterRemoval(ctx.Item, r.Index, ctx.MinModLevel);
            double pr = r.Probability / weightSum;
            pPrefix += pP * pr;
            foreach (var x in additions)
            {
                if (!acc.TryGetValue(x.Mod.Id, out var e)) acc[x.Mod.Id] = e = new ModCandidate { Mod = x.Mod, Weight = x.Weight };
                e.Probability += x.Probability * pr;
            }
        }
        var notes = new List<string>();
        if (ctx.OmenIs(OmenEffects.RemoveLowestLevel)) notes.Add("Omen of Whittling: removes the modifier with the lowest modifier level (assumption: ties are broken randomly).");
        return new StepPreview
        {
            RemoveCount = 1, AddCount = 1, Removals = removals,
            Additions = acc.Values.OrderByDescending(x => x.Probability).ToList(),
            PrefixProbability = pPrefix, SuffixProbability = 1 - pPrefix, Notes = notes, TwoStepChoice = true,
        };
    }

    public override void Execute(ExecuteContext ctx)
    {
        var candidates = CraftingEngine.Removable(ctx.Result, ctx.Omens);
        var chosen = CraftingEngine.ChosenRemovals(ctx).FirstOrDefault();
        // "choose outcome" with only the added mod picked: remove a random mod that leaves room for it
        if (chosen == null && ctx.Choice?.AddModIds.Count > 0)
        {
            var modId = ctx.Choice.AddModIds[0];
            candidates = RemovalCandidate.Uniform(candidates
                .Where(r => Engine.AdditionsAfterRemoval(ctx.Result, r.Index, ctx.MinModLevel).Additions.Any(a => a.Mod.Id == modId)).ToList());
            if (candidates.Count == 0) throw new InvalidOperationException("The chosen modifier cannot be added after any possible removal.");
        }
        Engine.RemoveOne(ctx, candidates, chosen);
        Engine.AddMods(ctx, 1, OmenEffects.None); // chaos omens only restrict the removal
    }
}
