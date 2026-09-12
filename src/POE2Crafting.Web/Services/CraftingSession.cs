using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Web.Services;

/// <summary>Per-user scoped crafting session: holds the current item, crafting history, RNG seed.</summary>
public sealed class CraftingSession
{
    private readonly GameData _data;
    private readonly CraftingEngine _engine;

    public Item? CurrentItem { get; private set; }
    public List<HistoryEntry> History { get; } = new();
    public Rng Rng { get; private set; } = new();
    public string? ProjectName { get; set; }
    public string? LastError { get; private set; }

    public CraftingSession(GameData data, ModPool pool)
    {
        _data = data;
        _engine = new CraftingEngine(data, pool);
    }

    public CraftingEngine Engine => _engine;
    public GameData Data => _data;

    public void SetError(string message) => LastError = message;
    public void ClearError() => LastError = null;

    public void ImportItem(string text)
    {
        try
        {
            var item = ItemParser.Parse(text, _data);
            if (item.Base == null) { LastError = $"Import failed: unknown base item \"{item.BaseName}\"."; return; }
            SetItem(item, "Imported");
            var unresolved = item.Mods.Count(m => m.IsAffix && m.Def == null);
            if (unresolved > 0) LastError = $"Imported, but {unresolved} modifier(s) could not be matched to game data and are shown as text only.";
        }
        catch (Exception ex) { LastError = $"Import failed: {ex.Message}"; }
    }

    /// <summary>Replace the current item (import, compose) and start a new history.</summary>
    public void SetItem(Item item, string action)
    {
        item.Bind(_data);
        CurrentItem = item;
        LastError = null;
        History.Clear();
        History.Add(new HistoryEntry
        {
            Action = action,
            Item = item.Clone(),
            Summary = $"{action} {item.BaseName} ({item.Rarity}, ilvl {item.ItemLevel}, {item.PrefixCount}P/{item.SuffixCount}S)",
        });
    }

    public StepPreview Preview(CraftAction action, int? forcedRemovalIndex = null) =>
        CurrentItem != null ? _engine.Preview(CurrentItem, action, forcedRemovalIndex) : new StepPreview { Applicability = Applicability.No("No item loaded.") };

    public CraftResult? Execute(CraftAction action, ManualChoice? choice = null)
    {
        if (CurrentItem == null) { LastError = "No item loaded."; return null; }
        CraftResult result;
        try
        {
            result = _engine.Execute(CurrentItem, action, Rng, choice);
        }
        catch (InvalidOperationException ex)
        {
            LastError = ex.Message;
            return null;
        }
        if (result.Applied && !result.Destroyed)
        {
            CurrentItem = result.Item;
            History.Add(new HistoryEntry { Action = action.DisplayName, Item = CurrentItem.Clone(), Summary = result.Summary, Details = result.Details });
        }
        LastError = !result.Applied ? result.Summary
                  : result.Destroyed ? $"{action.DisplayName} destroyed the item. The simulator keeps the previous state so you can try again."
                  : null;
        return result;
    }

    public void Undo()
    {
        if (History.Count <= 1) return;
        History.RemoveAt(History.Count - 1);
        CurrentItem = History[^1].Item.Clone();
        CurrentItem.Bind(_data);
        LastError = null;
    }

    public void ResetRng(int? seed = null) => Rng = new Rng(seed);

    /// <summary>All simulated currencies, essences and alloys with their applicability to the current item.</summary>
    public List<(CurrencyDef Currency, Applicability App)> ApplicableCurrencies()
    {
        if (CurrentItem == null) return new();
        return _data.Currencies.Where(c => c.Op != null).Concat(_data.EssenceCurrencies)
            .Select(c => (c, _engine.Check(CurrentItem, new CraftAction { Currency = c })))
            .ToList();
    }

    /// <summary>Crafting omens that can modify the given currency on the current item.</summary>
    public List<OmenDef> OmensFor(CurrencyDef currency)
    {
        if (CurrentItem == null) return new();
        return _data.Omens.Where(o => o.Crafting && o.TargetCurrency != null &&
            _engine.Check(CurrentItem, new CraftAction { Currency = currency, Omen = o }).Ok).ToList();
    }

    // ---- save / load ----

    public CraftingProject ToProject() => new()
    {
        Name = ProjectName ?? "Untitled",
        Item = CurrentItem,
        History = History.ToList(),
        RngSeed = Rng.Seed,
        SavedAt = DateTime.UtcNow,
    };

    public void LoadProject(CraftingProject project)
    {
        CurrentItem = project.Item?.Clone();
        CurrentItem?.Bind(_data);
        History.Clear();
        History.AddRange(project.History ?? new());
        foreach (var h in History) h.Item?.Bind(_data);
        ProjectName = project.Name;
        Rng = new Rng(project.RngSeed);
        LastError = null;
    }
}

public sealed class HistoryEntry
{
    public string Action { get; init; } = "";
    public Item Item { get; init; } = null!;
    public string Summary { get; init; } = "";
    public List<string> Details { get; init; } = new();
}

public sealed class CraftingProject
{
    public string Name { get; set; } = "Untitled";
    public Item? Item { get; set; }
    public List<HistoryEntry>? History { get; set; }
    public int RngSeed { get; set; }
    public DateTime SavedAt { get; set; }
}
