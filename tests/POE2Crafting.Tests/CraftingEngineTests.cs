namespace POE2Crafting.Tests;

/// <summary>Basic currency behaviour against the real data store (skipped when it is not available).</summary>
public class CraftingEngineTests
{

    [DataFact]
    public void Transmutation_on_normal_makes_magic()
    {
        var item = TestData.NewItem(TestBases.Wand);
        var action = TestData.Action("Orb of Transmutation");

        Assert.True(TestData.Engine!.Check(item, action).Ok);
        var result = TestData.Engine.Execute(item, action, new Rng(42));
        Assert.True(result.Applied);
        Assert.Equal(Rarity.Magic, result.Item.Rarity);
        Assert.Equal(1, result.Item.AffixCount);
    }

    [DataFact]
    public void Augment_adds_one_mod_to_magic()
    {
        var item = TestData.NewItem(TestBases.Wand, Rarity.Magic).WithAffixes(1, 0);
        var result = TestData.Engine!.Execute(item, TestData.Action("Orb of Augmentation"), new Rng(42));
        Assert.True(result.Applied);
        Assert.Equal(2, result.Item.AffixCount);
    }

    [DataFact]
    public void Annul_removes_one_mod()
    {
        var item = TestData.NewItem(TestBases.Wand, Rarity.Magic).WithAffixes(1, 1);
        var result = TestData.Engine!.Execute(item, TestData.Action("Orb of Annulment"), new Rng(42));
        Assert.True(result.Applied);
        Assert.Equal(1, result.Item.AffixCount);
    }

    [DataFact]
    public void Corrupted_item_cannot_be_modified()
    {
        var item = TestData.NewItem(TestBases.Wand);
        item.Corrupted = true;
        var check = TestData.Engine!.Check(item, TestData.Action("Orb of Transmutation"));
        Assert.False(check.Ok);
        Assert.Contains("Corrupted", check.Reason);
    }

    [DataFact]
    public void Preview_shows_candidates_for_transmute()
    {
        var preview = TestData.Engine!.Preview(TestData.NewItem(TestBases.Wand), TestData.Action("Orb of Transmutation"));
        Assert.True(preview.Applicability.Ok);
        Assert.NotEmpty(preview.Additions);
        Assert.Equal(1, preview.AddCount);
    }

    [DataFact]
    public void Every_simulated_currency_has_an_operation()
    {
        var item = TestData.NewItem(TestBases.Wand);
        var unsupported = TestData.Data!.AllCurrencies.Where(c => c.Op != null)
            .Where(c => TestData.Engine!.Check(item, new CraftAction { Currency = c }).Reason.Contains("planned for a later stage"))
            .Select(c => c.Op).Distinct().ToList();
        Assert.Empty(unsupported);
    }

    [DataFact]
    public void Every_simulated_currency_and_crafting_omen_has_info_with_icon_and_description()
    {
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

    [DataFact]
    public void Base_items_have_local_icons()
    {
        var data = TestData.Data!;
        var wwwroot = Path.Combine(data.DataFolder, "..", "src", "POE2Crafting.Web", "wwwroot");
        var visible = data.Bases.Where(b => !b.Hidden).ToList();
        var withIcon = visible.Where(b => data.BaseIconUrl(b) is { } url && File.Exists(Path.Combine(wwwroot, url))).ToList();
        // a few special bases have no poe2db page (Shrine Sceptre variants)
        Assert.True(withIcon.Count >= visible.Count - 5, $"{visible.Count - withIcon.Count} bases without icon: {string.Join(", ", visible.Except(withIcon).Take(10))}");
        foreach (var name in new[] { TestBases.Amulet, TestBases.Ring, TestBases.Wand, TestBases.Staff, TestBases.Jewel, TestBases.Body })
            Assert.NotNull(data.BaseIconUrl(data.FindBase(name)));
    }

    [DataFact]
    public void Hinekoras_lock_fixes_the_outcome_of_each_action_until_the_item_changes()
    {
        var engine = TestData.Engine!;
        var locked = TestData.Apply(TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(1, 1), "Hinekora's Lock").Item;
        Assert.True(locked.Foreseeing);

        var exalt = TestData.Action("Exalted Orb");
        var foreseen = engine.Foresee(locked, exalt).Item;
        Assert.Equal(foreseen.Affixes.Select(m => m.ModId), engine.Foresee(locked, exalt).Item.Affixes.Select(m => m.ModId));
        var applied = engine.Execute(locked, exalt, CraftingEngine.ForeseeRng(locked, exalt)).Item;
        Assert.Equal(foreseen.Affixes.Select(m => m.DisplayText()), applied.Affixes.Select(m => m.DisplayText()));
        Assert.False(applied.Foreseeing);
        Assert.Throws<InvalidOperationException>(() => engine.Foresee(applied, exalt));
    }
}
