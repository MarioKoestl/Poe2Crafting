using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Web.Services;

/// <summary>Per-user scoped crafting session: holds the current item, crafting history, RNG seed and pending Well of Souls reveals.</summary>
public sealed class CraftingSession
{
    private readonly GameData _data;
    private readonly CraftingEngine _engine;
    /// <summary>Options rolled at the Well of Souls per unrevealed mod index (cleared whenever the item changes).</summary>
    private readonly Dictionary<int, RevealState> _reveals = new();

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

    /// <summary>Parse and set an item text; returns false when nothing was imported (LastError says why, or warns about unmatched mods).</summary>
    public bool ImportItem(string text)
    {
        try
        {
            var item = ItemParser.Parse(text, _data);
            if (item.Base == null) { LastError = $"Import failed: unknown base item \"{item.BaseName}\"."; return false; }
            SetItem(item, "Imported");
            var unresolved = item.Mods.Count(m => m.IsAffix && m.Def == null && !m.Unrevealed);
            if (unresolved > 0) LastError = $"Imported, but {unresolved} modifier(s) could not be matched to game data and are shown as text only.";
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"Import failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>Replace the current item (import, compose) and start a new history.</summary>
    public void SetItem(Item item, string action)
    {
        History.Clear();
        EditItem(item, action);
    }

    /// <summary>Replace the current item by an edited version as a new history step (undo returns to the previous version).</summary>
    public void EditItem(Item item, string action = "Edited")
    {
        item.Bind(_data);
        Commit(item, action, $"{action} {item}");
    }

    public StepPreview Preview(CraftAction action, int? forcedRemovalIndex = null) =>
        CurrentItem != null ? _engine.Preview(CurrentItem, action, forcedRemovalIndex) : new StepPreview { Applicability = Applicability.No("No item loaded.") };

    public void Execute(CraftAction action, ManualChoice? choice = null) =>
        Apply(action.DisplayName, item => _engine.Execute(item, action, Rng, choice));

    public void Undo()
    {
        if (History.Count <= 1) return;
        History.RemoveAt(History.Count - 1);
        Restore(History[^1].Item);
    }

    public void ResetRng(int? seed = null) => Rng = new Rng(seed);

    /// <summary>All simulated currencies, essences, alloys and catalysts with their applicability to the current item.</summary>
    public List<(CurrencyDef Currency, Applicability App)> ApplicableCurrencies() =>
        CurrentItem == null ? new() : _data.AllCurrencies.Where(c => c.Op != null)
            .Select(c => (c, _engine.Check(CurrentItem, new CraftAction { Currency = c })))
            .ToList();

    /// <summary>Why the currency can't be used with these omens active together on the current item, or null.</summary>
    public string? CombinationProblem(CurrencyDef currency, IReadOnlyList<OmenDef> omens) =>
        CurrentItem == null ? "No item loaded." : _engine.Check(CurrentItem, new CraftAction { Currency = currency, Omens = omens }) is { Ok: false } app ? app.Reason : null;

    /// <summary>Crafting omens that can modify the given currency on the current item (each on its own).</summary>
    public List<OmenDef> OmensFor(CurrencyDef currency) =>
        CurrentItem == null ? new() : _data.Omens.Where(o => o.Crafting && o.TargetCurrency != null &&
            _engine.Check(CurrentItem, CraftAction.Of(currency, o)).Ok).ToList();

    // ---- Well of Souls ----

    private sealed class RevealState
    {
        public List<ModDef> Options { get; set; } = new();
        public bool RerollUsed { get; set; }
    }

    public IReadOnlyList<ModDef>? RevealOptions(int modIndex) => _reveals.TryGetValue(modIndex, out var s) ? s.Options : null;

    /// <summary>Roll the options for an unrevealed mod (once; use <see cref="RerollRevealOptions"/> with Omen of Abyssal Echoes).</summary>
    public void RollRevealOptions(int modIndex)
    {
        if (CurrentItem == null || _reveals.ContainsKey(modIndex)) return;
        _reveals[modIndex] = new RevealState { Options = _engine.RollRevealOptions(CurrentItem, modIndex, Rng) };
    }

    public bool CanRerollReveal(int modIndex) => _reveals.TryGetValue(modIndex, out var s) && !s.RerollUsed;

    /// <summary>Omen of Abyssal Echoes: reroll the options once.</summary>
    public void RerollRevealOptions(int modIndex)
    {
        if (CurrentItem == null || !_reveals.TryGetValue(modIndex, out var state) || state.RerollUsed) return;
        state.Options = _engine.RollRevealOptions(CurrentItem, modIndex, Rng);
        state.RerollUsed = true;
    }

    public void Reveal(int modIndex, string modId) =>
        Apply("Well of Souls", item => _engine.Reveal(item, modIndex, modId, Rng));

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
        History.Clear();
        History.AddRange(project.History ?? new());
        foreach (var h in History) h.Item?.Bind(_data);
        ProjectName = project.Name;
        Rng = new Rng(project.RngSeed);
        if (project.Item != null) Restore(project.Item);
    }

    // ---- state changes ----

    /// <summary>Run an engine action on the current item and record it; engine refusals become LastError instead of exceptions.</summary>
    private void Apply(string actionName, Func<Item, CraftResult> run)
    {
        if (CurrentItem == null) { LastError = "No item loaded."; return; }
        CraftResult result;
        try { result = run(CurrentItem); }
        catch (InvalidOperationException ex) { LastError = ex.Message; return; }

        if (!result.Applied) { LastError = result.Summary; return; }
        if (result.Destroyed) { LastError = $"{result.Summary} (The simulator keeps the previous state so you can try again.)"; return; }
        Commit(result.Item, actionName, result.Summary, result.Details);
    }

    private void Commit(Item item, string action, string summary, List<string>? details = null)
    {
        CurrentItem = item;
        LastError = null;
        _reveals.Clear();
        History.Add(new HistoryEntry { Action = action, Item = item.Clone(), Summary = summary, Details = details ?? new() });
    }

    private void Restore(Item item)
    {
        CurrentItem = item.Clone();
        CurrentItem.Bind(_data);
        LastError = null;
        _reveals.Clear();
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
