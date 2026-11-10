// Ported from dependencies/probably-stolen/decompiled/SlotMarker.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Removed UI refresh and equipment UI classification; transfer accounting and callback order preserved.
// Added fixture support checks before mutation.
#nullable disable
using System;

public class SlotMarker
{
    public GameItem item;

    public GameInventory inventory;

    public GridShape itemGridShape;

    public int index = -1;

    public GameItem targetItem;

    public int numTransfer;

    public int priority;

    public bool IsValid()
    {
        return numTransfer > 0;
    }



    public static void StackItemUnchecked(GameItem item, GameItem targetItem, int amount = -1)
    {
        Tuck.ProbablyStolenTestModel.ModelSupport.RequireFixedShapes(item, targetItem);
        if (amount == -1)
        {
            amount = item.unitCount;
        }
        targetItem.unitCount += amount;
        item.unitCount -= amount;
        targetItem.Validate();
        item.Validate();
        item.onChangeAmountItemFunc?.Invoke(item);
        if (item.parentInventory != null)
        {
            SlotMarker incompleteItemInventorySlot = item.parentInventory.GetIncompleteItemInventorySlot(item);
            item.onSlotChangeAmountItemFunc?.Invoke(item, item.parentInventory, incompleteItemInventorySlot);
            item.parentInventory.onSlotChangeAmountItemFunc?.Invoke(item, item.parentInventory, incompleteItemInventorySlot);
        }
        targetItem.onChangeAmountItemFunc?.Invoke(targetItem);
        if (targetItem.parentInventory != null)
        {
            SlotMarker incompleteItemInventorySlot2 = targetItem.parentInventory.GetIncompleteItemInventorySlot(targetItem);
            targetItem.onSlotChangeAmountItemFunc?.Invoke(targetItem, targetItem.parentInventory, incompleteItemInventorySlot2);
            targetItem.parentInventory.onSlotChangeAmountItemFunc?.Invoke(targetItem, targetItem.parentInventory, incompleteItemInventorySlot2);
        }
        if (item.unitCount < 1)
        {
            item.parentInventory?.Expel(item);
        }
    }

    public int TryAcceptOnce(int amount = -1)
    {
        Tuck.ProbablyStolenTestModel.ModelSupport.RequireFixedShapes(item, inventory, targetItem);
        if (item == null)
        {
            return 0;
        }
        if (amount < 0)
        {
            amount = numTransfer;
        }
        amount = Math.Min(amount, numTransfer);
        amount = Math.Min(amount, item.MaxNumRemove());
        if (amount <= 0)
        {
            return 0;
        }
        if (targetItem != null)
        {
            StackItemUnchecked(item, targetItem, amount);
            return amount;
        }
        if (inventory != null && itemGridShape != null)
        {
            if (amount < item.unitCount)
            {
                item = item.GetSplitItem(amount);
            }
            else
            {
                item.parentInventory?.Expel(item);
            }
            item.modifiedShape = new GridShapeBuilder(item.modifiedShape).SetTransform(itemGridShape);
            if (!inventory.UncheckedAccept(item))
            {
                return 0;
            }
            return amount;
        }
        return 0;
    }

    public int AcceptUnchecked()
    {
        Tuck.ProbablyStolenTestModel.ModelSupport.RequireFixedShapes(item, inventory);
        if (item == null)
        {
            return 0;
        }
        if (inventory != null && itemGridShape != null)
        {
            item.modifiedShape = new GridShapeBuilder(item.modifiedShape).SetTransform(itemGridShape);
            if (!inventory.UncheckedAccept(item))
            {
                return 0;
            }
            return item.unitCount;
        }
        return 0;
    }
}

