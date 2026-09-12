using System.Text.Json;
using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Web.Services;

/// <summary>Per-user scoped crafting session: holds the current item, crafting history, RNG seed.</summary>
public sealed class CraftingSession
{
    private readonly GameData _data;
    private readonly ModPool _pool;
    private readonly CraftingEngine _engine;

    public Item? CurrentItem { get; private set; }
    public List<HistoryEntry> History { get; } = new();
    public Rng Rng { get; private set; } = new();
    public string? ProjectName { get; set; }
    public string? LastError { get; private set; }

    public CraftingSession(GameData data, ModPool pool)
    {
        _data = data;
        _pool = pool;
        _engine = new CraftingEngine(data, pool);
    }

    public CraftingEngine Engine => _engine;
    public GameData Data => _data;

    public void ImportItem(string text)
    {
        try
        {
            CurrentItem = ItemParser.Parse(text, _data);
            LastError = null;
            History.Clear();
            History.Add(new HistoryEntry { Action = "Imported", Item = CurrentItem.Clone(), Summary = $"Imported {CurrentItem.BaseName}" });
        }
        catch (Exception ex) { LastError = $"Import failed: {ex.Message}"; }
    }

    public void CreateItem(string baseName, int itemLevel)
    {
        var b = _data.FindBase(baseName);
        if (b == null) { LastError = $"Base '{baseName}' not found."; return; }
        CurrentItem = new Item
        {
            BaseName = b.Name, ItemClass = b.ItemClass, Rarity = Rarity.Normal, ItemLevel = itemLevel,
            Base = b, Sockets = b.SocketLimit ?? 0,
        };
        LastError = null;
        History.Clear();
        History.Add(new HistoryEntry { Action = "Created", Item = CurrentItem.Clone(), Summary = $"Created {b.Name} (ilvl {itemLevel})" });
    }

    /// <summary>Create an item with specific rarity and modifiers (for the Item Composer).</summary>
    public void ComposeItem(string baseName, int itemLevel, Rarity rarity, List<ItemMod> mods)
    {
        var b = _data.FindBase(baseName);
        if (b == null) { LastError = $"Base '{baseName}' not found."; return; }
        CurrentItem = new Item
        {
            BaseName = b.Name, ItemClass = b.ItemClass, Rarity = rarity, ItemLevel = itemLevel,
            Base = b, Sockets = b.SocketLimit ?? 0,
            Mods = mods.Select(m => m.Clone()).ToList(),
        };
        // Bind mod defs
        foreach (var m in CurrentItem.Mods) m.Def ??= _data.FindMod(m.ModId);
        // Add implicit if base has one
        if (!string.IsNullOrEmpty(b.Implicit))
        {
            var ranges = ModText.ParseRanges(b.Implicit);
            var midValues = ranges.Select(r => Math.Round((r[0] + r[1]) / 2)).ToList();
            CurrentItem.Mods.Insert(0, new ItemMod
            {
                ModId = "base_implicit",
                Kind = ModKind.Implicit,
                RawText = ModText.Render(b.Implicit, midValues),
            });
        }
        LastError = null;
        History.Clear();
        var modSummary = $"{CurrentItem.PrefixCount}P/{CurrentItem.SuffixCount}S";
        History.Add(new HistoryEntry { Action = "Composed", Item = CurrentItem.Clone(), Summary = $"Composed {b.Name} ({rarity}, ilvl {itemLevel}, {modSummary})" });
    }

    public Applicability Check(CraftAction action) => CurrentItem != null ? _engine.Check(CurrentItem, action) : Applicability.No("No item loaded.");
    public StepPreview Preview(CraftAction action) => CurrentItem != null ? _engine.Preview(CurrentItem, action) : new StepPreview { Applicability = Applicability.No("No item loaded.") };

    public CraftResult? Execute(CraftAction action, ManualChoice? choice = null)
    {
        if (CurrentItem == null) { LastError = "No item loaded."; return null; }
        var result = _engine.Execute(CurrentItem, action, Rng, choice);
        if (result.Applied)
        {
            CurrentItem = result.Item;
            History.Add(new HistoryEntry { Action = action.DisplayName, Item = CurrentItem.Clone(), Summary = result.Summary, Details = result.Details });
        }
        LastError = result.Applied ? null : result.Summary;
        return result;
    }

    public void Undo()
    {
        if (History.Count <= 1) return;
        History.RemoveAt(History.Count - 1);
        CurrentItem = History[^1].Item.Clone();
        CurrentItem.Bind(_data);
    }

    public void ResetRng(int? seed = null) => Rng = new Rng(seed);

    /// <summary>Get all currencies applicable to the current item.</summary>
    public List<(CurrencyDef Currency, Applicability App)> ApplicableCurrencies()
    {
        if (CurrentItem == null) return new();
        return _data.Currencies.Where(c => c.Op != null)
            .Select(c => (c, _engine.Check(CurrentItem, new CraftAction { Currency = c })))
            .OrderByDescending(x => x.Item2.Ok).ThenBy(x => x.c.Name)
            .ToList();
    }

    /// <summary>Get omens that work with a given currency.</summary>
    public List<OmenDef> OmensFor(CurrencyDef currency)
    {
        return _data.Omens.Where(o => o.Crafting && o.TargetCurrency != null &&
            _engine.Check(CurrentItem!, new CraftAction { Currency = currency, Omen = o }).Ok).ToList();
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
