using Tuck.ProbablyStolenTestModel;

namespace Tuck.ProbablyStolenTestModel.Tests;

public class InventoryTests
{
    private static GridShape Mask(string cells = "1", int width = 1)
        => new GridShapeBuilder().SetData(cells, width).Build();

    private static ModelItem Item(string id = "item", string cells = "1", int width = 1, int count = 1)
        => new(id, Mask(cells, width), count);

    private static ModelItem Stack(int count, int capacity = 10)
    {
        var item = new ModelItem("stack", Mask(), count, stackCapacity: capacity);
        item.mayStackItemFunc = (a, b) => a.identifier == b.identifier;
        item.SplitFactory = () => Stack(1, capacity);
        return item;
    }

    [Fact]
    public void WholeItemMovePreservesIdentityAndUpdatesBothParents()
    {
        var ground = new GameGridInventory(2, 2);
        var pocket = new GameSlotInventory();
        var item = Item();
        Assert.True(ground.UncheckedAccept(item));
        Assert.Equal(0, TransferScenario.Move(item, pocket));
        Assert.Empty(ground.childItems);
        Assert.Same(item, pocket.childItem);
        Assert.Same(pocket, item.parentInventory);
    }

    [Fact]
    public void PartialMergeReturnsRemainingQuantityAndKeepsSourceOnGround()
    {
        var ground = new GameGridInventory(1, 1);
        var bag = new GameGridInventory(1, 1);
        var source = Stack(5);
        var target = Stack(8);
        ground.UncheckedAccept(source);
        bag.UncheckedAccept(target);
        Assert.Equal(3, TransferScenario.Move(source, bag));
        Assert.Equal(3, source.unitCount);
        Assert.Equal(10, target.unitCount);
        Assert.Same(ground, source.parentInventory);
        Assert.Single(bag.childItems);
    }

    [Fact]
    public void CompleteMergeConsumesAndDetachesSource()
    {
        var ground = new GameGridInventory(1, 1);
        var bag = new GameGridInventory(1, 1);
        var source = Stack(2);
        var target = Stack(8);
        ground.UncheckedAccept(source);
        bag.UncheckedAccept(target);
        Assert.Equal(0, TransferScenario.Move(source, bag));
        Assert.Null(source.parentInventory);
        Assert.Equal(0, source.unitCount);
        Assert.Empty(ground.childItems);
        Assert.Equal(10, target.unitCount);
    }

    [Fact]
    public void SlotMarkerReturnsMovedQuantityAndMergeNotifiesBeforeExpulsion()
    {
        var ground = new GameGridInventory(1, 1);
        var bag = new GameGridInventory(1, 1);
        var source = Stack(2);
        var target = Stack(8);
        ground.UncheckedAccept(source);
        bag.UncheckedAccept(target);
        var events = new List<string>();
        source.onChangeAmountItemFunc = _ => events.Add("source amount");
        source.onSlotChangeAmountItemFunc = (_, _, _) => events.Add("source slot");
        ground.onSlotChangeAmountItemFunc = (_, _, _) => events.Add("ground amount");
        target.onChangeAmountItemFunc = _ => events.Add("target amount");
        target.onSlotChangeAmountItemFunc = (_, _, _) => events.Add("target slot");
        bag.onSlotChangeAmountItemFunc = (_, _, _) => events.Add("bag amount");
        source.onSlotRemoveItemFunc = (_, _, _) => events.Add("source removed");
        ground.onSlotRemoveItemFunc = (_, _, _) => events.Add("ground removed");
        Assert.Equal(2, bag.TryFindOneValidInventorySlot(source).TryAcceptOnce());
        Assert.Equal(new[] { "source amount", "source slot", "ground amount", "target amount",
            "target slot", "bag amount", "source removed", "ground removed" }, events);
    }

    [Fact]
    public void SplitCreatesNewItemFromFactoryRatherThanCloningInstance()
    {
        var ground = new GameGridInventory(1, 1);
        var bag = new GameGridInventory(1, 1);
        var source = Stack(8);
        source.name = "instance-only name";
        ground.UncheckedAccept(source);
        Assert.Equal(0, TransferScenario.Move(source, bag, 3));
        var split = Assert.Single(bag.childItems);
        Assert.NotSame(source, split);
        Assert.Equal(5, source.unitCount);
        Assert.Equal(3, split.unitCount);
        Assert.Equal("", split.name);
        Assert.Same(ground, source.parentInventory);
        Assert.Same(bag, split.parentInventory);
        Assert.True(split.MayStack(source));
    }

    [Fact]
    public void MissingSplitFactoryFailsBeforeChangingQuantities()
    {
        var source = Stack(8);
        source.SplitFactory = null;
        var bag = new GameGridInventory(1, 1);
        Assert.Throws<NotSupportedException>(() => TransferScenario.Move(source, bag, 3));
        Assert.Equal(8, source.unitCount);
        Assert.Empty(bag.childItems);
    }

    [Fact]
    public void FullDestinationRefusesWithoutMutation()
    {
        var bag = new GameGridInventory(1, 1);
        var existing = Item("existing");
        var incoming = Item("incoming");
        bag.UncheckedAccept(existing);
        Assert.Equal(1, TransferScenario.Move(incoming, bag));
        Assert.Same(existing, Assert.Single(bag.childItems));
        Assert.Null(incoming.parentInventory);
    }

    [Fact]
    public void GridUsesHolesObstaclesAndOutsideBounds()
    {
        var bag = new GameGridInventory("0001", 2);
        var diagonal = Item(cells: "1001", width: 2);
        Assert.False(TransferScenario.Place(diagonal, bag, 0, 0).IsValid());
        var otherDiagonal = Item(cells: "0110", width: 2);
        Assert.True(TransferScenario.Place(otherDiagonal, bag, 0, 0).IsValid());
        Assert.False(TransferScenario.Place(otherDiagonal, bag, 1, 0).IsValid());
        Assert.Equal(1, TransferScenario.Place(otherDiagonal, bag, 0, 0).TryAcceptOnce());
        Assert.True(TransferScenario.Place(Item(), bag, 0, 0).IsValid());
        Assert.False(TransferScenario.Place(Item(), bag, 1, 0).IsValid());
    }

    [Fact]
    public void SearchRotatesAndUsesOriginalOrientationOrder()
    {
        var bag = new GameGridInventory(1, 2);
        var wide = Item(cells: "11", width: 2);
        var slot = bag.TryFindOneValidInventorySlot(wide);
        Assert.NotNull(slot);
        Assert.Equal(3, slot.itemGridShape.orientation); // 0, 3, 2, 1
        Assert.Equal(1, slot.TryAcceptOnce());
        Assert.Equal((1, 2), (wide.modifiedShape.globalWidth, wide.modifiedShape.globalHeight));
    }

    [Fact]
    public void CarrySlotDoesNotRestrictItemToItsVisualDimensions()
    {
        var pocket = new GameSlotInventory(1, 1);
        var large = Item(cells: "111111", width: 3);
        Assert.Equal(0, TransferScenario.Move(large, pocket));
        Assert.Same(large, pocket.childItem);
        Assert.Equal(1, TransferScenario.Move(Item(), pocket));
    }

    [Fact]
    public void BackpackSlotRequiresOwnedBackpack()
    {
        var slot = new GameSlotInventory();
        ContainerHelper.AllowOnlyTaggedItem(slot, "BACKPACK_TAG", ownedOnly: true);
        var bag = Item("bag");
        Assert.Equal(1, TransferScenario.Move(bag, slot));
        bag.EnableTag("BACKPACK_TAG");
        Assert.Equal(1, TransferScenario.Move(bag, slot));
        bag.EnableTag("IS_OWNED_TAG");
        Assert.Equal(0, TransferScenario.Move(bag, slot));
    }

    [Fact]
    public void AdmissionRunsBeforeStacking()
    {
        var bag = new GameGridInventory(1, 1);
        var target = Stack(3);
        var source = Stack(2);
        bag.UncheckedAccept(target);
        bag.mayInventoryAddItemFunc = (_, _) => false;
        Assert.Equal(2, TransferScenario.Move(source, bag));
        Assert.Equal(3, target.unitCount);
        Assert.Equal(2, source.unitCount);
    }

    [Fact]
    public void AdmissionDelegatesUseOrWithinEachSideButRequireBothSides()
    {
        var bag = new GameGridInventory(1, 1);
        var item = Item();
        bag.mayInventoryAddItemFunc = (_, _) => false;
        bag.mayInventoryAddItemFunc += (_, _) => true;
        Assert.True(bag.MayHaveValidInventorySlot(item));
        item.mayInventoryAddItemFunc = (_, _) => false;
        Assert.False(bag.MayHaveValidInventorySlot(item));
    }

    [Fact]
    public void StackCompatibilityRequiresBothItemsToAgree()
    {
        var a = Stack(2);
        var b = Stack(3);
        Assert.True(a.MayStack(b));
        b.mayStackItemFunc = (_, _) => false;
        Assert.False(a.MayStack(b));
        Assert.False(b.MayStack(a));
    }

    [Fact]
    public void QuantityLimitsCombineItemAndInventoryDelegates()
    {
        var source = Stack(8);
        var ground = new GameGridInventory(1, 1);
        ground.UncheckedAccept(source);
        ground.minSlotAmountItemFunc = (_, _, _) => 2;
        source.maxSlotRemoveItemFunc = (_, _, _) => 4;
        Assert.Equal(4, source.MaxNumRemove());
        var bag = new GameGridInventory(1, 1);
        bag.maxSlotAmountItemFunc = (_, _, _) => 3;
        bag.maxSlotAddItemFunc = (_, _, _) => 2;
        Assert.Equal(2, bag.TryFindOneValidInventorySlot(source).numTransfer);
    }

    [Fact]
    public void InsertLockIsHonored()
    {
        var bag = new GameGridInventory(1, 1);
        bag.LockInv();
        Assert.Equal(1, TransferScenario.Move(Item(), bag));
        bag.UnlockInv();
        Assert.Equal(0, TransferScenario.Move(Item(), bag));
    }

    [Fact]
    public void NestedContainerMovesWithContentsAndRejectsCycles()
    {
        var inside = new GameGridInventory(2, 2);
        var bag = new ModelItem("bag", Mask(), contents: inside);
        var loot = Item();
        inside.UncheckedAccept(loot);
        var pocket = new GameSlotInventory();
        Assert.Equal(0, TransferScenario.Move(bag, pocket));
        Assert.Same(bag, GraphUtils.FindParentType<GameItem>(inside));
        Assert.Same(inside, loot.parentInventory);
        Assert.False(inside.MayHaveValidInventorySlot(bag));
        Assert.Equal(1, TransferScenario.Move(bag, inside));
    }

    [Fact]
    public void OccupiedPocketCanSearchInsideContainerWhenPassthroughIsEnabled()
    {
        var inside = new GameGridInventory(1, 1);
        var bag = new ModelItem("bag", Mask(), contents: inside);
        bag.slotSearchPassthroughForItemFunc = (_, _, _, _) => true;
        var pocket = new GameSlotInventory();
        pocket.UncheckedAccept(bag);
        var loot = Item();
        Assert.Equal(0, TransferScenario.Move(loot, pocket));
        Assert.Same(inside, loot.parentInventory);
        Assert.Same(bag, pocket.childItem);
    }

    [Fact]
    public void OwnedItemCannotEnterUnownedContainer()
    {
        var inside = new GameGridInventory(1, 1);
        var bag = new ModelItem("bag", Mask(), contents: inside);
        var loot = Item();
        loot.EnableTag("IS_OWNED_TAG");
        Assert.Equal(1, TransferScenario.Move(loot, inside));
        bag.EnableTag("IS_OWNED_TAG");
        Assert.Equal(0, TransferScenario.Move(loot, inside));
    }

    [Fact]
    public void DynamicShapeBehaviorFailsExplicitly()
    {
        var item = Item();
        item.parentItemShapeModifierForItemFunc = (_, _, _, shape, _, _) => shape;
        var bag = new GameGridInventory(1, 1);
        Assert.Throws<NotSupportedException>(() => TransferScenario.Move(item, bag));
        Assert.Empty(bag.childItems);
        Assert.Null(item.parentInventory);
    }

    [Fact]
    public void FixtureCanMarkUnsupportedBehavior()
    {
        var item = Item();
        item.UnsupportedBehavior = "Contents change the container's tags.";
        Assert.Throws<NotSupportedException>(() => TransferScenario.Move(item, new GameSlotInventory()));
    }

    [Fact]
    public void NullDestinationReturnValueMatchesGameAndDoesNotProveSuccess()
    {
        var item = Item();
        Assert.Equal(0, GraphUtils.TryAcceptAll(null!, item));
        Assert.Null(item.parentInventory);
        Assert.Equal(1, item.unitCount);
    }

    [Fact]
    public void ReturnFromItemListDoesNotExposeMutableInventoryStorage()
    {
        var bag = new GameGridInventory(1, 1);
        var item = Item();
        bag.UncheckedAccept(item);
        bag.childItems.Clear();
        Assert.Same(item, Assert.Single(bag.childItems));
    }

    [Fact]
    public void StackTransferLoopRetainsTheGamesFortyAttemptLimit()
    {
        var bag = new GameGridInventory(1, 1);
        var source = Stack(50, 100);
        var target = Stack(1, 100);
        target.maxSlotAddItemFunc = (_, _, _) => 1;
        bag.UncheckedAccept(target);
        Assert.Equal(10, TransferScenario.Move(source, bag));
        Assert.Equal(10, source.unitCount);
        Assert.Equal(41, target.unitCount);
    }

    [Fact]
    public void SplittingInvokesFactoryAfterSourceAmountCallbacks()
    {
        var source = Stack(5);
        var events = new List<string>();
        source.onChangeAmountItemFunc = _ => events.Add("source");
        source.SplitFactory = () =>
        {
            events.Add("factory");
            var item = Stack(1);
            item.onChangeAmountItemFunc = _ => events.Add("split");
            return item;
        };
        source.GetSplitItem(2);
        Assert.Equal(new[] { "source", "factory", "split" }, events);
    }

    [Fact]
    public void StateValidityCallbackCanRefusePlacement()
    {
        var bag = new GameGridInventory(2, 2);
        var item = Item();
        item.isItemStateValidFunc = (_, _, _, _, _) => false;
        Assert.Equal(1, TransferScenario.Move(item, bag));
        Assert.Empty(bag.childItems);
    }

    [Fact]
    public void DeferredTagChangeBecomesVisibleWhenValidated()
    {
        var item = Item();
        item.EnableTag("IS_OWNED_TAG", skipValidate: true);
        Assert.False(item.IsTag("IS_OWNED_TAG"));
        item.ValidateShapeState();
        Assert.True(item.IsTag("IS_OWNED_TAG"));
    }

    [Fact]
    public void UncheckedStackingStillRejectsUnsupportedFixtureBeforeMutation()
    {
        var source = Stack(2);
        var target = Stack(3);
        target.UnsupportedBehavior = "Unsupported state modifier";
        Assert.Throws<NotSupportedException>(() => SlotMarker.StackItemUnchecked(source, target));
        Assert.Equal(2, source.unitCount);
        Assert.Equal(3, target.unitCount);
    }
}
