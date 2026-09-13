namespace POE2Crafting.Core.Items;

public enum DiffKind { Removed, Added, Changed }

/// <summary>One visible difference between two states of an item (a crafting step's "mod changes").</summary>
public sealed record DiffLine(DiffKind Kind, string Text);

/// <summary>What a crafting step changed on an item: modifier lines (affixes, implicits, enchantments), augments and item properties.</summary>
public static class ItemDiff
{
    public static List<DiffLine> Between(Item before, Item after)
    {
        var lines = new List<DiffLine>();
        Property(lines, "Rarity", before.Rarity, after.Rarity);
        Property(lines, "Quality", before.QualityText, after.QualityText);
        Property(lines, "Sockets", before.Sockets, after.Sockets);
        Flag(lines, "Corrupted", before.Corrupted, after.Corrupted);
        Flag(lines, "Sanctified", before.Sanctified, after.Sanctified);
        Flag(lines, "Mirrored", before.Mirrored, after.Mirrored);

        var (removedMods, addedMods) = Unmatched(before.Mods.Select(Describe), after.Mods.Select(Describe));
        // same modifier with other values = changed (e.g. Divine Orb); otherwise removed/added
        foreach (var (id, text) in removedMods)
        {
            int changed = addedMods.FindIndex(a => a.Id == id && id != "");
            if (changed >= 0)
            {
                lines.Add(new DiffLine(DiffKind.Changed, $"{text} → {addedMods[changed].Text}"));
                addedMods.RemoveAt(changed);
            }
            else lines.Add(new DiffLine(DiffKind.Removed, text));
        }
        lines.AddRange(addedMods.Select(a => new DiffLine(DiffKind.Added, a.Text)));

        var (removedRunes, addedRunes) = Unmatched(before.Runes, after.Runes);
        lines.AddRange(removedRunes.Select(r => new DiffLine(DiffKind.Removed, $"{r} (augment)")));
        lines.AddRange(addedRunes.Select(r => new DiffLine(DiffKind.Added, $"{r} (augment)")));
        return lines;
    }

    /// <summary>Multiset difference: entries of <paramref name="before"/> without an equal partner in <paramref name="after"/>, and the other way round.</summary>
    private static (List<T> Removed, List<T> Added) Unmatched<T>(IEnumerable<T> before, IEnumerable<T> after)
    {
        var removed = new List<T>();
        var added = after.ToList();
        foreach (var entry in before)
            if (!added.Remove(entry)) removed.Add(entry);
        return (removed, added);
    }

    /// <summary>Identity (mod id, empty for text-only lines) and visible text incl. fractured/crafted markers.</summary>
    private static (string Id, string Text) Describe(ItemMod mod)
    {
        var markers = new List<string>();
        if (mod.Fractured) markers.Add(ItemTextFormat.FracturedMarker);
        if (ItemTextFormat.Marker(mod.Kind) is { } marker) markers.Add(marker);
        var text = mod.DisplayText() + (markers.Count > 0 ? $" ({string.Join(", ", markers)})" : "");
        return (mod.Def != null ? mod.ModId : "", text);
    }

    private static void Property<T>(List<DiffLine> lines, string name, T before, T after)
    {
        if (!EqualityComparer<T>.Default.Equals(before, after)) lines.Add(new DiffLine(DiffKind.Changed, $"{name}: {before} → {after}"));
    }

    private static void Flag(List<DiffLine> lines, string name, bool before, bool after)
    {
        if (before != after) lines.Add(new DiffLine(after ? DiffKind.Added : DiffKind.Removed, name));
    }
}
