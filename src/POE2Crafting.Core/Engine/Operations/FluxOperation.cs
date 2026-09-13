using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>
/// Blazing/Chilling/Crackling/Void Flux: transforms the other single-element resistance modifiers into equivalent resistance
/// modifiers of the flux's element (Void: Fire, Cold and Lightning into Chaos). Equivalent = the target resistance tier with the
/// highest level not above the source mod's level; the rolled value keeps its relative position in the range.
/// </summary>
public sealed class FluxOperation : CraftOperation
{
    private static readonly string[] Elements = { "Fire", "Cold", "Lightning" };
    private static string ResistanceFamily(string element) => element + "Resistance";

    public FluxOperation(CraftingEngine engine) : base(engine, "flux") { }

    private static IEnumerable<string> SourceFamilies(CurrencyDef flux) =>
        Elements.Where(e => e != flux.Element).Select(ResistanceFamily);

    /// <summary>Affixes to transform, each with its replacement on this base.</summary>
    private List<(int Index, ItemMod Mod, ModDef Replacement)> Transformations(Item item, CurrencyDef flux)
    {
        var sources = SourceFamilies(flux).ToHashSet();
        var targets = Engine.Pool.AllForBase(item).Where(m => m.Family == ResistanceFamily(flux.Element!)).ToList();
        return item.Mods.Select((m, i) => (Index: i, Mod: m))
            .Where(t => t.Mod.IsAffix && t.Mod.Def?.Family is { } f && sources.Contains(f))
            .Select(t => (t.Index, t.Mod, Replacement: EquivalentTier(targets.Where(r => r.AffixType == t.Mod.Affix), t.Mod.Def!.Level)))
            .Where(t => t.Replacement != null)
            .Select(t => (t.Index, t.Mod, t.Replacement!))
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
            return Applicability.No($"The item has no {string.Join("/", SourceFamilies(ctx.Currency).Select(f => f.Replace("Resistance", "")))} resistance modifier to transform.");
        ctx.Notes.Add("Assumption: \"equivalent\" = highest target tier not above the source mod's level, value at the same relative position (UNVERIFIED).");
        return null;
    }

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex)
    {
        var preview = new StepPreview();
        foreach (var t in Transformations(ctx.Item, ctx.Currency))
            preview.Notes.Add($"{t.Mod.DisplayText()}  →  {ModText.Render(t.Replacement.Text, Rescaled(t.Mod, t.Replacement))}");
        return preview;
    }

    private static List<double> Rescaled(ItemMod mod, ModDef replacement) => ModText.RescaleValues(mod.Values, mod.Def!.Ranges, replacement.Ranges);

    public override void Execute(ExecuteContext ctx)
    {
        foreach (var t in Transformations(ctx.Result, ctx.Currency))
        {
            var before = t.Mod.DisplayText();
            ctx.Result.Mods.RemoveAt(t.Index);
            var added = ctx.Result.AddMod(t.Replacement, t.Mod.Kind, Rescaled(t.Mod, t.Replacement), t.Mod.SourceName, t.Index);
            added.Fractured = t.Mod.Fractured;
            ctx.Details.Add($"{before}  →  {added.DisplayText()}");
        }
    }
}
