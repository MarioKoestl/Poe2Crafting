using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>Orb of Alchemy: Normal/Magic → Rare with 4 mods (Sinistral/Dextral Alchemy: maximum prefixes/suffixes first).</summary>
internal sealed class AlchemyOperation : CraftOperation
{
    public AlchemyOperation(CraftingEngine engine) : base(engine, CurrencyOps.Alchemy) { }

    public override Applicability? Check(CraftContext ctx)
    {
        if (ctx.Item.Rarity == Rarity.Magic)
            ctx.Notes.Add(Assumptions.AlchemyOnMagicKeepsExistingMods
                ? "Assumption: Orb of Alchemy on a Magic item keeps its mods and fills up to 4 (config: alchemyOnMagicKeepsExistingMods)."
                : "Assumption: existing magic mods are rerolled (config: alchemyOnMagicKeepsExistingMods).");
        return null;
    }

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex)
    {
        var (pre, suf) = Engine.AdditionCandidates(ctx.Item, ctx.MinModLevel, ctx.Omens, Rarity.Rare);
        var combined = Engine.Combined(pre, suf, out var pP);
        var notes = new List<string> { $"Orb of Alchemy adds {Assumptions.AlchemyModCount} modifiers one after another; the distribution shown is for the first modifier." };
        if (OmenEffects.RestrictingOmen(ctx.Omens) is { } omen)
            notes.Add($"{omen.Name}: result will have the maximum number of {ctx.RestrictedType!.Value.Lower()}es.");
        return new StepPreview { AddCount = Assumptions.AlchemyModCount, Additions = combined, PrefixProbability = pP, SuffixProbability = 1 - pP, Notes = notes };
    }

    public override void Execute(ExecuteContext ctx)
    {
        var item = ctx.Result;
        if (item.Rarity == Rarity.Magic && !Assumptions.AlchemyOnMagicKeepsExistingMods) item.Mods.RemoveAll(m => m.IsAffix);
        CraftingEngine.MakeRare(item, ctx.Rng);
        int toAdd = Math.Max(0, Assumptions.AlchemyModCount - item.AffixCount);
        // the omen fills its affix type first, the rest is random
        var forced = ctx.RestrictedType;
        int forcedCount = forced is { } type ? Math.Min(Engine.FreeSlots(item, type, Rarity.Rare), toAdd) : 0;
        Engine.AddMods(ctx, toAdd, OmenEffects.None, i => i < forcedCount ? forced : null);
    }
}
