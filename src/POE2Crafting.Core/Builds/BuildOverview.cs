using System.Text.Json.Serialization;

namespace POE2Crafting.Core.Builds;

/// <summary>poe.ninja's index state (GET /poe2/api/data/index-state): the build snapshot of each league.</summary>
public sealed class NinjaIndexState
{
    [JsonPropertyName("snapshotVersions")] public List<NinjaSnapshot> Snapshots { get; set; } = new();
}

/// <summary>A league's build snapshot: <see cref="Url"/> = page slug ("forbiddenrites"), <see cref="SnapshotName"/> = API overview ("forbidden-rites"), <see cref="Version"/> changes with every update.</summary>
public sealed class NinjaSnapshot
{
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("snapshotName")] public string SnapshotName { get; set; } = "";
}

/// <summary>A value of a dimension with its number of characters; <see cref="Type"/> = item type for items ("Gloves").</summary>
public sealed record NamedCount(string Name, int Count, string? Type = null);

/// <summary>Filter of a build search; null parts are not filtered. <see cref="Item"/> = a used item value ("Rare Staff", "Headhunter").</summary>
public sealed record BuildFilter(string? Class = null, string? Skill = null, string? Item = null)
{
    public static readonly BuildFilter None = new();

    /// <summary>"&amp;class=Gemling%20Legionnaire&amp;skills=Twister&amp;items=Rare%20Staff".</summary>
    public string Query =>
        (Class != null ? "&class=" + Uri.EscapeDataString(Class) : "") + (Skill != null ? "&skills=" + Uri.EscapeDataString(Skill) : "")
        + (Item != null ? "&items=" + Uri.EscapeDataString(Item) : "");

    public override string ToString() => string.Join(" · ", new[] { Skill, Class, Item }.OfType<string>()) is { Length: > 0 } text ? text : "All builds";
}

/// <summary>A build = main skill + ascendancy, with how many of the listed top characters play it.</summary>
public sealed record BuildCount(string Skill, string Class, int Count);

/// <summary>A build search with names resolved: ascendancies, main skills, used items (rare/magic slots and uniques), the top characters.</summary>
public sealed class BuildOverview
{
    public const string RarePrefix = "Rare ";
    /// <summary>Item values that name a rarity and slot ("Rare Gloves") instead of a unique.</summary>
    private static readonly string[] SlotPrefixes = { RarePrefix, "Magic ", "Normal " };

    public int Total { get; init; }
    public IReadOnlyList<NamedCount> Classes { get; init; } = Array.Empty<NamedCount>();
    public IReadOnlyList<NamedCount> Skills { get; init; } = Array.Empty<NamedCount>();
    public IReadOnlyList<NamedCount> Items { get; init; } = Array.Empty<NamedCount>();
    public IReadOnlyList<CharacterRef> Characters { get; init; } = Array.Empty<CharacterRef>();
    /// <summary>Main skill (the skill poe.ninja shows the DPS of) + ascendancy of the listed top characters, most played first.</summary>
    public IReadOnlyList<BuildCount> TopBuilds { get; init; } = Array.Empty<BuildCount>();

    /// <summary>"Rare Gloves", "Rare Ring": how many characters wear a rare of the type.</summary>
    public IEnumerable<NamedCount> RareSlots => Items.Where(i => i.Name.StartsWith(RarePrefix, StringComparison.Ordinal));

    public IEnumerable<NamedCount> Uniques => Items.Where(i => !SlotPrefixes.Any(p => i.Name.StartsWith(p, StringComparison.Ordinal)));

    /// <summary>Resolve dimension keys through the dictionaries (values by hash; item types from the item dictionary's properties).</summary>
    public static BuildOverview From(BuildSearchResult search, Func<string, IReadOnlyList<string>> values,
        Func<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> properties)
    {
        IReadOnlyList<NamedCount> Resolve(string dimensionId, string? typeProperty = null)
        {
            if (search.Dimension(dimensionId) is not { } dimension || search.Dictionary(dimension.DictionaryId) is not { } reference) return Array.Empty<NamedCount>();
            var names = values(reference.Hash);
            var types = typeProperty != null && reference.PropertiesHash != null ? properties(reference.PropertiesHash).GetValueOrDefault(typeProperty) : null;
            return dimension.Counts.Where(c => c.Key < names.Count && c.Value > 0)
                .Select(c => new NamedCount(names[c.Key], c.Value, types != null && c.Key < types.Count ? types[c.Key] : null))
                .OrderByDescending(c => c.Count).ToList();
        }
        // per listed character: the dictionary value of an index column ("class" -> class dictionary, "dps.skill" -> gem dictionary)
        IReadOnlyList<string?> Column(string columnId)
        {
            if (search.IndexColumn(columnId) is not { } column || search.Dictionary(column.DictionaryId) is not { } reference) return Array.Empty<string?>();
            var names = values(reference.Hash);
            return column.Values.Select(v => v is { } i && i < names.Count ? names[i] : null).ToList();
        }
        var topBuilds = Column("class").Zip(Column("dps.skill"), (c, s) => (Class: c, Skill: s))
            .Where(b => !string.IsNullOrEmpty(b.Class) && !string.IsNullOrEmpty(b.Skill))
            .GroupBy(b => b)
            .Select(g => new BuildCount(g.Key.Skill!, g.Key.Class!, g.Count()))
            .OrderByDescending(b => b.Count).ThenBy(b => b.Skill)
            .ToList();
        return new BuildOverview
        {
            Total = search.Total,
            Classes = Resolve("class"),
            Skills = Resolve("skills"),
            Items = Resolve("items", "type"),
            Characters = search.Characters,
            TopBuilds = topBuilds,
        };
    }
}
