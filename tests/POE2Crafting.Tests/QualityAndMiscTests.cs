namespace POE2Crafting.Tests;

public class QualityAndMiscTests
{

    [DataFact]
    public void Without_affixes_keeps_everything_that_is_not_an_editable_affix()
    {
        var item = TestData.NewItem(TestBases.Wand, Rarity.Rare, withImplicit: true).WithAffixes(2, 1);
        item.Quality = 12;
        item.Corrupted = true;
        item.Affixes.First().Fractured = true;
        item.Mods.Add(new ItemMod { Kind = ModKind.Desecrated, Affix = AffixType.Suffix, Unrevealed = true });

        var stripped = item.WithoutAffixes();
        Assert.Equal(12, stripped.Quality);
        Assert.True(stripped.Corrupted);
        Assert.Contains(stripped.Mods, m => m.Kind == ModKind.Implicit);
        Assert.Empty(stripped.UnrevealedMods); // the draft's selection takes unrevealed mods over and adds them again
        Assert.Equal(4, item.Affixes.Count()); // the original is untouched

        var draft = new POE2Crafting.Core.Drafting.ItemDraft(TestData.Data!);
        draft.LoadFrom(item, copyValues: true);
        var edited = draft.BuildItem()!;
        Assert.Single(edited.UnrevealedMods);
        Assert.Equal((12, true), (edited.Quality, edited.Corrupted));
    }

    /// <summary>
    /// Minimum Modifier Level works per modifier type (tier group), not as a flat cut: a type whose tiers all sit below the minimum
    /// still offers its highest tier. On a ring the mana leech suffix tops out at level 38, so a Perfect Exalted Orb can still roll it.
    /// </summary>
    [DataFact]
    public void Minimum_modifier_level_filters_tiers_per_type_but_never_drops_a_type_completely()
    {
        var ring = TestData.NewItem(TestBases.Ring, Rarity.Rare, itemLevel: 81);
        var preview = TestData.Engine!.Preview(ring, TestData.Action("Perfect Exalted Orb"));
        Assert.True(preview.Applicability.Ok, preview.Applicability.Reason);
        int minLevel = TestData.Currency("Perfect Exalted Orb").MinModLevel!.Value;

        // every tier group offers only its tiers at or above the minimum ...
        foreach (var group in preview.Additions.GroupBy(c => ModTiers.TierGroupKey(c.Mod)))
        {
            var all = TestData.Pool!.AllForBase(ring).Where(m => ModTiers.TierGroupKey(m) == group.Key).ToList();
            if (all.Any(m => m.Level >= minLevel)) Assert.All(group, c => Assert.True(c.Mod.Level >= minLevel, c.Mod.Text));
            // ... unless none of them reaches it: then the highest tier of the type is offered instead
            else Assert.Equal(all.Max(m => m.Level), Assert.Single(group).Mod.Level);
        }

        var leech = Assert.Single(preview.Additions, c => c.Mod.Text.Contains("Physical Attack Damage as Mana"));
        Assert.Equal(("of the Arid", 38), (leech.Mod.Name, leech.Mod.Level));
    }

    [DataFact]
    public void Unrevealed_desecrated_modifiers_can_be_added_in_the_composer()
    {
        var draft = new POE2Crafting.Core.Drafting.ItemDraft(TestData.Data!) { Rarity = Rarity.Rare };
        draft.BaseName = TestBases.Wand;
        var selection = draft.Selection;

        selection.AddUnrevealed(AffixType.Suffix, Rarity.Rare);
        draft.Refresh();
        var (mod, _) = Assert.Single(draft.BuildItem()!.UnrevealedMods);
        Assert.Equal((ModKind.Desecrated, AffixType.Suffix, true), (mod.Kind, mod.Affix, mod.IsAffix));

        // they occupy a slot like any other affix
        while (selection.CanAddUnrevealed(Rarity.Rare, AffixType.Suffix)) selection.AddUnrevealed(AffixType.Suffix, Rarity.Rare);
        draft.Refresh();
        Assert.Equal(selection.Max(Rarity.Rare, AffixType.Suffix), selection.Count(AffixType.Suffix));
        selection.AddUnrevealed(AffixType.Suffix, Rarity.Rare);
        Assert.Equal(selection.Max(Rarity.Rare, AffixType.Suffix), selection.Count(AffixType.Suffix));

        // a magic item only holds one per type
        draft.Rarity = Rarity.Magic;
        Assert.Equal(1, selection.Count(AffixType.Suffix));
    }

    [DataFact]
    public void Only_one_modifier_can_be_fractured_and_divine_keeps_its_values()
    {
        var item = TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(2, 2);
        var fractured = TestData.Apply(item, "Fracturing Orb").Item;
        var locked = Assert.Single(fractured.Affixes, m => m.Fractured);
        Assert.Contains("only one per item", TestData.Engine!.Check(fractured, TestData.Action("Fracturing Orb")).Reason);

        locked.Values = locked.Def!.Ranges.Select(r => r[0]).ToList();
        for (int seed = 0; seed < 5; seed++)
            Assert.Equal(locked.Values, Assert.Single(TestData.Apply(fractured, "Divine Orb", seed: seed).Item.Affixes, m => m.Fractured).Values);
    }

    [DataFact]
    public void Etcher_adds_quality_per_rarity_up_to_the_maximum()
    {
        var item = TestData.NewItem(TestBases.Wand, Rarity.Rare);
        int perUse = TestData.Data!.Config.Assumptions.QualityPerUse[Rarity.Rare];
        Assert.Equal(perUse, TestData.Apply(item, "Arcanist's Etcher").Item.Quality);

        item.Quality = 20;
        Assert.Contains("maximum", TestData.Engine!.Check(item, TestData.Action("Arcanist's Etcher")).Reason);
        Assert.Contains("can only be used on", TestData.Engine.Check(item, TestData.Action("Blacksmith's Whetstone")).Reason);
    }

    [DataFact]
    public void Infuser_exceeds_the_maximum_and_corrupts_only_above_it()
    {
        var item = TestData.NewItem(TestBases.Wand, Rarity.Rare);
        item.Quality = 20;
        var atMax = TestData.Engine!.Preview(item, TestData.Action("Vaal Arcanist's Infuser"));
        Assert.Equal(1.0, atMax.SpecialOutcomes["Not corrupted"], 6);

        item.Quality = 24;
        var above = TestData.Engine.Preview(item, TestData.Action("Vaal Arcanist's Infuser")).SpecialOutcomes;
        Assert.Equal(0.20, above["Item corrupted"], 6);

        var corrupted = TestData.Apply(item, "Vaal Arcanist's Infuser", choice: new ManualChoice { SpecialOutcome = "Item corrupted" }).Item;
        Assert.True(corrupted.Corrupted);
        Assert.Equal(25, corrupted.Quality);
    }

    [DataFact]
    public void Catalyst_sets_quality_type_and_replaces_other_types()
    {
        var ring = TestData.NewItem(TestBases.Ring, Rarity.Rare);
        var life = TestData.Apply(ring, "Flesh Catalyst").Item;
        Assert.Equal("Life", life.QualityType);
        Assert.Equal(5, life.Quality);
        Assert.Equal("life", life.QualityTag);

        var mana = TestData.Apply(life, "Neural Catalyst").Item;
        Assert.Equal("Mana", mana.QualityType);
        Assert.Equal(10, mana.Quality);

        Assert.Contains("can only be used on", TestData.Engine!.Check(TestData.NewItem(TestBases.Wand), TestData.Action("Flesh Catalyst")).Reason);
    }

    [DataFact]
    public void Catalysing_exaltation_biases_tagged_mods_and_consumes_quality()
    {
        var ring = TestData.NewItem(TestBases.Ring, Rarity.Rare).WithAffixes(1, 1);
        Assert.False(TestData.Engine!.Check(ring, TestData.Action("Exalted Orb", "Omen of Catalysing Exaltation")).Ok);

        ring.QualityType = "Life";
        ring.Quality = 20;
        double LifeShare(string? omen) => TestData.Engine!.Preview(ring, TestData.Action("Exalted Orb", omen)).Additions
            .Where(a => a.Mod.ModTags.Contains("life")).Sum(a => a.Probability);
        Assert.True(LifeShare("Omen of Catalysing Exaltation") > LifeShare(null));

        var result = TestData.Apply(ring, "Exalted Orb", omens: "Omen of Catalysing Exaltation").Item;
        Assert.Equal(0, result.Quality);
        Assert.Null(result.QualityType);
    }

    [DataFact]
    public void Artificers_orb_adds_sockets_up_to_the_base_limit()
    {
        var wand = TestData.NewItem(TestBases.Wand);
        wand.Sockets = 0;
        Assert.Equal(1, TestData.Apply(wand, "Artificer's Orb").Item.Sockets);

        wand.Sockets = wand.Base!.SocketLimit!.Value;
        Assert.Contains("maximum", TestData.Engine!.Check(wand, TestData.Action("Artificer's Orb")).Reason);
    }

    [DataFact]
    public void Extraction_destroys_the_item_and_lists_the_augments()
    {
        var wand = TestData.NewItem(TestBases.Wand);
        Assert.False(TestData.Engine!.Check(wand, TestData.Action("Orb of Extraction")).Ok);
        wand.Runes.Add("+1 to Level of all Spell Skills");
        var result = TestData.Apply(wand, "Orb of Extraction");
        Assert.True(result.Destroyed);
        Assert.Contains("+1 to Level of all Spell Skills", result.Summary);
    }

    [DataFact]
    public void Blazing_flux_turns_cold_resistance_into_an_equivalent_fire_resistance_with_a_new_roll()
    {
        var ring = TestData.NewItem(TestBases.Ring, Rarity.Rare);
        var cold = TestData.Pool!.AllForBase(ring).Where(m => m.Family == "ColdResistance").MaxBy(m => m.Level)!;
        ring.AddMod(cold, values: new() { ModText.Bounds(cold.Ranges[0]).Lo });

        var rolled = new HashSet<double>();
        Item? result = null;
        for (int seed = 0; seed < 20; seed++)
        {
            result = TestData.Apply(ring, "Blazing Flux", seed: seed).Item;
            var fire = Assert.Single(result.Affixes);
            Assert.Equal("FireResistance", fire.Def!.Family);
            Assert.Equal(cold.Level, fire.Def.Level);
            var (lo, hi) = ModText.Bounds(fire.Def.Ranges[0]);
            Assert.InRange(fire.Values[0], lo, hi);
            rolled.Add(fire.Values[0]);
        }
        // the lowest cold roll does not stay the lowest fire roll: the value is rolled anew
        Assert.True(rolled.Count > 1);

        Assert.False(TestData.Engine!.Check(result!, TestData.Action("Blazing Flux")).Ok);
    }

    [DataFact]
    public void A_flux_only_transforms_the_explicit_modifiers_and_leaves_every_implicit_line_alone()
    {
        // Tournament Mail carries "+(20-25)% to Lightning Resistance" as its base implicit
        var armour = TestData.NewItem("Tournament Mail", Rarity.Rare, withImplicit: true);
        var lightning = TestData.Pool!.AllForBase(armour).Where(m => m.Family == "LightningResistance").MaxBy(m => m.Level)!;
        var fire = TestData.Pool.AllForBase(armour).Where(m => m.Family == "FireResistance").MaxBy(m => m.Level)!;
        armour.AddMod(lightning);
        armour.AddMod(fire, ModKind.CorruptedImplicit);
        var implicitText = armour.Mods[0].DisplayText();

        var result = TestData.Apply(armour, "Chilling Flux").Item;

        Assert.Equal("ColdResistance", Assert.Single(result.Affixes).Def!.Family);
        Assert.Equal(implicitText, result.Mods[0].DisplayText());
        Assert.Equal("FireResistance", result.Mods.Single(m => m.Kind == ModKind.CorruptedImplicit).Def!.Family);
    }

    [DataFact]
    public void Flux_values_can_be_chosen_inside_the_new_tier()
    {
        var ring = TestData.NewItem(TestBases.Ring, Rarity.Rare);
        var lightning = TestData.Pool!.AllForBase(ring).Where(m => m.Family == "LightningResistance").MaxBy(m => m.Level)!;
        ring.AddMod(lightning, values: new() { ModText.Bounds(lightning.Ranges[0]).Lo });

        var reroll = Assert.Single(TestData.Engine!.Preview(ring, TestData.Action("Chilling Flux")).ValueRerolls);
        Assert.Equal("ColdResistance", reroll.Mod.Family);
        var best = reroll.Mod.Ranges.Select(r => ModText.Bounds(r).Hi).ToList();

        var cold = Assert.Single(TestData.Apply(ring, "Chilling Flux", new ManualChoice { Rerolls = new() { [reroll.Index] = best } }).Item.Affixes);
        Assert.Equal(reroll.Mod.Id, cold.ModId);
        Assert.Equal(best, cold.Values);
    }
}

public class GameVersionTests
{
    [DataFact]
    public void Omens_no_longer_in_the_game_are_not_offered_but_still_resolve()
    {
        var data = TestData.Data!;
        Assert.False(data.IsAvailable("Omen of Sinistral Coronation"));
        Assert.DoesNotContain(data.CraftingOmens, o => o.Name == "Omen of Sinistral Coronation");
        Assert.Contains(data.CraftingOmens, o => o.Name == "Omen of Whittling");
        // old projects and guides that used a removed omen still load
        Assert.NotNull(data.FindOmen("Omen of Sinistral Coronation"));
        Assert.All(data.Config.UnavailableItems, name => Assert.True(data.FindOmen(name) != null || data.FindCurrency(name) != null, $"{name} is not in the data"));
    }

    [DataFact]
    public void Omen_data_has_no_duplicate_or_nameless_entries()
    {
        var names = TestData.Data!.Omens.Select(o => o.Name).ToList();
        Assert.DoesNotContain("", names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
