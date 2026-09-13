using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>
/// Orb of Transmutation / Augmentation (magic slots), Regal Orb and Exalted Orb (rare slots), incl. Greater/Perfect variants:
/// add one random mod (two with Omen of Greater Exaltation) and raise the rarity to the target rarity.
/// </summary>
internal sealed class AddModOperation : CraftOperation
{
    private readonly Rarity _targetRarity;
    private readonly bool _requiresNoAffixes;

    /// <param name="requiresNoAffixes">Orb of Transmutation: only on items without modifiers.</param>
    public AddModOperation(CraftingEngine engine, string op, Rarity targetRarity, bool requiresNoAffixes = false) : base(engine, op)
    {
        _targetRarity = targetRarity;
        _requiresNoAffixes = requiresNoAffixes;
    }

    private static int AddCount(CraftContext ctx) => ctx.OmenIs(OmenEffects.AddTwo) ? 2 : 1;

    public override Applicability? Check(CraftContext ctx)
    {
        if (_requiresNoAffixes) return ctx.Item.AffixCount != 0 ? Applicability.No($"{ctx.Currency.Name} needs an item without modifiers.") : null;

        int need = AddCount(ctx);
        if (Engine.FreeSlots(ctx.Item, _targetRarity, ctx.RestrictedType) < need) return CraftingEngine.NoSlot(ctx.Omens, need);
        var (pre, suf) = Engine.AdditionCandidates(ctx.Item, ctx.MinModLevel, ctx.Omens, _targetRarity);
        if (pre.Count + suf.Count == 0) return Applicability.No("No modifier can roll (item level too low, pool exhausted or omen restriction).");
        if (ctx.OmenIs(OmenEffects.Catalysing))
        {
            if (ctx.Item.QualityTag == null) return Applicability.No($"{ctx.Omens.WithEffect(OmenEffects.Catalysing)!.Name} needs catalyst quality on the item.");
            ctx.Notes.Add($"Catalysing Exaltation consumes the {ctx.Item.Quality}% {ctx.Item.QualityType} quality; assumption: {ctx.Item.QualityTag} mods get ×{1 + ctx.Item.Quality * Assumptions.CatalysingWeightBonusPerQuality:0.##} weight (config catalysingWeightBonusPerQuality).");
        }
        return null;
    }

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex)
    {
        var (pre, suf) = Engine.AdditionCandidates(ctx.Item, ctx.MinModLevel, ctx.Omens, _targetRarity);
        var combined = Engine.Combined(pre, suf, out var pP);
        var notes = new List<string>();
        if (Assumptions.AffixTypeSelection == AffixTypeSelection.Weighted)
            notes.Add("Assumption: prefix vs. suffix is chosen proportionally to the total weight of each pool (config: affixTypeSelection).");
        return new StepPreview { AddCount = AddCount(ctx), Additions = combined, PrefixProbability = pP, SuffixProbability = 1 - pP, Notes = notes };
    }

    public override void Execute(ExecuteContext ctx)
    {
        if (_targetRarity == Rarity.Rare) CraftingEngine.MakeRare(ctx.Result, ctx.Rng);
        else ctx.Result.Rarity = Rarity.Magic;
        Engine.AddMods(ctx, AddCount(ctx), ctx.Omens);
        if (ctx.OmenIs(OmenEffects.Catalysing))
        {
            ctx.Result.Quality = 0;
            ctx.Result.QualityType = null;
            ctx.Details.Add("Catalyst quality consumed.");
        }
    }
}
