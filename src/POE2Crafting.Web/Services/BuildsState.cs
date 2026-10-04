using POE2Crafting.Core.Builds;

namespace POE2Crafting.Web.Services;

/// <summary>
/// Per-user state of the Builds page (league, filter, last search, sample size, opened item class cards), kept in a scoped service so leaving
/// the page (e.g. to the simulator) and coming back shows it at once instead of loading everything again.
/// </summary>
public sealed class BuildsState
{
    public IReadOnlyList<NinjaSnapshot> Snapshots { get; set; } = Array.Empty<NinjaSnapshot>();
    public NinjaSnapshot? Snapshot { get; set; }
    public BuildFilter Filter { get; set; } = BuildFilter.None;
    /// <summary>The search of <see cref="Snapshot"/> and <see cref="Filter"/>, or null before the first load.</summary>
    public BuildOverview? Overview { get; set; }
    public int SampleSize { get; set; } = 40;
    /// <summary>Search over the analysed item classes and their bases.</summary>
    public string ClassSearch { get; set; } = "";
    /// <summary>Search over the sampled characters (name, ascendancy, skill, worn item).</summary>
    public string CharacterSearch { get; set; } = "";
    /// <summary>What the analysis pane shows: the item classes to craft, or the sampled builds themselves.</summary>
    public BuildsTab Tab { get; set; } = BuildsTab.Items;
    /// <summary>Item classes whose rare item card is open.</summary>
    public HashSet<string> ExpandedClasses { get; } = new();
}

/// <summary>The two views of an analysis: what to craft, and the builds it came from.</summary>
public enum BuildsTab { Items, Builds }
