using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Planning;

/// <summary>Actions to evaluate and the caches: the policies search overlapping states with overlapping actions, so previews and results are computed once per plan.</summary>
internal sealed partial class FromItemPlanner
{
    /// <summary>Omen effects whose outcome the planner can reason about.</summary>
    private static readonly string[] PlannableOmenEffects =
    {
        OmenEffects.AddPrefixOnly, OmenEffects.AddSuffixOnly, OmenEffects.RemovePrefixOnly, OmenEffects.RemoveSuffixOnly,
        OmenEffects.RemoveLowestLevel, OmenEffects.RemoveDesecratedOnly, OmenEffects.Catalysing,
        OmenEffects.GuaranteeUlaman, OmenEffects.GuaranteeAmanamu, OmenEffects.GuaranteeKurgal,
    };

    private readonly Dictionary<Item, string> _signatures = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, StepPreview> _previews = new();
    private readonly Dictionary<string, Item?> _simulations = new();
    private readonly Dictionary<(string Op, Tools Tools), List<OmenDef[]>> _omenSets = new();

    /// <summary>
    /// Every currency of the op, alone and (with the Omens tool) with each combination of up to two plannable omens that applies to it
    /// and doesn't contradict itself (e.g. Sinistral Necromancy + Omen of the Blackblooded).
    /// </summary>
    private IEnumerable<(CraftAction Action, StepPreview Preview)> Actions(Item item, string op, Tools tools, Func<CurrencyDef, bool>? filter = null)
    {
        var omenSets = OmenSets(op, tools);
        foreach (var currency in _data.CurrenciesOf(op).Where(c => filter == null || filter(c)))
            foreach (var omenSet in omenSets)
            {
                var action = new CraftAction { Currency = currency, Omens = omenSet };
                var preview = Preview(item, action);
                if (preview.Applicability.Ok) yield return (action, preview);
            }
    }

    /// <summary>No omen, each plannable omen of the op, and every non-conflicting pair (only with the Omens tool).</summary>
    private List<OmenDef[]> OmenSets(string op, Tools tools)
    {
        var key = (op, tools & Tools.Omens);
        if (_omenSets.TryGetValue(key, out var sets)) return sets;
        var omens = tools.HasFlag(Tools.Omens)
            ? _data.Omens.Where(o => o.Crafting && PlannableOmenEffects.Contains(o.Effect) && _data.OpOfOmenTarget(o.TargetCurrency) == op).ToList()
            : new List<OmenDef>();
        return _omenSets[key] = omens.Select((a, i) => omens.Skip(i + 1).Select(b => new[] { a, b }).Prepend(new[] { a }))
            .SelectMany(pairs => pairs)
            .Where(set => OmenEffects.Conflict(set) == null)
            .Prepend(Array.Empty<OmenDef>())
            .ToList();
    }

    /// <summary>Identifies an item state for the search (mods, values, rarity, quality, sockets, runes).</summary>
    private string SignatureOf(Item item)
    {
        if (!_signatures.TryGetValue(item, out var signature))
            _signatures[item] = signature = string.Join(";",
                new[] { item.Rarity.ToString(), $"{item.Quality}{item.QualityType}", item.Sockets.ToString(), string.Join(",", item.Runes) }
                    .Concat(item.Mods.Select(m => $"{m.ModId}|{m.Kind}|{m.Affix}|{m.Fractured}|{m.Unrevealed}|{string.Join(",", m.Values)}|{m.RawText}").Order()));
        return signature;
    }

    private StepPreview Preview(Item item, CraftAction action, int? forcedRemovalIndex = null)
    {
        var key = $"{SignatureOf(item)}#{action.DisplayName}#{forcedRemovalIndex}";
        if (!_previews.TryGetValue(key, out var preview)) _previews[key] = preview = _engine.Preview(item, action, forcedRemovalIndex);
        return preview;
    }

    private Item? Simulate(Item item, CraftAction action, ManualChoice choice)
    {
        var key = $"{SignatureOf(item)}#{action.DisplayName}#{string.Join(",", choice.RemoveIndices)}#{string.Join(",", choice.AddModIds)}#"
                  + $"{string.Join("/", choice.Values.Select(v => v == null ? "" : string.Join(",", v)))}#{choice.SpecialOutcome}#"
                  + string.Join("/", choice.Rerolls?.Select(r => $"{r.Key}:{string.Join(",", r.Value)}") ?? Array.Empty<string>());
        if (_simulations.TryGetValue(key, out var cached)) return cached;
        return _simulations[key] = OutcomeChance.TryExecute(_engine, item, action, choice);
    }
}
