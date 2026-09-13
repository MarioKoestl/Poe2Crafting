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

    public static Item NewItem(string baseName, Rarity rarity = Rarity.Normal, int itemLevel = 82) =>
        Item.FromBase(Data!.FindBase(baseName) ?? throw new InvalidOperationException($"Base {baseName} missing"), rarity, itemLevel);

    public static CurrencyDef Currency(string name) =>
        Data!.FindCurrency(name) ?? throw new InvalidOperationException($"Currency {name} missing");

    public static CraftAction Action(string currency, params string?[] omens) =>
        CraftAction.Of(Currency(currency), omens.OfType<string>().Select(o => Data!.FindOmen(o) ?? throw new InvalidOperationException($"Omen {o} missing")).ToArray());

    /// <summary>Add the first mods (one per family) of each affix type from the item's normal pool.</summary>
    public static Item WithAffixes(this Item item, int prefixes, int suffixes)
    {
        foreach (var (type, count) in new[] { (AffixType.Prefix, prefixes), (AffixType.Suffix, suffixes) })
            for (int i = 0; i < count; i++)
                item.AddMod(Pool!.Candidates(item, type).First().Mod);
        return item;
    }
}
