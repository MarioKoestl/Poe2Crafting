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
/// The reverse direction is <see cref="ItemTextWriter"/>.
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

    private static readonly string[] QualityNotCatalyst = { "augmented", "unmet" };

    /// <summary>Parse an item text. Optionally bind to game data for base and ModDef resolution.</summary>
    public static Item Parse(string text, GameData? data = null)
    {
        var sections = SplitSections(text);
        if (sections.Count == 0) throw new FormatException("No item text to parse.");

        var item = new Item();
        ParseHeaderSection(item, sections[0], data);
        if (string.IsNullOrWhiteSpace(item.BaseName)) throw new FormatException("Could not find the item name in the text.");

        for (int s = 1; s < sections.Count; s++)
            ParseSection(item, sections[s]);

        if (data != null)
        {
            item.Bind(data);
            // the game prints "Quality: +20% (Life Modifiers)"; items store the catalyst's own type ("Life")
            if (item.QualityType != null) item.QualityType = data.CanonicalQualityType(item.QualityType);
            foreach (var mod in item.Mods.Where(m => m.Def == null && m.RawText != null))
                TryResolveMod(mod, item, data);
        }
        return item;
    }

    private static List<List<string>> SplitSections(string text)
    {
        var sections = new List<List<string>>();
        var current = new List<string>();
        foreach (var line in text.Split('\n').Select(l => l.Trim()))
        {
            if (SectionSep.IsMatch(line))
            {
                if (current.Count > 0) sections.Add(current);
                current = new List<string>();
            }
            else if (line.Length > 0) current.Add(line);
        }
        if (current.Count > 0) sections.Add(current);
        return sections;
    }

    private static void ParseHeaderSection(Item item, List<string> lines, GameData? data)
    {
        const string classLabel = "Item Class:", rarityLabel = "Rarity:";
        var remaining = new List<string>();
        foreach (var line in lines)
        {
            if (line.StartsWith(classLabel, StringComparison.OrdinalIgnoreCase)) item.ItemClass = line[classLabel.Length..].Trim();
            else if (line.StartsWith(rarityLabel, StringComparison.OrdinalIgnoreCase))
                item.Rarity = Enum.TryParse<Rarity>(line[rarityLabel.Length..].Trim(), ignoreCase: true, out var rarity) ? rarity : Rarity.Normal;
            else remaining.Add(line);
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
        if (lines.Count == 1 && ApplyFlag(item, lines[0])) return;

        bool hasHeader = lines.Any(l => ModHeader.IsMatch(l));
        if (!hasHeader && lines.All(l => PropertyRx.IsMatch(l) && !l.StartsWith(ItemTextFormat.GrantsSkill, StringComparison.Ordinal)))
        {
            ParseProperties(item, lines);
            return;
        }
        new ModSectionParser(item).Parse(lines);
    }

    /// <summary>Footer flags; returns false for any other line.</summary>
    private static bool ApplyFlag(Item item, string line)
    {
        switch (line)
        {
            case "Corrupted": item.Corrupted = true; return true;
            case "Twice Corrupted": item.Corrupted = item.TwiceCorrupted = true; return true;
            case "Mirrored": item.Mirrored = true; return true;
            case "Unidentified": item.Identified = false; return true;
            case "Sanctified": item.Sanctified = true; return true;
            case "Fractured Item": return true;
            default: return false;
        }
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

    /// <summary>One section of modifier lines: headers start a mod whose stat lines follow; header-less lines are mods of their own.</summary>
    private sealed class ModSectionParser
    {
        private readonly Item _item;
        private ItemMod? _current;
        private readonly List<string> _statTexts = new();

        public ModSectionParser(Item item) => _item = item;

        public void Parse(List<string> lines)
        {
            foreach (var line in lines)
            {
                if (ModHeader.Match(line) is { Success: true } header)
                {
                    Flush();
                    _current = FromHeader(header);
                }
                else if (IsReminderText(line)) continue;
                else if (MarkerRx.Match(line) is { Success: true } marker) AddMarkedLine(line[..marker.Index], marker.Groups[1].Value.ToLowerInvariant());
                else if (_current == null) AddMarkedLine(line, "");
                else _statTexts.Add(line);
            }
            Flush();
        }

        private static ItemMod FromHeader(Match header)
        {
            var flags = header.Groups["flags"].Value;
            return new ItemMod
            {
                ModId = header.Groups["name"].Value,
                Kind = ItemTextFormat.KindOfHeaderFlags(flags),
                Affix = Enum.TryParse<AffixType>(header.Groups["affix"].Value, out var affix) ? affix : AffixType.Other,
                Fractured = flags.Contains("Fractured"),
                Unrevealed = flags.Contains("Unrevealed"),
            };
        }

        /// <summary>Reminder text such as "(Consecrated Ground ...)" carries no numbers and belongs to no mod.</summary>
        private static bool IsReminderText(string line) => line.StartsWith('(') && line.EndsWith(')') && ModText.RolledTokens(line).Count == 0;

        /// <summary>A line with its own marker (or without a header) is a mod of its own; runes are item augments.</summary>
        private void AddMarkedLine(string stat, string marker)
        {
            if (marker == ItemTextFormat.RuneMarker)
            {
                _item.Runes.Add(stat);
                return;
            }
            Flush();
            _current = new ItemMod
            {
                Kind = marker.Length > 0 ? ItemTextFormat.KindOfMarker(marker)
                    : stat.StartsWith(ItemTextFormat.GrantsSkill, StringComparison.Ordinal) ? ModKind.Implicit : ModKind.Explicit,
                Fractured = marker == ItemTextFormat.FracturedMarker,
            };
            _statTexts.Add(stat);
            Flush();
        }

        private void Flush()
        {
            if (_current == null) return;
            _current.RawText = string.Join("\n", _statTexts);
            _current.Values = ModText.RolledTokens(_current.RawText).Select(t => t.Value).ToList();
            if (_statTexts.Count > 0 || _current.ModId.Length > 0 || _current.Unrevealed) _item.Mods.Add(_current);
            _current = null;
            _statTexts.Clear();
        }
    }

    // ------------------------------------------------------------------ mod resolution

    /// <summary>Match an unresolved mod to a ModDef from the item's own pool: same affix type, affix name, text template, then value ranges.</summary>
    private static void TryResolveMod(ItemMod mod, Item item, GameData data)
    {
        if (mod.Unrevealed || string.IsNullOrWhiteSpace(mod.RawText)) return;
        // "(enchant)" lines on corrupted items are corruption enchantments; other implicits/enchants are shown as text
        if (mod.Kind == ModKind.Implicit || mod.Kind == ModKind.Enchant && !item.Corrupted) return;

        var pages = data.PagesFor(item.Base, item.ItemClass);
        var signature = ModText.StatSignature(mod.RawText);
        var itemRanges = ModText.RolledTokens(mod.RawText).Where(t => t.Range != null).Select(t => t.Range!).ToList();

        var candidates = data.Mods.Where(def =>
            ModCategories.CanAppearAs(def.Category, mod.Kind) &&
            (mod.Affix == AffixType.Other || def.AffixType == mod.Affix) &&
            def.IsOnAnyPage(pages) &&
            (def.StatSignature == signature || def.AltTexts.Any(t => ModText.StatSignature(t) == signature))).ToList();
        if (candidates.Count == 0) return;

        var name = mod.ModId;
        var best = candidates
            // the shown (min-max) range identifies the tier exactly; the affix name can be ambiguous across classes or wrong in pasted text
            .OrderByDescending(def => itemRanges.Count > 0 && RangesEqual(def.Ranges, itemRanges))
            .ThenByDescending(def => ValuesInside(def, mod.RawText))
            .ThenByDescending(def => name.Length > 0 && def.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ThenBy(def => def.Category == ModCategories.Normal ? 0 : 1)
            .First();

        mod.ModId = best.Id;
        mod.Def = best;
        if (mod.Kind == ModKind.Enchant) mod.Kind = ModKind.CorruptedImplicit;
        if (mod.Affix == AffixType.Other) mod.Affix = best.AffixType;
        mod.Values = AlignValues(best, mod.RawText);
    }

    private static bool RangesEqual(List<double[]> defRanges, List<double[]> itemRanges)
    {
        var defOnly = defRanges.Where(r => r[0] != r[1]).ToList();
        if (defOnly.Count != itemRanges.Count) return false;
        for (int i = 0; i < defOnly.Count; i++)
            if (Math.Abs(defOnly[i][0] - itemRanges[i][0]) > 0.001 || Math.Abs(defOnly[i][1] - itemRanges[i][1]) > 0.001) return false;
        return true;
    }

    private static bool ValuesInside(ModDef def, string rawText)
    {
        var values = AlignValues(def, rawText);
        if (values.Count != def.Ranges.Count || values.Count == 0) return false;
        for (int i = 0; i < values.Count; i++)
        {
            var (lo, hi) = ModText.Bounds(def.Ranges[i]);
            if (values[i] < lo - 0.001 || values[i] > hi + 0.001) return false;
        }
        return true;
    }

    /// <summary>
    /// Pick the rolled numbers that correspond to the template's ranges (skipping fixed numbers like "+1 to Level").
    /// When the item line has a different number of numbers than the template, the first ones are taken (best effort).
    /// </summary>
    private static List<double> AlignValues(ModDef def, string rawText)
    {
        var itemTokens = ModText.RolledTokens(rawText).Select(t => t.Value).ToList();
        var templateTokens = ModText.TemplateTokenIsRange(def.Text);
        if (itemTokens.Count == templateTokens.Count)
            return itemTokens.Where((_, i) => templateTokens[i]).ToList();
        return itemTokens.Take(def.Ranges.Count).ToList();
    }
}
