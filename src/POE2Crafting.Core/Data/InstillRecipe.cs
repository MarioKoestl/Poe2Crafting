using System.Text.Json.Serialization;

namespace POE2Crafting.Core.Data;

/// <summary>A Liquid Emotions recipe: three emotions in this order instill a notable passive on an amulet (data/instills.json).</summary>
public sealed class InstillRecipe
{
    /// <summary>The enchantment line of an instilled notable starts with this.</summary>
    public const string EnchantPrefix = "Allocates ";

    public string Notable { get; init; } = "";
    /// <summary>Short emotion names in order, e.g. ["Ire", "Guilt", "Ire"].</summary>
    public List<string> Emotions { get; init; } = new();
    public List<string> Effects { get; init; } = new();

    /// <summary>The enchantment line on the amulet.</summary>
    [JsonIgnore] public string EnchantText => EnchantTextFor(Notable);

    public static string EnchantTextFor(string notable) => EnchantPrefix + notable;

    /// <summary>Name of the instill action in histories and plans ("Instill Flamekeeper").</summary>
    private const string ActionPrefix = "Instill ";

    [JsonIgnore] public string ActionName => ActionPrefix + Notable;

    /// <summary>The notable of an instill action name, or null for other actions.</summary>
    public static string? NotableOfAction(string action) =>
        action.StartsWith(ActionPrefix, StringComparison.Ordinal) ? action[ActionPrefix.Length..] : null;

    /// <summary>The notable of an enchantment line ("Allocates Flamekeeper" → Flamekeeper), or null for other lines.</summary>
    public static string? NotableOf(string enchantText) =>
        enchantText.StartsWith(EnchantPrefix, StringComparison.Ordinal) ? enchantText[EnchantPrefix.Length..] : null;
}
