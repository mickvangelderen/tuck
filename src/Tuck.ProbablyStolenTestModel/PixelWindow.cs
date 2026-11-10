// Graph-only counterpart of PixelWindow. No rendering, shared window registry, or layout.
#nullable disable
public class PixelWindow : GraphNodeWindow
{
    public List<GameItem> parentItems { get; } = new();
    public GraphNodeStorage child { get; private set; }
    public List<GraphNodeStorage> parents => new(parentItems);
    public List<GraphNodeStorage> children => child == null ? new() : new() { child };
    public int xPadding => 0;
    public int yPadding => 0;

    public PixelWindow() { }
    internal PixelWindow(GraphNodeStorage storage)
    {
        if (!storage.AddParent(this))
            throw new ArgumentException("Storage already has a parent.", nameof(storage));
        child = storage;
    }
    public bool AddParent(GraphNodeStorage node)
    {
        if (node is not GameItem item || parentItems.Contains(item)) return false;
        parentItems.Add(item);
        return true;
    }
    public bool RemoveParent(GraphNodeStorage node) => node is GameItem item && parentItems.Remove(item);
    public bool MayHaveValidInventorySlot(GameItem item) => child?.MayHaveValidInventorySlot(item) ?? false;
    public SlotMarker TryFindOneValidInventorySlot(GameItem item) => child?.TryFindOneValidInventorySlot(item);
    public void Validate() { }
}
