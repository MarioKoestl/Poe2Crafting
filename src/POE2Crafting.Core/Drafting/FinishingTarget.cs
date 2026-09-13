using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine.Planning;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Drafting;

/// <summary>
/// The planner target beyond modifiers: minimum quality (and catalyst type for jewellery), socketed augments and an instilled notable.
/// </summary>
public sealed class FinishingTarget
{
    public int? MinQuality { get; set; }
    public string? QualityType { get; set; }
    /// <summary>Augment names, one per socket.</summary>
    public List<string> Augments { get; } = new();
    public string? InstillNotable { get; set; }

    /// <summary>Take quality, catalyst type, socketed augments (matched by their effect text) and the instilled notable of an item.</summary>
    public void LoadFrom(Item item, GameData data)
    {
        MinQuality = item.Quality > 0 ? item.Quality : null;
        QualityType = item.QualityType;
        Augments.Clear();
        var available = data.AugmentsFor(item.Base, item.ItemClass).ToList();
        foreach (var rune in item.Runes)
            if (available.FirstOrDefault(a => data.AugmentEffectText(a, item.Base, item.ItemClass) == rune) is { } match) Augments.Add(match.Name);
        InstillNotable = item.InstilledNotableName;
    }

    public void ApplyTo(TargetItemSpec spec)
    {
        spec.MinQuality = MinQuality;
        spec.QualityType = QualityType;
        spec.Augments = Augments.Select(a => new AugmentTarget { Name = a }).ToList();
        spec.InstillNotable = InstillNotable;
    }
}
