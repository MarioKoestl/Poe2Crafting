namespace POE2Crafting.Core.Data;

/// <summary>
/// An item of the Currency Exchange (data/exchange_items.json, tools/poe2_exchange_items.py): GGG's metadata id as the exchange API
/// names it ("Metadata/Items/Currency/CurrencyModValues"), the in-game name, the item class and the local icon.
/// </summary>
public sealed class ExchangeItemDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ItemClass { get; set; } = "";
    public string? Icon { get; set; }
}
