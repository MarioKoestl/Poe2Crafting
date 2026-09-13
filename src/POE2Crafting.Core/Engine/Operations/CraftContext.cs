using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>An action against the item state before it is applied (check / preview).</summary>
internal class CraftContext
{
    public Item Item { get; init; } = null!;
    public CurrencyDef Currency { get; init; } = null!;
    public IReadOnlyList<OmenDef> Omens { get; init; } = Array.Empty<OmenDef>();
    /// <summary>Assumptions and hints collected while checking.</summary>
    public List<string> Notes { get; } = new();

    public int MinModLevel => Currency.MinModLevel ?? 0;
    public bool OmenIs(string effect) => Omens.Has(effect);
    public AffixType? RestrictedType => OmenEffects.RestrictedType(Omens);
}

/// <summary>An action being applied: <see cref="Result"/> is a clone of <see cref="CraftContext.Item"/> that operations modify.</summary>
internal sealed class ExecuteContext : CraftContext
{
    public Item Result { get; init; } = null!;
    public Rng Rng { get; init; } = null!;
    public ManualChoice? Choice { get; init; }
    public List<string> Details { get; } = new();
    public bool Destroyed { get; set; }

    /// <summary>Corrupt the result and say so.</summary>
    public void Corrupt()
    {
        Result.Corrupted = true;
        Details.Add("Item is now Corrupted.");
    }
}
