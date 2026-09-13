namespace POE2Crafting.Tests;

public class ItemParserTests
{
    private const string RareStaff = @"Item Class: Staves
Rarity: Rare
Dusk Spire
Sanctified Staff
--------
Quality: +20% (Caster Modifiers)
Sockets: 3
--------
Item Level: 82
--------
{ Prefix Modifier ""Radiating"" (Tier: 3) — Caster, Elemental }
112(105-119)% increased Spell Damage
{ Prefix Modifier ""Searing"" (Tier: 5) — Elemental, Fire }
Adds 16(13-18) to 27(25-29) Fire Damage to Spells
{ Suffix Modifier ""of the Magus"" (Tier: 2) — Caster }
+38(35-39) to maximum Mana
{ Suffix Modifier ""of Expertise"" (Tier: 4) — Caster }
9(8-10)% increased Cast Speed";

    [Fact]
    public void Parses_rarity_and_names()
    {
        var item = ItemParser.Parse(RareStaff);
        Assert.Equal(Rarity.Rare, item.Rarity);
        Assert.Equal("Dusk Spire", item.Name);
        Assert.Equal(TestBases.Staff, item.BaseName);
        Assert.Equal("Staves", item.ItemClass);
    }

    [Fact]
    public void Parses_properties()
    {
        var item = ItemParser.Parse(RareStaff);
        Assert.Equal(20, item.Quality);
        Assert.Equal("Caster Modifiers", item.QualityType);
        Assert.Equal(3, item.Sockets);
        Assert.Equal(82, item.ItemLevel);
    }

    [Fact]
    public void Parses_prefix_and_suffix_mods()
    {
        var item = ItemParser.Parse(RareStaff);
        Assert.Equal(4, item.Mods.Count);
        Assert.Equal(2, item.PrefixCount);
        Assert.Equal(2, item.SuffixCount);
        Assert.Equal(AffixType.Prefix, item.Mods[0].Affix);
        Assert.Equal(AffixType.Suffix, item.Mods[2].Affix);
    }

    [Fact]
    public void Parses_stat_values()
    {
        var item = ItemParser.Parse(RareStaff);
        // "112(105-119)% increased Spell Damage" → value 112
        Assert.Contains(112.0, item.Mods[0].Values);
    }

    [Fact]
    public void Parses_normal_item()
    {
        var text = @"Item Class: Wands
Rarity: Normal
Siphoning Wand
--------
Item Level: 75";
        var item = ItemParser.Parse(text);
        Assert.Equal(Rarity.Normal, item.Rarity);
        Assert.Equal(TestBases.Wand, item.BaseName);
        Assert.Null(item.Name);
        Assert.Equal(75, item.ItemLevel);
        Assert.Empty(item.Mods);
    }

    [Fact]
    public void Parses_corrupted_flag()
    {
        var text = @"Item Class: Wands
Rarity: Normal
Siphoning Wand
--------
Item Level: 75
--------
Corrupted";
        var item = ItemParser.Parse(text);
        Assert.True(item.Corrupted);
    }

    [Fact]
    public void Parses_crafted_modifier()
    {
        var text = @"Item Class: Staves
Rarity: Rare
Test Staff
Sanctified Staff
--------
Item Level: 80
--------
{ Crafted Modifier ""Some Alloy Mod"" — Caster }
+50 to maximum Mana";
        var item = ItemParser.Parse(text);
        Assert.Single(item.Mods);
        Assert.Equal(ModKind.Crafted, item.Mods[0].Kind);
    }

    [Fact]
    public void ToText_roundtrip_preserves_key_fields()
    {
        var item = ItemParser.Parse(RareStaff);
        var text = ItemTextWriter.ToText(item);
        Assert.Contains("Rarity: Rare", text);
        Assert.Contains("Dusk Spire", text);
        Assert.Contains(TestBases.Staff, text);
        Assert.Contains("Item Level: 82", text);
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

    [Fact]
    public void Written_text_parses_back_to_the_same_item()
    {
        var item = ItemParser.Parse(RareStaff);
        item.Sanctified = true;
        item.Identified = false;
        item.Mods.Add(new ItemMod { Kind = ModKind.Desecrated, Affix = AffixType.Suffix, Unrevealed = true });

        var again = ItemParser.Parse(ItemTextWriter.ToText(item));
        Assert.Equal(item.Title, again.Title);
        Assert.Equal((item.Quality, item.QualityType, item.Sockets, item.ItemLevel), (again.Quality, again.QualityType, again.Sockets, again.ItemLevel));
        Assert.Equal((item.Sanctified, item.Identified, item.Corrupted), (again.Sanctified, again.Identified, again.Corrupted));
        Assert.Equal(item.Mods.Select(m => (m.Kind, m.Affix, m.Unrevealed, m.DisplayText())), again.Mods.Select(m => (m.Kind, m.Affix, m.Unrevealed, m.DisplayText())));
    }
}
