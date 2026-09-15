namespace POE2Crafting.Tests;

public class ModTextTests
{
    [Fact]
    public void Normalised_texts_of_item_line_and_template_are_equal()
    {
        Assert.Equal(ModText.StatSignature("+(209-248) to maximum Mana"), ModText.StatSignature("+238(209-248) to maximum Mana"));
        Assert.Equal(ModText.StatSignature("+(209-248) to maximum Mana"), ModText.StatSignature("+238 to maximum Mana"));
        Assert.Equal(ModText.StatSignature("Adds (13-18) to (25-29) Fire Damage"), ModText.StatSignature("Adds 16(13-18) to 27(25-29) Fire Damage"));
        Assert.NotEqual(ModText.StatSignature("(10-20)% increased Spell Damage"), ModText.StatSignature("15% increased Cast Speed"));
    }

    [Fact]
    public void Quality_tag_is_read_from_catalyst_and_item_text_names()
    {
        Assert.Equal("life", CatalystDef.QualityTagFor("Life"));
        Assert.Equal("caster", CatalystDef.QualityTagFor("Caster Modifiers"));
        Assert.Equal("defences", CatalystDef.QualityTagFor("Armour , Evasion and Energy Shield"));
        Assert.Null(CatalystDef.QualityTagFor(null));
    }

    [Fact]
    public void Catalyst_scaling_rounds_whole_numbers_down_and_keeps_two_decimals()
    {
        Assert.Equal("+3 to Level of all Melee Skills", ModText.ScaleNumbers("+3 to Level of all Melee Skills", 1.33));
        Assert.Equal("+4 to Level of all Melee Skills", ModText.ScaleNumbers("+3 to Level of all Melee Skills", 1.34));
        Assert.Equal(new List<double> { 4, 1.43 }, ModText.ScaleValues(new[] { 3.0, 1.07 }, 1.34));
    }

    [Fact]
    public void Chance_at_least_handles_integer_and_decimal_ranges()
    {
        Assert.Equal(6.0 / 11, ModText.ChanceAtLeast(new[] { 10.0, 20.0 }, 15), 6);
        Assert.Equal(0.5, ModText.ChanceAtLeast(new[] { 1.0, 2.0 }, 1.5), 6);
        Assert.Equal(1.0, ModText.ChanceAtLeast(new[] { 10.0, 20.0 }, 5), 6);
        Assert.Equal(0.0, ModText.ChanceAtLeast(new[] { 10.0, 20.0 }, 21), 6);
        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, ModText.PossibleRolls(new[] { 3.0, 1.0 }));
    }

    [Fact]
    public void Ranges_text_shows_rolled_ranges_only()
    {
        Assert.Equal("41–45", ModText.RangesText(ModText.ParseRanges("+(41-45)% to Lightning Resistance")));
        Assert.Equal("5–8 / 10–15", ModText.RangesText(ModText.ParseRanges("Adds (5-8) to (10-15) Fire Damage")));
        Assert.Null(ModText.RangesText(new List<double[]> { new[] { 3.0, 3.0 } }));
    }

    [Fact]
    public void Advanced_text_shows_each_value_with_its_range()
    {
        Assert.Equal("+74(71-79) to maximum Energy Shield", ModText.RenderWithRanges("+(71-79) to maximum Energy Shield", new[] { 74.0 }));
        Assert.Equal("Adds 6(5-8) to 12(10-15) Fire Damage", ModText.RenderWithRanges("Adds (5-8) to (10-15) Fire Damage", new[] { 6.0, 12.0 }));
        Assert.Equal("+3 to Level of all Spell Skills", ModText.RenderWithRanges("+3 to Level of all Spell Skills", Array.Empty<double>()));
    }
}
