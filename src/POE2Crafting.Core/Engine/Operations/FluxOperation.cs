using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>
/// Blazing/Chilling/Crackling/Void Flux: transforms the other single-element resistance modifiers into equivalent resistance
/// modifiers of the flux's element (Void: Fire, Cold and Lightning into Chaos). Equivalent = the target resistance tier with the
/// highest level not above the source mod's level; the value is rolled anew inside that tier's range (poe2wiki; seen in game:
/// 41% Lightning → 45% Cold), so a flux doubles as a value reroll.
/// </summary>
internal sealed class FluxOperation : CraftOperation
{
    private static readonly string[] Elements = { "Fire", "Cold", "Lightning" };
    private static string ResistanceFamily(string element) => element + "Resistance";

    public FluxOperation(CraftingEngine engine) : base(engine, CurrencyOps.Flux) { }

    private static IEnumerable<string> SourceElements(CurrencyDef flux) => Elements.Where(e => e != flux.Element);

    private sealed record Transformation(int Index, ItemMod Mod, ModDef Replacement);

    /// <summary>Affixes to transform, each with its replacement on this base.</summary>
    private List<Transformation> Transformations(Item item, CurrencyDef flux)
    {
        var sources = SourceElements(flux).Select(ResistanceFamily).ToHashSet();
        var targets = Engine.Pool.AllForBase(item).Where(m => m.Family == ResistanceFamily(flux.Element!)).ToList();
        return item.Mods.Select((m, i) => (Index: i, Mod: m))
            .Where(t => t.Mod.IsAffix && t.Mod.Def?.Family is { } f && sources.Contains(f))
            .Select(t => EquivalentTier(targets.Where(r => r.AffixType == t.Mod.Affix), t.Mod.Def!.Level) is { } replacement ? new Transformation(t.Index, t.Mod, replacement) : null)
            .OfType<Transformation>()
            .ToList();
    }

    /// <summary>The tier with the highest level not above <paramref name="level"/>, or the lowest tier when all are higher.</summary>
    private static ModDef? EquivalentTier(IEnumerable<ModDef> tiers, int level)
    {
        var list = tiers.ToList();
        return list.Where(r => r.Level <= level).MaxBy(r => r.Level) ?? list.MinBy(r => r.Level);
    }

    public override Applicability? Check(CraftContext ctx)
    {
        if (ctx.Currency.Element == null) return Applicability.No("Flux element missing in the data.");
        if (Transformations(ctx.Item, ctx.Currency).Count == 0)
            return Applicability.No($"The item has no {string.Join("/", SourceElements(ctx.Currency))} resistance modifier to transform.");
        ctx.Notes.Add("The value is rolled anew inside the new tier's range (poe2wiki). Same tier = the target tier with the highest modifier level not above the source mod's level.");
        return null;
    }

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex)
    {
        var transformations = Transformations(ctx.Item, ctx.Currency);
        var preview = new StepPreview { ValueRerolls = transformations.Select(t => new ValueReroll(t.Index, t.Replacement)).ToList() };
        foreach (var t in transformations)
            preview.Notes.Add($"{t.Mod.DisplayText()}  →  {t.Replacement.Text}");
        return preview;
    }

    public override void Execute(ExecuteContext ctx)
    {
        foreach (var t in Transformations(ctx.Result, ctx.Currency))
        {
            var before = t.Mod.DisplayText();
            var added = ctx.Result.ReplaceMod(t.Index, t.Replacement, t.Mod.Kind, CraftingEngine.RerolledValues(ctx, t.Index, t.Replacement), t.Mod.SourceName);
            added.Fractured = t.Mod.Fractured;
            ctx.Details.Add($"{before}  →  {added.DisplayText()}");
        }
    }
}
