using POE2Crafting.Core.Data;

namespace POE2Crafting.Web.Services;

/// <summary>Maps currency names to poe2db CDN icon URLs.</summary>
public static class CurrencyIconHelper
{
    private const string CdnBase = "https://cdn.poe2db.tw/image/Art/2DItems/Currency/";

    private static readonly Dictionary<string, string> NameToPath = new(StringComparer.OrdinalIgnoreCase)
    {
        // ---- Basic currencies ----
        ["Chaos Orb"] = "CurrencyRerollRare",
        ["Greater Chaos Orb"] = "CurrencyRerollRare",
        ["Perfect Chaos Orb"] = "CurrencyRerollRare",
        ["Exalted Orb"] = "CurrencyAddModToRare",
        ["Greater Exalted Orb"] = "CurrencyAddModToRare",
        ["Perfect Exalted Orb"] = "CurrencyAddModToRare",
        ["Orb of Transmutation"] = "CurrencyUpgradeToMagic",
        ["Greater Orb of Transmutation"] = "CurrencyUpgradeToMagic",
        ["Perfect Orb of Transmutation"] = "CurrencyUpgradeToMagic",
        ["Orb of Alchemy"] = "CurrencyUpgradeToRare",
        ["Orb of Augmentation"] = "CurrencyAddModToMagic",
        ["Greater Orb of Augmentation"] = "CurrencyAddModToMagic",
        ["Perfect Orb of Augmentation"] = "CurrencyAddModToMagic",
        ["Regal Orb"] = "CurrencyUpgradeMagicToRare",
        ["Greater Regal Orb"] = "CurrencyUpgradeMagicToRare",
        ["Perfect Regal Orb"] = "CurrencyUpgradeMagicToRare",
        ["Divine Orb"] = "CurrencyModValues",
        ["Orb of Annulment"] = "AnnullOrb",
        ["Vaal Orb"] = "CurrencyVaal",
        ["Mirror of Kalandra"] = "CurrencyDuplicate",
        ["Orb of Chance"] = "CurrencyUpgradeToUnique",
        ["Scroll of Wisdom"] = "CurrencyIdentification",

        // ---- Quality currencies ----
        ["Blacksmith's Whetstone"] = "CurrencyWeaponQuality",
        ["Armourer's Scrap"] = "CurrencyArmourQuality",
        ["Glassblower's Bauble"] = "CurrencyFlaskQuality",
        ["Arcanist's Etcher"] = "CurrencyWeaponMagicQuality",

        // ---- Socket / special ----
        ["Artificer's Orb"] = "CurrencyAddEquipmentSocket",
        ["Fracturing Orb"] = "FracturingOrb",
        ["Hinekora's Lock"] = "HinekorasLock",

        // ---- Incursion currencies ----
        ["Architect's Orb"] = "IncursionCraftingOrbs/IncursionGreaterVaalOrb",
        ["Orb of Extraction"] = "IncursionCraftingOrbs/IncursionSocketableExtractorCurrency",
        ["Vaal Blacksmith's Infuser"] = "IncursionCraftingOrbs/VaalBlacksmithsWhetstone",
        ["Vaal Armourer's Infuser"] = "IncursionCraftingOrbs/VaakArmourersScrap",
        ["Vaal Arcanist's Infuser"] = "IncursionCraftingOrbs/VaalArcanistsEtcher",
        ["Vaal Catalysing Infuser"] = "IncursionCraftingOrbs/VaalCatalyst",

        // ---- Sacrifice orbs ----
        ["Kamasa's Orb of Sacrifice"] = "IncursionCraftingOrbs/IncursionCorruptionOrb3",
        ["Kopec's Orb of Sacrifice"] = "IncursionCraftingOrbs/IncursionCorruptionOrb2",
        ["Yaomac's Orb of Sacrifice"] = "IncursionCraftingOrbs/IncursionCorruptionOrb1",
        ["Yugul's Orb of Sacrifice"] = "IncursionCraftingOrbs/IncursionCorruptionOrb4",

        // ---- Flux currencies ----
        ["Blazing Flux"] = "Expedition2/ArcaneFluxFire",
        ["Chilling Flux"] = "Expedition2/ArcaneFluxCold",
        ["Crackling Flux"] = "Expedition2/ArcaneFluxLightning",
        ["Void Flux"] = "Expedition2/ArcaneFlux",

        // ---- Desecration bones ----
        ["Preserved Rib"] = "Abyss/PreservedRibs",
        ["Preserved Jawbone"] = "Abyss/PreservedJawbone",
        ["Preserved Collarbone"] = "Abyss/PreservedCalvicle",
        ["Preserved Cranium"] = "Abyss/PreservedCranium",
        ["Gnawed Rib"] = "Abyss/GnawedRibs",
        ["Gnawed Jawbone"] = "Abyss/GnawedJawbone",
        ["Gnawed Collarbone"] = "Abyss/GnawedClavicle",
        ["Ancient Rib"] = "Abyss/AncientRibs",
        ["Ancient Jawbone"] = "Abyss/AncientJawbone",
        ["Ancient Collarbone"] = "Abyss/AncientClavicle",
        ["Altered Collarbone"] = "Breach/BreachDesecration",
    };

    /// <summary>Returns the poe2db CDN URL for a currency icon, or null if unknown.</summary>
    public static string? GetIconUrl(CurrencyDef currency)
    {
        if (NameToPath.TryGetValue(currency.Name, out var path))
            return $"{CdnBase}{path}.webp";
        return null;
    }

    /// <summary>Returns the poe2db CDN URL or an empty string (for safe use in img src).</summary>
    public static string GetIconUrlOrEmpty(CurrencyDef currency)
        => GetIconUrl(currency) ?? "";
}
