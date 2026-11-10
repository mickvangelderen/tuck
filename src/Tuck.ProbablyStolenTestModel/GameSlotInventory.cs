// Headless slot: constructor dimensions are visual only in the original, not capacity limits.
#nullable disable
public partial class GameSlotInventory : GameInventory
{
    private GameItem currentItem;
    public readonly int defaultOrientation;
    public GameItem childItem => currentItem;
    public override List<GameItem> childItems => currentItem == null ? new() : new() { currentItem };
    public override List<GraphNodeStorage> children => currentItem == null ? new() : new() { currentItem };
    public GameSlotInventory() : this(1, 1) { }
    public GameSlotInventory(int defaultGridWidth, int defaultGridHeight, bool stealUI = false, bool isUIDown = false) { }
    public override void Validate()
    {
        if (currentItem?.modifiedShape is GridShapeBuilder builder) builder.SetPosition(0, 0);
        currentItem?.Validate();
    }
}
