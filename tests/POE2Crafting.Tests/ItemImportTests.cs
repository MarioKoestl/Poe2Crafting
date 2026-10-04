namespace POE2Crafting.Tests;

/// <summary>Parser tests against the real data store (docs/STATUS.md 4.7: Mario's staff).</summary>
public class ItemImportTests
{
    private const string MariosStaff = @"Item Class: Staves
Rarity: Rare
Dusk Spire
Sanctified Staff
--------
Quality: +12% (augmented)
--------
Requires: Level 56, 99 Int
--------
Sockets: S S
--------
Item Level: 82
--------
+1 to Level of all Spell Skills (rune)
+1 to Level of all Plant Skill Gems (rune)
--------
Grants Skill: Level 18 Consecrate
--------
{ Prefix Modifier ""Glyphic"" (Tier: 2) — Damage, Caster }
200(189-208)% increased Spell Damage
{ Prefix Modifier ""Chalybeous"" (Tier: 3) — Mana }
+238(209-248) to maximum Mana
{ Prefix Modifier ""Electrifying"" (Tier: 2) — Damage, Elemental, Lightning }
Gain 49(49-54)% of Damage as Extra Lightning Damage
{ Suffix Modifier ""of Desolation"" (Tier: 2) — Physical, Caster, Gem }
+6(5-6) to Level of all Physical Spell Skills
{ Suffix Modifier ""of Havoc"" (Tier: 5) — Caster, Critical }
53(50-59)% increased Critical Hit Chance for Spells
{ Crafted Suffix Modifier ""of the Stars"" }
46(25-50)% chance to gain Nature's Archon when your Plants Overgrow";

    [DataFact]
    public void Parses_properties_runes_and_implicit_without_counting_them_as_affixes()
    {
        var item = ItemParser.Parse(MariosStaff, TestData.Data);

        Assert.Equal(TestBases.Staff, item.BaseName);
        Assert.NotNull(item.Base);
        Assert.Equal("Staff", item.ItemClass);
        Assert.Equal(12, item.Quality);
        Assert.Null(item.QualityType);
        Assert.Equal(2, item.Sockets);
        Assert.Equal(82, item.ItemLevel);
        Assert.Equal(2, item.Runes.Count);
        Assert.Contains(item.Mods, m => m.Kind == ModKind.Implicit && m.RawText!.StartsWith("Grants Skill"));
        Assert.Equal(3, item.PrefixCount);
        Assert.Equal(3, item.SuffixCount);
    }

    [DataFact]
    public void Resolves_every_affix_to_the_staff_version_of_the_mod()
    {
        var item = ItemParser.Parse(MariosStaff, TestData.Data);

        Assert.All(item.Affixes, m => Assert.NotNull(m.Def));
        var glyphic = item.Affixes.Single(m => m.Def!.Name == "Glyphic");
        Assert.Equal("(189-208)% increased Spell Damage", glyphic.Def!.Text);
        Assert.Equal(new List<double> { 200 }, glyphic.Values);
    }

    [DataTheory]
    [InlineData("Glyphic", 2)]
    [InlineData("Chalybeous", 3)]
    [InlineData("Electrifying", 2)]
    [InlineData("of Desolation", 2)]   // family holds several spell-skill stats; tiers count per stat
    [InlineData("of Havoc", 5)]
    public void Display_tiers_match_the_in_game_tiers(string affixName, int inGameTier)
    {
        var item = ItemParser.Parse(MariosStaff, TestData.Data);
        var mod = item.Affixes.Single(m => m.Def!.Name == affixName);
        Assert.Equal(inGameTier, TestData.Pool!.DisplayTier(mod.Def!, item));
    }

    [DataFact]
    public void Crafted_suffix_is_matched_to_the_alloy_mod()
    {
        var item = ItemParser.Parse(MariosStaff, TestData.Data);

        var crafted = Assert.Single(item.Mods, m => m.Kind == ModKind.Crafted);
        Assert.Equal(AffixType.Suffix, crafted.Affix);
        Assert.Equal("The Runebinder's Alloy", crafted.Def?.Name);
        Assert.Equal(new List<double> { 46 }, crafted.Values);
    }

    [DataFact]
    public void Magic_item_name_resolves_to_its_base()
    {
        var text = @"Item Class: Wands
Rarity: Magic
Runic Siphoning Wand of the Magus
--------
Item Level: 80";
        var item = ItemParser.Parse(text, TestData.Data);
        Assert.Equal(TestBases.Wand, item.BaseName);
        Assert.NotNull(item.Base);
    }

    [DataFact]
    public void Simple_ctrl_c_mod_lines_are_resolved_by_text_and_range()
    {
        var text = @"Item Class: Staves
Rarity: Magic
Sanctified Staff
--------
Item Level: 82
--------
200% increased Spell Damage";
        var item = ItemParser.Parse(text, TestData.Data);
        var mod = Assert.Single(item.Affixes);
        Assert.Equal(AffixType.Prefix, mod.Affix);
        Assert.Equal("(189-208)% increased Spell Damage", mod.Def!.Text);
    }

    [DataFact]
    public void Shown_value_range_wins_over_a_mismatching_affix_name()
    {
        var text = @"Item Class: Rings
Rarity: Rare
Doom Hold
Iron Ring
--------
Item Level: 82
--------
{ Prefix Modifier ""Rotund"" (Tier: 5) — Life }
+62(60-69) to maximum Life";
        var mod = Assert.Single(ItemParser.Parse(text, TestData.Data).Affixes);
        Assert.Equal("+(60-69) to maximum Life", mod.Def!.Text);
    }

    [DataFact]
    public void Simple_ctrl_c_suffix_line_is_resolved_as_suffix()
    {
        var text = @"Item Class: Staves
Rarity: Magic
Sanctified Staff
--------
Item Level: 82
--------
53% increased Critical Hit Chance for Spells";
        var mod = Assert.Single(ItemParser.Parse(text, TestData.Data).Affixes);
        Assert.Equal(AffixType.Suffix, mod.Affix);
        Assert.NotNull(mod.Def);
    }

    [DataFact]
    public void Enchant_line_on_corrupted_item_is_imported_as_corruption_enchantment()
    {
        var text = @"Item Class: Wands
Rarity: Rare
Grim Bane
Siphoning Wand
--------
Item Level: 80
--------
25(20-30)% increased Spell Damage (enchant)
--------
Corrupted";
        var item = ItemParser.Parse(text, TestData.Data);
        var (enchant, _) = Assert.Single(item.CorruptionEnchants);
        Assert.Equal("CorruptionSpellDamageOnWeapon1", enchant.Def!.Name);
        Assert.Contains("(enchant)", ItemTextWriter.ToText(item));
    }

    [DataFact]
    public void Catalyst_quality_of_the_item_text_is_stored_as_the_catalyst_type()
    {
        var text = @"Item Class: Amulets
Rarity: Rare
Doom Choker
Gold Amulet
--------
Quality: +20% (Life Modifiers)
--------
Item Level: 82";
        var item = ItemParser.Parse(text, TestData.Data);
        Assert.Equal("Life", item.QualityType);
        Assert.Equal("life", item.QualityTag);
        // a Flesh Catalyst (type Life) now tops up the same quality instead of replacing a "different" type
        Assert.Contains("already at the maximum", TestData.Engine!.Check(item, TestData.Action("Flesh Catalyst")).Reason);
    }

    [DataFact]
    public void Class_specific_slot_limits_ignore_the_class_name_case()
    {
        var rules = TestData.Data!.Config.Assumptions;
        Assert.Equal(rules.MaxAffixes("Jewel", Rarity.Rare, AffixType.Prefix, Array.Empty<string>()), rules.MaxAffixes("JEWEL", Rarity.Rare, AffixType.Prefix, Array.Empty<string>()));
        Assert.Equal(2, rules.MaxAffixes("jewel", Rarity.Rare, AffixType.Prefix, Array.Empty<string>()));
    }

    /// <summary>
    /// Mario's Mnemonic Ring (17.09.2026): two of its modifiers come from The Genesis Tree (poe2db breach_caster) and it carries an unrevealed
    /// desecrated suffix. The unrevealed modifier is only in the advanced item text (Ctrl+Alt+C) — the plain Ctrl+C text does not mention it at all.
    /// </summary>
    [DataFact]
    public void Advanced_text_of_a_genesis_tree_ring_keeps_its_unrevealed_desecrated_suffix()
    {
        var text = """
            Item Class: Rings
            Rarity: Rare
            Torment Band
            Mnemonic Ring
            --------
            Requirements:
            Level: 63
            --------
            Item Level: 82
            --------
            { Implicit Modifier }
            6% increased maximum Mana
            --------
            { Fractured Prefix Modifier "Occultist's" (Tier: 3) }
            22% increased Mana Cost Efficiency of Spells
            { Prefix Modifier "Prince's" (Tier: 2) }
            +179 to maximum Mana
            { Prefix Modifier "Passionate" (Tier: 1) }
            38% increased effect of Arcane Surge on you
            { Suffix Modifier "of the Veil" }
            Desecrated Suffix
            --------
            Fractured Item
            """;
        var item = ItemParser.Parse(text, TestData.Data!);

        Assert.Equal(("Mnemonic Ring", Rarity.Rare, 82), (item.BaseName, item.Rarity, item.ItemLevel));
        Assert.Equal((3, 1), (item.PrefixCount, item.SuffixCount));
        // every explicit modifier resolves, including the two of The Genesis Tree
        Assert.All(item.Affixes.Where(m => !m.Unrevealed), m => Assert.NotNull(m.Def));
        Assert.Equal(2, item.Affixes.Count(m => m.Def?.Category == ModCategories.GenesisCaster));
        Assert.True(item.Affixes.Single(m => m.Fractured).Def!.Category == ModCategories.GenesisCaster);

        var unrevealed = Assert.Single(item.UnrevealedMods).Mod;
        Assert.Equal((ModKind.Desecrated, AffixType.Suffix), (unrevealed.Kind, unrevealed.Affix));
    }

    /// <summary>
    /// An unrevealed desecrated modifier is its own line and the wording differs between the plain and the advanced item text (Mario 17.09.2026):
    /// every variant must become one unrevealed desecrated modifier. When the line names no affix type, the free slot decides — here the three
    /// prefixes are full, so it is the suffix.
    /// </summary>
    [DataTheory]
    [InlineData("Desecrated Suffix")]
    [InlineData("Unrevealed Desecrated Suffix")]
    [InlineData("Desecrated Modifier")]
    [InlineData("Unrevealed Modifier")]
    [InlineData("--------\nDesecrated Suffix")]
    [InlineData("{ Suffix Modifier \"of the Veil\" }\nDesecrated Suffix")]
    public void Unrevealed_desecrated_lines_are_recognised_in_every_wording(string unrevealedLines)
    {
        var text = """
            Rarity: Rare
            Torment Band
            Mnemonic Ring
            Ring
            --------
            Requires: Level 63
            --------
            Item Level: 82
            --------
            6% increased maximum Mana (implicit)
            --------
            22% increased Mana Cost Efficiency of Spells (fractured)
            +179 to maximum Mana
            38% increased effect of Arcane Surge on you
            LINES
            --------
            Fractured Item
            --------
            Note: 15 divine
            """.Replace("LINES", unrevealedLines);
        var item = ItemParser.Parse(text, TestData.Data!);

        Assert.Equal((3, 1), (item.PrefixCount, item.SuffixCount));
        var unrevealed = Assert.Single(item.UnrevealedMods).Mod;
        Assert.Equal((ModKind.Desecrated, AffixType.Suffix), (unrevealed.Kind, unrevealed.Affix));
    }

    [DataFact]
    public void A_tier_with_a_fixed_value_is_matched_by_that_value()
    {
        // movement speed has one fixed value per tier (10/15/20/25/30/35%), so only the number tells them apart
        var text = """
            Rarity: Rare
            Storm Trail
            Laced Boots
            --------
            Item Level: 79
            --------
            20% increased Movement Speed
            +53 to maximum Life
            """;
        var boots = ItemParser.Parse(text, TestData.Data!);

        var speed = boots.Affixes.Single(m => m.DisplayText().Contains("Movement Speed"));
        Assert.Equal("Stallion's", speed.Def!.Name);
        Assert.Equal("20% increased Movement Speed", speed.DisplayText());
        // the life tier is the one whose range holds 53
        var life = boots.Affixes.Single(m => m.DisplayText().Contains("maximum Life"));
        var (lo, hi) = ModText.Bounds(life.Def!.Ranges[0]);
        Assert.InRange(53, lo, hi);
    }

    [DataFact]
    public void A_unique_keeps_its_own_lines_instead_of_borrowing_rare_tiers()
    {
        // Crown of the Pale King: three of its lines also exist as craftable helmet modifiers, two do not
        var text = """
            Rarity: Unique
            Crown of the Pale King
            Cultist Crown
            --------
            Item Level: 79
            --------
            84% increased Armour and Energy Shield
            +76 to maximum Life
            10% increased Rarity of Items found
            11 to 22 Physical Thorns damage
            Thorns can Retaliate against all Hits
            """;
        var helmet = ItemParser.Parse(text, TestData.Data!);

        Assert.Equal(("Cultist Crown", Rarity.Unique), (helmet.BaseName, helmet.Rarity));
        // every line resolves against the unique itself: its own range, no tier, no prefix/suffix slot
        Assert.Equal(5, helmet.Mods.Count);
        Assert.All(helmet.Mods, m => Assert.Equal(ModCategories.Unique, m.Def!.Category));
        Assert.Empty(helmet.Affixes);
        var armour = helmet.Mods.First(m => m.DisplayText() == "84% increased Armour and Energy Shield");
        Assert.Equal("(50-100)% increased Armour and Energy Shield", armour.Def!.Text);
        Assert.Null(TestData.Pool!.TryDisplayTier(armour.Def, helmet));
        // the lines that exist nowhere else are there too
        Assert.Contains(helmet.Mods, m => m.DisplayText() == "Thorns can Retaliate against all Hits");
        Assert.Contains(helmet.Mods, m => m.DisplayText() == "11 to 22 Physical Thorns damage");
    }

    [DataFact]
    public void Unique_modifiers_the_data_does_not_have_stay_as_text()
    {
        // the data store holds the craftable modifiers; a unique's own lines have no definition and must survive as they are
        var text = """
            Rarity: Unique
            Briarpatch
            Laced Boots
            Ezomyte Boots
            --------
            Requires: Level 11, 17 Dex
            --------
            Item Level: 79
            --------
            Sockets: S S
            --------
            20% increased Movement Speed
            +53 to maximum Life
            +25% to Thorns Critical Hit Chance
            15 to 22 Physical Thorns damage
            --------
            Corrupted
            """;
        var boots = ItemParser.Parse(text, TestData.Data!);

        Assert.Equal(("Laced Boots", Rarity.Unique, true), (boots.BaseName, boots.Rarity, boots.Corrupted));
        Assert.Contains(boots.Mods, m => m.DisplayText() == "+25% to Thorns Critical Hit Chance");
        Assert.Contains(boots.Mods, m => m.DisplayText() == "15 to 22 Physical Thorns damage");
        // every line keeps the value the item shows
        Assert.Contains(boots.Mods, m => m.DisplayText() == "20% increased Movement Speed");
    }
}
