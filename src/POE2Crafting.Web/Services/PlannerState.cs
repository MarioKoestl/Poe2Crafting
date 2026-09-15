using POE2Crafting.Core.Data;
using POE2Crafting.Core.Drafting;
using POE2Crafting.Core.Engine.Planning;
using POE2Crafting.Core.Items;
using Microsoft.AspNetCore.Components;

namespace POE2Crafting.Web.Services;

/// <summary>
/// Per-user state of the Crafting Planner and Guides tabs (target draft, last result, chosen strategy and guide), kept in a scoped service
/// so switching tabs doesn't lose it. Components that show it subscribe to <see cref="Changed"/> (see <see cref="PlannerStateComponentBase"/>).
/// </summary>
public sealed class PlannerState
{
    public PlannerState(GameData data) => Draft = new ItemDraft(data);

    /// <summary>Raised after the result, the planning flag, the chosen strategy or the chosen guide changed.</summary>
    public event Action? Changed;

    public ItemDraft Draft { get; }
    public FinishingTarget Finishing { get; } = new();
    public bool AllowBetterTiers { get; set; } = true;
    /// <summary>Project item the planner starts from; null = the current item of the simulator.</summary>
    public string? SourceItemId { get; set; }
    /// <summary>Project item the target was last loaded from (shown in the selector).</summary>
    public string? TargetItemId { get; set; }

    /// <summary>The last planning result and the item it starts from, or null.</summary>
    public PlannerResult? Result { get; private set; }
    public CraftingStrategy? SelectedStrategy { get; private set; }
    public bool Planning { get; private set; }
    public string? SelectedGuideId { get; private set; }

    public void SetPlanning(bool planning) => Update(() => Planning = planning);

    public void ShowResult(PlannerResult result) => Update(() =>
    {
        Result = result;
        SelectedStrategy = result.Plan.Strategies.FirstOrDefault();
    });

    public void SelectStrategy(CraftingStrategy strategy) => Update(() => SelectedStrategy = strategy);

    public void SelectGuide(string? guideId) => Update(() => SelectedGuideId = guideId);

    private void Update(Action change)
    {
        change();
        Changed?.Invoke();
    }
}

/// <summary>A planning result with the item it starts from.</summary>
public sealed record PlannerResult(PlanResult Plan, Item Start);

/// <summary>A component that re-renders whenever the <see cref="PlannerState"/> changes (also when the change comes from a sibling component).</summary>
public abstract class PlannerStateComponentBase : ComponentBase, IDisposable
{
    [Inject] protected PlannerState Planner { get; set; } = null!;

    protected override void OnInitialized() => Planner.Changed += OnPlannerChanged;

    private void OnPlannerChanged() => InvokeAsync(StateHasChanged);

    public virtual void Dispose() => Planner.Changed -= OnPlannerChanged;
}
