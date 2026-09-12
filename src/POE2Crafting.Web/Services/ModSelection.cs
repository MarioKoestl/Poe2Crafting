using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Web.Services;

/// <summary>A modifier picked in the mod browser, with the category it was picked from (normal, breach_otherworldly, desecrated).</summary>
public sealed record SelectedMod(ModDef Mod, string Category)
{
    public ModKind Kind => Category == "desecrated" ? ModKind.Desecrated : ModKind.Explicit;
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
    public int Count(AffixType type) => _mods.Count(m => m.Mod.AffixType == type);
    public int Max(Rarity rarity, AffixType type) => _rules.MaxAffixes(rarity, type);

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

    public void RemoveAt(int index)
    {
        if (index >= 0 && index < _mods.Count) _mods.RemoveAt(index);
    }

    public void Clear() => _mods.Clear();

    /// <summary>Drop mods that no longer fit after a rarity or item level change (latest picks go first).</summary>
    public void Enforce(Rarity rarity, int itemLevel)
    {
        _mods.RemoveAll(m => m.Mod.Level > itemLevel);
        foreach (var type in new[] { AffixType.Prefix, AffixType.Suffix })
            while (Count(type) > Max(rarity, type))
                _mods.Remove(_mods.Last(m => m.Mod.AffixType == type));
    }

    /// <summary>Build an item from a base and the selection; implicit and mod values are set to the middle of their ranges.</summary>
    public Item BuildItem(BaseItem baseItem, Rarity rarity, int itemLevel)
    {
        var item = new Item
        {
            BaseName = baseItem.Name,
            ItemClass = baseItem.ItemClass,
            Rarity = rarity,
            ItemLevel = itemLevel,
            Base = baseItem,
            Sockets = baseItem.SocketLimit ?? 0,
        };
        if (!string.IsNullOrEmpty(baseItem.Implicit))
        {
            var mids = ModText.ParseRanges(baseItem.Implicit).Select(Mid).ToList();
            item.Mods.Add(new ItemMod { ModId = "base_implicit", Kind = ModKind.Implicit, RawText = ModText.Render(baseItem.Implicit, mids) });
        }
        foreach (var sm in _mods)
        {
            item.Mods.Add(new ItemMod
            {
                ModId = sm.Mod.Id,
                Def = sm.Mod,
                Kind = sm.Kind,
                Affix = sm.Mod.AffixType,
                Values = sm.Mod.Ranges.Select(Mid).ToList(),
            });
        }
        return item;
    }

    private static double Mid(double[] range)
    {
        double mid = (range[0] + range[1]) / 2;
        bool integer = range[0] == Math.Floor(range[0]) && range[1] == Math.Floor(range[1]);
        return integer ? Math.Round(mid, MidpointRounding.AwayFromZero) : Math.Round(mid, 2);
    }

    /// <summary>Target specs for the planner.</summary>
    public List<TargetMod> ToTargetMods(ModPool pool, Item virtualItem, bool allowBetterTiers) => _mods.Select(m => new TargetMod
    {
        Family = m.Mod.Family ?? m.Mod.Name,
        Tier = m.Mod.Tier,
        AffixType = m.Mod.AffixType,
        ResolvedMod = m.Mod,
        Category = m.Category,
        DisplayTier = pool.DisplayTier(m.Mod, virtualItem),
        AllowBetterTiers = allowBetterTiers,
    }).ToList();
}
