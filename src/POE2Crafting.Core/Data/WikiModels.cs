namespace POE2Crafting.Core.Data;

/// <summary>
/// One article of the in-app knowledge base (data/wiki.json): the game rules the simulator implements, what is confirmed in game
/// and what is an assumption. Texts may mark crafting item names as [[Essence of the Breach]] so the UI links them.
/// </summary>
public sealed class WikiArticle
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    /// <summary>Group in the article list, e.g. "Modifiers", "Currencies", "Techniques".</summary>
    public string Category { get; init; } = "";
    /// <summary>One or two sentences shown in the list and above the article.</summary>
    public string Summary { get; init; } = "";
    public List<string> Tags { get; init; } = new();
    public List<WikiSection> Sections { get; init; } = new();
    /// <summary>Rules tried out in the game client — the simulator follows these even against other sources.</summary>
    public List<string> Verified { get; init; } = new();
    /// <summary>Rules no source confirms: the simulator picks a behaviour and says so in the preview.</summary>
    public List<string> Assumptions { get; init; } = new();
    /// <summary>Open questions: what would have to be tried in game to settle them.</summary>
    public List<string> Open { get; init; } = new();
    /// <summary>Ids of related articles.</summary>
    public List<string> Related { get; init; } = new();

    /// <summary>Everything a search box matches against.</summary>
    public IEnumerable<string> SearchTexts =>
        new[] { Title, Category, Summary }
            .Concat(Tags)
            .Concat(Sections.SelectMany(s => s.SearchTexts))
            .Concat(Verified).Concat(Assumptions).Concat(Open);
}

/// <summary>A part of an article: an optional heading with paragraphs, bullet points and one table.</summary>
public sealed class WikiSection
{
    public string? Heading { get; init; }
    public List<string> Text { get; init; } = new();
    public List<string> Bullets { get; init; } = new();
    public WikiTable? Table { get; init; }

    public IEnumerable<string> SearchTexts =>
        new[] { Heading ?? "" }.Concat(Text).Concat(Bullets).Concat(Table?.SearchTexts ?? Enumerable.Empty<string>());
}

/// <summary>A table inside an article; the first column carries the row label.</summary>
public sealed class WikiTable
{
    public List<string> Columns { get; init; } = new();
    public List<List<string>> Rows { get; init; } = new();
    /// <summary>Line under the table.</summary>
    public string? Caption { get; init; }

    public IEnumerable<string> SearchTexts => Columns.Concat(Rows.SelectMany(r => r)).Append(Caption ?? "");
}
