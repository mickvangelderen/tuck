using System;
using System.Collections.Generic;
using System.Linq;

namespace Tuck.PackSolver;

public sealed class UnsupportedRuleException : NotSupportedException
{
    public UnsupportedRuleException(string message) : base(message) { }
}

public sealed class EffectiveItem
{
    public ShapeState Shape { get; }
    public int OriginX { get; }
    public int OriginY { get; }
    public ItemFacts State { get; }
    public long UnitValue { get; }
    internal EffectiveItem(ShapeState shape, int originX, int originY, ItemFacts state, long unitValue)
    { Shape = shape; OriginX = originX; OriginY = originY; State = state; UnitValue = unitValue; }
    public PlacedShape Place(ShapeState baseShape, Placement placement, InventoryLayout layout)
    {
        if (placement is Placement.Slot && layout is InventoryLayout.Slot slot)
            return new(Shape, new(orientation: slot.Orientation));
        if (placement is not Placement.Grid grid || layout is not InventoryLayout.Grid)
            throw new ArgumentException("Placement does not match inventory kind.");
        var offset = BaseOffset(baseShape, grid.Orientation, grid.Flipped);
        return new(Shape, new(grid.X - offset.X, grid.Y - offset.Y, grid.Orientation, grid.Flipped));
    }
    public Placement.Grid FromEffective(ShapeState baseShape, Placement.Grid transform)
    {
        var offset = BaseOffset(baseShape, transform.Orientation, transform.Flipped);
        return new(transform.X + offset.X, transform.Y + offset.Y, transform.Orientation, transform.Flipped);
    }
    private (int X, int Y) BaseOffset(ShapeState baseShape, int orientation, bool flipped)
    {
        var shape = new PlacedShape(Shape, new(orientation: orientation, flipped: flipped));
        var a = shape.Forward(OriginX, OriginY);
        var b = shape.Forward(OriginX + baseShape.Width - 1, OriginY + baseShape.Height - 1);
        return (Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));
    }
}

/// <summary>A disposable view of one immutable state. Derived caches never become recorded facts.</summary>
public sealed class StateView
{
    private readonly PackState _state;
    private readonly Dictionary<string, ItemState> _items;
    private readonly Dictionary<string, InventoryState> _inventories;
    private readonly ILookup<string, InventoryState> _owned;
    private readonly ILookup<string, ItemState> _contents;
    private readonly Dictionary<string, EffectiveItem> _effective = new();
    private readonly Dictionary<string, PlacedShape> _placed = new();
    public StateView(PackState state) : this(state, state.Items.ToDictionary(i => i.Id), state.Inventories.ToDictionary(i => i.Id)) { }
    internal StateView(PackState state, Dictionary<string, ItemState> items, Dictionary<string, InventoryState> inventories)
    {
        _state = state; _items = items; _inventories = inventories;
        _owned = state.Inventories.Where(i => i.OwnerItemId != null).ToLookup(i => i.OwnerItemId!);
        _contents = items.Values.ToLookup(i => i.Location.InventoryId);
    }
    // Simulation dictionaries are copy-on-write. No view observes a subsequently mutated map.
    internal StateView(StateView previous, Dictionary<string, ItemState> items, bool fixedGeometry)
    {
        _state = previous._state; _items = items; _inventories = previous._inventories;
        _owned = previous._owned; _contents = items.Values.ToLookup(i => i.Location.InventoryId);
        if (fixedGeometry) _effective = previous._effective;
    }
    public IEnumerable<ItemState> Contents(string inventoryId) => _contents[inventoryId];
    public bool IsDescendant(string itemId, string ancestor)
    {
        for (string? id = itemId; id != null; id = _inventories[_items[id].Location.InventoryId].OwnerItemId)
            if (id == ancestor) return true;
        return false;
    }
    public bool IsReturned(string itemId)
    {
        var inventory = _inventories[_items[itemId].Location.InventoryId];
        while (inventory.OwnerItemId != null) inventory = _inventories[_items[inventory.OwnerItemId].Location.InventoryId];
        return _state.Roots.IsReturn(inventory.Id);
    }
    public bool IsEffectivelyOwned(string itemId)
    {
        for (string? id = itemId; id != null; id = _inventories[_items[id].Location.InventoryId].OwnerItemId)
            if (!Effective(id).State.IsTag("IS_OWNED_TAG")) return false;
        return true;
    }
    public long PackUnitValue(string id) => PackValue.UnitValue(_items[id].Identifier, Effective(id).UnitValue);
    public long ReturnedValue => _items.Values.Where(i => IsReturned(i.Id)).Sum(i => checked(PackUnitValue(i.Id) * i.UnitCount));
    public PlacedShape Placed(string id)
    {
        if (_placed.TryGetValue(id, out var placed)) return placed;
        var item = _items[id];
        placed = Effective(id).Place(item.Shape, item.Location.Placement, _inventories[item.Location.InventoryId].Layout);
        _placed.Add(id, placed);
        return placed;
    }
    public EffectiveItem Effective(string id)
    {
        if (_effective.TryGetValue(id, out var result)) return result;
        var item = _items[id];
        var additions = new List<PlacedShape>();
        foreach (var inventory in _owned[id])
            foreach (var child in _contents[inventory.Id])
            {
                var rule = _state.Definitions[child.Identifier].Rules;
                if (inventory.UnsupportedParentEffects || rule.UnsupportedParentState || rule.ParentShape == ParentShapeRule.Unsupported)
                    throw new UnsupportedRuleException($"Cannot derive {id}: unrepresented parent modifier from {child.Id}/{inventory.Id}.");
                if (rule.ParentShape == ParentShapeRule.GunAttachment)
                {
                    var contribution = GunAttachments.Contribution(child.Identifier, item.Identifier, Effective(child.Id).Shape);
                    if (contribution != null) additions.Add(contribution);
                }
            }
        var (shape, originX, originY) = Merge(item.Shape, additions);
        // No active vanilla factory installs relevant parent-state modifiers. Unknown ones fail above,
        // rather than substituting observations or assuming that arbitrary modifier order commutes.
        result = new(shape, originX, originY, item.State, Value(item.UnitValue, item.UnitBaseValue, item.State));
        _effective.Add(id, result);
        return result;
    }
    private static (ShapeState Shape, int X, int Y) Merge(ShapeState source, List<PlacedShape> additions)
    {
        if (additions.Count == 0) return (source, 0, 0);
        int left = Math.Min(0, additions.Min(s => s.Transform.X)), top = Math.Min(0, additions.Min(s => s.Transform.Y));
        int right = Math.Max(source.Width, additions.Max(s => s.Transform.X + s.GlobalWidth));
        int bottom = Math.Max(source.Height, additions.Max(s => s.Transform.Y + s.GlobalHeight));
        int width = right - left, height = bottom - top;
        var cells = new byte[checked(width * height)];
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++) cells[(y - top) * width + x - left] = source.GetLocal(x, y);
        foreach (var addition in additions)
            for (int y = addition.Transform.Y; y < addition.Transform.Y + addition.GlobalHeight; y++)
                for (int x = addition.Transform.X; x < addition.Transform.X + addition.GlobalWidth; x++)
                {
                    int index = (y - top) * width + x - left;
                    byte cell = addition.Get(x, y);
                    if (cell == 1 || cell > cells[index]) cells[index] = cell;
                }
        return (new(width, height, cells, source.OutsideBounds), -left, -top);
    }
    public static long Value(long unitValue, long unitBaseValue, ItemFacts state)
    {
        int extra = 0;
        if (state.IsTag("power_source_item"))
        {
            int energy = state.Int("power_source_item_energy");
            if (state.IsTag("POWER_SOURCE_ITEM_VALUE_PERCENT_INT")) energy = energy * state.Int("POWER_SOURCE_ITEM_VALUE_PERCENT_INT") / 100;
            extra += (int)(energy + unitBaseValue);
        }
        if (state.IsTag("amenities")) extra += state.Int("amenities") * state.Int("amenities_use") / 2;
        if (state.IsTag("armor_item")) extra += (state.Int("current_armor_value") + state.Int("current_max_armor_value")) * state.Int("armor_efficiency") / 100;
        if (state.IsTag("LIQUID_CONTAINER_TAG")) extra += state.Int("LIQUID_CONTAINER_CURRENT") / 100;
        if (state.IsTag("ORGAN_ITEM_TAG"))
        {
            double health = state.Int("ORGAN_HEALTH_INT"), max = state.Int("ORGAN_HEALTH_MAX_INT");
            extra += (int)(health / max * 2.5 * max);
        }
        return (long)((unitValue + extra) * (state.IsTag("not_purchased") ? 1.1 : 1.0));
    }
}

/// <summary>GunModDirectory's shape-only rules; combat/rendering modifiers are outside packing.</summary>
public static class GunAttachments
{
    public static bool Supports(string id) => id is "silencer" or "silencer2" or "silencer_makeshift" or "barrel"
        or "compensator" or "bipod" or "gl" or "grip" or "mag_10mm_ext" or "mag_10mm_smg"
        or "mag_10mm_smg_high" or "mag_10mm_smg_box" or "mag_22_ext" or "stock" or "short_stock";
    internal static PlacedShape? Contribution(string id, string parent, ShapeState shape)
    {
        int width = shape.Width, height = shape.Height;
        (int X, int Y)? offset = id switch
        {
            "silencer" or "silencer2" or "silencer_makeshift" or "compensator" => parent switch
            { "printed_gun" or "handmade_pistol" or "heavy_handmade_pistol" or "glock_receiver" => (4, 0), "smg" or "shotgun" => (7, 0), _ => null },
            "barrel" when parent == "smg" => (7, 0),
            "bipod" or "gl" when parent == "smg" => (3, 2),
            "grip" when parent == "smg" => (4, 1),
            "grip" when parent == "shotgun" => (3, 1),
            "mag_10mm_ext" when parent is "heavy_handmade_pistol" or "glock_receiver" => (0, 0),
            "mag_22_ext" when parent is "printed_gun" or "handmade_pistol" => (0, 0),
            "mag_10mm_smg" when parent == "smg" => (3, 1),
            "mag_10mm_smg_high" when parent == "smg" => (2, 2),
            "mag_10mm_smg_box" when parent == "smg" => (2, 1),
            "stock" when parent is "smg" or "shotgun" => (-2, 0),
            "short_stock" when parent is "smg" or "shotgun" => (-1, 0),
            _ => null
        };
        if (!Supports(id)) throw new UnsupportedRuleException("Unknown gun attachment: " + id);
        if (offset == null) return null;
        if (id == "grip") width++;
        if (id is "mag_10mm_ext" or "mag_22_ext" or "mag_10mm_smg_box" or "stock" or "short_stock") height++;
        return new(ShapeState.Rectangle(width, height), new(offset.Value.X, offset.Value.Y));
    }
}
