namespace POE2Crafting.Tests;

public class ItemDiffTests
{
    [Fact]
    public void Replacing_one_of_two_equal_runes_is_a_change()
    {
        var before = new Item { Runes = { "+10% to Fire Resistance", "+10% to Fire Resistance" } };
        var after = new Item { Runes = { "+10% to Fire Resistance", "+10% to Cold Resistance" } };

        var lines = ItemDiff.Between(before, after);
        Assert.Contains(lines, l => l.Kind == DiffKind.Removed && l.Text.StartsWith("+10% to Fire Resistance"));
        Assert.Contains(lines, l => l.Kind == DiffKind.Added && l.Text.StartsWith("+10% to Cold Resistance"));
    }

    [Fact]
    public void Enchantment_lines_use_the_item_text_markers()
    {
        var after = new Item();
        after.Mods.Add(new ItemMod { Kind = ModKind.CorruptedImplicit, RawText = "25% increased Spell Damage" });

        var line = Assert.Single(ItemDiff.Between(new Item(), after));
        Assert.Equal("25% increased Spell Damage (enchant)", line.Text);
    }
}
