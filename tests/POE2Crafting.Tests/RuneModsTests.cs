namespace POE2Crafting.Tests;

/// <summary>Runes that unlock modifier types, "Modifier magnitudes" lines, Aldur runes that transform elements, and socketed augments on items.</summary>
public class RuneModsTests
{
    private const string StaffText = """
        Item Class: Staff
        Rarity: Rare
        Onslaught Song
        Chiming Staff
        --------
        Sockets: S S S
        Item Level: 81
        --------
        Transforms all Cold and Lightning modifiers on the item into equivalent Fire modifiers (rune)
        --------
        { Fractured Suffix Modifier "of Thunder" (Tier: 1) }
        +7 to Level of all Lightning Spell Skills
        """;

    private static Item RareStaff() => TestData.NewItem(TestBases.Staff, Rarity.Rare, 82);

    private static ItemMod AddMod(Item item, string text, AffixType type) => item.AddMod(TestData.BestMod(item, text, type));

    [DataFact]
    public void Imported_staff_shows_the_socketed_passion_of_aldur()
    {
        var staff = ItemParser.Parse(StaffText, TestData.Data!);
        Assert.Equal(3, staff.Sockets);
        var rune = Assert.Single(staff.Runes);
        Assert.Equal("Passion of Aldur", TestData.Data!.FindAugmentByEffect(rune, staff.Base, staff.ItemClass)?.Name);
        Assert.Contains(staff.Affixes, m => m.Fractured && m.DisplayText() == "+7 to Level of all Lightning Spell Skills");
    }

    [DataFact]
    public void Thruds_might_lets_destruction_modifiers_roll()
    {
        var staff = RareStaff().WithAffixes(1, 1);
        var exalt = TestData.Action("Exalted Orb");
        static bool Destruction(ModCandidate c) => c.Mod.Category == ModCategories.Destruction;
        Assert.DoesNotContain(TestData.Engine!.Preview(staff, exalt).Additions, Destruction);

        staff.Sockets = 1;
        staff.Runes.Add("Can roll Destruction modifiers");
        var additions = TestData.Engine.Preview(staff, exalt).Additions;
        var fire = Assert.Single(additions, c => c.Mod.Text.Contains("Explicit Fire Modifier magnitudes"));
        Assert.Equal(TestData.Data!.Config.Assumptions.RuneUnlockedModWeight, fire.Weight);
        Assert.Equal(1, additions.Sum(c => c.Probability), 6);
    }

    [DataFact]
    public void Magnitude_modifiers_raise_the_values_of_matching_explicit_modifiers()
    {
        var staff = RareStaff();
        var lightningSkills = AddMod(staff, "to Level of all Lightning Spell Skills", AffixType.Suffix);
        var mana = AddMod(staff, "to maximum Mana", AffixType.Prefix);
        var magnitude = staff.AddMod(TestData.Data!.Mods.First(m => m.Id == "DestructionInfluenceLightningModifierEffect"), values: new() { 20 });

        Assert.Equal(7 * 1.2, lightningSkills.StatValues[0] * staff.ValueFactor(lightningSkills), 6);
        Assert.Equal(8, staff.EffectiveValues(lightningSkills)[0]); // 8.4 rounds down like catalyst quality
        Assert.Equal("+8 to Level of all Lightning Spell Skills", staff.EffectiveText(lightningSkills));
        Assert.False(staff.ValuesEnhanced(mana));
        Assert.False(staff.ValuesEnhanced(magnitude)); // magnitude lines don't raise themselves
    }

    [DataFact]
    public void Passion_of_aldur_turns_lightning_spell_skills_into_fire_but_keeps_fractured_ones()
    {
        var engine = TestData.Engine!;
        var passion = TestData.Action("Passion of Aldur");

        var staff = RareStaff();
        staff.Sockets = 1;
        AddMod(staff, "to Level of all Lightning Spell Skills", AffixType.Suffix);
        var socketed = engine.Execute(staff, passion, new Rng(1)).Item;
        Assert.Contains(socketed.Affixes, m => m.DisplayText() == "+7 to Level of all Fire Spell Skills");
        Assert.DoesNotContain(socketed.Affixes, m => m.DisplayText().Contains("Lightning"));

        // tried in game (Mario, 15.09.2026, 0.5.5): a fractured modifier is not transformed
        var fractured = RareStaff();
        fractured.Sockets = 1;
        AddMod(fractured, "to Level of all Lightning Spell Skills", AffixType.Suffix).Fractured = true;
        Assert.False(TestData.Data!.Config.Assumptions.AldurRuneTransformsFractured);
        var kept = engine.Execute(fractured, passion, new Rng(1)).Item;
        Assert.Contains(kept.Affixes, m => m.Fractured && m.DisplayText() == "+7 to Level of all Lightning Spell Skills");
    }

    [DataFact]
    public void Astrids_creativity_allows_a_second_crafted_modifier_that_stays_after_replacing_the_rune()
    {
        // Mario in game: socket Astrid's Creativity, craft a second crafted modifier, take the rune out again - both crafted modifiers stay
        var engine = TestData.Engine!;
        var wand = TestData.NewItem(TestBases.Wand, Rarity.Rare, 82).WithAffixes(2, 2);
        wand.Sockets = 1;
        var sorcery = TestData.Action("Perfect Essence of Sorcery");
        wand = engine.Execute(wand, sorcery, new Rng(1)).Item;
        Assert.Equal(1, wand.Affixes.Count(m => m.Kind == ModKind.Crafted));
        Assert.False(engine.Check(wand, TestData.Action("Perfect Essence of Battle")).Ok);

        wand = engine.Execute(wand, TestData.Action("Astrid's Creativity"), new Rng(1)).Item;
        Assert.Equal(2, TestData.Data!.Config.Assumptions.MaxCraftedMods(wand));
        var second = TestData.Data.AllCurrencies.Where(c => c.Op == CurrencyOps.Essence && c.Essence!.AddsCraftedMod && c.Name != sorcery.Currency.Name)
            .Select(c => CraftAction.Of(c)).First(a => engine.Check(wand, a).Ok);
        wand = engine.Execute(wand, second, new Rng(2)).Item;
        Assert.Equal(2, wand.Affixes.Count(m => m.Kind == ModKind.Crafted));

        // replacing the rune (it can't be retrieved, poe2db) keeps both crafted modifiers
        var other = TestData.Data.AugmentsFor(wand.Base, wand.ItemClass).First(a => a.Name != "Astrid's Creativity" && !a.Effects.Any(e => e.Text.Contains("Crafted")));
        var replaced = engine.Execute(wand, TestData.Action(other.Name), new Rng(1)).Item;
        Assert.Equal(1, TestData.Data.Config.Assumptions.MaxCraftedMods(replaced));
        Assert.Equal(2, replaced.Affixes.Count(m => m.Kind == ModKind.Crafted));
    }

    [DataFact]
    public void Aldur_runes_leave_modifier_magnitude_lines_alone()
    {
        // seen in game (Mario, 15.09.2026): Betrayal of Aldur kept "increased Explicit Fire Modifier magnitudes"; poe2db's table has no magnitude rows
        var staff = RareStaff();
        staff.Sockets = 1;
        staff.AddMod(TestData.Data!.Mods.First(m => m.Id == "DestructionInfluenceFireModifierEffect"), values: new() { 18 });
        AddMod(staff, "to Level of all Fire Spell Skills", AffixType.Suffix);
        var socketed = TestData.Engine!.Execute(staff, TestData.Action("Betrayal of Aldur"), new Rng(1)).Item;
        Assert.Contains(socketed.Affixes, m => m.DisplayText() == "18% increased Explicit Fire Modifier magnitudes");
        Assert.Contains(socketed.Affixes, m => m.DisplayText() == "+7 to Level of all Chaos Spell Skills");
    }

    [Fact]
    public void Transforming_rune_texts_parse_their_elements()
    {
        var passion = RuneTransformation.Parse("Transforms all Cold and Lightning modifiers on the item into equivalent Fire modifiers")!;
        Assert.Equal(new[] { "Cold", "Lightning" }, passion.From);
        Assert.Equal("Fire", passion.To);
        var breath = RuneTransformation.Parse("When socketed, transforms all Fire and Lightning modifiers to equivalent Cold modifiers")!;
        Assert.Equal(("Cold", 2), (breath.To, breath.From.Count));
        Assert.Equal(new[] { "Fire", "Cold", "Lightning" }, RuneTransformation.Parse("Transforms all Fire, Cold and Lightning modifiers on the item into equivalent Chaos modifiers")!.From);
        Assert.Null(RuneTransformation.Parse("Can roll Destruction modifiers"));
        Assert.Equal(ModCategories.Destruction, ModCategories.UnlockedBy("Weapon: Can roll Destruction modifiers"));
    }

    /// <summary>
    /// The Genesis Tree (poe2db: breach_caster / breach_minion) grants its Caster and Minion modifiers to rings and belts. The mechanic itself is not
    /// simulated, so they never roll from a currency — but they must be browsable, tiered and survive a text round trip like any other modifier.
    /// </summary>
    [DataTheory]
    [InlineData(TestBases.Ring)]
    [InlineData("Rawhide Belt")]
    public void Genesis_tree_modifiers_are_browsable_on_rings_and_belts(string baseName)
    {
        var item = TestData.NewItem(baseName, Rarity.Rare, itemLevel: 82);
        foreach (var category in ModCategories.GenesisTree)
        {
            var mods = TestData.Pool!.AllForBaseByCategory(item, category).ToList();
            Assert.NotEmpty(mods);
            Assert.All(mods, mod => Assert.True(TestData.Pool!.TryDisplayTier(mod, item) is > 0, $"{mod.Text} has no tier"));
        }

        // no currency rolls them: they are not part of a normal addition
        var exalted = TestData.Engine!.Preview(item.Clone().WithAffixes(1, 1), TestData.Action("Exalted Orb"));
        Assert.DoesNotContain(exalted.Additions, c => ModCategories.GenesisTree.Contains(c.Mod.Category));

        // an item wearing one is written and parsed back with the same modifier
        var genesis = TestData.Pool!.AllForBaseByCategory(item, ModCategories.GenesisCaster).MaxBy(m => m.Level)!;
        item.AddMod(genesis);
        var parsed = ItemParser.Parse(ItemTextWriter.ToText(item, mod => TestData.Pool!.DisplayTier(mod, item)), TestData.Data!);
        Assert.Contains(parsed.Affixes, m => m.ModId == genesis.Id);
    }
}
