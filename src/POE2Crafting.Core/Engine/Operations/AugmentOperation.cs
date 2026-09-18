using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>
/// Socketing a rune, soul core or idol (synthetic currencies with Op socket_augment) into an augment socket: the augment grants the effect of the
/// item's class (Item.Runes holds one line per filled socket). A free socket is filled first; on a full item the chosen socket is replaced
/// (config augmentReplacesOccupiedSocket). "Bonded" effects only apply for a specific ascendancy and are not modelled.
/// Aldur runes transform the element of the item's modifiers when socketed (<see cref="RuneTransformation"/>).
/// </summary>
internal sealed class AugmentOperation : CraftOperation
{
    /// <summary>The outcome label of filling a free socket (StepPreview.SpecialOutcomes, ManualChoice.SpecialOutcome).</summary>
    internal const string FreeSocket = "Empty socket";
    private const int FreeSocketKey = -1;

    public AugmentOperation(CraftingEngine engine) : base(engine, CurrencyOps.SocketAugment) { }

    public override bool WorksOnCorrupted => Assumptions.AugmentWorksOnCorrupted;

    /// <summary>The text the augment adds to this item (effects of all its lines for the item's class).</summary>
    private string? EffectText(CraftContext ctx) => ctx.Currency.Augment is { } a ? Engine.Data.AugmentEffectText(a, ctx.Item.Base, ctx.Item.ItemClass) : null;

    /// <summary>Where the augment goes: the free socket, or (full item) each occupied socket index, equally likely.</summary>
    private static Dictionary<int, double> Targets(Item item) =>
        item.Runes.Count < item.Sockets
            ? new() { [FreeSocketKey] = 1 }
            : CraftingEngine.NormaliseOutcomes(item.Runes.Select((_, i) => new KeyValuePair<int, double>(i, 1.0)));

    private static string Label(Item item, int socket) => socket == FreeSocketKey ? FreeSocket : $"Replace socket {socket + 1}: {item.Runes[socket]}";

    public override Applicability? Check(CraftContext ctx)
    {
        var item = ctx.Item;
        if (EffectText(ctx) == null) return Applicability.No($"{ctx.Currency.Name} cannot be socketed into {item.ItemClass}.");
        if (item.Sockets == 0) return Applicability.No("The item has no augment socket (use an Artificer's Orb).");
        if (item.Runes.Count >= item.Sockets)
        {
            if (!Assumptions.AugmentReplacesOccupiedSocket) return Applicability.No("All augment sockets are filled.");
            ctx.Notes.Add("All sockets are filled: the augment replaces the chosen one and the old augment is destroyed (assumption).");
        }
        if (item.Runes.Any(r => ModText.ExtraCraftedAllowed(r) > 0) && item.Runes.Count >= item.Sockets)
            ctx.Notes.Add("Replacing Astrid's Creativity keeps the crafted modifiers already on the item.");
        if (ctx.Currency.Augment!.Effects.Any(e => e.Category == ModCategories.Bonded))
            ctx.Notes.Add("Bonded effects only apply with the Shaman's \"Wisdom of the Maji\" and are not shown.");
        return null;
    }

    /// <summary>The modifiers an Aldur rune transforms on the item (none for other augments).</summary>
    private List<RuneTransformation.Change> Transformations(CraftContext ctx, Item item) =>
        EffectText(ctx) is { } text && RuneTransformation.Parse(text) is { } rule
            ? RuneTransformation.Plan(item, rule, Engine.Pool, Assumptions.AldurRuneTransformsFractured)
            : new();

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex)
    {
        var preview = new StepPreview
        {
            SpecialOutcomes = Targets(ctx.Item).ToDictionary(kv => Label(ctx.Item, kv.Key), kv => kv.Value),
            Notes = { $"Adds: {EffectText(ctx)}" },
        };
        if (EffectText(ctx) is { } text && RuneTransformation.Parse(text) != null)
        {
            var changes = Transformations(ctx, ctx.Item);
            preview.Notes.AddRange(changes.Select(c => $"{c.Mod.DisplayText()}  →  {ModText.Render(c.Replacement.Text, c.Values)}"));
            if (changes.Count == 0) preview.Notes.Add("No modifier on the item is transformed.");
            // the unexplained in-game observation is documented in config.json (single source), not repeated here
            if (Assumptions.AldurRuneObservationNote is { } observation && changes.Any(c => c.Mod.DisplayText().Contains("of Damage as Extra", StringComparison.OrdinalIgnoreCase)))
                preview.Notes.Add($"Unclear: {observation}");
            preview.Notes.Add("Only the modifiers on the item now are transformed; modifiers added later stay as they roll. The rune can't be removed again, only replaced.");
            if (ctx.Item.Affixes.Any(m => m.Fractured))
                preview.Notes.Add(Assumptions.AldurRuneTransformsFractured
                    ? "Fractured modifiers are transformed too."
                    : "Fractured modifiers stay unchanged; only the normal ones are transformed.");
        }
        return preview;
    }

    public override void Execute(ExecuteContext ctx)
    {
        var text = EffectText(ctx)!;
        var socket = CraftingEngine.PickOutcome(ctx, Targets(ctx.Item), s => Label(ctx.Item, s));
        var runes = ctx.Result.Runes;
        if (socket == FreeSocketKey) runes.Add(text);
        else
        {
            ctx.Details.Add($"Destroyed augment: {runes[socket]}");
            runes[socket] = text;
        }
        ctx.Details.Add($"Socketed {ctx.Currency.Name}: {text}");
        foreach (var change in Transformations(ctx, ctx.Result))
        {
            var before = change.Mod.DisplayText();
            var transformed = ctx.Result.ReplaceMod(change.Index, change.Replacement, change.Mod.Kind, change.Values, change.Mod.SourceName);
            transformed.Fractured = change.Mod.Fractured;
            ctx.Details.Add($"{before}  →  {transformed.DisplayText()}");
        }
    }
}
