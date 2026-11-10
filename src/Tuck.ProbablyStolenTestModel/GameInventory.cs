// Headless scaffold for the inventory subset; dynamic parent modifiers are deliberately unsupported.
#nullable disable
using UnityEngine;

public abstract partial class GameInventory : GameInventoryFunc, GraphNodeStorage
{
    public string identifier;
    public bool overrideLockInsert;
    public bool overrideLockRemove;
    public virtual bool SupportsGroupPlacement => false;
    public GraphNodeStorage parent { get; protected set; }
    public List<GraphNodeStorage> parents => parent == null ? new() : new() { parent };
    public abstract List<GameItem> childItems { get; }
    public abstract List<GraphNodeStorage> children { get; }
    public abstract bool CheckInventorySlotStillValid(GameItem item, GridShape tempShape, TagSystem tempState);
    public abstract SlotMarker TryInventorySlot(GameItem item, int requestedNum, Vector2 itemGraphicsCenterPosition, GridShape tempShape, TagSystem itemState);
    public abstract SlotMarker GetItemInventorySlot(GameItem item);
    public abstract SlotMarker GetIncompleteItemInventorySlot(GameItem item);
    public abstract bool UncheckedAccept(GameItem item);
    public abstract bool Expel(GameItem item);
    public abstract bool MayHaveValidInventorySlot(GameItem item);
    public abstract SlotMarker TryFindOneValidInventorySlot(GameItem item);
    public virtual void Validate() { }
    public virtual bool AddParent(GraphNodeStorage node)
    {
        if (parent != null) return false;
        parent = node;
        return true;
    }
    public virtual bool RemoveParent(GraphNodeStorage node)
    {
        if (parent != node) return false;
        parent = null;
        return true;
    }
    public bool CheckRemoveParentShapeModifier(GameItem item)
    {
        Tuck.ProbablyStolenTestModel.ModelSupport.RequireFixedShapes(this, item);
        return true;
    }
    public bool CheckAcceptParentShapeStateModifier(GameItem item, SlotMarker slot, GridShape shape, TagSystem state)
    {
        Tuck.ProbablyStolenTestModel.ModelSupport.RequireFixedShapes(this, item);
        return true;
    }
    public bool CheckChangeParentShapeStateModifier(GameItem item, SlotMarker slot, GridShape shape, TagSystem state)
        => CheckAcceptParentShapeStateModifier(item, slot, shape, state);
}
