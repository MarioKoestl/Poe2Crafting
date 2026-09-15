using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine;

/// <summary>Result of checking whether an action can be applied to an item.</summary>
public sealed class Applicability
{
    public bool Ok { get; init; }
    public string Reason { get; init; } = "";
    public List<string> Notes { get; init; } = new();
    public static Applicability No(string reason) => new() { Ok = false, Reason = reason };
}

/// <summary>Everything the UI needs to show before a currency is applied.</summary>
public sealed class StepPreview
{
    public Applicability Applicability { get; init; } = Applicability.No("");
    public int RemoveCount { get; init; }
    public int AddCount { get; init; }
    /// <summary>Distribution of the mod that gets removed (first removal).</summary>
    public List<RemovalCandidate> Removals { get; init; } = new();
    /// <summary>Marginal distribution of the (first) added mod.</summary>
    public List<ModCandidate> Additions { get; init; } = new();
    /// <summary>False when the additions are only informational (e.g. what a desecrated mod could reveal into) and cannot be chosen.</summary>
    public bool AdditionsChoosable { get; init; } = true;
    public string? AdditionLabel { get; init; }
    /// <summary>A second group of possible additions shown below the first (reveals: the regular modifiers besides the exclusive ones).</summary>
    public List<ModCandidate> OtherAdditions { get; init; } = new();
    public string? OtherAdditionLabel { get; init; }
    /// <summary>For special currencies (Orb of Chance, Desecration ...): named outcomes with probabilities.</summary>
    public Dictionary<string, double> SpecialOutcomes { get; init; } = new();
    /// <summary>Assumptions and hints about the action (without <see cref="WeightsNote"/>).</summary>
    public List<string> Notes { get; init; } = new();
    /// <summary>Where the mod weights come from (the same for every action).</summary>
    public string? WeightsNote { get; init; }
    public double PrefixProbability { get; init; }
    public double SuffixProbability { get; init; }
    /// <summary>Removal and addition are separate random events (Chaos Orb): a manual choice picks the removal first, then the addition.</summary>
    public bool TwoStepChoice { get; init; }
    /// <summary>Heading for the Removals list (e.g. "Fracture Target" when the list is not a removal).</summary>
    public string? RemovalLabel { get; init; }
    /// <summary>Mods whose values the action rolls anew (Divine Orb, Flux); a manual choice sets them via <see cref="ManualChoice.Rerolls"/>.</summary>
    public List<ValueReroll> ValueRerolls { get; init; } = new();
    /// <summary>Catalysts: the quality after the use (default and allowed range); a manual choice sets it via <see cref="ManualChoice.Quality"/>.</summary>
    public QualityChoice? Quality { get; init; }

    /// <summary>A copy with the engine's generic parts: the applicability, its notes in front, and the weights note.</summary>
    internal StepPreview WithApplicability(Applicability applicability, string weightsNote) => new()
    {
        Applicability = applicability, WeightsNote = weightsNote,
        RemoveCount = RemoveCount, AddCount = AddCount, Removals = Removals, Additions = Additions, AdditionsChoosable = AdditionsChoosable,
        AdditionLabel = AdditionLabel, OtherAdditions = OtherAdditions, OtherAdditionLabel = OtherAdditionLabel, SpecialOutcomes = SpecialOutcomes, Notes = applicability.Notes.Concat(Notes).ToList(),
        PrefixProbability = PrefixProbability, SuffixProbability = SuffixProbability, TwoStepChoice = TwoStepChoice, RemovalLabel = RemovalLabel,
        ValueRerolls = ValueRerolls, Quality = Quality,
    };
}

/// <summary>A mod (by index into Item.Mods) whose values are rolled anew, and the modifier whose ranges the new values have (a Flux changes it).</summary>
public sealed record ValueReroll(int Index, ModDef Mod);

/// <summary>Quality of <paramref name="Type"/> after a catalyst: <paramref name="Default"/> when rolled, any value from <paramref name="Min"/> to <paramref name="Max"/> when chosen.</summary>
public sealed record QualityChoice(string Type, int Default, int Min, int Max);

/// <summary>Manual selection of an outcome instead of rolling.</summary>
public sealed class ManualChoice
{
    /// <summary>Indices (into Item.Mods) of the mods to remove, in order.</summary>
    public List<int> RemoveIndices { get; init; } = new();
    /// <summary>Ids of the mods to add, in order.</summary>
    public List<string> AddModIds { get; init; } = new();
    /// <summary>Optional rolled values per added mod (null = roll randomly within the tier).</summary>
    public List<List<double>?> Values { get; init; } = new();
    public string? SpecialOutcome { get; init; }
    /// <summary>Divine Orb, Flux: values per mod index (see <see cref="StepPreview.ValueRerolls"/>); missing indices roll randomly.</summary>
    public Dictionary<int, List<double>>? Rerolls { get; init; }
    /// <summary>Catalysts: the quality after the use (see <see cref="StepPreview.Quality"/>).</summary>
    public int? Quality { get; init; }
}

public sealed class CraftResult
{
    public bool Applied { get; init; }
    public Item Item { get; init; } = null!;
    public string Summary { get; init; } = "";
    public List<string> Details { get; init; } = new();
    public bool Destroyed { get; init; }
}

/// <summary>A manual choice that the action cannot produce on this item (e.g. a modifier that cannot roll right now).</summary>
public sealed class InvalidChoiceException : InvalidOperationException
{
    public InvalidChoiceException(string message) : base(message) { }
}
