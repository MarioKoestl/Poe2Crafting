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

    /// <summary>A crafted amulet in the newer item text format (quality type in the label, Enhancement block, desecrated normal-pool mod).</summary>
    private const string WoeBraid = @"Item Class: Amulets
Rarity: Rare
Woe Braid
Gold Amulet
--------
Quality (Caster Modifiers): +34% (augmented)
--------
Requires: Level 64
--------
Item Level: 81
--------
{ Enhancement }
Allocates Dominion — Unscalable Value
--------
{ Implicit Modifier }
17(12-20)% increased Rarity of Items found
--------
{ Prefix Modifier ""Incandescent"" (Tier: 1) — Energy Shield }
+80(80-89) to maximum Energy Shield
{ Prefix Modifier ""Unassailable"" (Tier: 1) — Energy Shield }
45(45-50)% increased maximum Energy Shield
{ Desecrated Prefix Modifier ""Countess'"" (Tier: 1) }
+50(47-50) to Spirit
{ Fractured Suffix Modifier ""of the Cloud"" (Tier: 8) — Elemental, Lightning, Resistance }
+9(6-10)% to Lightning Resistance
{ Suffix Modifier ""of the Sorcerer"" (Tier: 1) — Caster, Gem — 34% Increased }
+3 to Level of all Spell Skills
{ Suffix Modifier ""of Euphoria"" (Tier: 2) — Mana }
56(50-59)% increased Mana Regeneration Rate
--------
Fractured Item
";

    [DataFact]
    public void Newer_item_text_format_reads_quality_type_instill_and_desecrated_normal_pool_mods()
    {
        var item = ItemParser.Parse(WoeBraid, TestData.Data!);

        Assert.Equal((34, "Caster"), (item.Quality, item.QualityType));
        Assert.Equal(81, item.ItemLevel);
        Assert.Equal("Dominion", item.InstilledNotableName);
        Assert.Equal(6, item.AffixCount);
        Assert.All(item.Affixes, m => Assert.NotNull(m.Def));
        var spirit = Assert.Single(item.Affixes, m => m.Def!.Name == "Countess'");
        Assert.Equal(ModKind.Desecrated, spirit.Kind);
        Assert.True(Assert.Single(item.Affixes, m => m.Fractured).Def!.Name == "of the Cloud");
        Assert.Equal("+4 to Level of all Spell Skills", item.EffectiveText(item.Affixes.Single(m => m.Def!.Name == "of the Sorcerer")));

        // editing the item keeps the desecrated kind (the Countess' Spirit mod is a normal mod in the data)
        var draft = new POE2Crafting.Core.Drafting.ItemDraft(TestData.Data!);
        draft.LoadFrom(item, copyValues: true);
        var edited = draft.BuildItem()!;
        Assert.Equal(ModKind.Desecrated, edited.Affixes.Single(m => m.Def!.Name == "Countess'").Kind);
        Assert.False(TestData.Engine!.Check(edited, TestData.Action("Preserved Collarbone")).Ok);
    }

    [DataFact]
    public void Unrevealed_desecrated_line_of_the_game_is_an_unrevealed_modifier_that_cannot_be_fractured()
    {
        const string text = @"Item Class: Amulets
Rarity: Rare
Behemoth Beads
Gold Amulet
--------
Item Level: 81
--------
{ Implicit Modifier }
15(12-20)% increased Rarity of Items found
--------
{ Prefix Modifier ""Hoarder's"" (Tier: 1) }
16(16-19)% increased Rarity of Items found
{ Prefix Modifier ""Unassailable"" (Tier: 1) — Energy Shield }
45(45-50)% increased maximum Energy Shield
{ Suffix Modifier ""of the Sorcerer"" (Tier: 1) — Caster, Gem }
+3 to Level of all Spell Skills
{ Suffix Modifier ""of the Veil"" }
Desecrated Suffix
";
        var item = ItemParser.Parse(text, TestData.Data!);
        var (mod, _) = Assert.Single(item.UnrevealedMods);
        Assert.Equal((ModKind.Desecrated, AffixType.Suffix), (mod.Kind, mod.Affix));
        Assert.Equal(4, item.AffixCount);

        var fracture = TestData.Engine!.Preview(item, TestData.Action("Fracturing Orb"));
        Assert.Equal(3, fracture.Removals.Count);
        Assert.All(fracture.Removals, r => Assert.Equal(1.0 / 3, r.Probability, 6));
        // an item with a desecrated modifier (revealed or not) cannot be desecrated again
        var bone = TestData.Engine.Check(item, TestData.Action("Preserved Collarbone"));
        Assert.False(bone.Ok);
        Assert.Contains("cannot be desecrated again", bone.Reason);

        // an item saved with the old import (plain "Desecrated Suffix" text) is repaired when it is bound again
        var old = new Item { BaseName = "Gold Amulet", Rarity = Rarity.Rare, Mods = { new ItemMod { ModId = "of the Veil", Affix = AffixType.Suffix, RawText = "Desecrated Suffix" } } };
        old.Bind(TestData.Data!);
        Assert.True(old.Mods[0].Unrevealed);
    }
}
