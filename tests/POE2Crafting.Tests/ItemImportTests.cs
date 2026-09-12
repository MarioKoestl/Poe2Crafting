using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;
using Xunit;

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

    [SkippableFact]
    public void Parses_properties_runes_and_implicit_without_counting_them_as_affixes()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var item = ItemParser.Parse(MariosStaff, TestData.Data);

        Assert.Equal("Sanctified Staff", item.BaseName);
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

    [SkippableFact]
    public void Resolves_every_affix_to_the_staff_version_of_the_mod()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var item = ItemParser.Parse(MariosStaff, TestData.Data);

        Assert.All(item.Affixes, m => Assert.NotNull(m.Def));
        var glyphic = item.Affixes.Single(m => m.Def!.Name == "Glyphic");
        Assert.Equal("(189-208)% increased Spell Damage", glyphic.Def!.Text);
        Assert.Equal(new List<double> { 200 }, glyphic.Values);
    }

    [SkippableTheory]
    [InlineData("Glyphic", 2)]
    [InlineData("Chalybeous", 3)]
    [InlineData("Electrifying", 2)]
    [InlineData("of Desolation", 2)]   // family holds several spell-skill stats; tiers count per stat
    [InlineData("of Havoc", 5)]
    public void Display_tiers_match_the_in_game_tiers(string affixName, int inGameTier)
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var item = ItemParser.Parse(MariosStaff, TestData.Data);
        var mod = item.Affixes.Single(m => m.Def!.Name == affixName);
        Assert.Equal(inGameTier, TestData.Pool!.DisplayTier(mod.Def!, item));
    }

    [SkippableFact]
    public void Crafted_suffix_is_matched_to_the_alloy_mod()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var item = ItemParser.Parse(MariosStaff, TestData.Data);

        var crafted = Assert.Single(item.Mods, m => m.Kind == ModKind.Crafted);
        Assert.Equal(AffixType.Suffix, crafted.Affix);
        Assert.Equal("The Runebinder's Alloy", crafted.Def?.Name);
        Assert.Equal(new List<double> { 46 }, crafted.Values);
    }

    [SkippableFact]
    public void Magic_item_name_resolves_to_its_base()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var text = @"Item Class: Wands
Rarity: Magic
Runic Siphoning Wand of the Magus
--------
Item Level: 80";
        var item = ItemParser.Parse(text, TestData.Data);
        Assert.Equal("Siphoning Wand", item.BaseName);
        Assert.NotNull(item.Base);
    }

    [SkippableFact]
    public void Simple_ctrl_c_mod_lines_are_resolved_by_text_and_range()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
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

    [Fact]
    public void Normalised_texts_of_item_line_and_template_are_equal()
    {
        Assert.Equal(ItemParser.NormaliseText("+(209-248) to maximum Mana"), ItemParser.NormaliseText("+238(209-248) to maximum Mana"));
        Assert.Equal(ItemParser.NormaliseText("Adds (13-18) to (25-29) Fire Damage"), ItemParser.NormaliseText("Adds 16(13-18) to 27(25-29) Fire Damage"));
        Assert.NotEqual(ItemParser.NormaliseText("(10-20)% increased Spell Damage"), ItemParser.NormaliseText("15% increased Cast Speed"));
    }

    [Fact]
    public void Crafted_prefix_header_keeps_affix_type_without_data()
    {
        var text = @"Item Class: Staves
Rarity: Rare
Test Staff
Sanctified Staff
--------
Item Level: 80
--------
{ Crafted Prefix Modifier ""Celestial"" }
+150 to maximum Mana";
        var mod = Assert.Single(ItemParser.Parse(text).Mods);
        Assert.Equal(ModKind.Crafted, mod.Kind);
        Assert.Equal(AffixType.Prefix, mod.Affix);
    }
}
