namespace POE2Crafting.Core.Items;

/// <summary>The vocabulary of the in-game item text (Ctrl+Alt+C), shared by <see cref="ItemParser"/>, <see cref="ItemTextWriter"/> and <see cref="ItemDiff"/>.</summary>
public static class ItemTextFormat
{
    public const string Separator = "--------";
    public const string GrantsSkill = "Grants Skill:";
    public const string RuneMarker = "rune";
    public const string FracturedMarker = "fractured";

    /// <summary>Kinds with their trailing line marker ("+20 to Strength (crafted)") and modifier header flag ("{ Crafted Prefix Modifier ... }").</summary>
    private static readonly (ModKind Kind, string Marker, string HeaderFlag)[] Kinds =
    {
        (ModKind.Implicit, "implicit", "Implicit"),
        (ModKind.Enchant, "enchant", "Enchant"),
        (ModKind.CorruptedImplicit, "enchant", "Corrupted"),
        (ModKind.Crafted, "crafted", "Crafted"),
        (ModKind.Desecrated, "desecrated", "Desecrated"),
    };

    /// <summary>The marker of a kind ("crafted"), or null for explicit lines.</summary>
    public static string? Marker(ModKind kind) => Kinds.Where(k => k.Kind == kind).Select(k => k.Marker).FirstOrDefault();

    /// <summary>The kind of a line marker; unknown markers (and "fractured") are explicit lines. "enchant" = enchantment (a corruption enchantment once resolved).</summary>
    public static ModKind KindOfMarker(string marker) =>
        Kinds.Where(k => k.Marker.Equals(marker, StringComparison.OrdinalIgnoreCase)).Select(k => k.Kind).DefaultIfEmpty(ModKind.Explicit).First();

    /// <summary>Precedence when a header carries several flags ("Corrupted Implicit").</summary>
    private static readonly ModKind[] HeaderPrecedence = { ModKind.CorruptedImplicit, ModKind.Implicit, ModKind.Enchant, ModKind.Crafted, ModKind.Desecrated };

    /// <summary>The kind named by the flags of a modifier header; "Unrevealed" means desecrated.</summary>
    public static ModKind KindOfHeaderFlags(string flags) =>
        flags.Contains("Unrevealed") ? ModKind.Desecrated
        : HeaderPrecedence.Where(kind => flags.Contains(Kinds.First(k => k.Kind == kind).HeaderFlag)).DefaultIfEmpty(ModKind.Explicit).First();

    /// <summary>The header flag of a kind followed by a space ("Crafted "), empty for explicit mods.</summary>
    public static string HeaderFlag(ModKind kind) => Kinds.Where(k => k.Kind == kind).Select(k => k.HeaderFlag + " ").FirstOrDefault() ?? "";
}
