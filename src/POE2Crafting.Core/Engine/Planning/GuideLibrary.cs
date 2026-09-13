using System.Collections.Concurrent;
using POE2Crafting.Core.Data;

namespace POE2Crafting.Core.Engine.Planning;

/// <summary>The crafting guides of the data store with their walkthroughs, played once and shared by all sessions (guides are deterministic).</summary>
public sealed class GuideLibrary
{
    private readonly CraftingEngine _engine;
    private readonly ConcurrentDictionary<string, GuideWalkthrough> _walkthroughs = new();

    public GuideLibrary(CraftingEngine engine) => _engine = engine;

    public IReadOnlyList<CraftingGuide> Guides => _engine.Data.Guides;

    public CraftingGuide? Find(string? id) => Guides.FirstOrDefault(g => g.Id == id);

    public GuideWalkthrough Walkthrough(CraftingGuide guide) => _walkthroughs.GetOrAdd(guide.Id, _ => new GuideRunner(_engine).Run(guide));
}
