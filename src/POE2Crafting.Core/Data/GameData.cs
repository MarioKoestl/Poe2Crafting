using System.Text.Json;

namespace POE2Crafting.Core.Data;

/// <summary>All game data loaded from the JSON data store (data/ folder).</summary>
public sealed class GameData
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    public string DataFolder { get; }
    public IReadOnlyList<BaseItem> Bases { get; }
    public IReadOnlyList<ModDef> Mods { get; }
    public IReadOnlyList<CurrencyDef> Currencies { get; }
    public IReadOnlyList<EssenceDef> Essences { get; }
    public IReadOnlyList<EssenceDef> Alloys { get; }
    public IReadOnlyList<OmenDef> Omens { get; }
    public IReadOnlyList<CatalystDef> Catalysts { get; }
    public IReadOnlyList<ItemClassDef> ItemClasses { get; }
    public SimConfig Config { get; }

    private readonly Dictionary<string, BaseItem> _baseByName;
    private readonly Dictionary<string, ModDef> _modById;
    private readonly Dictionary<string, CurrencyDef> _currencyByName;
    private readonly Dictionary<string, OmenDef> _omenByName;
    private readonly Dictionary<string, EssenceDef> _essenceByName;

    private GameData(string folder, List<BaseItem> bases, List<ModDef> mods, List<CurrencyDef> currencies, List<EssenceDef> essences,
        List<EssenceDef> alloys, List<OmenDef> omens, List<CatalystDef> catalysts, List<ItemClassDef> classes, SimConfig config)
    {
        DataFolder = folder;
        Bases = bases; Mods = mods; Currencies = currencies; Essences = essences; Alloys = alloys; Omens = omens; Catalysts = catalysts;
        ItemClasses = classes; Config = config;
        _baseByName = bases.GroupBy(b => b.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _modById = mods.ToDictionary(m => m.Id);
        _currencyByName = currencies.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _omenByName = omens.GroupBy(o => o.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _essenceByName = essences.Concat(alloys).GroupBy(e => e.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        ComputeTiers(mods);
    }

    public static GameData Load(string folder)
    {
        T Read<T>(string file) where T : new()
        {
            var path = Path.Combine(folder, file);
            if (!File.Exists(path)) return new T();
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? new T();
        }
        return new GameData(folder,
            Read<List<BaseItem>>("bases.json"),
            Read<List<ModDef>>("mods.json"),
            Read<List<CurrencyDef>>("currencies.json"),
            Read<List<EssenceDef>>("essences.json"),
            Read<List<EssenceDef>>("alloys.json"),
            Read<List<OmenDef>>("omens.json"),
            Read<List<CatalystDef>>("catalysts.json"),
            Read<List<ItemClassDef>>("item_classes.json"),
            Read<SimConfig>("config.json"));
    }

    /// <summary>Tier = 1 + number of distinct higher levels within the same family, generation type, and category (global across item classes).</summary>
    private static void ComputeTiers(List<ModDef> mods)
    {
        foreach (var group in mods.Where(m => m.Family != null && (m.IsPrefix || m.IsSuffix))
                                  .GroupBy(m => (m.Family, m.Gen, m.Category)))
        {
            var levels = group.Select(m => m.Level).Distinct().OrderByDescending(l => l).ToList();
            foreach (var m in group)
            {
                m.Tier = levels.IndexOf(m.Level) + 1;
                m.TierCount = levels.Count;
            }
        }
        foreach (var m in mods.Where(m => m.Tier == 0)) { m.Tier = 1; m.TierCount = 1; }
    }

    public BaseItem? FindBase(string name) => _baseByName.TryGetValue(name.Trim(), out var b) ? b : null;
    public ModDef? FindMod(string id) => _modById.TryGetValue(id, out var m) ? m : null;
    public CurrencyDef? FindCurrency(string name) => _currencyByName.TryGetValue(name.Trim(), out var c) ? c : null;
    public OmenDef? FindOmen(string name) => _omenByName.TryGetValue(name.Trim(), out var o) ? o : null;
    public EssenceDef? FindEssenceOrAlloy(string name) => _essenceByName.TryGetValue(name.Trim(), out var e) ? e : null;

    public IEnumerable<BaseItem> BasesOfClass(string itemClass) => Bases.Where(b => b.ItemClass.Equals(itemClass, StringComparison.OrdinalIgnoreCase));

    /// <summary>All pages (poe2db ModifiersCalc pages) that belong to an item class, used as a fallback when a base has no page.</summary>
    public IEnumerable<string> PagesOfClass(string itemClass) =>
        ItemClasses.FirstOrDefault(c => c.Name.Equals(itemClass, StringComparison.OrdinalIgnoreCase))?.ModPages ?? Enumerable.Empty<string>();
}
