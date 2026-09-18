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
    /// <summary>Item classes whose rare item card is open.</summary>
    public HashSet<string> ExpandedClasses { get; } = new();
}
