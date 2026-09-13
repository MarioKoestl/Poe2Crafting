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

/// <summary>The search rule of all search boxes: the trimmed query is part of any of the texts (case-insensitive); an empty query matches everything.</summary>
public static class TextSearch
{
    public static bool Matches(string? query, params string?[] texts) => Matches(query, (IEnumerable<string?>)texts);

    public static bool Matches(string? query, IEnumerable<string?> texts)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var q = query.Trim();
        return texts.Any(t => t?.Contains(q, StringComparison.OrdinalIgnoreCase) == true);
    }
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
