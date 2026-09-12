using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Tests;

/// <summary>Loads the real data store once for all integration tests (null when the data folder is not available).</summary>
internal static class TestData
{
    private static readonly Lazy<GameData?> _data = new(() =>
    {
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "data"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "data"),
        };
        var folder = candidates.Select(Path.GetFullPath).FirstOrDefault(d => File.Exists(Path.Combine(d, "mods.json")));
        return folder == null ? null : GameData.Load(folder);
    });

    private static readonly Lazy<ModPool?> _pool = new(() => Data == null ? null : new ModPool(Data));

    public static GameData? Data => _data.Value;
    public static ModPool? Pool => _pool.Value;
    public static CraftingEngine? Engine => Data == null ? null : new CraftingEngine(Data, Pool);

    public static Item NewItem(string baseName, Rarity rarity = Rarity.Normal, int itemLevel = 82)
    {
        var b = Data!.FindBase(baseName) ?? throw new InvalidOperationException($"Base {baseName} missing");
        return new Item { BaseName = b.Name, ItemClass = b.ItemClass, Rarity = rarity, ItemLevel = itemLevel, Base = b };
    }

    public static ItemMod ModOf(ModDef def, ModKind kind = ModKind.Explicit) =>
        new() { ModId = def.Id, Def = def, Affix = def.AffixType, Kind = kind, Values = def.Ranges.Select(r => r[0]).ToList() };

    public static CurrencyDef Currency(string name) =>
        Data!.FindCurrency(name) ?? Data.EssenceCurrencies.First(c => c.Name == name);
}
