// Ported from dependencies/probably-stolen/decompiled/GameItem.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Selected methods unchanged except split: fixture factory replaces DirectoryMaster; missing factory
// is rejected before mutation. Factory invocation order is preserved. UI refresh removed.
#nullable disable
public abstract partial class GameItem
{
    public bool AddParent(GraphNodeStorage node)
    {
        if (parentInventory != null)
        {
            return false;
        }
        if (!(node is GameInventory))
        {
            return false;
        }
        parentInventory = (GameInventory)node;
        return true;
    }

    public bool RemoveParent(GraphNodeStorage node)
    {
        if (parentInventory != node)
        {
            return false;
        }
        parentInventory = null;
        return true;
    }

    public bool MayHaveValidInventorySlot(GameItem item)
    {
        return child?.MayHaveValidInventorySlot(item) ?? false;
    }

    public SlotMarker TryFindOneValidInventorySlot(GameItem item)
    {
        foreach (GraphNodeStorage child in children)
        {
            SlotMarker slotMarker = child.TryFindOneValidInventorySlot(item);
            if (slotMarker != null)
            {
                return slotMarker;
            }
        }
        return null;
    }

    public GameItem SetContentWindow(PixelWindow window)
    {
        contentWindow?.RemoveParent(this);
        contentWindow = window;
        contentWindow?.AddParent(this);
        return this;
    }

    public GameItem SetUnitCount(int num)
    {
        unitCount = num;
        Validate();
        return this;
    }

    public GameItem SetAmount(int num)
    {
        return SetUnitCount(num);
    }

    public GameItem SetStack(int num)
    {
        return SetUnitCount(num);
    }

    public GameItem SetValue(long num)
    {
        unitValue = num;
        return this;
    }

    public bool MayRemove()
    {
        if (parentInventory == null)
        {
            return false;
        }
        bool? flag = null;
        bool? flag2 = null;
        if (parentInventory.IsRemoveLocked())
        {
            return false;
        }
        if (GeneralHelper.IsInsideNonOwnedContainer(this))
        {
            return false;
        }
        bool? flag3 = mayInventoryRemoveItemFunc?.InvokeAllReduce(this, parentInventory, InvokeFuncExtensions.BoolReduceOR);
        bool? flag4 = parentInventory?.mayInventoryRemoveItemFunc?.InvokeAllReduce(this, parentInventory, InvokeFuncExtensions.BoolReduceOR);
        flag = flag & flag3 & flag4;
        flag2 = flag2 | flag3 | flag4;
        if (flag != false)
        {
            return true;
        }
        return false;
    }

    public int MaxNumRemove()
    {
        if (parentInventory == null)
        {
            return unitCount;
        }
        SlotMarker incompleteItemInventorySlot = parentInventory.GetIncompleteItemInventorySlot(this);
        if (!parentInventory.CheckRemoveParentShapeModifier(this))
        {
            return 0;
        }
        int num = Math.Max(incompleteItemInventorySlot.inventory.minSlotAmountItemFunc?.InvokeAllReduce(this, incompleteItemInventorySlot.inventory, incompleteItemInventorySlot, InvokeFuncExtensions.BoolReduceMin) ?? 0, minSlotAmountItemFunc?.InvokeAllReduce(this, incompleteItemInventorySlot.inventory, incompleteItemInventorySlot, InvokeFuncExtensions.BoolReduceMin) ?? 0);
        int num2 = unitCount - num;
        int num3 = Math.Min(incompleteItemInventorySlot.inventory.maxSlotRemoveItemFunc?.InvokeAllReduce(this, incompleteItemInventorySlot.inventory, incompleteItemInventorySlot, InvokeFuncExtensions.BoolReduceMin) ?? int.MaxValue, maxSlotRemoveItemFunc?.InvokeAllReduce(this, incompleteItemInventorySlot.inventory, incompleteItemInventorySlot, InvokeFuncExtensions.BoolReduceMin) ?? int.MaxValue);
        if (num3 < num2)
        {
            num2 = num3;
        }
        return num2;
    }

    public int MaxNumAdd()
    {
        if (parentInventory == null)
        {
            return int.MaxValue;
        }
        SlotMarker incompleteItemInventorySlot = parentInventory.GetIncompleteItemInventorySlot(this);
        incompleteItemInventorySlot.targetItem = this;
        return MaxNumAdd(incompleteItemInventorySlot);
    }

    public int MaxNumAdd(SlotMarker slot)
    {
        if (slot == null)
        {
            return 0;
        }
        if (slot.inventory == null)
        {
            return int.MaxValue;
        }
        int num = Math.Min(slot.inventory.maxSlotAmountItemFunc?.InvokeAllReduce(this, slot.inventory, slot, InvokeFuncExtensions.BoolReduceMax) ?? int.MaxValue, maxSlotAmountItemFunc?.InvokeAllReduce(this, slot.inventory, slot, InvokeFuncExtensions.BoolReduceMax) ?? int.MaxValue);
        if (slot.inventory.maxSlotAmountItemFunc == null && maxSlotAmountItemFunc == null)
        {
            num = 1;
        }
        int num2 = num - (slot.targetItem?.unitCount ?? 0);
        int num3 = Math.Min(slot.inventory.maxSlotAddItemFunc?.InvokeAllReduce(this, slot.inventory, slot, InvokeFuncExtensions.BoolReduceMax) ?? int.MaxValue, maxSlotAddItemFunc?.InvokeAllReduce(this, slot.inventory, slot, InvokeFuncExtensions.BoolReduceMax) ?? int.MaxValue);
        if (num3 < num2)
        {
            num2 = num3;
        }
        return num2;
    }

    public bool MayStack(GameItem otherItem)
    {
        if (otherItem == null)
        {
            return false;
        }
        Func<GameItem, GameItem, bool> func = otherItem.mayStackItemFunc;
        if (func == null || !func.InvokeAllReduce(this, otherItem, InvokeFuncExtensions.BoolReduceOR))
        {
            return false;
        }
        Func<GameItem, GameItem, bool> func2 = mayStackItemFunc;
        if (func2 == null || !func2.InvokeAllReduce(this, otherItem, InvokeFuncExtensions.BoolReduceOR))
        {
            return false;
        }
        return true;
    }

    public SlotMarker GetStackSlot(GameItem itemToBeStacked)
    {
        if (parentInventory == null)
        {
            return null;
        }
        if (this == itemToBeStacked)
        {
            return null;
        }
        if (!MayStack(itemToBeStacked))
        {
            return null;
        }
        SlotMarker incompleteItemInventorySlot = parentInventory.GetIncompleteItemInventorySlot(this);
        incompleteItemInventorySlot.item = itemToBeStacked;
        incompleteItemInventorySlot.targetItem = this;
        incompleteItemInventorySlot.numTransfer = MaxNumAdd();
        return incompleteItemInventorySlot;
    }

    public SlotMarker GetIncompleteSlotInInventory()
    {
        if (parentInventory == null)
        {
            return null;
        }
        return parentInventory.GetIncompleteItemInventorySlot(this);
    }

    public bool CheckIsStateValid(GameInventory parentInventory, SlotMarker slot, GridShape tempShape, TagSystem itemState)
    {
        if (parentInventory == null)
        {
            return false;
        }
        bool? flag = null;
        bool? flag2 = null;
        bool? flag3 = isItemStateValidFunc?.Invoke(this, parentInventory, slot, tempShape, itemState);
        bool? flag4 = parentInventory?.isItemStateValidFunc?.Invoke(this, parentInventory, slot, tempShape, itemState);
        flag = flag & flag3 & flag4;
        flag2 = flag2 | flag3 | flag4;
        if (flag != false)
        {
            return true;
        }
        return false;
    }

    public int GetModifiedItemSize()
    {
        int num = 0;
        for (int i = 0; i < modifiedShape.height; i++)
        {
            for (int j = 0; j < modifiedShape.width; j++)
            {
                if (modifiedShape.GetLocal(j, i) == 1)
                {
                    num++;
                }
            }
        }
        return num;
    }
    public GameItem GetSplitItem(int requestedAmount)
    {
        Tuck.ProbablyStolenTestModel.ModelSupport.RequireSplitFactory(this);

        unitCount -= requestedAmount;
        Validate();
        onChangeAmountItemFunc?.Invoke(this);
        if (parentInventory != null)
        {
            onSlotChangeAmountItemFunc?.Invoke(this, parentInventory, parentInventory.GetIncompleteItemInventorySlot(this));
            parentInventory?.onSlotChangeAmountItemFunc?.Invoke(this, parentInventory, parentInventory.GetIncompleteItemInventorySlot(this));
        }

        GameItem gameItem = Tuck.ProbablyStolenTestModel.ModelSupport.CreateSplitItem(this);
        gameItem.unitCount = requestedAmount;
        gameItem?.onChangeAmountItemFunc?.Invoke(gameItem);
        if (gameItem?.parentInventory != null)
        {
            gameItem.onSlotChangeAmountItemFunc?.Invoke(gameItem, gameItem.parentInventory, gameItem.parentInventory.GetIncompleteItemInventorySlot(gameItem));
            gameItem.parentInventory?.onSlotChangeAmountItemFunc?.Invoke(gameItem, gameItem.parentInventory, gameItem.parentInventory.GetIncompleteItemInventorySlot(gameItem));
        }
        return gameItem;
    }
}
