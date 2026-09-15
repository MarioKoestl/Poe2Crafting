namespace POE2Crafting.Core.Data;

/// <summary>A base item type (e.g. "Siphoning Wand"). Loaded from data/bases.json.</summary>
public sealed class BaseItem
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string ItemClass { get; init; } = "";
    /// <summary>poe2db ModifiersCalc page whose weights apply (e.g. "Wands", "Body_Armours_int"). Null = all pages of the class.</summary>
    public string? ModPage { get; init; }
    /// <summary>Defence type of armour ("Energy Shield", "Armour/Evasion"), "Life"/"Mana" for flasks, "Radius" for jewels; null otherwise.</summary>
    public string? SubType { get; init; }
    public List<string> Tags { get; init; } = new();
    public string? Implicit { get; init; }
    public int? SocketLimit { get; init; }
    public int? Quality { get; init; }
    public bool Hidden { get; init; }
    /// <summary>"level", "str", "dex", "int".</summary>
    public Dictionary<string, int> Requirements { get; init; } = new();
    /// <summary>Base defences: "Armour", "Evasion", "EnergyShield", "Ward", "BlockChance" (%), "MovementPenalty" (fraction).</summary>
    public Dictionary<string, double>? Armour { get; init; }
    /// <summary>"PhysicalMin/Max", "FireMin/Max" (etc.), "CritChanceBase" (%), "AttackRateBase" (attacks per second), "Range", "ReloadTimeBase" (s).</summary>
    public Dictionary<string, double>? Weapon { get; init; }
    public BaseFlaskStats? Flask { get; init; }
    public BaseFlaskStats? Charm { get; init; }

    /// <summary>Runeforged variant of a base (league bases with Ward).</summary>
    public bool IsRuneforged => Tags.Contains("runeforged");

    public override string ToString() => Name;
}

/// <summary>Recovery, duration and charges of a flask or charm base.</summary>
public sealed class BaseFlaskStats
{
    public double? Life { get; init; }
    public double? Mana { get; init; }
    /// <summary>Seconds.</summary>
    public double? Duration { get; init; }
    public int? ChargesUsed { get; init; }
    public int? ChargesMax { get; init; }
}
