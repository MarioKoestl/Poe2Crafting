namespace POE2Crafting.Core.Items;

/// <summary>Comparing two states of an item as multisets: which entries (modifier lines, runes, ids) were removed and which were added.</summary>
public static class Multiset
{
    /// <summary>
    /// Multiset difference: entries of <paramref name="before"/> without an equal partner in <paramref name="after"/>, and the other way round.
    /// Duplicates count: two identical entries before and one after leave one entry in <c>Removed</c>.
    /// </summary>
    public static (List<T> Removed, List<T> Added) Unmatched<T>(IEnumerable<T> before, IEnumerable<T> after)
    {
        var removed = new List<T>();
        var added = after.ToList();
        foreach (var entry in before)
            if (!added.Remove(entry)) removed.Add(entry);
        return (removed, added);
    }
}
