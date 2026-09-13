using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine;
using POE2Crafting.Core.Items;
using Xunit;

namespace POE2Crafting.Tests;

public class DesecrationTests
{
    private const string Wand = "Siphoning Wand";

    private static CraftResult Desecrate(Item item, string bone = "Preserved Jawbone", string? omen = null, int seed = 1) =>
        TestData.Engine!.Execute(item, TestData.Action(bone, omen), new Rng(seed));

    [SkippableFact]
    public void Necromancy_and_boss_omen_work_together()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var action = TestData.Action("Preserved Jawbone", "Omen of Sinistral Necromancy", "Omen of the Blackblooded");
        Assert.Equal("Preserved Jawbone + Omen of Sinistral Necromancy + Omen of the Blackblooded", action.DisplayName);

        for (int seed = 0; seed < 5; seed++)
        {
            var result = TestData.Engine!.Execute(TestData.NewItem(Wand, Rarity.Rare).WithAffixes(1, 1), action, new Rng(seed));
            Assert.True(result.Applied, result.Summary);
            var (mod, index) = Unrevealed(result.Item);
            Assert.Equal(AffixType.Prefix, mod.Affix);
            Assert.All(TestData.Engine.RevealPool(result.Item, index), c => Assert.Contains("kurgal_mod", c.Mod.ModTags));
        }
    }

    [SkippableTheory]
    [InlineData("Omen of Sinistral Necromancy", "Omen of Dextral Necromancy", "contradict")]
    [InlineData("Omen of the Sovereign", "Omen of the Liege", "cannot be combined")]
    [InlineData("Omen of Putrefaction", "Omen of Dextral Necromancy", "cannot be combined")]
    [InlineData("Omen of Sinistral Necromancy", "Omen of Sinistral Exaltation", "does not affect")]
    public void Contradicting_or_unrelated_omens_are_refused(string first, string second, string reason)
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var check = TestData.Engine!.Check(TestData.NewItem(Wand, Rarity.Rare).WithAffixes(1, 1), TestData.Action("Preserved Jawbone", first, second));
        Assert.False(check.Ok);
        Assert.Contains(reason, check.Reason);
    }

    private static (ItemMod Mod, int Index) Unrevealed(Item item) => Assert.Single(item.UnrevealedMods);

    [SkippableFact]
    public void Bone_adds_an_unrevealed_desecrated_mod_into_a_free_slot()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var result = Desecrate(TestData.NewItem(Wand, Rarity.Rare).WithAffixes(2, 2));

        Assert.True(result.Applied, result.Summary);
        var (mod, _) = Unrevealed(result.Item);
        Assert.Equal(ModKind.Desecrated, mod.Kind);
        Assert.Equal(5, result.Item.AffixCount);
        Assert.Contains("Unrevealed Desecrated", mod.DisplayText());
    }

    [SkippableFact]
    public void Full_item_loses_a_mod_and_the_unrevealed_mod_takes_its_affix_type()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        for (int seed = 0; seed < 10; seed++)
        {
            var result = Desecrate(TestData.NewItem(Wand, Rarity.Rare).WithAffixes(3, 3), seed: seed);
            Assert.Equal(3, result.Item.PrefixCount);
            Assert.Equal(3, result.Item.SuffixCount);
            Assert.Single(result.Item.UnrevealedMods);
        }
    }

    [SkippableFact]
    public void Item_with_desecrated_mod_cannot_be_desecrated_again()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var once = Desecrate(TestData.NewItem(Wand, Rarity.Rare).WithAffixes(1, 1)).Item;
        var check = TestData.Engine!.Check(once, TestData.Action("Preserved Jawbone"));
        Assert.False(check.Ok);
        Assert.Contains("cannot be desecrated again", check.Reason);
    }

    [SkippableTheory]
    [InlineData("Preserved Rib", "can only be used on Armour")]
    [InlineData("Gnawed Jawbone", "up to item level 64")]
    public void Bone_restrictions_are_checked(string bone, string reason)
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var check = TestData.Engine!.Check(TestData.NewItem(Wand, Rarity.Rare, itemLevel: 82).WithAffixes(1, 1), TestData.Action(bone));
        Assert.False(check.Ok);
        Assert.Contains(reason, check.Reason);
    }

    [SkippableFact]
    public void Sovereign_omen_restricts_the_reveal_pool_to_ulaman_mods()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var item = Desecrate(TestData.NewItem(Wand, Rarity.Rare).WithAffixes(1, 1), omen: "Omen of the Sovereign").Item;
        var (mod, index) = Unrevealed(item);

        var pool = TestData.Engine!.RevealPool(item, index);
        Assert.NotEmpty(pool);
        Assert.All(pool, c => Assert.Contains("ulaman_mod", c.Mod.ModTags));
        Assert.All(pool, c => Assert.Equal(mod.Affix, c.Mod.AffixType));
        Assert.Equal(1.0, pool.Sum(c => c.Probability), 6);
    }

    [SkippableFact]
    public void Reveal_offers_distinct_options_and_replaces_the_unrevealed_mod_in_place()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var item = Desecrate(TestData.NewItem(Wand, Rarity.Rare).WithAffixes(2, 2)).Item;
        var (_, index) = Unrevealed(item);

        var options = TestData.Engine!.RollRevealOptions(item, index, new Rng(5));
        Assert.Equal(Math.Min(TestData.Data!.Config.Assumptions.RevealOptionCount, TestData.Engine.RevealPool(item, index).Count), options.Count);
        Assert.Equal(options.Count, options.Select(o => o.Id).Distinct().Count());

        var revealed = TestData.Engine.Reveal(item, index, options[0].Id, new Rng(5)).Item;
        var mod = revealed.Mods[index];
        Assert.False(mod.Unrevealed);
        Assert.Equal(ModKind.Desecrated, mod.Kind);
        Assert.Equal(options[0].Id, mod.Def!.Id);
        Assert.Equal(item.AffixCount, revealed.AffixCount);
    }

    [SkippableFact]
    public void Putrefaction_replaces_all_mods_and_corrupts()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var result = Desecrate(TestData.NewItem(Wand, Rarity.Rare).WithAffixes(2, 1), omen: "Omen of Putrefaction");
        Assert.True(result.Item.Corrupted);
        Assert.Equal(TestData.Data!.Config.Assumptions.PutrefactionUnrevealedCount, result.Item.UnrevealedMods.Count());
        Assert.Equal(result.Item.AffixCount, result.Item.UnrevealedMods.Count());
    }

    [SkippableFact]
    public void Omen_of_light_annuls_only_desecrated_mods()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var item = Desecrate(TestData.NewItem(Wand, Rarity.Rare).WithAffixes(2, 2)).Item;
        var result = TestData.Engine!.Execute(item, TestData.Action("Orb of Annulment", "Omen of Light"), new Rng(1));
        Assert.Empty(result.Item.UnrevealedMods);
        Assert.Equal(4, result.Item.AffixCount);
    }
}
