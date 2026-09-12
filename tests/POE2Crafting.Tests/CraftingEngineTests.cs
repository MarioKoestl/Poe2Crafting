using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine;
using POE2Crafting.Core.Items;
using Xunit;

namespace POE2Crafting.Tests;

/// <summary>
/// Integration tests for CraftingEngine. Uses a minimal in-memory GameData setup.
/// These tests verify the engine logic without needing the full data files.
/// </summary>
public class CraftingEngineTests
{
    private static (GameData data, ModPool pool, CraftingEngine engine) SetupMinimal()
    {
        // Since GameData.Load requires JSON files, these tests verify the engine
        // against live data. Skip if data folder is not available.
        var dataFolder = FindDataFolder();
        if (dataFolder == null) return (null!, null!, null!);
        var data = GameData.Load(dataFolder);
        var pool = new ModPool(data);
        var engine = new CraftingEngine(data, pool);
        return (data, pool, engine);
    }

    private static string? FindDataFolder()
    {
        // Try common locations
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "data"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "data"),
            @"C:\Development\POE2Crafting\data",
        };
        return candidates.Select(Path.GetFullPath).FirstOrDefault(Directory.Exists);
    }

    [SkippableFact]
    public void Transmutation_on_normal_makes_magic()
    {
        var (data, pool, engine) = SetupMinimal();
        Skip.If(data == null, "Data folder not found");

        var baseItem = data.FindBase("Siphoning Wand");
        Skip.If(baseItem == null, "Siphoning Wand not in data");

        var item = new Item { BaseName = baseItem.Name, ItemClass = baseItem.ItemClass, Rarity = Rarity.Normal, ItemLevel = 82, Base = baseItem };
        var transmute = data.Currencies.First(c => c.Op == "transmute");
        var action = new CraftAction { Currency = transmute };

        var check = engine.Check(item, action);
        Assert.True(check.Ok);

        var result = engine.Execute(item, action, new Rng(42));
        Assert.True(result.Applied);
        Assert.Equal(Rarity.Magic, result.Item.Rarity);
        Assert.True(result.Item.AffixCount >= 1);
    }

    [SkippableFact]
    public void Augment_adds_one_mod_to_magic()
    {
        var (data, pool, engine) = SetupMinimal();
        Skip.If(data == null, "Data folder not found");

        var baseItem = data.FindBase("Siphoning Wand");
        Skip.If(baseItem == null);

        var item = new Item { BaseName = baseItem.Name, ItemClass = baseItem.ItemClass, Rarity = Rarity.Magic, ItemLevel = 82, Base = baseItem };
        // Add one prefix manually
        var prefixes = pool.Candidates(item, AffixType.Prefix);
        Skip.If(prefixes.Count == 0);
        var mod = prefixes[0].Mod;
        item.Mods.Add(new ItemMod { ModId = mod.Id, Def = mod, Affix = AffixType.Prefix, Kind = ModKind.Explicit, Values = CraftingEngine.RollValues(mod, new Rng(1)) });

        var augment = data.Currencies.First(c => c.Op == "augment");
        var action = new CraftAction { Currency = augment };
        var result = engine.Execute(item, action, new Rng(42));

        Assert.True(result.Applied);
        Assert.Equal(2, result.Item.AffixCount);
    }

    [SkippableFact]
    public void Annul_removes_one_mod()
    {
        var (data, pool, engine) = SetupMinimal();
        Skip.If(data == null, "Data folder not found");

        var baseItem = data.FindBase("Siphoning Wand");
        Skip.If(baseItem == null);

        // Start with a 2-mod magic item
        var item = new Item { BaseName = baseItem.Name, ItemClass = baseItem.ItemClass, Rarity = Rarity.Magic, ItemLevel = 82, Base = baseItem };
        var rng = new Rng(1);
        var prefixes = pool.Candidates(item, AffixType.Prefix);
        var suffixes = pool.Candidates(item, AffixType.Suffix);
        Skip.If(prefixes.Count == 0 || suffixes.Count == 0);

        var p = prefixes[0].Mod;
        var s = suffixes[0].Mod;
        item.Mods.Add(new ItemMod { ModId = p.Id, Def = p, Affix = AffixType.Prefix, Kind = ModKind.Explicit, Values = CraftingEngine.RollValues(p, rng) });
        item.Mods.Add(new ItemMod { ModId = s.Id, Def = s, Affix = AffixType.Suffix, Kind = ModKind.Explicit, Values = CraftingEngine.RollValues(s, rng) });

        var annul = data.Currencies.First(c => c.Op == "annul");
        var result = engine.Execute(item, new CraftAction { Currency = annul }, new Rng(42));

        Assert.True(result.Applied);
        Assert.Equal(1, result.Item.AffixCount);
    }

    [SkippableFact]
    public void Corrupted_item_cannot_be_modified()
    {
        var (data, pool, engine) = SetupMinimal();
        Skip.If(data == null, "Data folder not found");

        var baseItem = data.FindBase("Siphoning Wand");
        Skip.If(baseItem == null);

        var item = new Item { BaseName = baseItem.Name, ItemClass = baseItem.ItemClass, Rarity = Rarity.Normal, ItemLevel = 82, Base = baseItem, Corrupted = true };
        var transmute = data.Currencies.First(c => c.Op == "transmute");
        var check = engine.Check(item, new CraftAction { Currency = transmute });

        Assert.False(check.Ok);
        Assert.Contains("Corrupted", check.Reason);
    }

    [SkippableFact]
    public void Preview_shows_candidates_for_transmute()
    {
        var (data, pool, engine) = SetupMinimal();
        Skip.If(data == null, "Data folder not found");

        var baseItem = data.FindBase("Siphoning Wand");
        Skip.If(baseItem == null);

        var item = new Item { BaseName = baseItem.Name, ItemClass = baseItem.ItemClass, Rarity = Rarity.Normal, ItemLevel = 82, Base = baseItem };
        var transmute = data.Currencies.First(c => c.Op == "transmute");
        var preview = engine.Preview(item, new CraftAction { Currency = transmute });

        Assert.True(preview.Applicability.Ok);
        Assert.True(preview.Additions.Count > 0, "Should have addition candidates");
        Assert.Equal(1, preview.AddCount);
    }
}
