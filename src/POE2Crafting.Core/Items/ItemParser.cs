using System.Text.RegularExpressions;
using POE2Crafting.Core.Data;

namespace POE2Crafting.Core.Items;

/// <summary>
/// Parses the in-game Ctrl+Alt+C item text into an <see cref="Item"/>.
/// Sections are separated by "--------". Handles:
///   - Header section (rarity, name, base)
///   - Properties (quality, sockets, item level)
///   - Modifier lines with optional headers: { Prefix Modifier "Name" (Tier: N) — Tag, Tag }
///   - Stat lines with optional (min-max) ranges
///   - Footer flags (Corrupted, Mirrored, etc.)
/// </summary>
public static class ItemParser
{
    private static readonly Regex SectionSep = new(@"^-{4,}$", RegexOptions.Compiled);
    private static readonly Regex ModHeader = new(@"^\{\s*(Prefix|Suffix)\s+Modifier\s+""([^""]+)""\s*(?:\(Tier:\s*(\d+)\))?\s*(?:—\s*(.*))?\s*\}$", RegexOptions.Compiled);
    private static readonly Regex CraftedHeader = new(@"^\{\s*Crafted\s+Modifier\s+""([^""]+)""\s*(?:—\s*(.*))?\s*\}$", RegexOptions.Compiled);
    private static readonly Regex StatLine = new(@"^([+-]?\d+(?:\.\d+)?)\s*(?:\((\d+(?:\.\d+)?)-(\d+(?:\.\d+)?)\))?\s*(.*)", RegexOptions.Compiled);
    private static readonly Regex QualityRx = new(@"Quality:\s*\+(\d+)%(?:\s*\((.+?)\))?", RegexOptions.Compiled);
    private static readonly Regex ItemLevelRx = new(@"Item Level:\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex SocketsRx = new(@"Sockets:\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex ImplicitHeader = new(@"^\{\s*Implicit\s+Modifier\s+""([^""]*)""\s*\}", RegexOptions.Compiled);
    private static readonly Regex CorruptedImplicitHeader = new(@"^\{\s*Corrupted\s+Implicit\s+Modifier", RegexOptions.Compiled);

    /// <summary>Parse a Ctrl+Alt+C item text. Optionally bind to game data for ModDef resolution.</summary>
    public static Item Parse(string text, GameData? data = null)
    {
        var item = new Item();
        var sections = SplitSections(text);
        if (sections.Count == 0) throw new FormatException("No item text to parse.");

        ParseHeaderSection(item, sections[0]);

        for (int s = 1; s < sections.Count; s++)
            ParseSection(item, sections[s]);

        // post-process: bind to game data if available
        if (data != null)
        {
            item.Bind(data);
            // try to resolve unmatched mods by text search
            foreach (var mod in item.Mods.Where(m => m.Def == null && m.RawText != null))
                TryResolveMod(mod, item, data);
        }

        return item;
    }

    private static List<List<string>> SplitSections(string text)
    {
        var sections = new List<List<string>>();
        var current = new List<string>();
        foreach (var raw in text.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (SectionSep.IsMatch(raw))
            {
                if (current.Count > 0) sections.Add(current);
                current = new List<string>();
            }
            else current.Add(raw);
        }
        if (current.Count > 0) sections.Add(current);
        return sections;
    }

    private static void ParseHeaderSection(Item item, List<string> lines)
    {
        // Line 1: "Item Class: Staves" (optional)
        // Then: "Rarity: Rare"
        // Then: name (for magic/rare/unique) or base name
        foreach (var line in lines)
        {
            if (line.StartsWith("Item Class:", StringComparison.OrdinalIgnoreCase))
            {
                item.ItemClass = line["Item Class:".Length..].Trim();
                continue;
            }
            if (line.StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase))
            {
                var rarityStr = line["Rarity:".Length..].Trim();
                item.Rarity = rarityStr.ToLower() switch
                {
                    "normal" => Rarity.Normal,
                    "magic" => Rarity.Magic,
                    "rare" => Rarity.Rare,
                    "unique" => Rarity.Unique,
                    _ => Rarity.Normal
                };
                continue;
            }
        }
        // After rarity, remaining lines are name + base
        var remaining = lines.Where(l => !l.StartsWith("Item Class:") && !l.StartsWith("Rarity:")).ToList();
        if (item.Rarity is Rarity.Rare or Rarity.Unique && remaining.Count >= 2)
        {
            item.Name = remaining[0].Trim();
            item.BaseName = remaining[1].Trim();
        }
        else if (remaining.Count >= 1)
        {
            item.BaseName = remaining[0].Trim();
        }
    }

    private static void ParseSection(Item item, List<string> lines)
    {
        // Check for known single-line sections first
        if (lines.Count == 1)
        {
            var l = lines[0].Trim();
            if (l == "Corrupted") { item.Corrupted = true; return; }
            if (l == "Mirrored") { item.Mirrored = true; return; }
            if (l == "Unidentified") { item.Identified = false; return; }
            if (l == "Sanctified") { item.Sanctified = true; return; }
        }

        // Check for property lines (quality, item level, sockets)
        bool hadProperties = false;
        foreach (var line in lines)
        {
            var qm = QualityRx.Match(line);
            if (qm.Success) { item.Quality = int.Parse(qm.Groups[1].Value); item.QualityType = qm.Groups[2].Success ? qm.Groups[2].Value : null; hadProperties = true; continue; }
            var im = ItemLevelRx.Match(line);
            if (im.Success) { item.ItemLevel = int.Parse(im.Groups[1].Value); hadProperties = true; continue; }
            var sm = SocketsRx.Match(line);
            if (sm.Success) { item.Sockets = int.Parse(sm.Groups[1].Value); hadProperties = true; continue; }
        }
        if (hadProperties) return;

        // Check for mod headers -> modifier section
        ParseModSection(item, lines);
    }

    private static void ParseModSection(Item item, List<string> lines)
    {
        string? currentModId = null;
        AffixType currentAffix = AffixType.Other;
        ModKind currentKind = ModKind.Explicit;
        int currentTier = 0;
        var statTexts = new List<string>();

        void FlushMod()
        {
            if (statTexts.Count == 0 && currentModId == null) return;
            var rawText = string.Join("\n", statTexts);
            var mod = new ItemMod
            {
                ModId = currentModId ?? rawText,
                Kind = currentKind,
                Affix = currentAffix,
                RawText = rawText,
                Values = ParseStatValues(statTexts),
            };
            item.Mods.Add(mod);
            statTexts.Clear();
            currentModId = null;
            currentAffix = AffixType.Other;
            currentKind = ModKind.Explicit;
            currentTier = 0;
        }

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            // { Prefix Modifier "Name" (Tier: N) — Tags }
            var hm = ModHeader.Match(line);
            if (hm.Success)
            {
                FlushMod();
                currentAffix = hm.Groups[1].Value == "Prefix" ? AffixType.Prefix : AffixType.Suffix;
                currentKind = ModKind.Explicit;
                currentModId = hm.Groups[2].Value;  // the "Name" from the header — we'll resolve to a ModDef later
                if (hm.Groups[3].Success) currentTier = int.Parse(hm.Groups[3].Value);
                continue;
            }

            // { Crafted Modifier "Name" — Tags }
            var cm = CraftedHeader.Match(line);
            if (cm.Success)
            {
                FlushMod();
                currentKind = ModKind.Crafted;
                currentModId = cm.Groups[1].Value;
                continue;
            }

            // { Implicit Modifier "Name" }
            var im = ImplicitHeader.Match(line);
            if (im.Success)
            {
                FlushMod();
                currentKind = ModKind.Implicit;
                currentModId = im.Groups[1].Value;
                continue;
            }

            // { Corrupted Implicit Modifier ... }
            var cim = CorruptedImplicitHeader.Match(line);
            if (cim.Success)
            {
                FlushMod();
                currentKind = ModKind.CorruptedImplicit;
                continue;
            }

            // stat line: "112(105-119)% increased Spell Damage"
            statTexts.Add(line.Trim());
        }
        FlushMod();
    }

    private static List<double> ParseStatValues(List<string> statLines)
    {
        var values = new List<double>();
        foreach (var line in statLines)
        {
            // match patterns like "112(105-119)" or just plain "112" at the start
            var m = Regex.Match(line, @"^([+-]?\d+(?:\.\d+)?)(?:\((\d+(?:\.\d+)?)-(\d+(?:\.\d+)?)\))?");
            if (m.Success) values.Add(double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        }
        return values;
    }

    /// <summary>Try to match an unresolved mod to a ModDef by comparing the stat text against known mod texts.</summary>
    private static void TryResolveMod(ItemMod mod, Item item, GameData data)
    {
        if (mod.RawText == null) return;
        // strip numbers from the raw text to compare against mod templates
        var stripped = Regex.Replace(mod.RawText, @"\d+(?:\.\d+)?(?:\(\d+(?:\.\d+)?-\d+(?:\.\d+)?\))?", "#");
        foreach (var def in data.Mods)
        {
            if (def.AffixType == AffixType.Other && mod.Kind == ModKind.Explicit) continue;
            var defStripped = Regex.Replace(def.Text, @"\(-?\d+(?:\.\d+)?\s*-\s*-?\d+(?:\.\d+)?\)", "#");
            if (stripped.Equals(defStripped, StringComparison.OrdinalIgnoreCase) ||
                mod.RawText.Contains(def.Text.Split('(')[0].Trim(), StringComparison.OrdinalIgnoreCase))
            {
                mod.ModId = def.Id;
                mod.Def = def;
                if (mod.Affix == AffixType.Other) mod.Affix = def.AffixType;
                break;
            }
        }
    }

    /// <summary>Serialize an item back to the Ctrl+Alt+C text format (for display/export).</summary>
    public static string ToText(Item item)
    {
        var sb = new System.Text.StringBuilder();
        // Header
        if (!string.IsNullOrEmpty(item.ItemClass)) sb.AppendLine($"Item Class: {item.ItemClass}");
        sb.AppendLine($"Rarity: {item.Rarity}");
        if (item.Name != null) sb.AppendLine(item.Name);
        sb.AppendLine(item.BaseName);
        sb.AppendLine("--------");
        // Properties
        if (item.Quality > 0) sb.AppendLine($"Quality: +{item.Quality}%{(item.QualityType != null ? $" ({item.QualityType})" : "")}");
        if (item.Sockets > 0) sb.AppendLine($"Sockets: {item.Sockets}");
        sb.AppendLine($"Item Level: {item.ItemLevel}");
        sb.AppendLine("--------");
        // Mods
        foreach (var mod in item.Mods)
        {
            if (mod.Kind == ModKind.Implicit)
            {
                sb.AppendLine($"{{ Implicit Modifier \"{mod.Def?.Name ?? mod.ModId}\" }}");
                sb.AppendLine(mod.DisplayText());
                continue;
            }
            if (mod.Kind is ModKind.Explicit or ModKind.Desecrated)
            {
                var typeStr = mod.Affix == AffixType.Prefix ? "Prefix" : "Suffix";
                var tier = mod.Def != null ? $" (Tier: {mod.Def.Tier})" : "";
                sb.AppendLine($"{{ {typeStr} Modifier \"{mod.Def?.Name ?? mod.ModId}\"{tier} }}");
            }
            else if (mod.Kind == ModKind.Crafted)
            {
                sb.AppendLine($"{{ Crafted Modifier \"{mod.Def?.Name ?? mod.ModId}\" }}");
            }
            sb.AppendLine(mod.DisplayText());
        }
        // Flags
        if (item.Corrupted) { sb.AppendLine("--------"); sb.AppendLine("Corrupted"); }
        if (item.Mirrored) { sb.AppendLine("--------"); sb.AppendLine("Mirrored"); }
        return sb.ToString().TrimEnd();
    }
}
