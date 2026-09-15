using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine;
using POE2Crafting.Core.Engine.Planning;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Web.Services;

/// <summary>
/// Per-user scoped crafting session: the open project with its items (each with its own history), the current item, RNG and pending
/// Well of Souls reveals. Every change of the project is saved automatically; a failed save is reported in <see cref="LastError"/>.
/// </summary>
public sealed class CraftingSession
{
    private readonly GameData _data;
    private readonly ProjectStore _store;
    private readonly GuideCatalog _guides;
    private readonly Rng _rng = new();
    /// <summary>Options rolled at the Well of Souls per unrevealed mod index (cleared whenever the item changes).</summary>
    private readonly Dictionary<int, RevealState> _reveals = new();
    private bool _projectLoaded;

    public CraftingSession(GameData data, CraftingEngine engine, ProjectStore store, GuideCatalog guides)
    {
        _data = data;
        Engine = engine;
        _store = store;
        _guides = guides;
    }

    public CraftingEngine Engine { get; }

    public CraftingProject? Project { get { EnsureProject(); return _project; } }
    public ProjectItem? ProjectItem { get { EnsureProject(); return _projectItem; } }
    public Item? CurrentItem { get { EnsureProject(); return _currentItem; } }
    /// <summary>History of the current item (empty without an item).</summary>
    public IReadOnlyList<HistoryEntry> History => ProjectItem?.History ?? (IReadOnlyList<HistoryEntry>)Array.Empty<HistoryEntry>();
    public string? LastError { get; private set; }

    private CraftingProject? _project;
    private ProjectItem? _projectItem;
    private Item? _currentItem;

    /// <summary>The most recently changed project is opened on first use (not in the constructor: no disk access while the page prerenders).</summary>
    private void EnsureProject()
    {
        if (_projectLoaded) return;
        _projectLoaded = true;
        if (_store.List().FirstOrDefault() is { } recent) OpenProject(recent.Id);
    }

    // ---- projects ----

    public IReadOnlyList<ProjectSummary> Projects() => _store.List();

    public void CreateProject(string name)
    {
        EnsureProject();
        _project = new CraftingProject { Name = string.IsNullOrWhiteSpace(name) ? $"Project {DateTime.Now:yyyy-MM-dd HH:mm}" : name.Trim() };
        SelectItem(null);
        Save();
    }

    public void OpenProject(string id)
    {
        _projectLoaded = true;
        if (_store.Load(id) is not { } project) { LastError = "The project could not be loaded."; return; }
        _project = project;
        SelectItem(project.Items.FirstOrDefault(i => i.Id == project.SelectedItemId) ?? project.Items.FirstOrDefault());
    }

    public void RenameProject(string name)
    {
        if (Project == null || string.IsNullOrWhiteSpace(name)) return;
        Project.Name = name.Trim();
        Save();
    }

    public void DeleteProject()
    {
        if (Project is not { } project) return;
        _store.Delete(project.Id);
        _project = null;
        SelectItem(null);
        if (_store.List().FirstOrDefault() is { } next) OpenProject(next.Id);
    }

    /// <summary>The latest state of an item of the open project, or null when the project has no such item.</summary>
    public Item? FindProjectItem(string? itemId) => itemId == null ? null : Project?.Items.FirstOrDefault(i => i.Id == itemId)?.Current;

    /// <summary>Make an item of the project the current item (nothing happens when it already is).</summary>
    public void OpenItem(string itemId)
    {
        if (Project?.Items.FirstOrDefault(i => i.Id == itemId) is not { } item || ReferenceEquals(item, ProjectItem)) return;
        SelectItem(item);
        Save(touch: false);
    }

    public void RemoveItem(string itemId)
    {
        if (Project?.Items.FirstOrDefault(i => i.Id == itemId) is not { } item) return;
        Project.Items.Remove(item);
        if (ReferenceEquals(item, _projectItem)) SelectItem(Project.Items.LastOrDefault());
        Save();
    }

    private void SelectItem(ProjectItem? item)
    {
        _projectItem = item;
        if (_project != null) _project.SelectedItemId = item?.Id;
        if (item?.Current is { } current) Restore(current);
        else { _currentItem = null; _reveals.Clear(); }
    }

    private void Save(bool touch = true)
    {
        if (_project == null) return;
        try
        {
            _store.Save(_project, touch);
        }
        catch (IOException ex)
        {
            LastError = $"Saving the project failed: {ex.Message}";
        }
    }

    // ---- items ----

    /// <summary>Parse an item text and add it to the project; returns false when nothing was imported (LastError says why, or warns about unmatched mods).</summary>
    public bool ImportItem(string text)
    {
        Item item;
        try
        {
            item = ItemParser.Parse(text, _data);
        }
        catch (FormatException ex)
        {
            LastError = $"Import failed: {ex.Message}";
            return false;
        }
        if (item.Base == null) { LastError = $"Import failed: unknown base item \"{item.BaseName}\"."; return false; }
        SetItem(item, "Imported");
        var unresolved = item.Mods.Count(m => m.IsAffix && m.Def == null && !m.Unrevealed);
        if (unresolved > 0) LastError = $"Imported, but {unresolved} modifier(s) could not be matched to game data and are shown as text only.";
        return true;
    }

    /// <summary>Add a new item (import, compose, guide) to the project — a new project is created when none is open — and make it the current item.</summary>
    public void SetItem(Item item, string action)
    {
        if (Project == null) CreateProject("");
        var projectItem = new ProjectItem();
        _project!.Items.Add(projectItem);
        SelectItem(projectItem);
        EditItem(item, action);
    }

    /// <summary>Replace the current item by an edited version as a new history step (undo returns to the previous version).</summary>
    public void EditItem(Item item, string action = "Edited")
    {
        item.Bind(_data);
        Commit(item, action, $"{action} {item}");
    }

    public StepPreview Preview(CraftAction action, int? forcedRemovalIndex = null) =>
        CurrentItem != null ? Engine.Preview(CurrentItem, action, forcedRemovalIndex) : new StepPreview { Applicability = Applicability.No("No item loaded.") };

    /// <summary>Apply an action; on a foreseeing item (Hinekora's Lock) a random roll gives exactly the foreseen result.</summary>
    public void Execute(CraftAction action, ManualChoice? choice = null) =>
        Apply(action.DisplayName, item => Engine.Execute(item, action, item.Foreseeing && choice == null ? CraftingEngine.ForeseeRng(item, action) : _rng, choice));

    /// <summary>Hinekora's Lock: the result the action will have on the current item, or null when the item doesn't foresee or the action can't be used.</summary>
    public CraftResult? Foresee(CraftAction action) =>
        CurrentItem is { Foreseeing: true } item && Engine.Check(item, action).Ok ? Engine.Foresee(item, action) : null;

    /// <summary>Instill a notable on the current amulet.</summary>
    public void Instill(InstillRecipe recipe) => Apply(recipe.ActionName, item => Engine.Instill(item, recipe));

    public Applicability CheckInstill(InstillRecipe recipe) =>
        CurrentItem != null ? Engine.CheckInstill(CurrentItem, recipe) : Applicability.No("No item loaded.");

    /// <summary>Whether the current item's history has crafting steps that can be saved as a guide.</summary>
    public bool CanSaveAsGuide => History.Count > 1;

    /// <summary>Save the current item's history (starting item and every step) as a guide; returns its id, or null when saving failed (LastError).</summary>
    public string? SaveAsGuide(string name, string notes)
    {
        if (!CanSaveAsGuide) return null;
        var start = History[0].Item;
        var guide = new RecordedGuide
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"{start.BaseName} → {CurrentItem!.Title}" : name.Trim(),
            Notes = notes.Trim(),
            History = History.Select(e => new HistoryEntry { Action = e.Action, Summary = e.Summary, Item = e.Item.Clone() }).ToList(),
        };
        foreach (var entry in guide.History) entry.Item.Bind(_data);
        try
        {
            _guides.Save(guide);
            LastError = null;
            return guide.Id;
        }
        catch (IOException ex)
        {
            LastError = $"Saving the guide failed: {ex.Message}";
            return null;
        }
    }

    public void Undo()
    {
        if (ProjectItem is not { History.Count: > 1 } item) return;
        item.History.RemoveAt(item.History.Count - 1);
        Restore(item.History[^1].Item);
        Save();
    }

    /// <summary>All simulated currencies, essences, alloys and catalysts with their applicability to the current item.</summary>
    public List<(CurrencyDef Currency, Applicability App)> ApplicableCurrencies() =>
        CurrentItem == null ? new() : _data.AllCurrencies.Where(c => c.Op != null)
            .Select(c => (c, Engine.Check(CurrentItem, new CraftAction { Currency = c })))
            .ToList();

    /// <summary>Why the currency can't be used with these omens active together on the current item, or null.</summary>
    public string? CombinationProblem(CurrencyDef currency, IReadOnlyList<OmenDef> omens) =>
        CurrentItem == null ? "No item loaded." : Engine.Check(CurrentItem, new CraftAction { Currency = currency, Omens = omens }) is { Ok: false } app ? app.Reason : null;

    /// <summary>Crafting omens that can modify the given currency on the current item (each on its own).</summary>
    public List<OmenDef> OmensFor(CurrencyDef currency) =>
        CurrentItem == null ? new() : _data.Omens.Where(o => o.Crafting && o.TargetCurrency != null &&
            Engine.Check(CurrentItem, CraftAction.Of(currency, o)).Ok).ToList();

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
        _reveals[modIndex] = new RevealState { Options = Engine.RollRevealOptions(CurrentItem, modIndex, _rng) };
    }

    public bool CanRerollReveal(int modIndex) => _reveals.TryGetValue(modIndex, out var s) && !s.RerollUsed;

    /// <summary>Omen of Abyssal Echoes: reroll the options once.</summary>
    public void RerollRevealOptions(int modIndex)
    {
        if (CurrentItem == null || !_reveals.TryGetValue(modIndex, out var state) || state.RerollUsed) return;
        state.Options = Engine.RollRevealOptions(CurrentItem, modIndex, _rng);
        state.RerollUsed = true;
    }

    // ---- state changes ----

    /// <summary>Run an engine action on the current item and record it; an impossible manual choice becomes LastError instead of an exception.</summary>
    private void Apply(string actionName, Func<Item, CraftResult> run)
    {
        if (CurrentItem == null) { LastError = "No item loaded."; return; }
        CraftResult result;
        try { result = run(CurrentItem); }
        catch (InvalidChoiceException ex) { LastError = ex.Message; return; }

        if (!result.Applied) { LastError = result.Summary; return; }
        if (result.Destroyed) { LastError = $"{result.Summary} (The simulator keeps the previous state so you can try again.)"; return; }
        Commit(result.Item, actionName, result.Summary);
    }

    private void Commit(Item item, string action, string summary)
    {
        _currentItem = item;
        LastError = null;
        _reveals.Clear();
        ProjectItem!.History.Add(new HistoryEntry { Action = action, Item = item.Clone(), Summary = summary });
        Save();
    }

    private void Restore(Item item)
    {
        _currentItem = item.Clone();
        _currentItem.Bind(_data);
        LastError = null;
        _reveals.Clear();
    }
}
