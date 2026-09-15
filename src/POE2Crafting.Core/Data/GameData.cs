using System.Text.Json;
using System.Text.Json.Serialization;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Data;

/// <summary>All game data loaded from the JSON data store (data/ folder). Read-only after loading, so it is shared by all sessions.</summary>
public sealed class GameData
{
    /// <summary>Options for reading the data store (camelCase, comments, enums as names).</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string DataFolder { get; }
    public IReadOnlyList<BaseItem> Bases { get; }
    public IReadOnlyList<ModDef> Mods { get; }
    public IReadOnlyList<OmenDef> Omens { get; }
    public IReadOnlyList<CatalystDef> Catalysts { get; }
    public IReadOnlyList<ItemClassDef> ItemClasses { get; }
    public SimConfig Config { get; }
    /// <summary>Runes, soul cores, idols and other augments (from the "socketable" mods).</summary>
    public IReadOnlyList<AugmentDef> Augments { get; }
    /// <summary>Liquid Emotions recipes for instilling notables on amulets (data/instills.json).</summary>
    public IReadOnlyList<InstillRecipe> Instills { get; }
    /// <summary>Curated crafting sequences with explanations (data/guides.json).</summary>
    public IReadOnlyList<CraftingGuide> Guides { get; }
    /// <summary>Items of the Currency Exchange by GGG metadata id (data/exchange_items.json, optional).</summary>
    public IReadOnlyDictionary<string, ExchangeItemDef> ExchangeItems { get; }

    /// <summary>
    /// Every usable crafting item as a currency: currencies.json plus one synthetic currency per essence/alloy/liquid emotion (Op essence),
    /// per catalyst (Op catalyst) and per augment (Op socket_augment), so they all flow through the same engine/UI path. Section groups them for the UI.
    /// </summary>
    public IReadOnlyList<CurrencyDef> AllCurrencies { get; }

    /// <summary>The highest quality an item can reach with quality currencies and catalysts: default maximum plus the best "+% to Maximum Quality" modifier.</summary>
    public int HighestReachableQuality { get; }

    /// <summary>Value multiplier of catalyst quality at <see cref="HighestReachableQuality"/>.</summary>
    public double HighestCatalystFactor => 1 + HighestReachableQuality / 100.0;

    /// <summary>Revealing an unrevealed desecrated modifier, offered like a currency so Omen of Abyssal Echoes can be added (no item is used up).</summary>
    public static readonly CurrencyDef WellOfSouls = new()
    {
        Name = "Well of Souls", Slug = "Abyssal_Depths", Op = CurrencyOps.Reveal, Consumed = false,
        Description = { "Reveals an unrevealed Desecrated modifier: choose one of the offered modifiers (Omen of Abyssal Echoes rerolls the options once)." },
    };

    public const string EssenceSection = "Essences", AlloySection = "Alloys", CatalystSection = "Catalysts",
        LiquidSection = "Liquid Emotions", AugmentSection = "Runes & Soul Cores";

    private readonly Dictionary<string, BaseItem> _baseByName;
    private readonly Dictionary<string, ModDef> _modById;
    private readonly Dictionary<string, CurrencyDef> _currencyByName;
    private readonly ILookup<string, CurrencyDef> _currenciesByOp;
    private readonly Dictionary<string, OmenDef> _omenByName;
    private readonly Dictionary<string, ItemClassDef> _classByName;
    private readonly Dictionary<string, List<ModDef>> _essenceModsByName;
    private readonly Dictionary<string, CraftItemInfo> _craftItemByName;
    private readonly Dictionary<string, InstillRecipe> _instillByNotable;
    /// <summary>Local icon path per poe2db slug (data/icons.json): currencies, omens, augments and base items.</summary>
    private readonly Dictionary<string, string> _iconsBySlug;

    private GameData(string folder, List<BaseItem> bases, List<ModDef> mods, List<CurrencyDef> currencies, List<EssenceDef> essences,
        List<EssenceDef> alloys, List<OmenDef> omens, List<CatalystDef> catalysts, List<ItemClassDef> classes, SimConfig config,
        Dictionary<string, string> iconsBySlug, List<InstillRecipe> instills, List<CraftingGuide> guides, List<ExchangeItemDef> exchangeItems)
    {
        DataFolder = folder;
        Bases = bases; Mods = mods; Omens = omens; Catalysts = catalysts;
        ItemClasses = classes; Config = config; Instills = instills; Guides = guides;
        ExchangeItems = exchangeItems.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
        _baseByName = ByName(bases, b => b.Name);
        _modById = mods.ToDictionary(m => m.Id);
        _omenByName = ByName(omens, o => o.Name);
        _classByName = ByName(classes, c => c.Name);
        _instillByNotable = ByName(instills, i => i.Notable);
        _essenceModsByName = mods.Where(m => ModCategories.EssenceResults.Contains(m.Category))
            .GroupBy(m => m.Name).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        Augments = mods.Where(m => m.Category == ModCategories.Socketable).GroupBy(m => m.Name)
            .Select(g => new AugmentDef { Name = g.Key, Kind = AugmentDef.KindOf(g.Key), Effects = g.ToList() })
            .OrderBy(a => a.Name).ToList();
        AllCurrencies = BuildCurrencies(currencies, essences.Concat(alloys));
        _currencyByName = ByName(AllCurrencies, c => c.Name);
        _currenciesByOp = AllCurrencies.Where(c => c.Op != null).ToLookup(c => c.Op!);
        _iconsBySlug = iconsBySlug;
        _craftItemByName = BuildCraftItemInfo(iconsBySlug);

        HighestReachableQuality = config.Assumptions.DefaultMaxQuality
            + (int)mods.Where(m => m.Family == ModFamilies.MaximumQuality).SelectMany(m => m.StatRanges).Select(r => r.Max()).DefaultIfEmpty(0).Max();
        ComputeTiers(mods);
    }

    private static Dictionary<string, T> ByName<T>(IEnumerable<T> items, Func<T, string> name) =>
        items.GroupBy(name).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    /// <summary>currencies.json plus the synthetic currencies of essences, alloys, liquid emotions, catalysts and augments.</summary>
    private List<CurrencyDef> BuildCurrencies(List<CurrencyDef> currencies, IEnumerable<EssenceDef> essences)
    {
        // Liquid Emotions are plain currencies in currencies.json; on jewels they work like essences (their mods are the "liquid" category)
        var liquids = currencies.Where(c => c.Op == null && _essenceModsByName.TryGetValue(c.Name, out var m) && m[0].Category == ModCategories.Liquid).ToList();
        var liquidEssences = liquids.Select(c => new EssenceDef
        {
            Name = c.Name, Slug = c.Slug, Tier = EssenceTiers.Liquid, RemovesRandomModifier = true, StackSize = c.StackSize,
            RarityIn = new() { nameof(Rarity.Rare) }, Description = c.Description,
        });
        var essenceCurrencies = essences.Concat(liquidEssences).Select(e => new CurrencyDef
        {
            Name = e.Name, Slug = e.Slug, StackSize = e.StackSize, Description = e.Description,
            Section = e.Tier switch { EssenceTiers.Alloy => AlloySection, EssenceTiers.Liquid => LiquidSection, _ => EssenceSection },
            Op = CurrencyOps.Essence, RarityIn = e.RarityIn, Essence = e,
        });
        var catalystCurrencies = Catalysts.Select(k => new CurrencyDef
        {
            Name = k.Name, Slug = k.Slug, Section = CatalystSection, Description = k.Description,
            Op = CurrencyOps.Catalyst, Target = k.ClassTarget, Catalyst = k,
        });
        var augmentCurrencies = Augments.Select(a => new CurrencyDef
        {
            Name = a.Name, Slug = AugmentDef.SlugOf(a.Name), Section = AugmentSection,
            Description = AugmentLines(a), Op = CurrencyOps.SocketAugment, Augment = a,
        });
        return currencies.Except(liquids).Concat(essenceCurrencies).Concat(catalystCurrencies).Concat(augmentCurrencies).Append(WellOfSouls).ToList();
    }

    /// <summary>The local icon of a base item (its id is the poe2db slug, e.g. "Gold_Amulet"), or null.</summary>
    public string? BaseIconUrl(BaseItem? baseItem) => baseItem != null && _iconsBySlug.TryGetValue(baseItem.Id, out var url) ? url : null;

    private Dictionary<string, CraftItemInfo> BuildCraftItemInfo(Dictionary<string, string> iconsBySlug)
    {
        string? Icon(string? slug) => slug != null && iconsBySlug.TryGetValue(slug.Replace("/us/", ""), out var url) ? url : null;
        return ByName(AllCurrencies.Select(c => new CraftItemInfo(c.Name, CurrencyKind(c), Icon(c.Slug), c.Description, CurrencyFacts(c)))
            .Concat(Omens.Select(o => new CraftItemInfo(o.Name, "Omen", Icon(o.Slug), new[] { o.Description },
                o.StackSize != null ? new[] { $"Stack size: {o.StackSize}" } : Array.Empty<string>()))), i => i.Name);
    }

    private static string CurrencyKind(CurrencyDef c) => c.Section switch
    {
        EssenceSection => "Essence",
        AlloySection => "Alloy",
        CatalystSection => "Catalyst",
        LiquidSection => "Liquid Emotion",
        AugmentSection => c.Augment!.Kind,
        _ => "Currency",
    };

    /// <summary>"Martial Weapon / Wand ...: +14% to Fire Resistance" style description of an augment: one line per effect with the classes it applies to.</summary>
    private List<string> AugmentLines(AugmentDef augment) =>
        augment.Effects.Select(e => $"{string.Join(", ", ClassesOfPages(e.Weights.Keys))}: {e.Text}").ToList();

    private IEnumerable<string> ClassesOfPages(IEnumerable<string> pages) =>
        ItemClasses.Where(c => c.ModPages.Any(pages.Contains)).Select(c => c.Name).Distinct();

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
        T Read<T>(string file, bool required = true) where T : new()
        {
            var path = Path.Combine(folder, file);
            if (!File.Exists(path))
                return required ? throw new FileNotFoundException($"Required data file {file} is missing in {folder}.", path) : new T();
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
            Read<Dictionary<string, string>>("icons.json", required: false),
            Read<List<InstillRecipe>>("instills.json", required: false),
            Read<List<CraftingGuide>>("guides.json", required: false),
            Read<List<ExchangeItemDef>>("exchange_items.json", required: false));
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

    // ------------------------------------------------------------------ lookups

    public BaseItem? FindBase(string name) => _baseByName.TryGetValue(name.Trim(), out var b) ? b : null;
    public ModDef? FindMod(string id) => _modById.TryGetValue(id, out var m) ? m : null;
    public CurrencyDef? FindCurrency(string name) => _currencyByName.TryGetValue(name.Trim(), out var c) ? c : null;
    public OmenDef? FindOmen(string name) => _omenByName.TryGetValue(name.Trim(), out var o) ? o : null;
    public ItemClassDef? FindItemClass(string name) => _classByName.TryGetValue(name.Trim(), out var c) ? c : null;
    public InstillRecipe? FindInstill(string notable) => _instillByNotable.TryGetValue(notable.Trim(), out var r) ? r : null;

    /// <summary>Info (kind, icon, description) of a currency, essence, alloy, catalyst or omen by name; null for anything else.</summary>
    public CraftItemInfo? FindCraftItem(string name) => _craftItemByName.TryGetValue(name.Trim(), out var i) ? i : null;

    /// <summary>All currencies (incl. synthetic ones) of one operation.</summary>
    public IEnumerable<CurrencyDef> CurrenciesOf(string op) => _currenciesByOp[op];

    /// <summary>The op of the currency an omen names ("Chaos Orb" → chaos, "Essence"/"Desecration" → the whole group).</summary>
    public string? OpOfOmenTarget(string? targetCurrency) => targetCurrency switch
    {
        null => null,
        "Essence" => CurrencyOps.Essence,
        "Desecration" => CurrencyOps.Desecrate,
        _ => FindCurrency(targetCurrency)?.Op,
    };

    /// <summary>Base whose name appears in a magic item's full name ("Glyphic Siphoning Wand of the Stars"); longest match wins.</summary>
    public BaseItem? FindBaseInName(string fullName) =>
        FindBase(fullName) ?? Bases.Where(b => b.Name.Length > 0 && fullName.Contains(b.Name, StringComparison.OrdinalIgnoreCase))
                                   .OrderByDescending(b => b.Name.Length).FirstOrDefault();

    public IEnumerable<BaseItem> BasesOfClass(string itemClass) => Bases.Where(b => b.ItemClass.Equals(itemClass, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether an item class fits a target group (<see cref="ClassTargets"/>); no target = any class.</summary>
    public bool ClassMatchesTarget(string itemClass, string? target) => ClassTargets.Contains(target, FindItemClass(itemClass));

    /// <summary>Pages whose mods and weights apply to a base (its own page, or all pages of its class as a fallback).</summary>
    public IReadOnlyList<string> PagesFor(BaseItem? baseItem, string itemClass) =>
        baseItem?.ModPage is { } page ? new[] { page } : FindItemClass(baseItem?.ItemClass ?? itemClass)?.ModPages ?? new List<string>();

    private List<ModDef> ModsOnPages(IEnumerable<ModDef> mods, BaseItem? baseItem, string itemClass)
    {
        var pages = PagesFor(baseItem, itemClass);
        return mods.Where(m => m.IsOnAnyPage(pages)).ToList();
    }

    /// <summary>The stronger version of a corruption enchantment on this base (Orb of Sacrifice), or null if it has none.</summary>
    public ModDef? CorruptionUpgradeFor(ModDef enchant, BaseItem? baseItem, string itemClass)
    {
        const string prefix = "Corruption";
        if (enchant.Category != ModCategories.Corrupted || !enchant.Name.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var upgradeName = "CorruptionUpgrade" + enchant.Name[prefix.Length..];
        return ModsOnPages(Mods.Where(m => m.Category == ModCategories.CorruptionUpgrade && m.Name == upgradeName), baseItem, itemClass).FirstOrDefault();
    }

    // ------------------------------------------------------------------ essences, catalysts, augments, instills

    /// <summary>
    /// The modifiers an essence, alloy or liquid emotion can grant on the given base (empty if it has no effect on that item class).
    /// Usually one; several are equally likely outcomes (e.g. Potent Liquid Contempt: a prefix or a suffix).
    /// </summary>
    public IReadOnlyList<ModDef> EssenceModsFor(EssenceDef essence, BaseItem? baseItem, string itemClass) =>
        _essenceModsByName.TryGetValue(essence.Name, out var mods) ? ModsOnPages(mods, baseItem, itemClass) : Array.Empty<ModDef>();

    /// <summary>The modifiers an essence currency can grant on the item (empty for other currencies).</summary>
    public IReadOnlyList<ModDef> EssenceModsFor(CurrencyDef currency, Item item) =>
        currency.Essence is { } essence ? EssenceModsFor(essence, item.Base, item.ItemClass) : Array.Empty<ModDef>();

    /// <summary>Catalysts whose quality enhances the mod.</summary>
    public IEnumerable<CatalystDef> CatalystsEnhancing(ModDef? mod) => Catalysts.Where(c => c.Enhances(mod));

    /// <summary>Catalyst quality types that can be applied to items of this base.</summary>
    public IEnumerable<string> CatalystQualityTypesFor(BaseItem baseItem) =>
        Catalysts.Where(c => c.QualityType != null && ClassMatchesTarget(baseItem.ItemClass, c.ClassTarget)).Select(c => c.QualityType!).Distinct();

    /// <summary>The catalyst quality type of a quality line as the game prints it ("Life Modifiers" → Life), or the text itself when no catalyst matches.</summary>
    public string CanonicalQualityType(string qualityType) =>
        Catalysts.FirstOrDefault(c => c.ModTag != null && c.ModTag == CatalystDef.QualityTagFor(qualityType))?.QualityType ?? qualityType;

    /// <summary>The effects an augment grants on the given base (empty if it cannot be socketed into that item class).</summary>
    public IReadOnlyList<ModDef> AugmentEffectsFor(AugmentDef augment, BaseItem? baseItem, string itemClass) => ModsOnPages(augment.Effects, baseItem, itemClass);

    /// <summary>The socket line an augment adds to an item of this base (all its effects for the class), or null if it doesn't fit the class.</summary>
    public string? AugmentEffectText(AugmentDef augment, BaseItem? baseItem, string itemClass) =>
        AugmentEffectsFor(augment, baseItem, itemClass) is { Count: > 0 } effects
            ? string.Join(", ", effects.Select(e => ModText.RenderMid(e.Text)))
            : null;

    /// <summary>Augments that can be socketed into items of this base.</summary>
    public IEnumerable<AugmentDef> AugmentsFor(BaseItem? baseItem, string itemClass) => Augments.Where(a => AugmentEffectsFor(a, baseItem, itemClass).Count > 0);

    /// <summary>Whether notables can be instilled on items of this class.</summary>
    public static bool CanInstill(string itemClass) => itemClass.Equals(ClassTargets.InstillClass, StringComparison.OrdinalIgnoreCase);

    /// <summary>The (non-ancient) Liquid Emotion currency of a short emotion name used in instill recipes, e.g. "Ire" → Diluted Liquid Ire.</summary>
    public CurrencyDef? EmotionCurrency(string emotion) =>
        AllCurrencies.FirstOrDefault(c => c.Section == LiquidSection && !c.Name.StartsWith("Ancient ", StringComparison.Ordinal)
                                          && c.Name.EndsWith("Liquid " + emotion, StringComparison.OrdinalIgnoreCase));

    /// <summary>The Liquid Emotions an instill consumes (currency name → count).</summary>
    public Dictionary<string, int> InstillMaterials(InstillRecipe recipe) => recipe.Emotions
        .Select(e => EmotionCurrency(e)?.Name ?? $"Liquid {e}")
        .GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count());
}
