using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>
/// Vaal Orb: corrupts the item with one of: no change, a corruption enchantment, 1-3 modifiers rerolled,
/// +1 socket ignoring the limit (martial weapons, armour, foci) or quality up to 23% (wands, staves).
/// Outcome weights come from config (vaalOutcomes); outcomes impossible on the item are dropped. Omen of Corruption removes "no change".
/// </summary>
public sealed class VaalOperation : CraftOperation
{
    private const string NoChange = "no_change", Enchant = "corrupted_implicit", Reroll = "reroll_mods", SocketOrQuality = "add_socket_or_quality";

    public VaalOperation(CraftingEngine engine) : base(engine, "vaal") { }

    private bool GetsSocket(Item item) => Engine.Data.ClassMatchesTarget(item.ItemClass, "martial_weapon_or_armour");
    private bool GetsQuality(Item item) => Engine.Data.ClassMatchesTarget(item.ItemClass, "wand_or_staff") && item.Quality < Engine.A.VaalQualityCap;

    private string Label(string outcome, Item item) => outcome switch
    {
        NoChange => "No change (corrupted only)",
        Enchant => "Corruption enchantment",
        Reroll => $"{Engine.A.VaalRerollMinMods}-{Engine.A.VaalRerollMaxMods} modifiers rerolled",
        _ => GetsSocket(item) ? "+1 socket (ignores the socket limit)" : $"Quality increased (up to {Engine.A.VaalQualityCap}%)",
    };

    private bool IsPossible(string outcome, CraftContext ctx) => outcome switch
    {
        NoChange => !ctx.OmenIs(OmenEffects.ForceChange),
        Enchant => Engine.Pool.CorruptionEnchantCandidates(ctx.Item).Count > 0,
        Reroll => ctx.Item.Rarity is Rarity.Magic or Rarity.Rare && CraftingEngine.Removable(ctx.Item, OmenEffects.None).Count > 0,
        SocketOrQuality => GetsSocket(ctx.Item) || GetsQuality(ctx.Item),
        _ => false,
    };

    /// <summary>Outcome label → probability for this item.</summary>
    private Dictionary<string, double> Outcomes(CraftContext ctx) =>
        CraftingEngine.NormaliseOutcomes(Engine.A.VaalOutcomes.Where(kv => IsPossible(kv.Key, ctx))
            .Select(kv => new KeyValuePair<string, double>(Label(kv.Key, ctx.Item), kv.Value)));

    private string OutcomeId(string label, Item item) => Engine.A.VaalOutcomes.Keys.First(id => Label(id, item) == label);

    public override Applicability? Check(CraftContext ctx)
    {
        if (Outcomes(ctx).Count == 0) return Applicability.No("No Vaal Orb outcome is possible on this item (config: vaalOutcomes).");
        ctx.Notes.Add($"Outcome weights: {Engine.A.VaalOutcomesNote}");
        if (IsPossible(Reroll, ctx))
            ctx.Notes.Add("Assumption: \"affixes are randomized\" replaces each hit modifier with a new random modifier of the same affix type (UNVERIFIED).");
        if (ctx.Item.Rarity == Rarity.Unique)
            ctx.Notes.Add("Unique modifiers are not modelled: the unique value reroll (x0.78-1.22) is not simulated.");
        return null;
    }

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex) => new()
    {
        SpecialOutcomes = Outcomes(ctx),
        Additions = Engine.Pool.CorruptionEnchantCandidates(ctx.Item),
        AdditionLabel = "Possible corruption enchantments (if that outcome is rolled)",
    };

    public override void Execute(ExecuteContext ctx)
    {
        var item = ctx.Result;
        item.Corrupted = true;
        // choosing a specific enchantment implies the enchantment outcome
        var chosenEnchant = ctx.Choice?.AddModIds.FirstOrDefault();
        var outcome = chosenEnchant != null ? Enchant : OutcomeId(CraftingEngine.PickOutcome(ctx, Outcomes(ctx)), ctx.Item);
        switch (outcome)
        {
            case Enchant:
                Engine.AddCorruptionEnchant(ctx, chosenEnchant);
                break;
            case Reroll:
                RerollMods(ctx);
                break;
            case SocketOrQuality when GetsSocket(item):
                item.Sockets++;
                ctx.Details.Add($"Added a socket ({item.Sockets} sockets).");
                break;
            case SocketOrQuality:
                item.Quality = ctx.Rng.Next(Engine.A.VaalQualityCap - item.Quality) + item.Quality + 1;
                ctx.Details.Add($"Quality increased to {item.Quality}%.");
                break;
            default:
                ctx.Details.Add("No change.");
                break;
        }
        ctx.Details.Add("Item is now Corrupted.");
    }

    private void RerollMods(ExecuteContext ctx)
    {
        int max = Math.Min(Engine.A.VaalRerollMaxMods, CraftingEngine.Removable(ctx.Result, OmenEffects.None).Count);
        int count = Math.Min(max, Engine.A.VaalRerollMinMods + ctx.Rng.Next(Engine.A.VaalRerollMaxMods - Engine.A.VaalRerollMinMods + 1));
        for (int i = 0; i < count; i++)
        {
            var removed = Engine.RemoveOne(ctx, CraftingEngine.Removable(ctx.Result, OmenEffects.None), null);
            if (!Engine.AddOne(ctx, removed.Mod.Affix, null, OmenEffects.None)) ctx.Details.Add($"No {removed.Mod.Affix.ToString().ToLower()} could be rolled in its place.");
        }
    }
}
