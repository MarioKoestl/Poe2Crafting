using System.Text;
using POE2Crafting.Core.Data;

namespace POE2Crafting.Core.Items;

/// <summary>Writes an item in the in-game Ctrl+Alt+C text format; <see cref="ItemParser.Parse"/> reads it back.</summary>
public static class ItemTextWriter
{
    /// <param name="tierOf">Tier to print for a mod (e.g. ModPool.DisplayTier); defaults to the global tier.</param>
    public static string ToText(Item item, Func<ModDef, int>? tierOf = null)
    {
        tierOf ??= d => d.Tier;
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(item.ItemClass)) sb.AppendLine($"Item Class: {item.ItemClass}");
        sb.AppendLine($"Rarity: {item.Rarity}");
        if (item.Name != null) sb.AppendLine(item.Name);
        sb.AppendLine(item.BaseName);

        Section(sb);
        if (item.Quality > 0) sb.AppendLine($"Quality: +{item.Quality}%{(item.QualityType != null ? $" ({item.QualityType})" : "")}");
        if (item.Sockets > 0) sb.AppendLine($"Sockets: {string.Join(" ", Enumerable.Repeat("S", item.Sockets))}");
        sb.AppendLine($"Item Level: {item.ItemLevel}");

        if (item.Runes.Count > 0)
        {
            Section(sb);
            foreach (var rune in item.Runes) sb.AppendLine($"{rune} ({ItemTextFormat.RuneMarker})");
        }

        var implicits = item.Mods.Where(m => m.Kind.IsImplicitLine()).ToList();
        if (implicits.Count > 0)
        {
            Section(sb);
            foreach (var mod in implicits) sb.AppendLine(ImplicitLine(mod));
        }

        var affixes = item.Affixes.OrderBy(m => m.Affix).ToList();
        if (affixes.Count > 0)
        {
            Section(sb);
            foreach (var mod in affixes)
            {
                var flags = (mod.Fractured ? "Fractured " : "") + (mod.Unrevealed ? "Unrevealed " : ItemTextFormat.HeaderFlag(mod.Kind));
                var tier = mod.Def != null && mod.Kind == ModKind.Explicit ? $" (Tier: {tierOf(mod.Def)})" : "";
                sb.AppendLine($"{{ {flags}{mod.Affix} Modifier \"{mod.Def?.Name ?? mod.ModId}\"{tier} }}");
                if (!mod.Unrevealed) sb.AppendLine(mod.AdvancedText());
            }
        }

        foreach (var (flag, text) in new[] { (!item.Identified, "Unidentified"), (item.Corrupted, item.TwiceCorrupted ? "Twice Corrupted" : "Corrupted"), (item.Mirrored, "Mirrored"), (item.Sanctified, "Sanctified") })
            if (flag)
            {
                Section(sb);
                sb.AppendLine(text);
            }
        return sb.ToString().TrimEnd();
    }

    private static void Section(StringBuilder sb) => sb.AppendLine(ItemTextFormat.Separator);

    /// <summary>An implicit-like line with its marker; "Grants Skill:" lines are implicit without one.</summary>
    private static string ImplicitLine(ItemMod mod)
    {
        var text = mod.AdvancedText();
        bool unmarked = mod.Kind == ModKind.Implicit && text.StartsWith(ItemTextFormat.GrantsSkill, StringComparison.Ordinal);
        return unmarked ? text : $"{text} ({ItemTextFormat.Marker(mod.Kind)})";
    }
}
