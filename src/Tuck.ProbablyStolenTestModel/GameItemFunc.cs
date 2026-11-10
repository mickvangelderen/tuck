// Ported from dependencies/probably-stolen/decompiled/GameItemFunc.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Only callbacks with supported model types are exposed. Parent shape modifiers are rejected when used.
#nullable disable
public abstract class GameItemFunc
{
    public Func<GameItem, GameInventory, bool> mayInventoryAddItemFunc { get; set; }

    public Func<GameItem, GameInventory, bool> mayInventoryRemoveItemFunc { get; set; }

    public Func<GameItem, GameItem, bool> mayStackItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, int> maxSlotAddItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, int> maxSlotRemoveItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, int> maxSlotAmountItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, int> minSlotAmountItemFunc { get; set; }

    public Action<GameItem, GameInventory, SlotMarker> onSlotAddItemFunc { get; set; }

    public Action<GameItem, GameInventory, SlotMarker> onSlotRemoveItemFunc { get; set; }

    public Action<GameItem, GameInventory, SlotMarker> onSlotChangeAmountItemFunc { get; set; }

    public Action<GameItem> onChangeAmountItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, int> slotPriorityFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, GameItem, bool> slotSearchPassthroughForItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, GridShape, TagSystem, GameItem, GridShape> parentItemShapeModifierForItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, GridShape, TagSystem, bool> isItemStateValidFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, bool> maySelectSlotItemFunc { get; set; }

    public Func<GameItem, GameInventory, SlotMarker, bool> canSelectSlotItemFunc { get; set; }

    public Func<GameItem, GameItem, bool> mayItemTargetItemFunc { get; set; }

    public Func<GameItem, GameItem, bool> canItemTargetItemFunc { get; set; }

    public Action<GameItem, GameItem> onItemTargetItemFunc { get; set; }

    public Func<GameItem, GameItem, bool> mayThisTargetItemFunc { get; set; }

    public Func<GameItem, GameItem, bool> canThisTargetItemFunc { get; set; }

    public Action<GameItem, GameItem> onThisTargetItemFunc { get; set; }

    public Func<GameItem, GameItem, bool> mayItemTargetThisFunc { get; set; }

    public Func<GameItem, GameItem, bool> canItemTargetThisFunc { get; set; }

    public Action<GameItem, GameItem> onItemTargetThisFunc { get; set; }

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

    public Action<GameItem, GameInventory, SlotMarker> onConfigureSlotItemFunc { get; set; }

    public Action<GameItem, GameInventory, SlotMarker> onCycleEndEarlySlotItemFunc { get; set; }

    public Action<GameItem, GameInventory, SlotMarker> onCycleEndSlotItemFunc { get; set; }

    public Action<GameItem, GameInventory, SlotMarker> onCycleEndLateSlotItemFunc { get; set; }

    public Action<GameItem, GameInventory, SlotMarker> onTurnEndSlotItemFunc { get; set; }

    public Action<GameItem, GameInventory, SlotMarker> onOneSecondItemFunc { get; set; }

    public Action<GameItem> onLoaded { get; set; }

    public Action<GameItem> onUnlockedFunc { get; set; }

    public Action<GameItem, GameItem> onUnloadChildItemFunc { get; set; }
}
