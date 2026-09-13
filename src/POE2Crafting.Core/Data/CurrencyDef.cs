using System.Text.Json.Serialization;

namespace POE2Crafting.Core.Data;

/// <summary>A currency the engine can apply (currencies.json, plus synthetic ones for essences, catalysts and augments — see GameData.AllCurrencies).</summary>
public sealed class CurrencyDef
{
    public string Name { get; init; } = "";
    public string? Slug { get; init; }
    public string? Section { get; init; }
    public string? StackSize { get; init; }
    public int? MinModLevel { get; init; }
    public int? MaxItemLevel { get; init; }
    public List<string> Description { get; init; } = new();
    /// <summary>Engine operation id (<see cref="CurrencyOps"/>). Null = not simulated.</summary>
    public string? Op { get; init; }
    public List<string>? RarityIn { get; init; }
    public int? MinMods { get; init; }
    public bool? RequiresCorrupted { get; init; }
    public string? Target { get; init; }
    public string? QualityTarget { get; init; }
    public string? Element { get; init; }
    public bool? Otherworldly { get; init; }

    /// <summary>For <see cref="CurrencyOps.Essence"/>: the essence, alloy or liquid emotion this synthetic currency applies.</summary>
    [JsonIgnore] public EssenceDef? Essence { get; init; }

    /// <summary>For <see cref="CurrencyOps.Catalyst"/>: the catalyst this synthetic currency applies.</summary>
    [JsonIgnore] public CatalystDef? Catalyst { get; init; }

    /// <summary>For <see cref="CurrencyOps.SocketAugment"/>: the rune, soul core or idol this synthetic currency sockets.</summary>
    [JsonIgnore] public AugmentDef? Augment { get; init; }

    /// <summary>Class group the currency can be used on (Target, or QualityTarget for quality currencies).</summary>
    [JsonIgnore] public string? ClassTarget => Target ?? QualityTarget;

    [JsonIgnore] public string DescriptionText => string.Join(" ", Description);
    public override string ToString() => Name;
}

/// <summary>Operation ids of <see cref="CurrencyDef.Op"/>; each has exactly one CraftOperation in the engine.</summary>
public static class CurrencyOps
{
    public const string Transmute = "transmute", Augment = "augment", Regal = "regal", Exalt = "exalt";
    public const string Alchemy = "alchemy", Chaos = "chaos", Annul = "annul", Divine = "divine", Chance = "chance", Fracture = "fracture";
    public const string Mirror = "mirror", Identify = "identify", Lock = "lock";
    public const string Essence = "essence", Desecrate = "desecrate";
    public const string Vaal = "vaal", Sacrifice = "sacrifice", Architect = "architect";
    public const string Quality = "quality", VaalQuality = "vaal_quality", Catalyst = "catalyst";
    public const string Socket = "socket", Extract = "extract", Flux = "flux", SocketAugment = "socket_augment";
}

/// <summary>A crafting item (currency, essence, alloy, catalyst or omen) with what the UI shows about it: kind, icon, description and rule facts.</summary>
public sealed record CraftItemInfo(string Name, string Kind, string? IconUrl, IReadOnlyList<string> Description, IReadOnlyList<string> Facts);
