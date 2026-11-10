// Graph/search subset of MachineryPanelRow.cs, Assembly-CSharp MVID
// 911508a6-e0eb-4cc0-91e8-e14b16ea28d7. Flat storage panels only; rendering and tabs omitted.
#nullable disable
public class MachineryPanelRow : GraphNodeStorage
{
    public bool searchUsingPriority = true;
    private readonly List<GraphNodeStorage> storage = new();
    public GraphNodeStorage parent { get; set; }
    public List<GraphNodeStorage> parents => parent == null ? new() : new() { parent };
    public List<GraphNodeStorage> children => new(storage);

    public MachineryPanelRow() { }

    internal MachineryPanelRow(params GraphNodeStorage[] inventories)
    {
        if (inventories.Any(i => i == null || i.parents.Count != 0)
            || inventories.Distinct().Count() != inventories.Length)
            throw new ArgumentException("Each panel needs distinct, unattached storage.", nameof(inventories));
        foreach (var inventory in inventories)
        {
            inventory.AddParent(this);
            storage.Add(inventory);
        }
    }

    public GraphNodeStorage GetStorage(int index) =>
        index < 0 || index >= storage.Count ? null : storage[index];

    public bool AddParent(GraphNodeStorage node)
    {
        if (parent != null) return false;
        parent = node;
        return true;
    }
    public bool RemoveParent(GraphNodeStorage node)
    {
        if (parent != node) return false;
        parent = null;
        return true;
    }
    public void Validate() { }
    public bool MayHaveValidInventorySlot(GameItem item)
    {
        foreach (var child in children)
            if (child != null && child.MayHaveValidInventorySlot(item)) return true;
        return false;
    }

    // Same priority/tie handling as the game's TryFindOneValidInventorySlot with no selected tabs.
    public SlotMarker TryFindOneValidInventorySlot(GameItem item)
    {
        var list = children;
        if (!searchUsingPriority)
        {
            for (int index = list.Count - 1; index >= 0; index--)
            {
                var slot = list[index]?.TryFindOneValidInventorySlot(item);
                if (slot != null && slot.numTransfer > 0) return slot;
            }
            return null;
        }
        SlotMarker best = null;
        foreach (var child in list)
        {
            var slot = child?.TryFindOneValidInventorySlot(item);
            if (slot != null && slot.numTransfer > 0 && slot.priority >= (best?.priority ?? int.MinValue))
                best = slot;
        }
        return best;
    }
}
