using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>Orb of Alchemy: Normal/Magic → Rare with 4 mods (Sinistral/Dextral Alchemy: maximum prefixes/suffixes first).</summary>
public sealed class AlchemyOperation : CraftOperation
{
    public AlchemyOperation(CraftingEngine engine) : base(engine, "alchemy") { }

    public override Applicability? Check(CraftContext ctx)
    {
        if (ctx.Item.Rarity == Rarity.Magic)
            ctx.Notes.Add(Engine.A.AlchemyOnMagicKeepsExistingMods
                ? "Assumption: Orb of Alchemy on a Magic item keeps its mods and fills up to 4 (config: alchemyOnMagicKeepsExistingMods)."
                : "Assumption: existing magic mods are rerolled (config: alchemyOnMagicKeepsExistingMods).");
        return null;
    }

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex)
    {
        var (pre, suf) = Engine.AdditionCandidates(ctx.Item, ctx.MinModLevel, ctx.Omens, Rarity.Rare);
        var combined = Engine.Combined(pre, suf, out var pP);
        var notes = new List<string> { $"Orb of Alchemy adds {Engine.A.AlchemyModCount} modifiers one after another; the distribution shown is for the first modifier." };
        if (ctx.RestrictedType is { } forced) notes.Add($"{ctx.Omens.First(o => OmenEffects.RestrictedType(o) != null).Name}: result will have the maximum number of {forced.ToString().ToLower()}es.");
        return new StepPreview { AddCount = Engine.A.AlchemyModCount, Additions = combined, PrefixProbability = pP, SuffixProbability = 1 - pP, Notes = notes };
    }

    public override void Execute(ExecuteContext ctx)
    {
        var item = ctx.Result;
        if (item.Rarity == Rarity.Magic && !Engine.A.AlchemyOnMagicKeepsExistingMods) item.Mods.RemoveAll(m => m.IsAffix);
        CraftingEngine.MakeRare(item, ctx.Rng);
        int toAdd = Math.Max(0, Engine.A.AlchemyModCount - item.AffixCount);
        int forcedCount = ctx.RestrictedType is { } forced ? Math.Min(Engine.FreeSlots(item, forced, Rarity.Rare), toAdd) : 0;
        for (int i = 0; i < toAdd; i++)
            if (!Engine.AddOne(ctx, i < forcedCount ? ctx.RestrictedType : null, CraftingEngine.ChoiceAt(ctx.Choice, i), OmenEffects.None))
            {
                ctx.Details.Add("No modifier could be added.");
                break;
            }
    }
}
