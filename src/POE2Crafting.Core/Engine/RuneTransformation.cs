using System.Text.RegularExpressions;
using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine;

/// <summary>
/// Aldur runes (Passion/Ire/Breath/Betrayal of Aldur): "Transforms all Cold and Lightning modifiers on the item into equivalent Fire modifiers" when socketed.
/// Equivalent = the modifier whose text names the new element instead ("+7 to Level of all Lightning Spell Skills" → "+7 to Level of all Fire Spell Skills",
/// poe2db's table), of the same tier rank on the base; the value keeps its position in the range. Modifiers added later are not transformed (poe2db).
/// "Modifier magnitudes" lines are never transformed (not in poe2db's table, seen in game). Open observation: see config aldurRuneObservationNote.
/// Fractured modifiers stay unchanged: tried in game (Mario, 15.09.2026, 0.5.5) — an older GGG forum bug report (07.06.2026) said they were transformed, that no longer happens.
/// config aldurRuneTransformsFractured switches it (see runeModsNote).
/// </summary>
public static class RuneTransformation
{
    /// <summary>What a transforming rune does: the source elements and the element they become.</summary>
    public sealed record Rule(IReadOnlyList<string> From, string To);

    /// <summary>One transformed affix: its index, the modifier now, the equivalent modifier and its values.</summary>
    public sealed record Change(int Index, ItemMod Mod, ModDef Replacement, List<double> Values);

    private static readonly Regex Transforms = new(@"transforms all (?<from>[A-Za-z ,]+?) modifiers (?:on the item )?(?:in)?to equivalent (?<to>[A-Za-z]+) modifiers",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>The rule of a rune effect text, or null for runes that don't transform.</summary>
    public static Rule? Parse(string effectText)
    {
        if (Transforms.Match(effectText) is not { Success: true } m) return null;
        var from = Regex.Split(m.Groups["from"].Value, @",\s*|\s+and\s+").Select(e => e.Trim()).Where(e => e.Length > 0).ToList();
        return from.Count == 0 ? null : new Rule(from, m.Groups["to"].Value);
    }

    /// <summary>The affixes the rule transforms on the item, each with its equivalent modifier on the item's base.</summary>
    public static List<Change> Plan(Item item, Rule rule, ModPool pool, bool includeFractured)
    {
        var changes = new List<Change>();
        for (int i = 0; i < item.Mods.Count; i++)
        {
            var mod = item.Mods[i];
            if (!mod.IsAffix || mod.Unrevealed || mod.Def is not { } def || mod.Fractured && !includeFractured) continue;
            // "increased Explicit Fire Modifier magnitudes" is no element modifier: poe2db's equivalence table has no such rows, and in game it stayed (Mario, 15.09.2026)
            if (ModMagnitudes.Parse(def.Text) != null || ModMagnitudes.Parse(mod.DisplayText()) != null) continue;
            if (rule.From.FirstOrDefault(e => NamesElement(def.Text, e)) is not { } element) continue;
            if (Equivalent(item, def, Regex.Replace(def.Text, $@"\b{element}\b", rule.To), pool) is not { } replacement) continue;
            var values = replacement.Ranges.Select((range, r) => r < def.Ranges.Count && r < mod.Values.Count
                ? ModText.SamePosition(mod.Values[r], def.Ranges[r], range)
                : ModText.MidValue(range)).ToList();
            changes.Add(new Change(i, mod, replacement, values));
        }
        return changes;
    }

    private static bool NamesElement(string text, string element) => Regex.IsMatch(text, $@"\b{Regex.Escape(element)}\b");

    /// <summary>The same tier rank among the base's mods with the transformed text (same category and affix type): exact text first, else by rank of level.</summary>
    private static ModDef? Equivalent(Item item, ModDef source, string transformedText, ModPool pool)
    {
        var sameType = pool.AllForBaseByCategory(item, source.Category, source.AffixType).ToList();
        var signature = ModText.StatSignature(transformedText);
        var targets = sameType.Where(m => m.StatSignature == signature).OrderByDescending(m => m.Level).ToList();
        if (targets.Count == 0) return null;
        if (targets.FirstOrDefault(m => m.Text == transformedText && m.Level == source.Level) is { } exact) return exact;
        int rank = sameType.Where(m => m.StatSignature == source.StatSignature).OrderByDescending(m => m.Level).ToList().FindIndex(m => m.Id == source.Id);
        return targets[Math.Clamp(rank, 0, targets.Count - 1)];
    }
}
