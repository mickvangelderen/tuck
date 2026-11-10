namespace Tuck.ProbablyStolenTestModel;

/// <summary>
/// Fixture construction only. Algorithms should use GameItem, not this subclass.
/// Supply resolved masks/tags and explicit game-equivalent callbacks; identifiers do not install rules.
/// Quantities greater than one require a declared stackCapacity. Parent state modifiers are unsupported:
/// without them the game recomputes modifiedState as a shallow clone of state during validation.
/// </summary>
public sealed class ModelItem : GameItem
{
    private bool destroyed;
    /// <summary>
    /// Counterpart of DirectoryMaster.Item(identifier), used to create a fresh item when splitting.
    /// Must return a detached item with the same identifier and factory defaults, not clone this instance.
    /// Instance-scoped so fixtures can run concurrently without a global item registry.
    /// </summary>
    public Func<GameItem>? SplitFactory { get; set; }

    /// <summary>Mark unsupported features when importing a fixture; operations then fail explicitly.</summary>
    public string? UnsupportedBehavior { get; set; }

    public ModelValuationContext ValuationContext
    {
        get => valuationContext;
        set => valuationContext = value ?? throw new ArgumentNullException(nameof(value));
    }

    public ModelItem(string identifier, GridShape shape, int count = 1, GameInventory? contents = null,
        int stackCapacity = 1)
        : base(identifier)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        if (stackCapacity < 1) throw new ArgumentOutOfRangeException(nameof(stackCapacity));
        if (count > stackCapacity)
            throw new ArgumentException("Initial quantity exceeds the declared stack capacity.", nameof(count));
        if (count > 1 && contents != null)
            throw new ArgumentException("Stacked containers are not supported by this fixture.", nameof(contents));
        this.shape = shape.Clone();
        modifiedShape = shape.Clone();
        unitCount = count;
        if (stackCapacity > 1)
            maxSlotAmountItemFunc = (_, _, _) => stackCapacity;
        if (contents != null) SetContentWindow(new PixelWindow(contents));
    }

    // Compatibility is deliberately not inferred from the identifier: fixtures must provide
    // mayStackItemFunc where appropriate. Quantity limits and stack compatibility are separate rules.
    internal void ValidateQuantity()
    {
        if (unitCount < 0)
            throw new InvalidOperationException("A fixture cannot have a negative quantity.");
        if (unitCount > 1 && maxSlotAmountItemFunc == null && parentInventory?.maxSlotAmountItemFunc == null)
            throw new InvalidOperationException("A stack fixture needs an explicit quantity-limit rule.");
    }

    public override void Validate() => ValidateQuantity();

    public override void Destroy()
    {
        if (destroyed) return;
        destroyed = true;
        parentInventory?.Expel(this);
        foreach (var item in GraphUtils.FindChildrenType<GameItem>(this))
            item.Destroy();
        contentWindow?.RemoveParent(this);
        contentWindow = null;
    }
}
