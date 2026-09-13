using POE2Crafting.Core.Engine;
using POE2Crafting.Core.Items;
using Xunit;

namespace POE2Crafting.Tests;

/// <summary>Basic currency behaviour against the real data store (skipped when it is not available).</summary>
public class CraftingEngineTests
{
    private const string Wand = "Siphoning Wand";

    [SkippableFact]
    public void Transmutation_on_normal_makes_magic()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var item = TestData.NewItem(Wand);
        var action = TestData.Action("Orb of Transmutation");

        Assert.True(TestData.Engine!.Check(item, action).Ok);
        var result = TestData.Engine.Execute(item, action, new Rng(42));
        Assert.True(result.Applied);
        Assert.Equal(Rarity.Magic, result.Item.Rarity);
        Assert.Equal(1, result.Item.AffixCount);
    }

    [SkippableFact]
    public void Augment_adds_one_mod_to_magic()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var item = TestData.NewItem(Wand, Rarity.Magic).WithAffixes(1, 0);
        var result = TestData.Engine!.Execute(item, TestData.Action("Orb of Augmentation"), new Rng(42));
        Assert.True(result.Applied);
        Assert.Equal(2, result.Item.AffixCount);
    }

    [SkippableFact]
    public void Annul_removes_one_mod()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var item = TestData.NewItem(Wand, Rarity.Magic).WithAffixes(1, 1);
        var result = TestData.Engine!.Execute(item, TestData.Action("Orb of Annulment"), new Rng(42));
        Assert.True(result.Applied);
        Assert.Equal(1, result.Item.AffixCount);
    }

    [SkippableFact]
    public void Corrupted_item_cannot_be_modified()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var item = TestData.NewItem(Wand);
        item.Corrupted = true;
        var check = TestData.Engine!.Check(item, TestData.Action("Orb of Transmutation"));
        Assert.False(check.Ok);
        Assert.Contains("Corrupted", check.Reason);
    }

    [SkippableFact]
    public void Preview_shows_candidates_for_transmute()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var preview = TestData.Engine!.Preview(TestData.NewItem(Wand), TestData.Action("Orb of Transmutation"));
        Assert.True(preview.Applicability.Ok);
        Assert.NotEmpty(preview.Additions);
        Assert.Equal(1, preview.AddCount);
    }

    [SkippableFact]
    public void Every_simulated_currency_has_an_operation()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var item = TestData.NewItem(Wand);
        var unsupported = TestData.Data!.AllCurrencies.Where(c => c.Op != null)
            .Where(c => TestData.Engine!.Check(item, new CraftAction { Currency = c }).Reason.Contains("planned for a later stage"))
            .Select(c => c.Op).Distinct().ToList();
        Assert.Empty(unsupported);
    }

    [SkippableFact]
    public void Every_simulated_currency_and_crafting_omen_has_info_with_icon_and_description()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var names = TestData.Data!.AllCurrencies.Where(c => c.Op != null).Select(c => c.Name)
            .Concat(TestData.Data.Omens.Where(o => o.Crafting).Select(o => o.Name));
        var incomplete = names.Where(n => TestData.Data.FindCraftItem(n) is not { IconUrl: not null } info || info.Description.Count == 0).ToList();
        Assert.Empty(incomplete);

        // icons are served locally from the web project's wwwroot (downloaded by tools/poe2db_icons.py)
        var wwwroot = Path.Combine(TestData.Data.DataFolder, "..", "src", "POE2Crafting.Web", "wwwroot");
        var missingFiles = names.Select(n => TestData.Data.FindCraftItem(n)!.IconUrl!).Distinct().Where(url => !File.Exists(Path.Combine(wwwroot, url))).ToList();
        Assert.Empty(missingFiles);

        var exalt = TestData.Data.FindCraftItem("Perfect Exalted Orb")!;
        Assert.Equal("Currency", exalt.Kind);
        Assert.Contains("Minimum modifier level: 50", exalt.Facts);
        Assert.Equal("Omen", TestData.Data.FindCraftItem("Omen of Sinistral Exaltation")!.Kind);
    }
}
