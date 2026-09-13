using POE2Crafting.Core.Data;

namespace POE2Crafting.Core.Engine;

/// <summary>
/// What the user wants to apply: a currency (essences/alloys are synthetic currencies with Op essence), modified by the omens
/// active in the inventory (any number, as long as they don't contradict each other — see <see cref="OmenEffects.Conflict"/>).
/// </summary>
public sealed class CraftAction
{
    public CurrencyDef Currency { get; init; } = null!;
    public IReadOnlyList<OmenDef> Omens { get; init; } = Array.Empty<OmenDef>();

    /// <summary>"Chaos Orb + Omen of Whittling": shown to the user and the identity of an action (Hinekora's Lock, planner caches).</summary>
    public string DisplayName => string.Join(" + ", Omens.Select(o => o.Name).Prepend(Currency.Name));

    public static CraftAction Of(CurrencyDef currency, params OmenDef?[] omens) =>
        new() { Currency = currency, Omens = omens.OfType<OmenDef>().ToList() };
}
