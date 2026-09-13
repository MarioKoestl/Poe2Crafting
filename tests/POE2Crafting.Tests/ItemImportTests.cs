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
}
