using POE2Crafting.Core.Data;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>
/// One currency operation (<see cref="CurrencyDef.Op"/>). The engine runs the generic checks (rarity, target class, mirrored, corrupted ...)
/// before calling <see cref="Check"/>; <see cref="Preview"/> and <see cref="Execute"/> are only called when the check passed.
/// An impossible manual choice in <see cref="Execute"/> throws <see cref="InvalidChoiceException"/>.
/// </summary>
internal abstract class CraftOperation
{
    protected CraftOperation(CraftingEngine engine, string op)
    {
        Engine = engine;
        Op = op;
    }

    protected CraftingEngine Engine { get; }
    protected SimAssumptions Assumptions => Engine.Assumptions;
    public string Op { get; }

    public virtual bool WorksOnCorrupted => false;

    /// <summary>Only usable on corrupted items (Orbs of Sacrifice, Architect's Orb).</summary>
    public virtual bool RequiresCorrupted => false;

    /// <summary>Class group used when the currency data defines none (<see cref="ClassTargets"/>).</summary>
    public virtual string? DefaultClassTarget => null;

    /// <summary>Whether the omen modifies this operation (by default: the omen's target currency maps to this op).</summary>
    public virtual bool AcceptsOmen(CraftContext ctx, OmenDef omen) => Engine.Data.OpOfOmenTarget(omen.TargetCurrency) == Op;

    /// <summary>Operation-specific applicability; return null when applicable (and add assumption notes to ctx.Notes).</summary>
    public abstract Applicability? Check(CraftContext ctx);

    public abstract StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex);

    public abstract void Execute(ExecuteContext ctx);
}
