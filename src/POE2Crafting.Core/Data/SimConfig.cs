using System.Text.Json.Serialization;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Data;

/// <summary>data/config.json: the simulator's assumptions about mechanics the data store doesn't contain.</summary>
public sealed class SimConfig
{
    public SimAssumptions Assumptions { get; init; } = new();
}

/// <summary>How an addition picks prefix vs. suffix when both are possible.</summary>
public enum AffixTypeSelection
{
    /// <summary>P(prefix) proportional to the total prefix weight vs. the total suffix weight.</summary>
    Weighted,
    /// <summary>50/50.</summary>
    Equal,
}

public sealed class SimAssumptions
{
    public string WeightsSource { get; init; } = "";
    public AffixTypeSelection AffixTypeSelection { get; init; } = AffixTypeSelection.Weighted;
    public int MagicMaxPrefixes { get; init; } = 1;
    public int MagicMaxSuffixes { get; init; } = 1;
    public int RareMaxPrefixes { get; init; } = 3;
    public int RareMaxSuffixes { get; init; } = 3;
    public int AlchemyModCount { get; init; } = 4;
    public bool AlchemyOnMagicKeepsExistingMods { get; init; } = true;
    /// <summary>Vaal Orb outcome weights by id: no_change, corrupted_implicit, reroll_mods, add_socket_or_quality.</summary>
    public Dictionary<string, double> VaalOutcomes { get; init; } = new();
    public string? VaalOutcomesNote { get; init; }
    /// <summary>"1-3 affixes are randomized": how many mods the reroll outcome replaces (uniform in range).</summary>
    public int VaalRerollMinMods { get; init; } = 1;
    public int VaalRerollMaxMods { get; init; } = 3;
    /// <summary>Quality cap of the wand/staff quality outcome.</summary>
    public int VaalQualityCap { get; init; } = 23;
    public double ArchitectSuccessChance { get; init; } = 0.5;
    /// <summary>Orb of Chance: chance to turn the item unique instead of magic.</summary>
    public double ChanceUniqueChance { get; init; } = 0.05;
    /// <summary>Fracturing Orb: minimum number of affixes when the currency data doesn't say.</summary>
    public int FractureMinMods { get; init; } = 4;

    /// <summary>Maximum quality when the base does not define one.</summary>
    public int DefaultMaxQuality { get; init; } = 20;
    /// <summary>Quality a catalyst adds per use.</summary>
    public int CatalystQualityPerUse { get; init; } = 5;
    /// <summary>How far Vaal Infusers can exceed the maximum quality.</summary>
    public int InfuserOverCap { get; init; } = 10;
    /// <summary>Infuser corruption chance per point of starting quality above the maximum (0 at or below the maximum).</summary>
    public double InfuserCorruptChancePerQuality { get; init; } = 0.05;
    /// <summary>Omen of Catalysing Exaltation: weight multiplier for matching mods = 1 + quality × this.</summary>
    public double CatalysingWeightBonusPerQuality { get; init; } = 0.05;
    /// <summary>Quality added by one quality currency / Vaal Infuser per item rarity.</summary>
    public Dictionary<Rarity, int> QualityPerUse { get; init; } = new();
    public string? QualityPerUseNote { get; init; }
    public bool OnlyOneCraftedModPerItem { get; init; } = true;
    /// <summary>Number of options offered when revealing a desecrated modifier at the Well of Souls.</summary>
    public int RevealOptionCount { get; init; } = 3;
    /// <summary>Options of a reveal that are always exclusive Lich (desecrated) modifiers ("at least one", expertgamereviews.com).</summary>
    public int RevealGuaranteedExclusiveOptions { get; init; } = 1;
    /// <summary>Chance that each further option is a regular modifier instead of a Lich modifier (poe2db: "may include base modifiers"). ASSUMPTION, no data.</summary>
    public double RevealRegularOptionChance { get; init; } = 0.5;
    /// <summary>
    /// True (poe2db: "Reveal desecrated modifiers may include base modifiers. Unless you use Omen to guarantee named modifiers"; Mario in game:
    /// 3 Kurgal options with Omen of the Blackblooded): with Omen of the Sovereign/Liege/Blackblooded every option is a modifier of that Lich.
    /// False: only the exclusive option is, the others stay regular.
    /// </summary>
    public bool RevealBossOmenOnlyLichModifiers { get; init; } = true;
    public string? RevealNote { get; init; }
    /// <summary>Unrevealed modifiers created by Omen of Putrefaction ("up to 6").</summary>
    public int PutrefactionUnrevealedCount { get; init; } = 6;

    /// <summary>Rare prefix and suffix limit (each) for item classes that differ from the default, e.g. Jewel = 2.</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, int> RareMaxAffixesPerTypeByClass { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Socketing an augment into an item without a free socket replaces the chosen socketed augment.</summary>
    public bool AugmentReplacesOccupiedSocket { get; init; } = true;
    public bool AugmentWorksOnCorrupted { get; init; } = true;
    /// <summary>Instilling an amulet that already has an instilled notable replaces it.</summary>
    public bool InstillReplacesExisting { get; init; } = true;
    public string? AugmentInstillNote { get; init; }

    private int DefaultMaxAffixes(Rarity r, AffixType type) => (r, type) switch
    {
        (Rarity.Magic, AffixType.Prefix) => MagicMaxPrefixes,
        (Rarity.Magic, AffixType.Suffix) => MagicMaxSuffixes,
        (Rarity.Rare, AffixType.Prefix) => RareMaxPrefixes,
        (Rarity.Rare, AffixType.Suffix) => RareMaxSuffixes,
        _ => 0,
    };

    /// <summary>
    /// Affix limit of one type for an item of <paramref name="itemClass"/> at rarity <paramref name="r"/>, plus the extra slots its mods grant
    /// (e.g. "+1 Suffix Modifier allowed"). The single source of slot limits: never compare against the plain rarity limits directly.
    /// </summary>
    public int MaxAffixes(string itemClass, Rarity r, AffixType type, IEnumerable<string> modTexts)
    {
        int limit = r == Rarity.Rare && type != AffixType.Other && RareMaxAffixesPerTypeByClass.TryGetValue(itemClass, out var byClass)
            ? byClass
            : DefaultMaxAffixes(r, type);
        return limit == 0 ? 0 : limit + modTexts.Sum(t => ModText.ExtraAffixesAllowed(t, type));
    }

    public int MaxAffixes(Item item, Rarity r, AffixType type) => MaxAffixes(item.ItemClass, r, type, item.Affixes.Select(m => m.DisplayText()));
}
