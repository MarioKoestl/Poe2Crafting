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

    /// <summary>
    /// Every usable crafting item as a currency: currencies.json plus one synthetic currency per essence/alloy (Op "essence")
    /// and per catalyst (Op "catalyst"), so they all flow through the same engine/UI path. Section groups them for the UI.
    /// </summary>
    public IReadOnlyList<CurrencyDef> AllCurrencies { get; }

    public const string EssenceSection = "Essences", AlloySection = "Alloys", CatalystSection = "Catalysts";

    private readonly Dictionary<string, BaseItem> _baseByName;
    private readonly Dictionary<string, ModDef> _modById;
    private readonly Dictionary<string, CurrencyDef> _currencyByName;
    private readonly Dictionary<string, OmenDef> _omenByName;
    private readonly Dictionary<string, List<ModDef>> _essenceModsByName;
    private readonly Dictionary<string, CraftItemInfo> _craftItemByName;

    private GameData(string folder, List<BaseItem> bases, List<ModDef> mods, List<CurrencyDef> currencies, List<EssenceDef> essences,
        List<EssenceDef> alloys, List<OmenDef> omens, List<CatalystDef> catalysts, List<ItemClassDef> classes, SimConfig config,
        Dictionary<string, string> iconsBySlug)
    {
        DataFolder = folder;
        Bases = bases; Mods = mods; Currencies = currencies; Essences = essences; Alloys = alloys; Omens = omens; Catalysts = catalysts;
        ItemClasses = classes; Config = config;
        _baseByName = bases.GroupBy(b => b.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _modById = mods.ToDictionary(m => m.Id);
        _omenByName = omens.GroupBy(o => o.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _essenceModsByName = mods.Where(m => ModCategories.EssenceResults.Contains(m.Category))
            .GroupBy(m => m.Name).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var essenceCurrencies = essences.Concat(alloys).Select(e => new CurrencyDef
        {
            Name = e.Name, Slug = e.Slug, Section = e.Tier == "Alloy" ? AlloySection : EssenceSection, Description = e.Description,
            Op = "essence", RarityIn = e.RarityIn, RarityOut = e.RarityOut, Essence = e,
        });
        var catalystCurrencies = catalysts.Select(k => new CurrencyDef
        {
            Name = k.Name, Slug = k.Slug, Section = CatalystSection, Description = k.Description,
            Op = "catalyst", Target = k.ClassTarget, Catalyst = k,
        });
        AllCurrencies = currencies.Concat(essenceCurrencies).Concat(catalystCurrencies).ToList();
        _currencyByName = AllCurrencies.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        string? Icon(string? slug) => slug != null && iconsBySlug.TryGetValue(slug.Replace("/us/", ""), out var url) ? url : null;
        _craftItemByName = AllCurrencies.Select(c => new CraftItemInfo(c.Name, CurrencyKind(c), Icon(c.Slug), c.Description, CurrencyFacts(c)))
            .Concat(omens.Select(o => new CraftItemInfo(o.Name, "Omen", Icon(o.Slug), new[] { o.Description },
                o.StackSize != null ? new[] { $"Stack size: {o.StackSize}" } : Array.Empty<string>())))
            .GroupBy(i => i.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        ComputeTiers(mods);
    }

    private static string CurrencyKind(CurrencyDef c) => c.Section switch
    {
        EssenceSection => "Essence",
        AlloySection => "Alloy",
        CatalystSection => "Catalyst",
        _ => "Currency",
    };

    private static List<string> CurrencyFacts(CurrencyDef c)
    {
        var facts = new List<string>();
        if (c.MinModLevel is { } min) facts.Add($"Minimum modifier level: {min}");
        if (c.MaxItemLevel is { } max) facts.Add($"Only items up to item level {max}");
        if (c.StackSize != null) facts.Add($"Stack size: {c.StackSize}");
        return facts;
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
            Read<SimConfig>("config.json"),
            Read<Dictionary<string, string>>("icons.json"));
    }

    /// <summary>
    /// Global tier = 1 + number of distinct higher levels within the same family, stat, generation type, and category (across all item classes).
    /// Only a fallback: what the UI shows comes from ModPool.DisplayTier, which ranks per base.
    /// </summary>
    private static void ComputeTiers(List<ModDef> mods)
    {
        var ranks = ModTiers.RankAll(mods);
        foreach (var m in mods)
            (m.Tier, m.TierCount) = ranks.TryGetValue(m.Id, out var r) ? (r.Tier, r.Count) : (1, 1);
    }

    public BaseItem? FindBase(string name) => _baseByName.TryGetValue(name.Trim(), out var b) ? b : null;
    public ModDef? FindMod(string id) => _modById.TryGetValue(id, out var m) ? m : null;
    public CurrencyDef? FindCurrency(string name) => _currencyByName.TryGetValue(name.Trim(), out var c) ? c : null;
    public OmenDef? FindOmen(string name) => _omenByName.TryGetValue(name.Trim(), out var o) ? o : null;
    /// <summary>Info (kind, icon, description) of a currency, essence, alloy, catalyst or omen by name; null for anything else.</summary>
    public CraftItemInfo? FindCraftItem(string name) => _craftItemByName.TryGetValue(name.Trim(), out var i) ? i : null;

    /// <summary>Base whose name appears in a magic item's full name ("Glyphic Siphoning Wand of the Stars"); longest match wins.</summary>
    public BaseItem? FindBaseInName(string fullName) =>
        FindBase(fullName) ?? Bases.Where(b => b.Name.Length > 0 && fullName.Contains(b.Name, StringComparison.OrdinalIgnoreCase))
                                   .OrderByDescending(b => b.Name.Length).FirstOrDefault();

    public IEnumerable<BaseItem> BasesOfClass(string itemClass) => Bases.Where(b => b.ItemClass.Equals(itemClass, StringComparison.OrdinalIgnoreCase));

    /// <summary>All pages (poe2db ModifiersCalc pages) that belong to an item class, used as a fallback when a base has no page.</summary>
    public IEnumerable<string> PagesOfClass(string itemClass) =>
        ItemClasses.FirstOrDefault(c => c.Name.Equals(itemClass, StringComparison.OrdinalIgnoreCase))?.ModPages ?? Enumerable.Empty<string>();

    private sealed record TargetGroup(string DisplayName, Func<ItemClassDef, bool> Matches);

    /// <summary>Item class groups used by currency targets (currencies.json Target/QualityTarget) and engine rules.</summary>
    private static readonly Dictionary<string, TargetGroup> TargetGroups = new()
    {
        ["weapon_or_quiver"] = new("Weapons or Quivers", c => c.Group.EndsWith("Weapon", StringComparison.Ordinal) || c.Name == "Quiver"),
        ["martial_weapon"] = new("Martial Weapons", c => c.Group == "Martial Weapon"),
        ["caster_weapon"] = new("Wands, Staves or Sceptres", c => c.Group == "Caster Weapon"),
        ["armour"] = new("Armour", c => c.Group == "Armour" || c.Name == "Focus"),
        ["jewellery"] = new("Amulets, Rings or Belts", c => c.Group == "Jewellery"),
        ["ring_or_amulet"] = new("Rings or Amulets", c => c.Name is "Ring" or "Amulet"),
        ["jewel"] = new("Jewels", c => c.Group == "Jewel"),
        ["flask"] = new("Flasks", c => c.Name.EndsWith("Flask", StringComparison.Ordinal)),
        // Artificer's Orb: "Martial Weapon, wand, staff or Armour"
        ["socketable"] = new("Martial Weapons, Wands, Staves or Armour", c => c.Group is "Martial Weapon" or "Armour" || c.Name is "Wand" or "Staff" or "Focus"),
        // Vaal Orb outcome groups: extra socket vs. extra quality
        ["martial_weapon_or_armour"] = new("Martial Weapons or Armour", c => c.Group is "Martial Weapon" or "Armour" || c.Name == "Focus"),
        ["wand_or_staff"] = new("Wands or Staves", c => c.Name is "Wand" or "Staff"),
        ["equipment"] = new("Equipment", c => c.Group is not ("Flask" or "Jewel")),
        ["equipment_or_jewel"] = new("Equipment or Jewels", c => c.Group != "Flask"),
    };

    /// <summary>Whether an item class fits a target group; no target = any class.</summary>
    public bool ClassMatchesTarget(string itemClass, string? target)
    {
        if (target == null) return true;
        var cls = ItemClasses.FirstOrDefault(c => c.Name.Equals(itemClass, StringComparison.OrdinalIgnoreCase));
        return cls != null && TargetGroups.TryGetValue(target, out var group) && group.Matches(cls);
    }

    /// <summary>Human readable name of a target group.</summary>
    public static string TargetDisplayName(string? target) => target != null && TargetGroups.TryGetValue(target, out var g) ? g.DisplayName : "any item";

    /// <summary>The stronger version of a corruption enchantment on this base (Orb of Sacrifice), or null if it has none.</summary>
    public ModDef? CorruptionUpgradeFor(ModDef enchant, BaseItem? baseItem, string itemClass)
    {
        const string prefix = "Corruption";
        if (enchant.Category != ModCategories.Corrupted || !enchant.Name.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var upgradeName = "CorruptionUpgrade" + enchant.Name[prefix.Length..];
        var pages = PagesFor(baseItem, itemClass);
        return Mods.FirstOrDefault(m => m.Category == ModCategories.CorruptionUpgrade && m.Name == upgradeName && m.Weights.Keys.Any(pages.Contains));
    }

    /// <summary>Pages whose mods and weights apply to a base (its own page, or all pages of its class as a fallback).</summary>
    public IReadOnlyList<string> PagesFor(BaseItem? baseItem, string itemClass)
    {
        if (baseItem?.ModPage is { } p) return new[] { p };
        return PagesOfClass(baseItem?.ItemClass ?? itemClass).ToList();
    }

    /// <summary>The modifier an essence or alloy grants on the given base, or null if it has no effect on that item class.</summary>
    public ModDef? EssenceModFor(EssenceDef essence, BaseItem? baseItem, string itemClass)
    {
        if (!_essenceModsByName.TryGetValue(essence.Name, out var mods)) return null;
        var pages = PagesFor(baseItem, itemClass);
        return mods.FirstOrDefault(m => m.Weights.Keys.Any(pages.Contains));
    }
}
