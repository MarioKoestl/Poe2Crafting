using POE2Crafting.Core.Data;

namespace POE2Crafting.Web.Services;

/// <summary>Display helpers for prefix/suffix markers used across components.</summary>
public static class AffixUi
{
    /// <summary>"P" / "S", "I" for implicits and enchantments.</summary>
    public static string Letter(AffixType type) => type switch { AffixType.Prefix => "P", AffixType.Suffix => "S", _ => "I" };

    /// <summary>CSS classes: "prefix"/"suffix"/"implicit" plus an optional suffix such as "-row".</summary>
    public static string Css(AffixType type, string suffix = "") => type switch { AffixType.Prefix => "prefix", AffixType.Suffix => "suffix", _ => "implicit" } + suffix;
    public static string TypeCss(AffixType type) => "type-" + Css(type);
}
