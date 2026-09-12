using System.Globalization;
using System.Text.RegularExpressions;
using POE2Crafting.Core.Data;

namespace POE2Crafting.Core.Items;

/// <summary>
/// Parses the in-game item text (Ctrl+C or Ctrl+Alt+C) into an <see cref="Item"/>.
/// Sections are separated by "--------". Handles:
///   - Header section (item class, rarity, name, base; magic names contain the base)
///   - Properties (quality, sockets "S S", item level, requirements, weapon/armour stats)
///   - Modifier headers: { [Crafted|Desecrated|Fractured] Prefix|Suffix Modifier "Name" (Tier: N) — Tag, Tag }
///   - Stat lines with optional (min-max) ranges and trailing markers (implicit), (rune), (enchant), (crafted), (desecrated), (fractured)
///   - Footer flags (Corrupted, Mirrored, Sanctified, Unidentified, Fractured Item)
/// With game data, mods are resolved against the base's own mod pool by affix name, text template and value ranges.
/// </summary>
public static class ItemParser
{
    private static readonly Regex SectionSep = new(@"^-{4,}\s*$", RegexOptions.Compiled);
    private static readonly Regex ModHeader = new(
        @"^\{\s*(?<flags>(?:(?:Crafted|Desecrated|Fractured|Unrevealed|Unique|Implicit|Corrupted|Enchant)\s+)*)(?<affix>Prefix|Suffix)?\s*Modifier(?:\s+""(?<name>[^""]*)"")?\s*(?:\((?:Tier|Rank):\s*(?<tier>\d+)\))?\s*(?:[—–-]\s*(?<tags>.*?))?\s*\}\s*$",
        RegexOptions.Compiled);
    private static readonly Regex QualityRx = new(@"^Quality:\s*\+(\d+)%(?:\s*\((.+?)\))?", RegexOptions.Compiled);
    private static readonly Regex ItemLevelRx = new(@"^Item Level:\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex SocketsRx = new(@"^Sockets:\s*(.*)$", RegexOptions.Compiled);
    private static readonly Regex PropertyRx = new(@"^[A-Z][A-Za-z' ]{1,40}:(\s|$)", RegexOptions.Compiled);
    private static readonly Regex MarkerRx = new(@"\s*\((implicit|rune|enchant|crafted|desecrated|fractured|augmented)\)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // "238(209-248)" | "(209-248)" | "238"
    private static readonly Regex ValueToken = new(@"(?<v>-?\d+(?:\.\d+)?)(?:\((?<a>-?\d+(?:\.\d+)?)-(?<b>-?\d+(?:\.\d+)?)\))?", RegexOptions.Compiled);
    private static readonly Regex TemplateToken = new(@"\((?<a>-?\d+(?:\.\d+)?)\s*-\s*(?<b>-?\d+(?:\.\d+)?)\)|(?<v>-?\d+(?:\.\d+)?)", RegexOptions.Compiled);

    private static readonly string[] QualityNotCatalyst = { "augmented", "unmet" };

    /// <summary>Parse an item text. Optionally bind to game data for base and ModDef resolution.</summary>
    public static Item Parse(string text, GameData? data = null)
    {
        var sections = SplitSections(text ?? "");
        if (sections.Count == 0) throw new FormatException("No item text to parse.");

        var item = new Item();
        ParseHeaderSection(item, sections[0], data);
        if (string.IsNullOrWhiteSpace(item.BaseName)) throw new FormatException("Could not find the item name in the text.");

        for (int s = 1; s < sections.Count; s++)
            ParseSection(item, sections[s]);

        if (data != null)
        {
            item.Bind(data);
            foreach (var mod in item.Mods.Where(m => m.Def == null && m.RawText != null))
                TryResolveMod(mod, item, data);
        }
        return item;
    }

    private static List<List<string>> SplitSections(string text)
    {
        var sections = new List<List<string>>();
        var current = new List<string>();
        foreach (var raw in text.Split('\n').Select(l => l.TrimEnd('\r').Trim()))
        {
            if (SectionSep.IsMatch(raw))
            {
                if (current.Count > 0) sections.Add(current);
                current = new List<string>();
            }
            else if (raw.Length > 0) current.Add(raw);
        }
        if (current.Count > 0) sections.Add(current);
        return sections;
    }

    private static void ParseHeaderSection(Item item, List<string> lines, GameData? data)
    {
        var remaining = new List<string>();
        foreach (var line in lines)
        {
            if (line.StartsWith("Item Class:", StringComparison.OrdinalIgnoreCase)) { item.ItemClass = line["Item Class:".Length..].Trim(); continue; }
            if (line.StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase))
            {
                item.Rarity = line["Rarity:".Length..].Trim().ToLowerInvariant() switch
                {
                    "magic" => Rarity.Magic,
                    "rare" => Rarity.Rare,
                    "unique" => Rarity.Unique,
                    _ => Rarity.Normal,
                };
                continue;
            }
            remaining.Add(line);
        }

        if (item.Rarity is Rarity.Rare or Rarity.Unique && remaining.Count >= 2)
        {
            item.Name = remaining[0];
            item.BaseName = remaining[1];
        }
        else if (remaining.Count >= 1)
        {
            item.BaseName = remaining[0];
        }

        // Magic items show "Prefix Base of Suffix"; also covers unknown decorations on normal items.
        if (data != null && data.FindBase(item.BaseName) == null && data.FindBaseInName(item.BaseName) is { } found)
            item.BaseName = found.Name;
    }

    private static void ParseSection(Item item, List<string> lines)
    {
        if (lines.Count == 1)
        {
            switch (lines[0])
            {
                case "Corrupted": item.Corrupted = true; return;
                case "Mirrored": item.Mirrored = true; return;
                case "Unidentified": item.Identified = false; return;
                case "Sanctified": item.Sanctified = true; return;
                case "Fractured Item": return;
            }
        }

        bool hasHeader = lines.Any(l => ModHeader.IsMatch(l));
        if (!hasHeader && lines.All(l => PropertyRx.IsMatch(l) && !l.StartsWith("Grants Skill:", StringComparison.Ordinal)))
        {
            ParseProperties(item, lines);
            return;
        }
        ParseModSection(item, lines);
    }

    private static void ParseProperties(Item item, List<string> lines)
    {
        foreach (var line in lines)
        {
            var qm = QualityRx.Match(line);
            if (qm.Success)
            {
                item.Quality = int.Parse(qm.Groups[1].Value, CultureInfo.InvariantCulture);
                var qt = qm.Groups[2].Success ? qm.Groups[2].Value : null;
                item.QualityType = qt != null && !QualityNotCatalyst.Contains(qt, StringComparer.OrdinalIgnoreCase) ? qt : null;
                continue;
            }
            var im = ItemLevelRx.Match(line);
            if (im.Success) { item.ItemLevel = int.Parse(im.Groups[1].Value, CultureInfo.InvariantCulture); continue; }
            var sm = SocketsRx.Match(line);
            if (sm.Success)
            {
                var v = sm.Groups[1].Value.Trim();
                item.Sockets = int.TryParse(v, out var n) ? n : v.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            }
            // Requires, weapon/armour stats etc. are derived from the base and not tracked.
        }
    }

    private static void ParseModSection(Item item, List<string> lines)
    {
        ItemMod? current = null;
        var statTexts = new List<string>();

        void Flush()
        {
            if (current == null) return;
            current.RawText = string.Join("\n", statTexts);
            current.Values = statTexts.SelectMany(l => ValueToken.Matches(l).Select(m => ParseDouble(m.Groups["v"].Value))).ToList();
            if (statTexts.Count > 0 || current.ModId.Length > 0) item.Mods.Add(current);
            current = null;
            statTexts.Clear();
        }

        foreach (var line in lines)
        {
            var hm = ModHeader.Match(line);
            if (hm.Success)
            {
                Flush();
                var flags = hm.Groups["flags"].Value;
                var kind = flags.Contains("Corrupted") ? ModKind.CorruptedImplicit
                         : flags.Contains("Implicit") ? ModKind.Implicit
                         : flags.Contains("Enchant") ? ModKind.Enchant
                         : flags.Contains("Crafted") ? ModKind.Crafted
                         : flags.Contains("Desecrated") || flags.Contains("Unrevealed") ? ModKind.Desecrated
                         : ModKind.Explicit;
                current = new ItemMod
                {
                    ModId = hm.Groups["name"].Value,
                    Kind = kind,
                    Affix = hm.Groups["affix"].Value switch { "Prefix" => AffixType.Prefix, "Suffix" => AffixType.Suffix, _ => AffixType.Other },
                    Fractured = flags.Contains("Fractured"),
                    Unrevealed = flags.Contains("Unrevealed"),
                };
                continue;
            }

            // reminder text, e.g. "(Consecrated Ground ...)"
            if (line.StartsWith('(') && line.EndsWith(')') && !TemplateToken.IsMatch(line.Trim('(', ')'))) continue;

            var marker = MarkerRx.Match(line);
            if (current == null || marker.Success)
            {
                // a header-less line is its own mod
                var stat = marker.Success ? line[..marker.Index] : line;
                var tag = marker.Success ? marker.Groups[1].Value.ToLowerInvariant() : "";
                if (tag == "rune") { item.Runes.Add(stat); continue; }
                Flush();
                current = new ItemMod
                {
                    Kind = tag switch
                    {
                        "implicit" => ModKind.Implicit,
                        "enchant" => ModKind.Enchant,
                        "crafted" => ModKind.Crafted,
                        "desecrated" => ModKind.Desecrated,
                        _ => stat.StartsWith("Grants Skill:", StringComparison.Ordinal) ? ModKind.Implicit : ModKind.Explicit,
                    },
                    Fractured = tag == "fractured",
                };
                statTexts.Add(stat);
                Flush();
                continue;
            }
            statTexts.Add(line);
        }
        Flush();
    }

    // ------------------------------------------------------------------ mod resolution

    /// <summary>Match an unresolved mod to a ModDef from the item's own pool: same affix type, affix name, text template, then value ranges.</summary>
    private static void TryResolveMod(ItemMod mod, Item item, GameData data)
    {
        if (string.IsNullOrWhiteSpace(mod.RawText)) return;
        if (mod.Kind is ModKind.Implicit or ModKind.Enchant) return;

        var pages = data.PagesFor(item.Base, item.ItemClass);
        var template = NormaliseText(mod.RawText);
        var itemRanges = mod.RawText.Split('\n').SelectMany(l => ValueToken.Matches(l))
            .Where(m => m.Groups["a"].Success).Select(m => (ParseDouble(m.Groups["a"].Value), ParseDouble(m.Groups["b"].Value))).ToList();

        var candidates = data.Mods.Where(def =>
            CategoryFits(def.Category, mod.Kind) &&
            (mod.Affix == AffixType.Other || def.AffixType == mod.Affix) &&
            def.Weights.Keys.Any(pages.Contains) &&
            (NormaliseText(def.Text) == template || def.AltTexts.Any(t => NormaliseText(t) == template))).ToList();
        if (candidates.Count == 0) return;

        var name = mod.ModId;
        var best = candidates
            .OrderByDescending(def => name.Length > 0 && def.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(def => itemRanges.Count > 0 && RangesEqual(def.Ranges, itemRanges))
            .ThenByDescending(def => ValuesInside(def, mod.RawText))
            .ThenBy(def => def.Category == "normal" ? 0 : 1)
            .First();

        mod.ModId = best.Id;
        mod.Def = best;
        if (mod.Affix == AffixType.Other) mod.Affix = best.AffixType;
        mod.Values = AlignValues(best, mod.RawText);
    }

    private static bool CategoryFits(string category, ModKind kind) => kind switch
    {
        ModKind.Crafted => GameData.EssenceCategories.Contains(category),
        ModKind.Desecrated => category == "desecrated",
        ModKind.CorruptedImplicit => category is "corrupted" or "corruption_upgrade",
        _ => category is not ("corrupted" or "corruption_upgrade" or "socketable" or "bonded") && !GameData.EssenceCategories.Contains(category),
    };

    /// <summary>"+238(209-248) to maximum Mana" and "+(209-248) to maximum Mana" both become "+# to maximum mana".</summary>
    public static string NormaliseText(string text)
    {
        var s = RolledWithRange.Replace(text, "#");   // item text: 238(209-248)
        s = BareRange.Replace(s, "#");                // template:  (209-248)
        s = BareNumber.Replace(s, "#");               // fixed numbers on both sides
        s = Regex.Replace(s, @"\s+", " ");
        return s.Trim().ToLowerInvariant();
    }

    private static readonly Regex RolledWithRange = new(@"-?\d+(?:\.\d+)?\(-?\d+(?:\.\d+)?\s*-\s*-?\d+(?:\.\d+)?\)", RegexOptions.Compiled);
    private static readonly Regex BareRange = new(@"\(-?\d+(?:\.\d+)?\s*-\s*-?\d+(?:\.\d+)?\)", RegexOptions.Compiled);
    private static readonly Regex BareNumber = new(@"\d+(?:\.\d+)?", RegexOptions.Compiled);

    private static bool RangesEqual(List<double[]> defRanges, List<(double a, double b)> itemRanges)
    {
        var defOnly = defRanges.Where(r => r[0] != r[1]).ToList();
        if (defOnly.Count != itemRanges.Count) return false;
        for (int i = 0; i < defOnly.Count; i++)
            if (Math.Abs(defOnly[i][0] - itemRanges[i].a) > 0.001 || Math.Abs(defOnly[i][1] - itemRanges[i].b) > 0.001) return false;
        return true;
    }

    private static bool ValuesInside(ModDef def, string rawText)
    {
        var values = AlignValues(def, rawText);
        if (values.Count != def.Ranges.Count || values.Count == 0) return false;
        for (int i = 0; i < values.Count; i++)
        {
            double lo = Math.Min(def.Ranges[i][0], def.Ranges[i][1]), hi = Math.Max(def.Ranges[i][0], def.Ranges[i][1]);
            if (Math.Abs(values[i]) < lo - 0.001 || Math.Abs(values[i]) > hi + 0.001) return false;
        }
        return true;
    }

    /// <summary>Pick the rolled numbers that correspond to the template's ranges (skipping fixed numbers like "+1 to Level").</summary>
    private static List<double> AlignValues(ModDef def, string rawText)
    {
        var itemTokens = rawText.Split('\n').SelectMany(l => ValueToken.Matches(l)).Select(m => ParseDouble(m.Groups["v"].Value)).ToList();
        var templateTokens = def.Text.Split('\n').SelectMany(l => TemplateToken.Matches(l)).Select(m => m.Groups["a"].Success).ToList();
        if (itemTokens.Count == templateTokens.Count)
            return itemTokens.Where((_, i) => templateTokens[i]).ToList();
        return itemTokens.Take(def.Ranges.Count).ToList();
    }

    private static double ParseDouble(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ export

    /// <summary>Serialize an item back to the Ctrl+Alt+C text format (for display/export).</summary>
    /// <param name="tierOf">Tier to print for a mod (e.g. ModPool.DisplayTier); defaults to the global tier.</param>
    public static string ToText(Item item, Func<ModDef, int>? tierOf = null)
    {
        tierOf ??= d => d.Tier;
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(item.ItemClass)) sb.AppendLine($"Item Class: {item.ItemClass}");
        sb.AppendLine($"Rarity: {item.Rarity}");
        if (item.Name != null) sb.AppendLine(item.Name);
        sb.AppendLine(item.BaseName);
        sb.AppendLine("--------");
        if (item.Quality > 0) sb.AppendLine($"Quality: +{item.Quality}%{(item.QualityType != null ? $" ({item.QualityType})" : "")}");
        if (item.Sockets > 0) sb.AppendLine($"Sockets: {string.Join(" ", Enumerable.Repeat("S", item.Sockets))}");
        sb.AppendLine($"Item Level: {item.ItemLevel}");

        if (item.Runes.Count > 0)
        {
            sb.AppendLine("--------");
            foreach (var r in item.Runes) sb.AppendLine($"{r} (rune)");
        }
        var implicits = item.Mods.Where(m => m.Kind is ModKind.Implicit or ModKind.CorruptedImplicit or ModKind.Enchant).ToList();
        if (implicits.Count > 0)
        {
            sb.AppendLine("--------");
            foreach (var m in implicits) sb.AppendLine(m.DisplayText() + (m.Kind == ModKind.Enchant ? " (enchant)" : m.Kind == ModKind.Implicit && !m.DisplayText().StartsWith("Grants Skill:") ? " (implicit)" : ""));
        }
        var affixes = item.Mods.Where(m => m.IsAffix).OrderBy(m => m.Affix).ToList();
        if (affixes.Count > 0)
        {
            sb.AppendLine("--------");
            foreach (var mod in affixes)
            {
                var flags = (mod.Fractured ? "Fractured " : "") + mod.Kind switch { ModKind.Crafted => "Crafted ", ModKind.Desecrated => "Desecrated ", _ => "" };
                var tier = mod.Def != null && mod.Kind == ModKind.Explicit ? $" (Tier: {tierOf(mod.Def)})" : "";
                sb.AppendLine($"{{ {flags}{mod.Affix} Modifier \"{mod.Def?.Name ?? mod.ModId}\"{tier} }}");
                sb.AppendLine(mod.DisplayText());
            }
        }
        if (item.Corrupted) { sb.AppendLine("--------"); sb.AppendLine("Corrupted"); }
        if (item.Mirrored) { sb.AppendLine("--------"); sb.AppendLine("Mirrored"); }
        return sb.ToString().TrimEnd();
    }
}
