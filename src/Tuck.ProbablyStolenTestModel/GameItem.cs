// Headless scaffold for the inventory subset of GameItem; see GameItem.Port.cs for ported methods.
#nullable disable
public abstract partial class GameItem : GameItemFunc, GraphNodeStorage, DirectoryEntry
{
    public GameInventory parentInventory { get; set; }
    public PixelWindow contentWindow { get; protected set; }
    public GraphNodeWindow child => contentWindow;
    public List<GraphNodeStorage> parents => parentInventory == null ? new() : new() { parentInventory };
    public virtual List<GraphNodeStorage> children => child == null ? new() : new() { child };
    public GridShape shape { get; set; }
    public GridShape modifiedShape { get; set; }
    public TagSystem state { get; set; } = new();
    public TagSystem modifiedState { get; set; } = new();
    public string identifier { get; set; }
    public string identifierName { get; set; }
    public virtual string name { get; set; } = "";
    public int uniqueId { get; set; }
    public int unitCount { get; set; } = 1;
    public long unitValue { get; set; }
    public long unitBaseValue { get; set; }
    public long lateUnitValue { get; set; }
    public long backupUnitValue { get; set; }
    public List<ItemFeature> itemFeatures = new();
    internal Tuck.ProbablyStolenTestModel.ModelValuationContext valuationContext = new();

    public GameItem(string identifier, PixelWindow window = null)
    {
        this.identifier = identifier;
        contentWindow = window;
        window?.AddParent(this);
        shape = new GridShapeBuilder(1, 1);
        modifiedShape = shape.Clone();
    }

    public abstract void Validate();
    public abstract void Destroy();

    // Shapes are resolved in the fixture. With no parent state modifiers, the game's
    // ComputeModifiedShapeState uses this shallow clone (including shared TagState instances).
    public void ValidateShapeState()
    {
        Tuck.ProbablyStolenTestModel.ModelSupport.RequireSupportedAncestors(this);
        modifiedState = state.Clone();
        Validate();
        parentInventory?.Validate();
    }

    public bool IsTag(string tagName) => modifiedState.IsTag(tagName);
    public bool EnableTag(string tagName, bool skipValidate = false)
    {
        if (state.IsTag(tagName)) return false;
        state.EnableTag(tagName);
        if (!skipValidate) ValidateShapeState();
        return true;
    }
    public bool DisableTag(string tagName, bool skipValidate = false)
    {
        if (!state.IsTag(tagName)) return false;
        state.DisableTag(tagName);
        if (!skipValidate) ValidateShapeState();
        return true;
    }

}
