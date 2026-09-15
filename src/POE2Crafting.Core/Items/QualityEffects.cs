namespace POE2Crafting.Core.Items;

/// <summary>
/// What catalyst quality does to one modifier: its line now, at a chosen quality, at the item's maximum quality, and the next quality above
/// the chosen one where the line improves (whole numbers round down, so e.g. +3 skills only becomes +4 at 34%) — possibly above the item's
/// maximum (then more maximum quality is needed, e.g. Essence of the Breach).
/// </summary>
public sealed record QualityEffect(int Index, string Now, string At, string AtMax, int? NextStepQuality, string? NextStepText);

public static class QualityEffects
{
    /// <summary>
    /// The effect of <paramref name="qualityType"/> quality at <paramref name="quality"/>% and at the item's <paramref name="maxQuality"/>% on every mod
    /// it enhances; the next step is searched up to <paramref name="reachableQuality"/>% (the highest quality any item can get).
    /// </summary>
    public static List<QualityEffect> For(Item item, string qualityType, int quality, int maxQuality, int reachableQuality)
    {
        var at = item.WithQuality(quality, qualityType);
        var max = item.WithQuality(maxQuality, qualityType);
        return item.Mods.Select((mod, index) => (mod, index))
            .Where(x => max.QualityEnhances(x.mod))
            .Select(x =>
            {
                var text = at.EffectiveText(x.mod);
                var next = Enumerable.Range(quality + 1, Math.Max(0, Math.Max(maxQuality, reachableQuality) - quality))
                    .Select(q => (Quality: q, Text: item.WithQuality(q, qualityType).EffectiveText(x.mod)))
                    .FirstOrDefault(s => s.Text != text);
                return new QualityEffect(x.index, item.EffectiveText(x.mod), text, max.EffectiveText(x.mod),
                    next.Text != null ? next.Quality : null, next.Text);
            })
            .ToList();
    }
}
