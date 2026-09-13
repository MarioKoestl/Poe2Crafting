using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine;
using POE2Crafting.Core.Engine.Planning;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Drafting;

/// <summary>
/// A modifier picked in the mod browser (or taken from an item), with its category and optional values per <see cref="ModDef.StatRanges"/> entry:
/// the actual values in the Item Composer, minimum values in the Planner (null = middle of the range / any value).
/// </summary>
public sealed record SelectedMod(ModDef Mod, string Category)
{
    public List<double?> Values { get; init; } = Mod.StatRanges.Select(_ => (double?)null).ToList();
    public ModKind Kind => ModCategories.KindFor(Category);

    /// <summary>
    /// Allowed input range of a value: the roll range, or for planner targets of mods that catalyst quality enhances up to the value at the
    /// highest reachable catalyst quality (e.g. a fixed +3 skills can be targeted as +4).
    /// </summary>
    public (double Lo, double Hi) ValueBounds(int index, bool allowQuality, GameData data)
    {
        var (lo, hi) = ModText.Bounds(Mod.StatRanges[index]);
        return allowQuality && data.CatalystsEnhancing(Mod).Any() ? (lo, Math.Floor(hi * data.HighestCatalystFactor)) : (lo, hi);
    }
}

/// <summary>
/// The list of modifiers chosen for a composed or target item, enforcing the item rules shared by the Item Composer and the Planner:
/// slot limits per rarity, item level, and family exclusivity (never two mods of one family, whatever their category).
/// </summary>
public sealed class ModSelection
{
    private readonly SimAssumptions _rules;
    private readonly List<SelectedMod> _mods = new();

    public ModSelection(SimAssumptions rules) => _rules = rules;

    public IReadOnlyList<SelectedMod> Mods => _mods;
    /// <summary>Item class of the draft's base (slot limits differ per class, e.g. jewels).</summary>
    public string ItemClass { get; set; } = "";
    public int Count(AffixType type) => _mods.Count(m => m.Mod.AffixType == type);
    public int Max(Rarity rarity, AffixType type) => _rules.MaxAffixes(ItemClass, rarity, type, _mods.Select(m => m.Mod.Text));

    public bool Contains(ModDef mod) => _mods.Any(m => m.Mod.Id == mod.Id);
    public bool HasFamily(string? family) => family != null && _mods.Any(m => m.Mod.Family == family);

    /// <summary>A mod can be added if its level fits, and either it replaces another tier of an already chosen family or a slot is free.</summary>
    public bool CanAdd(ModDef mod, Rarity rarity, int itemLevel)
    {
        if (mod.Level > itemLevel) return false;
        if (HasFamily(mod.Family)) return _mods.Any(m => m.Mod.Family == mod.Family && m.Mod.AffixType == mod.AffixType);
        return Count(mod.AffixType) < Max(rarity, mod.AffixType);
    }

    /// <summary>Select the mod (replacing another tier of its family) or deselect it when already chosen.</summary>
    public void Toggle(ModDef mod, string category, Rarity rarity, int itemLevel)
    {
        var existing = _mods.FindIndex(m => m.Mod.Id == mod.Id);
        if (existing >= 0) { _mods.RemoveAt(existing); return; }
        if (!CanAdd(mod, rarity, itemLevel)) return;
        var sameFamily = _mods.FindIndex(m => m.Mod.Family == mod.Family);
        if (sameFamily >= 0) _mods[sameFamily] = new SelectedMod(mod, category);
        else _mods.Add(new SelectedMod(mod, category));
    }

    /// <summary>Take over a mod that is already on an item (no browser rules: the item may hold mods the browser does not offer).</summary>
    public void AddExisting(ItemMod mod, bool withValues)
    {
        if (mod.Def == null || Contains(mod.Def)) return;
        var selected = new SelectedMod(mod.Def, mod.Def.Category);
        if (withValues)
            for (int i = 0; i < selected.Values.Count && i < mod.Values.Count; i++) selected.Values[i] = mod.Values[i];
        _mods.Add(selected);
    }

    public void RemoveAt(int index)
    {
        if (index >= 0 && index < _mods.Count) _mods.RemoveAt(index);
    }

    public void Clear() => _mods.Clear();

    /// <summary>Drop mods that no longer fit after a rarity or item level change (latest picks go first).</summary>
    public void Enforce(Rarity rarity, int itemLevel)
    {
        _mods.RemoveAll(m => m.Mod.Level > itemLevel);
        foreach (var type in AffixTypeExtensions.Both)
            while (Count(type) > Max(rarity, type))
                _mods.Remove(_mods.Last(m => m.Mod.AffixType == type));
    }

    /// <summary>
    /// Build an item from a base and the selection; unset values (and the implicit) use the middle of their ranges.
    /// With a <paramref name="template"/> of the same base (an edited item), everything but its affixes is kept (implicits, quality, sockets,
    /// runes, corruption, unrevealed mods) and re-added mods keep their fractured flag.
    /// </summary>
    public Item BuildItem(BaseItem baseItem, Rarity rarity, int itemLevel, Item? template = null)
    {
        var edit = ReferenceEquals(template?.Base, baseItem);
        var item = edit ? template!.WithoutAffixes() : Item.FromBase(baseItem, rarity, itemLevel, withImplicit: true);
        item.Rarity = rarity;
        item.ItemLevel = itemLevel;
        foreach (var sm in _mods)
        {
            var mod = item.AddMod(sm.Mod, sm.Kind, sm.Values.Take(sm.Mod.Ranges.Count).Select((v, i) => v ?? ModText.MidValue(sm.Mod.Ranges[i])).ToList());
            mod.Fractured = edit && template!.Affixes.Any(a => a.ModId == sm.Mod.Id && a.Fractured);
        }
        return item;
    }

    /// <summary>Target specs for the planner; set values become minimum values.</summary>
    public List<TargetMod> ToTargetMods(ModPool pool, Item virtualItem, bool allowBetterTiers) => _mods.Select(m => new TargetMod
    {
        Family = m.Mod.Family ?? m.Mod.Name,
        Tier = m.Mod.Tier,
        AffixType = m.Mod.AffixType,
        ResolvedMod = m.Mod,
        Category = m.Category,
        DisplayTier = pool.DisplayTier(m.Mod, virtualItem),
        AllowBetterTiers = allowBetterTiers,
        MinValues = m.Values.Any(v => v != null) ? m.Values.ToList() : null,
    }).ToList();
}
