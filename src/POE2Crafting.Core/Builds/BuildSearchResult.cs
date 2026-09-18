namespace POE2Crafting.Core.Builds;

/// <summary>A poe.ninja character reference (account + character name) as the search lists it.</summary>
public sealed record CharacterRef(string Account, string Name);

/// <summary>One counted dimension of a build search (e.g. "skills"): value index into its dictionary → number of characters.</summary>
public sealed record SearchDimension(string Id, string DictionaryId, IReadOnlyDictionary<int, int> Counts);

/// <summary>A per-character column of dictionary indices (e.g. "class", "dps.skill" = main skill): one value per listed character, null where it has none.</summary>
public sealed record SearchIndexColumn(string Id, string DictionaryId, IReadOnlyList<int?> Values);

/// <summary>A dictionary the search refers to by hash (values) and optional properties overlay hash.</summary>
public sealed record SearchDictionaryRef(string Id, string Hash, string? PropertiesHash);

/// <summary>
/// poe.ninja's build search (GET /poe2/api/builds/&lt;version&gt;/search?overview=&lt;league&gt;[&amp;class=..&amp;skills=..&amp;items=..], protobuf "NinjaSearchResult").
/// Field numbers from the site script: SearchResult 1 total, 2 dimensions, 6 dictionaries, 12 columns; Dimension 1 id, 2 dictionary id, 3 counts (1 key, 2 count);
/// DictionaryReference 1 id, 2 hash, 3 properties hash; Column 1 id, 6 int32 values (packed), 7 string values, 10 nulls (packed), 11 dictionary id, 13 row count,
/// 14 row ids (packed, sparse columns). The characters are the first page (100) of the filtered list.
/// </summary>
public sealed class BuildSearchResult
{
    public int Total { get; init; }
    public IReadOnlyList<SearchDimension> Dimensions { get; init; } = Array.Empty<SearchDimension>();
    public IReadOnlyList<SearchDictionaryRef> Dictionaries { get; init; } = Array.Empty<SearchDictionaryRef>();
    public IReadOnlyList<CharacterRef> Characters { get; init; } = Array.Empty<CharacterRef>();
    public IReadOnlyList<SearchIndexColumn> IndexColumns { get; init; } = Array.Empty<SearchIndexColumn>();

    public SearchDimension? Dimension(string id) => Dimensions.FirstOrDefault(d => d.Id == id);

    public SearchIndexColumn? IndexColumn(string id) => IndexColumns.FirstOrDefault(c => c.Id == id);

    public SearchDictionaryRef? Dictionary(string id) => Dictionaries.FirstOrDefault(d => d.Id == id);

    public static BuildSearchResult Parse(byte[] data)
    {
        var wrapper = ProtoReader.Read(data);
        if (!wrapper.Any(f => f.Number == 1 && f.WireType == 2)) throw new FormatException("The build search response has no result.");
        var result = wrapper.First(f => f.Number == 1).Message;
        var columnMessages = result.All(12).Select(c => c.Message).ToList();
        var columns = columnMessages.ToDictionary(c => c.FirstText(1) ?? "", c => c.All(7).Select(v => v.Text).ToList());
        var names = columns.GetValueOrDefault("name") ?? new();
        var accounts = columns.GetValueOrDefault("account") ?? new();
        return new BuildSearchResult
        {
            Total = result.All(1).Select(f => f.Int).FirstOrDefault(),
            Dimensions = result.All(2).Select(f => f.Message).Select(d => new SearchDimension(d.FirstText(1) ?? "", d.FirstText(2) ?? "",
                d.All(3).Select(c => c.Message).GroupBy(c => c.All(1).Select(k => k.Int).FirstOrDefault())
                    .ToDictionary(g => g.Key, g => g.Sum(c => c.All(2).Select(v => v.Int).FirstOrDefault())))).ToList(),
            Dictionaries = result.All(6).Select(f => f.Message).Select(d => new SearchDictionaryRef(d.FirstText(1) ?? "", d.FirstText(2) ?? "", d.FirstText(3))).ToList(),
            Characters = names.Zip(accounts, (name, account) => new CharacterRef(account, name)).ToList(),
            IndexColumns = columnMessages.Where(c => c.FirstText(11) != null && c.Any(f => f.Number == 6)).Select(IndexColumn).ToList(),
        };
    }

    private static List<int> Ints(IReadOnlyList<ProtoField> message, int number) =>
        message.All(number).SelectMany(f => f.WireType == 2 ? ProtoReader.PackedVarints(f.Bytes) : new List<int> { f.Int }).ToList();

    /// <summary>Dictionary indices per row: sparse (values for the listed row ids) or dense (one value per row that is not null).</summary>
    private static SearchIndexColumn IndexColumn(IReadOnlyList<ProtoField> column)
    {
        var values = Ints(column, 6);
        var rowIds = Ints(column, 14);
        var nulls = Ints(column, 10);
        int rows = column.Any(f => f.Number == 13) ? column.All(13).First().Int : values.Count;
        var result = new int?[rows];
        if (rowIds.Count > 0)
        {
            for (int i = 0; i < rowIds.Count && i < values.Count; i++)
                if (rowIds[i] < rows) result[rowIds[i]] = values[i];
        }
        else
        {
            int next = 0;
            for (int row = 0; row < rows && next < values.Count; row++)
                if (!(row < nulls.Count && nulls[row] != 0)) result[row] = values[next++];
        }
        return new SearchIndexColumn(column.FirstText(1) ?? "", column.FirstText(11) ?? "", result);
    }
}
