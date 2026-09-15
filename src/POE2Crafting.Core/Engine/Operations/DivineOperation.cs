using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>
/// Divine Orb: reroll the values of all non-fractured mods inside their ranges (fractured mods keep their values;
/// Omen of the Blessed: implicits only; Omen of Sanctification: sanctify).
/// </summary>
internal sealed class DivineOperation : CraftOperation
{
    public DivineOperation(CraftingEngine engine) : base(engine, CurrencyOps.Divine) { }

    /// <summary>A mod whose values a Divine Orb rerolls.</summary>
    internal static bool Rerollable(ItemMod mod) => mod.IsAffix && !mod.Fractured && mod.Def?.Ranges.Count > 0;

    public override Applicability? Check(CraftContext ctx)
    {
        if (!ctx.OmenIs(OmenEffects.ImplicitsOnly) && !ctx.Item.Affixes.Any(Rerollable))
            return Applicability.No("No modifier with a value range to reroll (fractured modifiers keep their values).");
        if (ctx.Item.Affixes.Any(m => m.Fractured)) ctx.Notes.Add("Fractured modifiers keep their values.");
        if (ctx.OmenIs(OmenEffects.Sanctify)) ctx.Notes.Add("Sanctify: the item is marked Sanctified (cannot be desecrated); further effects are UNVERIFIED.");
        if (ctx.OmenIs(OmenEffects.ImplicitsOnly)) ctx.Notes.Add("Implicit values are not tracked numerically; the omen only prevents explicit rerolls.");
        return null;
    }

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex) => new()
    {
        Notes = { "Every modifier value is rerolled uniformly inside its tier range." },
        ValueRerolls = ctx.OmenIs(OmenEffects.ImplicitsOnly) ? new()
            : ctx.Item.Mods.Select((m, i) => (m, i)).Where(x => Rerollable(x.m)).Select(x => new ValueReroll(x.i, x.m.Def!)).ToList(),
    };

    public override void Execute(ExecuteContext ctx)
    {
        if (ctx.OmenIs(OmenEffects.ImplicitsOnly)) ctx.Details.Add("Implicit modifier values rerolled (not tracked numerically).");
        else
        {
            for (int i = 0; i < ctx.Result.Mods.Count; i++)
            {
                var m = ctx.Result.Mods[i];
                if (!Rerollable(m)) continue;
                var before = m.DisplayText();
                m.Values = CraftingEngine.RerolledValues(ctx, i, m.Def!);
                ctx.Details.Add($"{before}  ->  {m.DisplayText()}");
            }
        }
        if (ctx.OmenIs(OmenEffects.Sanctify)) { ctx.Result.Sanctified = true; ctx.Details.Add("Item is now Sanctified."); }
    }
}
