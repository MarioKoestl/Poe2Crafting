using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>
/// Well of Souls (synthetic currency, <see cref="GameData.WellOfSouls"/>): reveal an unrevealed desecrated modifier as one of the modifiers its bone
/// and omen allow. In game it offers a few options to pick from (<see cref="RollOptions"/>; Omen of Abyssal Echoes rerolls them once);
/// a manual choice picks the modifier (and with several unrevealed modifiers which one), a random use picks one by weight.
/// </summary>
internal sealed class RevealOperation : CraftOperation
{
    public RevealOperation(CraftingEngine engine) : base(engine, CurrencyOps.Reveal) { }

    /// <summary>Putrefaction corrupts the item and leaves unrevealed modifiers behind.</summary>
    public override bool WorksOnCorrupted => true;

    public override bool AcceptsOmen(CraftContext ctx, OmenDef omen) => omen.Effect == OmenEffects.RerollRevealOnce;

    private static List<RemovalCandidate> Unrevealed(Item item) => RemovalCandidate.UniformOf(item, m => m.Unrevealed);

    public override Applicability? Check(CraftContext ctx)
    {
        if (!ctx.Item.UnrevealedMods.Any()) return Applicability.No("The item has no unrevealed desecrated modifier.");
        ctx.Notes.Add($"The Well of Souls offers {Assumptions.RevealOptionCount} options: at least {Assumptions.RevealGuaranteedExclusiveOptions} exclusive Lich modifier, "
                      + $"each other one a regular modifier of the same type with {Assumptions.RevealRegularOptionChance:P0} (assumption, config revealRegularOptionChance)"
                      + (Assumptions.RevealBossOmenOnlyLichModifiers ? "; with a boss omen only that Lich's modifiers" : "")
                      + ". Roll them, or choose any modifier below (percentages: chance to be offered).");
        if (ctx.OmenIs(OmenEffects.RerollRevealOnce)) ctx.Notes.Add("Omen of Abyssal Echoes: the options can be rerolled once.");
        return null;
    }

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex)
    {
        var unrevealed = Unrevealed(ctx.Item);
        int index = unrevealed.FirstOrDefault(r => r.Index == forcedRemovalIndex)?.Index ?? unrevealed[0].Index;
        var (exclusive, regular) = Engine.RevealOffers(ctx.Item, index);
        return new StepPreview
        {
            Removals = unrevealed.Count > 1 ? unrevealed : new(), RemovalLabel = "Unrevealed modifier to reveal", TwoStepChoice = unrevealed.Count > 1,
            Additions = exclusive, AdditionLabel = Engine.RevealExclusiveLabel(ctx.Item, index),
            OtherAdditions = regular, OtherAdditionLabel = DesecrateOperation.RegularLabel,
        };
    }

    public override void Execute(ExecuteContext ctx)
    {
        var unrevealed = Unrevealed(ctx.Result);
        var chosen = CraftingEngine.ChosenRemovals(ctx).FirstOrDefault();
        var target = chosen == null ? unrevealed[0]
            : unrevealed.FirstOrDefault(r => ReferenceEquals(r.Mod, chosen)) ?? throw new InvalidChoiceException("The chosen modifier is not unrevealed.");
        ModDef pick;
        if (ctx.Choice?.AddModIds.FirstOrDefault() is { } chosenId)
            pick = Engine.RevealPool(ctx.Result, target.Index).FirstOrDefault(c => c.Mod.Id == chosenId)?.Mod
                   ?? throw new InvalidChoiceException("The chosen modifier cannot be revealed from this desecrated modifier.");
        else
        {
            // a random reveal: the offered options, one of them taken at random
            var options = Engine.RollRevealOptions(ctx.Result, target.Index, ctx.Rng);
            if (options.Count == 0) throw new InvalidChoiceException("Nothing can be revealed from this desecrated modifier.");
            pick = options[ctx.Rng.Next(options.Count)];
        }
        var revealed = ctx.Result.ReplaceMod(target.Index, pick, ModKind.Desecrated, CraftingEngine.ValuesFor(ctx.Choice, pick, ctx.Rng), target.Mod.SourceName);
        ctx.Details.Add($"Revealed {Engine.Describe(revealed, ctx.Result)}");
    }
}
