namespace POE2Crafting.Core.Data;

/// <summary>A rune, soul core, idol or other augment that can be socketed into an augment socket; built from the "socketable" mods of mods.json.</summary>
public sealed class AugmentDef
{
    public string Name { get; init; } = "";
    /// <summary>Rune, Soul Core, Idol, Abyssal Eye or Augment.</summary>
    public string Kind { get; init; } = "";
    /// <summary>One mod per effect line, each with the class pages it applies to.</summary>
    public List<ModDef> Effects { get; init; } = new();

    public static string KindOf(string name) =>
        name.Contains("Rune") ? "Rune"
        : name.Contains("Soul Core") || name.EndsWith("Thesis", StringComparison.Ordinal) ? "Soul Core"
        : name.Contains("Idol") || name.StartsWith("Carved ", StringComparison.Ordinal) ? "Idol"
        : name.EndsWith("Gaze", StringComparison.Ordinal) ? "Abyssal Eye"
        : "Augment";

    /// <summary>Slug of the synthetic augment currency (icons.json key); tools/poe2db_icons.py builds the same slugs.</summary>
    public static string SlugOf(string name) => name.Replace("'", "").Replace(' ', '_');

    public override string ToString() => Name;
}
