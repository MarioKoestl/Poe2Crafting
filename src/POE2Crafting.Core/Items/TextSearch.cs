namespace POE2Crafting.Core.Items;

/// <summary>The search rule of all search boxes: every word of the query is part of one of the texts (case-insensitive); an empty query matches everything.</summary>
public static class TextSearch
{
    public static bool Matches(string? query, params string?[] texts) => Matches(query, (IEnumerable<string?>)texts);

    /// <summary>Every word of the query appears in one of the texts, in any order ("quality caster" finds the Sibilant Catalyst).</summary>
    public static bool Matches(string? query, IEnumerable<string?> texts)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var candidates = texts.OfType<string>().ToList();
        return query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .All(word => candidates.Any(t => t.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }
}
