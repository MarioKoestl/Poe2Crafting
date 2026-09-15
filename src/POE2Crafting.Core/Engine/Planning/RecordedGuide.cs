using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Planning;

/// <summary>One state of an item's crafting history: the action that led to it (<see cref="CraftAction.DisplayName"/>, or e.g. "Imported"), the item after it and a summary.</summary>
public sealed class HistoryEntry
{
    public string Action { get; init; } = "";
    public Item Item { get; init; } = null!;
    public string Summary { get; init; } = "";
}

/// <summary>A crafting run of the simulator saved as a guide: its recorded history, the first entry being the starting item.</summary>
public sealed class RecordedGuide
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public List<HistoryEntry> History { get; init; } = new();
}

/// <summary>
/// Turns a <see cref="RecordedGuide"/> into a walkthrough like a curated guide: the recorded items after each step, the currencies and omens used,
/// and per step the chance to get that result again (the same removed or fractured mod, an added mod of the same tier or better) from
/// <see cref="CraftingEngine.Preview"/> — so chances follow the current rules while the path stays exactly what was crafted.
/// </summary>
public sealed class RecordedGuideRunner
{
    /// <summary>Tag of every recorded guide in the guide list.</summary>
    public const string Tag = "My guide";

    private const string ObservedNote = "Chance to get this result again: the same removed/fractured mod and an added mod of this tier or better.";
    private const string UndeterminedNote = "The chance of this result could not be derived from the preview (e.g. a named outcome); the step counts as certain.";

    private readonly CraftingEngine _engine;
    private readonly GameData _data;

    public RecordedGuideRunner(CraftingEngine engine)
    {
        _engine = engine;
        _data = engine.Data;
    }

    public GuideWalkthrough Run(RecordedGuide recorded)
    {
        var start = recorded.History.FirstOrDefault()?.Item;
        var final = recorded.History.LastOrDefault()?.Item;
        var guide = new CraftingGuide
        {
            Id = recorded.Id,
            Name = recorded.Name,
            Summary = !string.IsNullOrWhiteSpace(recorded.Notes) ? recorded.Notes
                : start != null ? $"Recorded in the simulator: {start.BaseName} ({start.Rarity}, ilvl {start.ItemLevel}) → {final!.Title}." : "",
            Tags = start != null ? new() { Tag, start.BaseName } : new() { Tag },
            Start = start != null ? new GuideItem { Base = start.BaseName, Rarity = start.Rarity, ItemLevel = start.ItemLevel } : new(),
        };
        var walkthrough = new GuideWalkthrough
        {
            Guide = guide, Start = start,
            Strategy = new CraftingStrategy { Id = "recorded-" + recorded.Id, Name = recorded.Name, StartLabel = "Starting item" },
        };
        if (start == null)
        {
            walkthrough.Problems.Add("The guide has no recorded item.");
            return walkthrough;
        }
        for (int i = 1; i < recorded.History.Count; i++)
            walkthrough.Strategy.Add(Step(recorded.History[i - 1].Item, recorded.History[i], walkthrough));
        return walkthrough;
    }

    private CraftStep Step(Item before, HistoryEntry entry, GuideWalkthrough walkthrough)
    {
        var strategy = walkthrough.Strategy;
        string Problem(string reason) => $"Step {strategy.Steps.Count + 1} ({entry.Action}): {reason}";

        if (ActionOf(entry.Action) is not { } action)
        {
            // not a currency: an instill consumes its emotions, anything else (manual edit) has no chance or materials
            var recipe = InstillRecipe.NotableOfAction(entry.Action) is { } notable ? _data.FindInstill(notable) : null;
            var manual = strategy.NewStep(CraftAction.Of(new CurrencyDef { Name = entry.Action }), entry.Summary, 1, entry.Item);
            manual.Materials = recipe != null ? _data.InstillMaterials(recipe) : new();
            if (recipe == null) walkthrough.Problems.Add(Problem("not a currency step — its chance and materials are not included."));
            return manual;
        }

        var preview = _engine.Preview(before, action);
        if (!preview.Applicability.Ok)
        {
            walkthrough.Problems.Add(Problem($"cannot be applied with the current rules: {preview.Applicability.Reason}"));
            return strategy.NewStep(action, entry.Summary, 1, entry.Item);
        }
        var (chance, determined) = ObservedChance(before, entry.Item, action, preview);
        var step = strategy.NewStep(action, entry.Summary, chance, entry.Item);
        if (chance < 1 || !determined) step.Notes.Add(determined ? ObservedNote : UndeterminedNote);
        step.Notes.AddRange(preview.Notes.Distinct());
        return step;
    }

    /// <summary>The recorded action as a currency with omens, or null when it isn't one (or a name is no longer in the data store).</summary>
    private CraftAction? ActionOf(string name)
    {
        var parts = name.Split(" + ");
        if (_data.FindCurrency(parts[0]) is not { Op: not null } currency) return null;
        var omens = parts.Skip(1).Select(_data.FindOmen).ToList();
        return omens.Contains(null) ? null : CraftAction.Of(currency, omens.ToArray());
    }

    /// <summary>
    /// Chance that the action gives the recorded result again: the removed (or fractured) mod times an added mod of the same tier group at this
    /// tier or better. <c>Determined</c> is false when the action has random parts that the preview can't attribute (named outcomes).
    /// </summary>
    private (double Chance, bool Determined) ObservedChance(Item before, Item after, CraftAction action, StepPreview preview)
    {
        bool random = preview.Removals.Count > 1 || preview.Additions.Count > 1 || preview.SpecialOutcomes.Count > 1;
        bool determined = false;
        double chance = 1;

        var removedIds = ModIdsWithout(before.Mods, after.Mods);
        var hitIds = removedIds
            .Concat(after.Mods.Where(m => m.Fractured && !before.Mods.Any(b => b.ModId == m.ModId && b.Fractured)).Select(m => m.ModId))
            .ToHashSet();
        RemovalCandidate? removal = null;
        if (preview.Removals.Count > 0 && OutcomeChance.Of(preview.Removals, r => hitIds.Contains(r.Mod.ModId)) is { Best: { } best } removed)
        {
            chance *= removed.Chance;
            removal = best;
            determined = true;
        }

        var addedIds = ModIdsWithout(after.Mods, before.Mods);
        var added = after.Affixes.Where(m => m.Def != null && addedIds.Contains(m.ModId)).Select(m => m.Def!).ToList();
        if (added.Count > 0 && preview.Additions.Count > 0 && preview.AdditionsChoosable)
        {
            // when removal and addition are separate random events (Chaos Orb), the addition pool depends on the removed mod
            var additions = removal != null && preview.TwoStepChoice ? _engine.Preview(before, action, removal.Index).Additions : preview.Additions;
            if (OutcomeChance.Of(additions, m => added.Any(a => ModTiers.IsSameOrBetterTier(m, a))) is { Best: not null } hit)
            {
                chance *= hit.Chance;
                determined = true;
            }
        }
        return (chance, determined || !random);
    }

    /// <summary>Ids of the known mods in <paramref name="mods"/> without a partner in <paramref name="others"/> (multiset).</summary>
    private static List<string> ModIdsWithout(IEnumerable<ItemMod> mods, IEnumerable<ItemMod> others)
    {
        var remaining = others.Where(m => m.Def != null).Select(m => m.ModId).ToList();
        return mods.Where(m => m.Def != null && !remaining.Remove(m.ModId)).Select(m => m.ModId).ToList();
    }
}
