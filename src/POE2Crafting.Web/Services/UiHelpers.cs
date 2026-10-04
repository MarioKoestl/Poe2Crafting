using Microsoft.AspNetCore.Components;

namespace POE2Crafting.Web.Services;

public static class CollectionExtensions
{
    /// <summary>Add the value when missing, remove it when present (expanded sections, ticked steps).</summary>
    public static void Toggle<T>(this ISet<T> set, T value)
    {
        if (!set.Remove(value)) set.Add(value);
    }
}

/// <summary>Links to poe.ninja, shared by everything that shows a sampled character.</summary>
public static class NinjaLinks
{
    /// <summary>A character's page in a league ("forbiddenrites").</summary>
    public static string Character(string leagueSlug, POE2Crafting.Core.Builds.CharacterRef character) =>
        $"https://poe.ninja/poe2/builds/{leagueSlug}/character/{Uri.EscapeDataString(character.Account)}/{Uri.EscapeDataString(character.Name)}";
}

/// <summary>A component that changes shared state and tells its parent afterwards (the parent re-renders the item, history, preview).</summary>
public abstract class ChangingComponentBase : ComponentBase
{
    /// <summary>Raised after the component changed state that the page shows elsewhere.</summary>
    [Parameter] public EventCallback OnChanged { get; set; }

    /// <summary>Apply a change, then notify the parent.</summary>
    protected Task Change(Action change)
    {
        change();
        return OnChanged.InvokeAsync();
    }
}
