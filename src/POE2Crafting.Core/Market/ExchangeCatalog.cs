using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using POE2Crafting.Core.Data;

namespace POE2Crafting.Core.Market;

/// <summary>The currencies every item can be priced in and converted through on the exchange (GGG metadata ids).</summary>
public static class ExchangeCurrencies
{
    public const string Chaos = "Metadata/Items/Currency/CurrencyRerollRare";
    public const string Divine = "Metadata/Items/Currency/CurrencyModValues";
    public const string Exalted = "Metadata/Items/Currency/CurrencyAddModToRare";

    /// <summary>Reference currencies of the Market page and intermediate currencies of indirect routes.</summary>
    public static readonly IReadOnlyList<string> Core = new[] { Divine, Exalted, Chaos };
}

/// <summary>Groups of the Market page.</summary>
public static class ExchangeCategories
{
    public const string Currency = "Currency", Essences = "Essences & Alloys", Omens = "Omens", Catalysts = "Catalysts",
        Augments = "Runes & Soul Cores", Liquid = "Liquid Emotions", Gems = "Gems", Fragments = "Fragments & Keys", Other = "Other";

    private static readonly string[] Ordered = { Currency, Essences, Omens, Catalysts, Augments, Liquid, Gems, Fragments, Other };

    public static IReadOnlyList<string> All => Ordered;

    /// <summary>Position of a category in <see cref="All"/>.</summary>
    public static int Order(string category) => Array.IndexOf(Ordered, category);

    private static readonly HashSet<string> FragmentClasses = new()
        { "MapFragment", "VaultKey", "PinnacleKeyStackable", "Breachstone", "AtlasCurrency", "Expedition2Logbooks" };

    /// <summary>Kind of a simulator crafting item first (essence, catalyst, rune...), otherwise the item class of the exchange data.</summary>
    public static string Of(CraftItemInfo? craft, string? itemClass) => craft?.Kind switch
    {
        "Currency" => Currency,
        "Essence" or "Alloy" => Essences,
        "Omen" => Omens,
        "Catalyst" => Catalysts,
        "Liquid Emotion" => Liquid,
        not null => Augments,
        null => itemClass switch
        {
            "StackableCurrency" or "IncubatorStackable" => Currency,
            "Omen" => Omens,
            "SoulCore" => Augments,
            { } c when c.Contains("Gem", StringComparison.Ordinal) => Gems,
            { } c when FragmentClasses.Contains(c) => Fragments,
            _ => Other,
        },
    };
}

/// <summary>An exchange item as the Market page shows it.</summary>
/// <param name="IsCraftingItem">The simulator knows it (currency, essence, omen, catalyst, rune...).</param>
public sealed record ExchangeItem(string Id, string Name, string? IconUrl, string Category, bool IsCraftingItem);

/// <summary>Names, icons and categories of exchange items from data/exchange_items.json and the simulator's crafting items.</summary>
public sealed class ExchangeCatalog(GameData data)
{
    private readonly ConcurrentDictionary<string, ExchangeItem> _items = new();

    public ExchangeItem Item(string id) => _items.GetOrAdd(id, Describe);

    /// <summary>"direct" or "via Exalted Orb".</summary>
    public string RouteText(ExchangeQuote quote) => quote.Via is { } via ? $"via {Item(via).Name}" : "direct";

    private ExchangeItem Describe(string id)
    {
        var def = data.ExchangeItems.GetValueOrDefault(id);
        var name = def?.Name ?? NameFromId(id);
        var craft = data.FindCraftItem(name);
        return new ExchangeItem(id, name, def?.Icon ?? craft?.IconUrl, ExchangeCategories.Of(craft, def?.ItemClass), craft != null);
    }

    /// <summary>"Metadata/Items/Currency/CurrencyRerollRare" → "Currency Reroll Rare" for items missing in exchange_items.json.</summary>
    public static string NameFromId(string id) => Regex.Replace(id[(id.LastIndexOf('/') + 1)..], "(?<=[a-z])(?=[A-Z0-9])", " ");
}
