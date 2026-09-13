using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Web.Services;

/// <summary>
/// Editable item definition behind the Item Composer and the Planner target: base, rarity, item level and chosen mods,
/// with a live preview item. Changing base, rarity or level re-applies the selection rules.
/// </summary>
public sealed class ItemDraft
{
    private readonly GameData _data;
    private string _baseName = "";
    private int _itemLevel = 82;
    private Rarity _rarity = Rarity.Rare;
    /// <summary>The loaded item: its non-affix parts (implicits, quality, sockets, corruption, ...) are kept until the base changes.</summary>
    private Item? _template;

    public ItemDraft(GameData data)
    {
        _data = data;
        Selection = new ModSelection(data.Config.Assumptions);
    }

    public ModSelection Selection { get; }
    public string ItemClass { get; set; } = "";
    public BaseItem? Base { get; private set; }
    public Item? Preview { get; private set; }

    public string BaseName
    {
        get => _baseName;
        set
        {
            _baseName = value;
            Base = string.IsNullOrEmpty(value) ? null : _data.FindBase(value);
            _template = null;
            Selection.Clear();
            Refresh();
        }
    }

    public int ItemLevel
    {
        get => _itemLevel;
        set { _itemLevel = Math.Clamp(value, 1, 100); Refresh(); }
    }

    public Rarity Rarity
    {
        get => _rarity;
        set { _rarity = value; Refresh(); }
    }

    /// <summary>
    /// Take an existing item: base, rarity, item level and all known affixes (values only when <paramref name="copyValues"/>);
    /// everything else of the item is kept when the draft is built.
    /// </summary>
    public void LoadFrom(Item item, bool copyValues)
    {
        if (item.Base == null) return;
        ItemClass = item.Base.ItemClass;
        BaseName = item.Base.Name;
        _template = item.Clone();
        _itemLevel = item.ItemLevel;
        _rarity = item.Rarity;
        foreach (var mod in item.Affixes) Selection.AddExisting(mod, copyValues);
        Preview = BuildItem();
    }

    /// <summary>Re-apply selection rules and rebuild the preview (call after the selection changed).</summary>
    public void Refresh()
    {
        Selection.Enforce(_rarity, _itemLevel);
        Preview = BuildItem();
    }

    /// <summary>The draft as an item with its chosen mods (built on the loaded item, if any).</summary>
    public Item? BuildItem() => Base != null ? Selection.BuildItem(Base, _rarity, _itemLevel, _template) : null;
}
