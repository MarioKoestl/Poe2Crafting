using POE2Crafting.Core.Engine.Operations;
namespace POE2Crafting.Tests;

public class DesecrationTests
{

    private static CraftResult Desecrate(Item item, string bone = "Preserved Jawbone", string? omen = null, int seed = 1) =>
        TestData.Apply(item, bone, seed: seed, omens: omen);

    [DataFact]
    public void Necromancy_and_boss_omen_work_together()
    {
        var action = TestData.Action("Preserved Jawbone", "Omen of Sinistral Necromancy", "Omen of the Blackblooded");
        Assert.Equal("Preserved Jawbone + Omen of Sinistral Necromancy + Omen of the Blackblooded", action.DisplayName);

        for (int seed = 0; seed < 5; seed++)
        {
            var result = TestData.Engine!.Execute(TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(1, 1), action, new Rng(seed));
            Assert.True(result.Applied, result.Summary);
            var (mod, index) = Unrevealed(result.Item);
            Assert.Equal(AffixType.Prefix, mod.Affix);
            Assert.All(TestData.Engine.RevealOffers(result.Item, index).Exclusive, c => Assert.Contains("kurgal_mod", c.Mod.ModTags));
        }
    }

    [DataTheory]
    [InlineData("Omen of Sinistral Necromancy", "Omen of Dextral Necromancy", "contradict")]
    [InlineData("Omen of the Sovereign", "Omen of the Liege", "cannot be combined")]
    [InlineData("Omen of Putrefaction", "Omen of Dextral Necromancy", "cannot be combined")]
    [InlineData("Omen of Sinistral Necromancy", "Omen of Sinistral Exaltation", "does not affect")]
    public void Contradicting_or_unrelated_omens_are_refused(string first, string second, string reason)
    {
        var check = TestData.Engine!.Check(TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(1, 1), TestData.Action("Preserved Jawbone", first, second));
        Assert.False(check.Ok);
        Assert.Contains(reason, check.Reason);
    }

    private static (ItemMod Mod, int Index) Unrevealed(Item item) => Assert.Single(item.UnrevealedMods);

    [DataFact]
    public void Bone_adds_an_unrevealed_desecrated_mod_into_a_free_slot()
    {
        var result = Desecrate(TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(2, 2));

        Assert.True(result.Applied, result.Summary);
        var (mod, _) = Unrevealed(result.Item);
        Assert.Equal(ModKind.Desecrated, mod.Kind);
        Assert.Equal(5, result.Item.AffixCount);
        Assert.Contains("Unrevealed Desecrated", mod.DisplayText());
    }

    [DataFact]
    public void Full_item_loses_a_mod_and_the_unrevealed_mod_takes_its_affix_type()
    {
        for (int seed = 0; seed < 10; seed++)
        {
            var result = Desecrate(TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(3, 3), seed: seed);
            Assert.Equal(3, result.Item.PrefixCount);
            Assert.Equal(3, result.Item.SuffixCount);
            Assert.Single(result.Item.UnrevealedMods);
        }
    }

    [DataFact]
    public void Item_with_desecrated_mod_cannot_be_desecrated_again()
    {
        var once = Desecrate(TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(1, 1)).Item;
        var check = TestData.Engine!.Check(once, TestData.Action("Preserved Jawbone"));
        Assert.False(check.Ok);
        Assert.Contains("cannot be desecrated again", check.Reason);
    }

    [DataTheory]
    [InlineData("Preserved Rib", "can only be used on Armour")]
    [InlineData("Gnawed Jawbone", "up to item level 64")]
    public void Bone_restrictions_are_checked(string bone, string reason)
    {
        var check = TestData.Engine!.Check(TestData.NewItem(TestBases.Wand, Rarity.Rare, itemLevel: 82).WithAffixes(1, 1), TestData.Action(bone));
        Assert.False(check.Ok);
        Assert.Contains(reason, check.Reason);
    }

    [DataFact]
    public void Sovereign_omen_restricts_the_reveal_pool_to_ulaman_mods()
    {
        var item = Desecrate(TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(1, 1), omen: "Omen of the Sovereign").Item;
        var (mod, index) = Unrevealed(item);

        var (exclusive, regular) = TestData.Engine!.RevealOffers(item, index);
        Assert.NotEmpty(exclusive);
        // boss omen: every option is an Ulaman modifier, no regular ones (poe2db note, Mario in game; config revealBossOmenOnlyLichModifiers)
        Assert.All(exclusive, c => Assert.Contains("ulaman_mod", c.Mod.ModTags));
        Assert.Empty(regular);
        Assert.All(exclusive, c => Assert.Equal(mod.Affix, c.Mod.AffixType));
        var offered = Math.Min(1.0, (double)TestData.Data!.Config.Assumptions.RevealOptionCount / exclusive.Count);
        Assert.All(exclusive, c => Assert.Equal(offered, c.Probability, 6));
        Assert.All(TestData.Engine.RollRevealOptions(item, index, new Rng(3)), o => Assert.Contains("ulaman_mod", o.ModTags));
    }

    [DataFact]
    public void Reveal_offers_distinct_options_and_replaces_the_unrevealed_mod_in_place()
    {
        var item = Desecrate(TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(2, 2)).Item;
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

    [DataFact]
    public void Putrefaction_replaces_all_mods_and_corrupts()
    {
        var result = Desecrate(TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(2, 1), omen: "Omen of Putrefaction");
        Assert.True(result.Item.Corrupted);
        Assert.Equal(TestData.Data!.Config.Assumptions.PutrefactionUnrevealedCount, result.Item.UnrevealedMods.Count());
        Assert.Equal(result.Item.AffixCount, result.Item.UnrevealedMods.Count());
    }

    [DataFact]
    public void Omen_of_light_annuls_only_desecrated_mods()
    {
        var item = Desecrate(TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(2, 2)).Item;
        var result = TestData.Engine!.Execute(item, TestData.Action("Orb of Annulment", "Omen of Light"), new Rng(1));
        Assert.Empty(result.Item.UnrevealedMods);
        Assert.Equal(4, result.Item.AffixCount);
    }

    [DataFact]
    public void Desecration_always_leaves_an_unrevealed_modifier_and_lists_what_it_can_become()
    {
        var item = TestData.NewItem(TestBases.Amulet, Rarity.Rare).WithAffixes(1, 1);
        var action = TestData.Action("Altered Collarbone", "Omen of the Blackblooded");
        var preview = TestData.Engine!.Preview(item, action);
        Assert.False(preview.AdditionsChoosable);
        Assert.All(preview.Additions, c => Assert.Contains("kurgal_mod", c.Mod.ModTags));
        Assert.Empty(preview.OtherAdditions);

        var desecrated = TestData.Engine.Execute(item, action, new Rng(2), new ManualChoice { AddModIds = { preview.Additions[0].Mod.Id } }).Item;
        var (_, index) = Unrevealed(desecrated);
        Assert.All(TestData.Engine.RevealOffers(desecrated, index).Exclusive, c => Assert.Contains("kurgal_mod", c.Mod.ModTags));
    }

    [DataFact]
    public void Well_of_souls_reveals_like_a_currency_and_takes_abyssal_echoes()
    {
        var item = Desecrate(TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(1, 1)).Item;
        var (_, index) = Unrevealed(item);
        var wellOfSouls = TestData.Action("Well of Souls", "Omen of Abyssal Echoes");
        Assert.True(TestData.Engine!.Check(item, wellOfSouls).Ok);
        Assert.False(TestData.Engine.Check(item, TestData.Action("Preserved Jawbone", "Omen of Abyssal Echoes")).Ok);

        var preview = TestData.Engine.Preview(item, wellOfSouls);
        var wanted = preview.Additions.Last().Mod;
        var revealed = TestData.Engine.Execute(item, wellOfSouls, new Rng(1), new ManualChoice { AddModIds = { wanted.Id } }).Item;
        Assert.Equal(wanted.Id, revealed.Mods[index].ModId);
        Assert.Empty(revealed.UnrevealedMods);
        Assert.False(TestData.Engine.Check(revealed, wellOfSouls).Ok);

        // the omen is used up, the Well of Souls is not
        var step = new CraftingStrategy().NewStep(wellOfSouls, "reveal", 1, revealed);
        Assert.Equal(new[] { "Omen of Abyssal Echoes" }, step.Materials.Keys);
    }

    [DataFact]
    public void Well_of_souls_offers_one_exclusive_lich_modifier_and_regular_ones()
    {
        // Mario's ring: in game the options were Cold Resistance, Mana Regeneration and an Amanamu "Remnants" modifier
        const string text = @"Item Class: Rings
Rarity: Rare
Sol Knot
Sapphire Ring
--------
Item Level: 81
--------
{ Implicit Modifier — Elemental, Cold, Resistance }
+20(20-30)% to Cold Resistance
--------
{ Prefix Modifier ""Robust"" (Tier: 3) — Life }
+76(70-84) to maximum Life
{ Prefix Modifier ""Fleet"" (Tier: 6) — Evasion }
+70(52-79) to Evasion Rating
{ Prefix Modifier ""Darkened"" (Tier: 3) — Damage, Chaos }
20(18-22)% increased Chaos Damage
{ Suffix Modifier ""of Fury"" — Caster, Critical }
21(18-21)% increased Critical Spell Damage Bonus
{ Suffix Modifier ""of Calamity"" — Caster, Critical }
18(16-18)% increased Critical Hit Chance for Spells
{ Suffix Modifier ""of the Veil"" }
Desecrated Suffix
";
        var ring = ItemParser.Parse(text, TestData.Data!);
        var (_, index) = Unrevealed(ring);
        var pool = TestData.Engine!.RevealPool(ring, index);
        Assert.Contains(pool, c => c.Mod.Category == ModCategories.Normal && c.Mod.Text.Contains("to Cold Resistance"));
        Assert.Contains(pool, c => c.Mod.Category == ModCategories.Normal && c.Mod.Text.Contains("Mana Regeneration"));
        Assert.Contains(pool, c => c.Mod.Category == ModCategories.Desecrated && c.Mod.Text.Contains("Remnants can be collected"));
        Assert.All(pool, c => Assert.Equal(AffixType.Suffix, c.Mod.AffixType));

        for (int seed = 0; seed < 10; seed++)
        {
            var options = TestData.Engine.RollRevealOptions(ring, index, new Rng(seed));
            Assert.Equal(TestData.Data!.Config.Assumptions.RevealOptionCount, options.Count);
            Assert.InRange(options.Count(o => o.Category == ModCategories.Desecrated), TestData.Data.Config.Assumptions.RevealGuaranteedExclusiveOptions, options.Count);
            Assert.Equal(options.Count, options.Select(o => o.Id).Distinct().Count());
        }

        // a random reveal still gives a desecrated-kind modifier from the pool
        var revealed = TestData.Apply(ring, "Well of Souls").Item.Mods[index];
        Assert.False(revealed.Unrevealed);
        Assert.Equal(ModKind.Desecrated, revealed.Kind);
        Assert.Contains(pool, c => c.Mod.Id == revealed.ModId);
    }

    [DataFact]
    public void Bone_preview_shows_only_the_affix_type_the_unrevealed_mod_can_get()
    {
        // 3 prefixes and 2 suffixes: only a suffix slot is free
        var amulet = TestData.NewItem(TestBases.Amulet, Rarity.Rare).WithAffixes(3, 2);
        var preview = TestData.Engine!.Preview(amulet, TestData.Action("Preserved Collarbone"));
        Assert.Equal(new[] { DesecrateOperation.OutcomeName(AffixType.Suffix) }, preview.SpecialOutcomes.Keys);
        Assert.NotEmpty(preview.Additions);
        Assert.NotEmpty(preview.OtherAdditions);
        Assert.All(preview.Additions.Concat(preview.OtherAdditions), c => Assert.Equal(AffixType.Suffix, c.Mod.AffixType));
        Assert.All(preview.Additions, c => Assert.NotEqual(ModCategories.Normal, c.Mod.Category));
        Assert.All(preview.OtherAdditions, c => Assert.Equal(ModCategories.Normal, c.Mod.Category));

        // with Omen of the Blackblooded: only Kurgal suffixes
        Assert.Contains("at least 1 of the 3 options", preview.AdditionLabel);
        var kurgal = TestData.Engine.Preview(amulet, TestData.Action("Preserved Collarbone", "Omen of the Blackblooded"));
        Assert.StartsWith("Kurgal modifiers — all 3 options", kurgal.AdditionLabel);
        Assert.All(kurgal.Additions, c => Assert.Contains("kurgal_mod", c.Mod.ModTags));
        Assert.All(kurgal.Additions, c => Assert.Equal(AffixType.Suffix, c.Mod.AffixType));
        Assert.Empty(kurgal.OtherAdditions);

        // full item: prefix or suffix (the removed mod's slot)
        var full = TestData.NewItem(TestBases.Amulet, Rarity.Rare).WithAffixes(3, 3);
        var fullPreview = TestData.Engine.Preview(full, TestData.Action("Preserved Collarbone", "Omen of the Blackblooded"));
        Assert.Contains(fullPreview.Additions, c => c.Mod.AffixType == AffixType.Prefix);
        Assert.Contains(fullPreview.Additions, c => c.Mod.AffixType == AffixType.Suffix);
    }
}
