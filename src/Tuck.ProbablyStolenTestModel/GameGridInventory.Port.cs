// Ported from dependencies/probably-stolen/decompiled/GameGridInventory.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Source: GameGridInventory.cs. Rendering/store effects removed; finite screen coordinates rejected. Search order and callback order preserved.
#nullable disable
using UnityEngine;
public partial class GameGridInventory
{

    public override bool CheckInventorySlotStillValid(GameItem item, GridShape tempShape, TagSystem tempState)
    {
        SlotMarker slotMarker = new SlotMarker();
        slotMarker.item = item;
        slotMarker.inventory = this;
        slotMarker.itemGridShape = tempShape;
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
        if (!tempShape.FullyIntersects(inventoryShape, (byte b, byte bo) => b != 1 || bo == 0))
        {
            return false;
        }
        if (!items.All((GameItem o) => item == o || !tempShape.Intersects(o.modifiedShape)))
        {
            return false;
        }
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
        if (!MayHaveValidInventorySlot(item))
        {
            return slotMarker;
        }
        if (float.IsFinite(itemGraphicsCenterPosition.x))
        {
            throw new NotSupportedException("Screen-space placement is not modeled; provide a grid shape.");
        }
        slotMarker.itemGridShape = tempShape;
        if (!tempShape.FullyIntersects(inventoryShape, (byte b, byte bo) => b != 1 || bo == 0))
        {
            return slotMarker;
        }
        if (!items.All((GameItem o) => (item == o && requestedNum >= item.unitCount) || !tempShape.Intersects(o.modifiedShape)))
        {
            return slotMarker;
        }
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
        SlotMarker slotMarker = TryInventorySlot(item, item.modifiedShape);
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
        if ((item.modifiedShape.width > inventoryShape.width || item.modifiedShape.height > inventoryShape.height) && (item.modifiedShape.width > inventoryShape.height || item.modifiedShape.height > inventoryShape.width))
        {
            return false;
        }
        Func<GameItem, GameInventory, bool> func = item.mayInventoryAddItemFunc;
        if (func != null && !func.InvokeAllReduce(item, this, InvokeFuncExtensions.BoolReduceOR))
        {
            return false;
        }
        if (HopperHelper.MayBypassGridRules(item, this))
        {
            return true;
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
        if (items.Contains(item))
        {
            return false;
        }
        onSlotAddItemFunc?.Invoke(item, this, GetIncompleteItemInventorySlot(item));
        items.Add(item);
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
        if (!items.Contains(item))
        {
            return false;
        }
        item.onSlotRemoveItemFunc?.Invoke(item, this, GetIncompleteItemInventorySlot(item));
        onSlotRemoveItemFunc?.Invoke(item, this, GetIncompleteItemInventorySlot(item));
        bool flag = items.Remove(item);
        if (!flag)
        {
            return false;
        }
        item.RemoveParent(this);

        onSlotRemoveItemEndFunc?.Invoke(item, this, GetIncompleteItemInventorySlot(item));
        onSlotRemoveItemLateFunc?.Invoke(item, this, GetIncompleteItemInventorySlot(item));
        GameItem gameItem = GraphUtils.FindParentType<GameItem>(this);
        gameItem?.ValidateShapeState();
        gameItem?.Validate();
        return flag;
    }

    public SlotMarker TryFindOneValidInventorySlot(GameItem item, bool keepOrientation = false)
    {
        int num = item.MaxNumRemove();
        if (num < 1)
        {
            return null;
        }
        items.Sort((GameItem x, GameItem y) =>
        {
            if (x.modifiedShape.minX == y.modifiedShape.minX && x.modifiedShape.minY == y.modifiedShape.minY)
            {
                return 0;
            }
            return ((x.modifiedShape.minX > y.modifiedShape.minX && x.modifiedShape.minY == y.modifiedShape.minY) || x.modifiedShape.minY > y.modifiedShape.minY) ? 1 : (-1);
        });
        SlotMarker slotMarker = new SlotMarker();
        slotMarker.item = item;
        slotMarker.inventory = this;
        slotMarker.itemGridShape = item.modifiedShape;
        foreach (GameItem item2 in items)
        {
            if (item == item2)
            {
                continue;
            }
            Func<GameItem, GameInventory, SlotMarker, GameItem, bool> func = item2.slotSearchPassthroughForItemFunc;
            if (func == null || !func(item, this, slotMarker, item2))
            {
                Func<GameItem, GameInventory, SlotMarker, GameItem, bool> func2 = slotSearchPassthroughForItemFunc;
                if (func2 == null || !func2(item, this, slotMarker, item2))
                {
                    continue;
                }
            }
            SlotMarker slotMarker2 = item2.TryFindOneValidInventorySlot(item);
            if (slotMarker2 != null)
            {
                return slotMarker2;
            }
        }
        if (!MayHaveValidInventorySlot(item))
        {
            return null;
        }
        foreach (GameItem item3 in items)
        {
            if (item != item3 && item.MayStack(item3))
            {
                slotMarker.targetItem = item3;
                slotMarker.numTransfer = Math.Min(num, item3.MaxNumAdd());
                if (slotMarker.numTransfer >= 1)
                {
                    slotMarker.priority = Math.Max(slotPriorityFunc?.Invoke(item, this, slotMarker) ?? 0, item.slotPriorityFunc?.Invoke(item, this, slotMarker) ?? 0);
                    return slotMarker;
                }
            }
        }
        GridShapeBuilder gridShapeBuilder = new GridShapeBuilder(item.modifiedShape);
        int num2 = (keepOrientation ? 1 : 4);
        int num3 = (keepOrientation ? 1 : 2);
        for (int num4 = 0; num4 < num3; num4++)
        {
            for (int num5 = 0; num5 < num2; num5++)
            {
                for (int num6 = 0; num6 < inventoryShape.height; num6++)
                {
                    for (int num7 = 0; num7 < inventoryShape.width; num7++)
                    {
                        int orientation = 4 - num5;
                        bool flipped = num4 != 0;
                        gridShapeBuilder.SetTransform(num7, num6, flipped, orientation);
                        slotMarker = TryInventorySlot(item, num, gridShapeBuilder);
                        if (slotMarker.numTransfer > 0)
                        {
                            return slotMarker;
                        }
                    }
                }
            }
        }
        return null;
    }

}
