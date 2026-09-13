namespace POE2Crafting.Tests;

/// <summary>Loads the real data store once for all integration tests (null when the data folder is not available) and offers shared test helpers.</summary>
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
    private static readonly Lazy<CraftingEngine?> _engine = new(() => Data == null ? null : new CraftingEngine(Data, Pool));

    public static GameData? Data => _data.Value;
    public static ModPool? Pool => _pool.Value;
    public static CraftingEngine? Engine => _engine.Value;
    public static CraftingPathFinder PathFinder => new(Engine!);

    public static Item NewItem(string baseName, Rarity rarity = Rarity.Normal, int itemLevel = Item.DefaultItemLevel, bool withImplicit = false) =>
        Item.FromBase(Data!.FindBase(baseName) ?? throw new InvalidOperationException($"Base {baseName} missing"), rarity, itemLevel, withImplicit);

    public static CurrencyDef Currency(string name) =>
        Data!.FindCurrency(name) ?? throw new InvalidOperationException($"Currency {name} missing");

    public static CraftAction Action(string currency, params string?[] omens) =>
        CraftAction.Of(Currency(currency), omens.OfType<string>().Select(o => Data!.FindOmen(o) ?? throw new InvalidOperationException($"Omen {o} missing")).ToArray());

    /// <summary>Apply a currency (with omens) to the item: random with the seed, or the manual choice.</summary>
    public static CraftResult Apply(Item item, string currency, ManualChoice? choice = null, int seed = 1, params string?[] omens) =>
        Engine!.Execute(item, Action(currency, omens), new Rng(seed), choice);

    /// <summary>The highest tier of a normal mod on the item's base whose text contains <paramref name="text"/>.</summary>
    public static ModDef BestMod(Item item, string text, AffixType? type = null) =>
        Pool!.AllForBase(item, type).Where(m => m.Text.Contains(text)).MaxBy(m => m.Level) ?? throw new InvalidOperationException($"No mod \"{text}\" on {item.BaseName}");

    /// <summary>Add the first mods (one per family) of each affix type from the item's normal pool.</summary>
    public static Item WithAffixes(this Item item, int prefixes, int suffixes)
    {
        foreach (var (type, count) in new[] { (AffixType.Prefix, prefixes), (AffixType.Suffix, suffixes) })
            for (int i = 0; i < count; i++)
                item.AddMod(Pool!.Candidates(item, type).First().Mod);
        return item;
    }

    /// <summary>A planner target with these mods (resolved) at the rarity.</summary>
    public static TargetItemSpec Spec(Rarity rarity, IEnumerable<ModDef> mods, bool better = true) => new()
    {
        TargetRarity = rarity,
        TargetMods = mods.Select(m => new TargetMod
        {
            Family = m.Family!, Tier = m.Tier, AffixType = m.AffixType, ResolvedMod = m, AllowBetterTiers = better, Category = m.Category,
        }).ToList(),
    };

    public static PlanResult Plan(Item current, TargetItemSpec target) => PathFinder.FindPathsFromItem(current, target);

    public static ModDef[] Defs(Item item) => item.Affixes.Select(m => m.Def!).ToArray();
}

/// <summary>Bases used by the tests.</summary>
internal static class TestBases
{
    public const string Wand = "Siphoning Wand", Staff = "Sanctified Staff", Ring = "Iron Ring", Amulet = "Gold Amulet", MagicAmulet = "Crimson Amulet";
    public const string Jewel = "Sapphire", Body = "Vile Robe", Boots = "Stacked Sabatons";
}

/// <summary>A fact that needs the real data store; skipped when the data folder is not available.</summary>
public sealed class DataFactAttribute : FactAttribute
{
    public DataFactAttribute()
    {
        if (TestData.Data == null) Skip = "Data folder not found";
    }
}

/// <summary>A theory that needs the real data store; skipped when the data folder is not available.</summary>
public sealed class DataTheoryAttribute : TheoryAttribute
{
    public DataTheoryAttribute()
    {
        if (TestData.Data == null) Skip = "Data folder not found";
    }
}
