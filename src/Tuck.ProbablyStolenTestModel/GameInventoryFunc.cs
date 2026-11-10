// Ported from dependencies/probably-stolen/decompiled/GameInventoryFunc.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Only callbacks with supported model types are exposed. Parent shape modifiers are rejected when used.
#nullable disable
public abstract class GameInventoryFunc : TagSystem
{
    public Func<GameItem, GameInventory, bool> mayInventoryAddItemFunc { get; set; }

    public Func<GameItem, GameInventory, bool> mayInventoryRemoveItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, int> maxSlotAddItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, int> maxSlotRemoveItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, int> maxSlotAmountItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, int> minSlotAmountItemFunc { get; set; }

    public Action<GameItem, GameInventory, SlotMarker> onSlotAddItemFunc { get; set; }

    public Action<GameItem, GameInventory, SlotMarker> onSlotAddItemEndFunc { get; set; }

    public Action<GameItem, GameInventory, SlotMarker> onSlotAddItemLateFunc { get; set; }

    public Action<GameItem, GameInventory, SlotMarker> onSlotRemoveItemFunc { get; set; }

    public Action<GameItem, GameInventory, SlotMarker> onSlotRemoveItemEndFunc { get; set; }

    public Action<GameItem, GameInventory, SlotMarker> onSlotRemoveItemLateFunc { get; set; }

    public Action<GameItem, GameInventory, SlotMarker> onSlotChangeAmountItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, int> slotPriorityFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, GameItem, bool> slotSearchPassthroughForItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, GridShape, TagSystem, GameItem, GridShape> parentItemShapeModifierForItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, GridShape, TagSystem, bool> isItemStateValidFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, bool> maySelectSlotItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, bool> canSelectSlotItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, GameItem, bool> maySlotItemTargetItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, GameItem, bool> canSlotItemTargetItemFunc { get; set; }

    public Action<GameItem, GameInventory, SlotMarker, GameItem> onSlotItemTargetItemFunc { get; set; }

    public Func<GameItem, GameItem, GameInventory, SlotMarker, bool> mayItemTargetSlotItemFunc { get; set; }

    public Func<GameItem, GameItem, GameInventory, SlotMarker, bool> canItemTargetSlotItemFunc { get; set; }

    public Action<GameItem, GameItem, GameInventory, SlotMarker> onItemTargetSlotItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, bool> mayActivateSlotItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, bool> canActivateSlotItemFunc { get; set; }

    public Action<GameItem, GameInventory, SlotMarker> onActivateSlotItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, bool> mayToggleSlotItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, bool> canToggleSlotItemFunc { get; set; }

    public Action<GameItem, GameInventory, SlotMarker> onToggleSlotItemFunc { get; set; }
}
