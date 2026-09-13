using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>
/// Orbs of Sacrifice (Yaomac's, Kopec's, Kamasa's, Yugul's): upgrade a corruption enchantment to its stronger version
/// and remove a random modifier. A Twice-Corrupted item can have each of its two enchantments upgraded once.
/// </summary>
internal sealed class SacrificeOperation : CraftOperation
{
    public SacrificeOperation(CraftingEngine engine) : base(engine, CurrencyOps.Sacrifice) { }

    public override bool RequiresCorrupted => true;

    /// <summary>An enchantment on the item that has a stronger version on this base.</summary>
    private sealed record Upgrade(int Index, ItemMod Enchant, ModDef Mod);

    private List<Upgrade> Upgradable(Item item) =>
        item.CorruptionEnchants
            .Select(e => e.Mod.Def != null && Engine.Data.CorruptionUpgradeFor(e.Mod.Def, item.Base, item.ItemClass) is { } upgrade ? new Upgrade(e.Index, e.Mod, upgrade) : null)
            .OfType<Upgrade>()
            .ToList();

    public override Applicability? Check(CraftContext ctx)
    {
        if (Upgradable(ctx.Item).Count == 0) return Applicability.No("The item has no corruption enchantment that can be upgraded.");
        if (CraftingEngine.Removable(ctx.Item, OmenEffects.None).Count == 0)
            ctx.Notes.Add("The item has no removable modifier; assumption: the enchantment is still upgraded (UNVERIFIED).");
        return null;
    }

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex)
    {
        var upgrades = Upgradable(ctx.Item);
        return new StepPreview
        {
            RemoveCount = 1,
            Removals = CraftingEngine.Removable(ctx.Item, OmenEffects.None),
            Additions = upgrades.Select(u => new ModCandidate { Mod = u.Mod, Weight = 1, Probability = 1.0 / upgrades.Count }).ToList(),
            AdditionLabel = "Enchantment upgrade",
            Notes = { upgrades.Count > 1 ? "Assumption: with two upgradable enchantments one is picked at random (UNVERIFIED)." : $"Upgrades: {upgrades[0].Enchant.DisplayText()}" },
        };
    }

    public override void Execute(ExecuteContext ctx)
    {
        var chosenRemoval = CraftingEngine.ChosenRemovals(ctx).FirstOrDefault();
        var upgrades = Upgradable(ctx.Result);
        var chosenId = ctx.Choice?.AddModIds.FirstOrDefault();
        var upgrade = CraftingEngine.Pick(ctx.Rng, upgrades, _ => 1.0, chosenId != null ? u => u.Mod.Id == chosenId : null, "The chosen enchantment cannot be upgraded.");

        var upgraded = ctx.Result.ReplaceMod(upgrade.Index, upgrade.Mod, ModKind.CorruptedImplicit, CraftingEngine.RollValues(upgrade.Mod, ctx.Rng), ctx.Currency.Name);
        ctx.Details.Add($"Upgraded {upgrade.Enchant.DisplayText()}  ->  {upgraded.DisplayText()}");

        var removable = CraftingEngine.Removable(ctx.Result, OmenEffects.None);
        if (removable.Count > 0) Engine.RemoveOne(ctx, removable, chosenRemoval);
    }
}
