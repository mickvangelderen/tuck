namespace Tuck.ProbablyStolenTestModel.Tests;

public class ReviewRegressionTests
{
    private static GridShape Mask() => new GridShapeBuilder(1, 1).SetDataFill(1);
    private static ModelItem Item() => new("item", Mask());

    [Fact]
    public void DirectStateEditsAreAppliedByInventoryValidation()
    {
        var item = Item();
        item.state.EnableTag("IS_OWNED_TAG");
        new GameGridInventory(1, 1).UncheckedAccept(item);
        Assert.True(item.IsTag("IS_OWNED_TAG"));
        Assert.NotSame(item.state, item.modifiedState);
        Assert.Same(item.state.GetTag("IS_OWNED_TAG"), item.modifiedState.GetTag("IS_OWNED_TAG"));
    }

    [Fact]
    public void DeferredEditsToExistingTagsAreVisibleThroughSharedTagStates()
    {
        var item = Item();
        item.EnableTag("tag");
        item.DisableTag("tag", skipValidate: true);
        Assert.False(item.IsTag("tag"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsupportedSiblingDoesNotBlockMovingOrdinaryItem(bool shapeModifier)
    {
        var ground = new GameGridInventory(2, 1);
        var sibling = Item();
        var moved = Item();
        ground.UncheckedAccept(sibling);
        ground.UncheckedAccept(moved);
        if (shapeModifier)
            sibling.parentItemShapeModifierForItemFunc = (_, _, _, shape, _, _) => shape;
        else
            sibling.UnsupportedBehavior = "Unsupported sibling";
        var pocket = new GameSlotInventory();
        Assert.True(GraphUtils.CanAccept(pocket, moved));
        Assert.Equal(0, GraphUtils.TryAcceptAll(pocket, moved));
        Assert.Same(moved, pocket.childItem);
        Assert.Same(ground, sibling.parentInventory);
    }

    [Fact]
    public void NestedSiblingDoesNotBlockParentValidationAfterRemoval()
    {
        var contents = new GameGridInventory(2, 1);
        var container = new ModelItem("bag", Mask(), contents: contents);
        var sibling = Item();
        var moved = Item();
        contents.UncheckedAccept(sibling);
        contents.UncheckedAccept(moved);
        sibling.UnsupportedBehavior = "Unsupported sibling";
        Assert.Equal(0, GraphUtils.TryAcceptAll(new GameSlotInventory(), moved));
        Assert.Same(container, GraphUtils.FindParentType<GameItem>(contents));
    }

    [Fact]
    public void UnsupportedDestinationSiblingDoesNotBlockPlacement()
    {
        var destination = new GameGridInventory(2, 1);
        var sibling = Item();
        destination.UncheckedAccept(sibling);
        sibling.UnsupportedBehavior = "Unsupported sibling";
        var moved = Item();
        Assert.Equal(0, GraphUtils.TryAcceptAll(destination, moved));
        Assert.Same(destination, moved.parentInventory);
    }

    [Fact]
    public void SupportChecksDoNotWalkSiblingContents()
    {
        var ground = new GameGridInventory(2, 1);
        var sibling = new SiblingWithUnreadableContents();
        var moved = Item();
        ground.UncheckedAccept(sibling);
        ground.UncheckedAccept(moved);
        sibling.BlockChildren = true;
        Assert.Equal(0, GraphUtils.TryAcceptAll(new GameSlotInventory(), moved));
    }

    [Fact]
    public void MovingContainerWithUnsupportedContentsStillFails()
    {
        var contents = new GameGridInventory(1, 1);
        var container = new ModelItem("bag", Mask(), contents: contents);
        var child = Item();
        contents.UncheckedAccept(child);
        child.UnsupportedBehavior = "Unsupported contents";
        Assert.Throws<NotSupportedException>(() => GraphUtils.TryAcceptAll(new GameSlotInventory(), container));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsupportedSourceOrDestinationAncestorStillFails(bool destinationAncestor)
    {
        var contents = new GameGridInventory(1, 1);
        var container = new ModelItem("bag", Mask(), contents: contents);
        var moved = Item();
        if (!destinationAncestor) contents.UncheckedAccept(moved);
        container.UnsupportedBehavior = "Unsupported ancestor";
        GameInventory destination = destinationAncestor ? contents : new GameSlotInventory();
        Assert.Throws<NotSupportedException>(() => GraphUtils.TryAcceptAll(destination, moved));
    }

    [Fact]
    public void DestructionDetachesParentBeforeChildrenAndRunsOnlyOnce()
    {
        var contents = new GameGridInventory(1, 1);
        var container = new ModelItem("bag", Mask(), contents: contents);
        var child = Item();
        contents.UncheckedAccept(child);
        var pocket = new GameSlotInventory();
        pocket.UncheckedAccept(container);
        var events = new List<string>();
        container.onSlotRemoveItemFunc = (_, _, _) => events.Add("parent");
        child.onSlotRemoveItemFunc = (_, _, _) =>
        {
            Assert.Null(container.parentInventory);
            events.Add("child");
        };
        container.Destroy();
        container.Destroy();
        Assert.Equal(new[] { "parent", "child" }, events);
        Assert.Null(pocket.childItem);
        Assert.Null(child.parentInventory);
        Assert.Null(container.child);
    }

    [Fact]
    public void InitialStacksRequireCapacityAndRejectOverCapacity()
    {
        Assert.Throws<ArgumentException>(() => new ModelItem("item", Mask(), count: 2));
        Assert.Throws<ArgumentException>(() => new ModelItem("item", Mask(), count: 6, stackCapacity: 5));
        var stack = new ModelItem("item", Mask(), count: 5, stackCapacity: 5);
        Assert.Equal(0, GraphUtils.TryAcceptAll(new GameGridInventory(1, 1), stack));
    }

    [Fact]
    public void RawQuantityEditsCannotSneakInUnconfiguredStacks()
    {
        var item = Item();
        item.unitCount = 2;
        Assert.Throws<InvalidOperationException>(() => GraphUtils.TryAcceptAll(new GameSlotInventory(), item));
    }

    private sealed class SiblingWithUnreadableContents : GameItem
    {
        internal bool BlockChildren;
        internal SiblingWithUnreadableContents() : base("sibling")
            => modifiedShape = shape = Mask();
        public override List<GraphNodeStorage> children => BlockChildren
            ? throw new InvalidOperationException("Sibling contents were traversed.") : new();
        public override void Validate() { }
        public override void Destroy() { }
    }
}
