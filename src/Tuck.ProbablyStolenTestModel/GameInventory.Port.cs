// Ported from dependencies/probably-stolen/decompiled/GameInventory.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Source: GameInventory.cs. Selected methods unchanged.
#nullable disable
using UnityEngine;
public abstract partial class GameInventory
{
    public GameItem GetParentItem()
    {
        List<GameItem> list = GraphUtils.FindParentsType<GameItem>(this);
        if (list.Count < 1)
        {
            return null;
        }
        return list[0];
    }

    public SlotMarker TryInventorySlot(GameItem item, GridShape tempShape, TagSystem itemState = null)
    {
        if (itemState == null)
        {
            itemState = item.modifiedState;
        }
        return TryInventorySlot(item, item.unitCount, Vector2.negativeInfinity, tempShape, itemState);
    }

    public SlotMarker TryInventorySlot(GameItem item, int requestedNum, GridShape tempShape, TagSystem itemState = null)
    {
        if (itemState == null)
        {
            itemState = item.modifiedState;
        }
        return TryInventorySlot(item, requestedNum, Vector2.negativeInfinity, tempShape, itemState);
    }

    public GameInventory UncheckedAcceptAll(List<GameItem> items)
    {
        items.ForEach((GameItem item) =>
        {
            UncheckedAccept(item);
        });
        return this;
    }

    public GameInventory ExpelAll()
    {
        childItems?.ForEach((GameItem i) =>
        {
            Expel(i);
        });
        return this;
    }

    public bool IsInsertLocked()
    {
        if (overrideLockInsert)
        {
            return true;
        }
        return false;
    }

    public bool IsRemoveLocked()
    {
        if (overrideLockRemove)
        {
            return true;
        }
        return false;
    }

    public void LockInv()
    {
        overrideLockInsert = true;
        overrideLockRemove = true;
    }

    public void UnlockInv()
    {
        overrideLockInsert = false;
        overrideLockRemove = false;
    }
}
