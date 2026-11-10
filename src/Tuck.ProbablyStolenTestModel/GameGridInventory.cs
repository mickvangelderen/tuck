// Constructors retain the game's masks (0 = usable; outside = 1), without creating render nodes.
#nullable disable
public partial class GameGridInventory : GameInventory
{
    private readonly List<GameItem> items = new();
    public GridShape inventoryShape { get; private set; }
    public override List<GameItem> childItems => new(items);
    public override List<GraphNodeStorage> children => new(items);
    public GameGridInventory(int width, int height)
        => inventoryShape = new GridShapeBuilder(width, height).SetDataFill(0).SetDataOutside(1).Build();
    public GameGridInventory(string shape, int width)
        => inventoryShape = new GridShapeBuilder().SetData(shape, width).SetDataOutside(1).Build();
    public GameGridInventory SetShape(int width, int height)
    {
        inventoryShape = new GridShapeBuilder(width, height).SetDataFill(0).SetDataOutside(1).Build();
        Validate();
        return this;
    }
    public GameGridInventory SetShape(string shape, int width)
    {
        inventoryShape = new GridShapeBuilder().SetData(shape, width).SetDataOutside(1).Build();
        Validate();
        return this;
    }
    // The decompiler prints a self-call in this wrapper. Dispatch explicitly to the search overload.
    public override SlotMarker TryFindOneValidInventorySlot(GameItem item)
        => TryFindOneValidInventorySlot(item, false);
}
