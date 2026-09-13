namespace POE2Crafting.Core.Engine.Operations;

/// <summary>Divine Orb: reroll all mod values inside their ranges (Omen of the Blessed: implicits only; Omen of Sanctification: sanctify).</summary>
public sealed class DivineOperation : CraftOperation
{
    public DivineOperation(CraftingEngine engine) : base(engine, "divine") { }

    public override Applicability? Check(CraftContext ctx)
    {
        if (!ctx.OmenIs(OmenEffects.ImplicitsOnly) && !ctx.Item.Affixes.Any(m => m.Def?.Ranges.Count > 0))
            return Applicability.No("No modifier with a value range to reroll.");
        if (ctx.OmenIs(OmenEffects.Sanctify)) ctx.Notes.Add("Sanctify: the item is marked Sanctified (cannot be desecrated); further effects are UNVERIFIED.");
        if (ctx.OmenIs(OmenEffects.ImplicitsOnly)) ctx.Notes.Add("Implicit values are not tracked numerically; the omen only prevents explicit rerolls.");
        return null;
    }

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex) =>
        new() { Notes = { "Every modifier value is rerolled uniformly inside its tier range." } };

    public override void Execute(ExecuteContext ctx)
    {
        if (ctx.OmenIs(OmenEffects.ImplicitsOnly)) ctx.Details.Add("Implicit modifier values rerolled (not tracked numerically).");
        else
        {
            for (int i = 0; i < ctx.Result.Mods.Count; i++)
            {
                var m = ctx.Result.Mods[i];
                if (!m.IsAffix || m.Def == null || m.Def.Ranges.Count == 0) continue;
                var before = m.DisplayText();
                m.Values = ctx.Choice?.Rerolls?.TryGetValue(i, out var vals) == true ? vals.ToList() : CraftingEngine.RollValues(m.Def, ctx.Rng);
                ctx.Details.Add($"{before}  ->  {m.DisplayText()}");
            }
        }
        if (ctx.OmenIs(OmenEffects.Sanctify)) { ctx.Result.Sanctified = true; ctx.Details.Add("Item is now Sanctified."); }
    }
}
