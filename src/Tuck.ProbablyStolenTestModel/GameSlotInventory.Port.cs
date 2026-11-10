// Ported from dependencies/probably-stolen/decompiled/GameSlotInventory.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Source: GameSlotInventory.cs. Rendering/store effects removed; finite screen coordinates rejected. Search order and callback order preserved.
#nullable disable
using UnityEngine;
public partial class GameSlotInventory
{

    public override bool CheckInventorySlotStillValid(GameItem item, GridShape tempShape, TagSystem tempState)
    {
        SlotMarker slotMarker = new SlotMarker();
        slotMarker.item = item;
        slotMarker.inventory = this;
        if (item == null)
        {
            return false;
        }
        if (tempShape == null)
        {
            tempShape = item.modifiedShape;
        }
        if (tempState == null)
        {
            tempState = item.modifiedState;
        }
        slotMarker.itemGridShape = tempShape;
        if (!MayHaveValidInventorySlot(item))
        {
            return false;
        }
        if (currentItem != null && currentItem != item)
        {
            return false;
        }
        slotMarker.itemGridShape = new GridShapeBuilder(tempShape).SetTransform(0, 0, flipped: false, defaultOrientation);
        if (!item.CheckIsStateValid(this, slotMarker, tempShape, tempState))
        {
            return false;
        }
        if (!CheckChangeParentShapeStateModifier(item, slotMarker, tempShape, tempState))
        {
            return false;
        }
        if (item.MaxNumAdd(slotMarker) < 1)
        {
            return false;
        }
        return true;
    }

    public override SlotMarker TryInventorySlot(GameItem item, int requestedNum, Vector2 itemGraphicsCenterPosition, GridShape tempShape, TagSystem tempState)
    {
        SlotMarker slotMarker = new SlotMarker();
        slotMarker.item = item;
        slotMarker.inventory = this;
        slotMarker.itemGridShape = tempShape;
        if (item == null)
        {
            return slotMarker;
        }
        if (tempShape == null)
        {
            tempShape = item.modifiedShape;
        }
        if (tempState == null)
        {
            tempState = item.modifiedState;
        }
        if (currentItem != null && currentItem != item)
        {
            return slotMarker;
        }
        if (!MayHaveValidInventorySlot(item))
        {
            return slotMarker;
        }
        if (float.IsFinite(itemGraphicsCenterPosition.x))
        {
            throw new NotSupportedException("Screen-space placement is not modeled; provide a grid shape.");
        }
        slotMarker.itemGridShape = new GridShapeBuilder(tempShape).SetTransform(0, 0, flipped: false, defaultOrientation);
        if (!item.CheckIsStateValid(this, slotMarker, tempShape, tempState))
        {
            return slotMarker;
        }
        if (!CheckAcceptParentShapeStateModifier(item, slotMarker, tempShape, tempState))
        {
            return slotMarker;
        }
        int val = item.MaxNumRemove();
        int val2 = item.MaxNumAdd(slotMarker);
        slotMarker.numTransfer = Math.Min(Math.Min(val, val2), requestedNum);
        if (slotMarker.numTransfer < 1)
        {
            return slotMarker;
        }
        slotMarker.priority = Math.Max(slotPriorityFunc?.Invoke(item, this, slotMarker) ?? 0, item.slotPriorityFunc?.Invoke(item, this, slotMarker) ?? 0);
        return slotMarker;
    }

    public override SlotMarker GetIncompleteItemInventorySlot(GameItem item)
    {
        return new SlotMarker
        {
            item = item,
            inventory = this,
            itemGridShape = item.modifiedShape,
            index = childItems.IndexOf(item)
        };
    }

    public override SlotMarker GetItemInventorySlot(GameItem item)
    {
        SlotMarker slotMarker = TryInventorySlot(item, item.unitCount, item.modifiedShape);
        slotMarker.index = childItems.IndexOf(item);
        return slotMarker;
    }

    public override bool MayHaveValidInventorySlot(GameItem item)
    {
        Tuck.ProbablyStolenTestModel.ModelSupport.RequireFixedShapes(this, item);
        if (item == null)
        {
            return false;
        }
        if (IsInsertLocked())
        {
            return false;
        }
        if (GraphUtils.FindParentEquals(this, item))
        {
            return false;
        }
        if (!GeneralHelper.MayOwnershipAcceptItem(item, this))
        {
            return false;
        }
        Func<GameItem, GameInventory, bool> func = item.mayInventoryAddItemFunc;
        if (func != null && !func.InvokeAllReduce(item, this, InvokeFuncExtensions.BoolReduceOR))
        {
            return false;
        }
        Func<GameItem, GameInventory, bool> func2 = mayInventoryAddItemFunc;
        if (func2 != null && !func2.InvokeAllReduce(item, this, InvokeFuncExtensions.BoolReduceOR))
        {
            return false;
        }
        return true;
    }

    public override bool UncheckedAccept(GameItem item)
    {
        Tuck.ProbablyStolenTestModel.ModelSupport.RequireFixedShapes(this, item);
        if (item == null)
        {
            return false;
        }
        if (item.parentInventory != null)
        {
            return false;
        }
        if (currentItem == item)
        {
            return false;
        }
        onSlotAddItemFunc?.Invoke(item, this, GetIncompleteItemInventorySlot(item));
        currentItem = item;
        item.AddParent(this);

        item.onSlotAddItemFunc?.Invoke(item, this, GetIncompleteItemInventorySlot(item));
        onSlotAddItemEndFunc?.Invoke(item, this, GetIncompleteItemInventorySlot(item));
        onSlotAddItemLateFunc?.Invoke(item, this, GetIncompleteItemInventorySlot(item));


        item.ValidateShapeState();
        return true;
    }

    public override bool Expel(GameItem item)
    {
        Tuck.ProbablyStolenTestModel.ModelSupport.RequireFixedShapes(this, item);
        if (item == null)
        {
            return false;
        }
        if (item.parentInventory != this)
        {
            return false;
        }
        if (currentItem != item)
        {
            return false;
        }
        item.onSlotRemoveItemFunc?.Invoke(item, this, GetIncompleteItemInventorySlot(item));
        onSlotRemoveItemFunc?.Invoke(item, this, GetIncompleteItemInventorySlot(item));

        currentItem = null;
        item.RemoveParent(this);

        onSlotRemoveItemEndFunc?.Invoke(item, this, GetIncompleteItemInventorySlot(item));
        onSlotRemoveItemLateFunc?.Invoke(item, this, GetIncompleteItemInventorySlot(item));
        Validate();
        GameItem gameItem = GraphUtils.FindParentType<GameItem>(this);
        gameItem?.ValidateShapeState();
        gameItem?.Validate();
        return true;
    }

    public override SlotMarker TryFindOneValidInventorySlot(GameItem item)
    {
        int num = item.MaxNumRemove();
        if (num < 1)
        {
            return null;
        }
        SlotMarker slotMarker = new SlotMarker();
        slotMarker.item = item;
        slotMarker.inventory = this;
        slotMarker.itemGridShape = item.modifiedShape;
        if (item != currentItem && currentItem != null)
        {
            Func<GameItem, GameInventory, SlotMarker, GameItem, bool> func = currentItem.slotSearchPassthroughForItemFunc;
            if (func == null || !func(item, this, slotMarker, currentItem))
            {
                Func<GameItem, GameInventory, SlotMarker, GameItem, bool> func2 = slotSearchPassthroughForItemFunc;
                if (func2 == null || !func2(item, this, slotMarker, currentItem))
                {
                    goto IL_008f;
                }
            }
            SlotMarker slotMarker2 = currentItem.TryFindOneValidInventorySlot(item);
            if (slotMarker2 != null)
            {
                return slotMarker2;
            }
        }
        goto IL_008f;
    IL_008f:
        if (currentItem != null && item != currentItem)
        {
            return null;
        }
        if (!MayHaveValidInventorySlot(item))
        {
            return null;
        }
        if (item != currentItem)
        {
            GameItem gameItem = currentItem;
            if (gameItem != null && gameItem.MayStack(item))
            {
                slotMarker.targetItem = currentItem;
                slotMarker.numTransfer = Math.Min(num, currentItem.MaxNumAdd());
                slotMarker.priority = Math.Max(slotPriorityFunc?.Invoke(item, this, slotMarker) ?? 0, item.slotPriorityFunc?.Invoke(item, this, slotMarker) ?? 0);
                if (slotMarker.numTransfer > 0)
                {
                    return slotMarker;
                }
            }
        }
        slotMarker = TryInventorySlot(item, num, item.modifiedShape.Clone());
        if (slotMarker.numTransfer > 0)
        {
            return slotMarker;
        }
        return null;
    }

}
