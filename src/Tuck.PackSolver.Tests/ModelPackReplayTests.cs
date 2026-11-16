using static Tuck.PackSolver.Tests.PackSolverTests;
using Xunit.Sdk;

namespace Tuck.PackSolver.Tests;

public class ModelPackReplayTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CannotCarryAParentWhoseChildHasUnknownEffects(bool stateModifier)
    {
        var state = State([Item("bag", "ground", 1), Item("child", "inside", 10)],
            Inventories().Append(new InventoryState("inside", new InventoryLayout.Grid(ShapeState.Rectangle(1, 1, 0, 1)),
                new(AdmissionKind.Owned), "bag")).ToArray(),
            new ItemDefinition("child", rules: new(parentShape: stateModifier ? ParentShapeRule.None : ParentShapeRule.Unsupported,
                unsupportedParentState: stateModifier)));
        var model = new ModelPackReplay(state);

        Assert.Throws<NotSupportedException>(() => model.Execute(new(state.StateId, [Place("bag", "left")])));

        Assert.Same(model.Inventories["ground"], model.Items["bag"].parentInventory);
        Assert.Same(model.Inventories["inside"], model.Items["child"].parentInventory);
    }

    [Fact]
    public void CannotCarryAParentWhoseInventoryHasUnknownEffects()
    {
        var state = State([Item("bag", "ground", 1)],
            Inventories().Append(new InventoryState("inside", new InventoryLayout.Grid(ShapeState.Rectangle(1, 1, 0, 1)),
                new(AdmissionKind.Container), "bag", unsupportedParentEffects: true)).ToArray());
        var model = new ModelPackReplay(state);

        Assert.Throws<NotSupportedException>(() => model.Execute(new(state.StateId, [Place("bag", "left")])));

        Assert.Same(model.Inventories["ground"], model.Items["bag"].parentInventory);
    }

    [Fact]
    public void LiveAdmissionRefusalRejectsAPlanTheSolverReplayAccepts()
    {
        var state = State([Item("loot", "ground", 10)]);
        var plan = new PackPlan(state.StateId, [Place("loot", "left")]);
        Assert.Equal("left", PlanReplay.Replay(state, plan).Items.Single().Location.InventoryId);
        var model = new ModelPackReplay(state);
        model.Inventories["left"].mayInventoryAddItemFunc = (_, _) => false;

        Assert.ThrowsAny<XunitException>(() => model.Execute(plan));
        Assert.Same(model.Inventories["ground"], model.Items["loot"].parentInventory);
        Assert.Equal(0, model.ReturnedValue);
    }

    [Fact]
    public void VanillaMachineBayWitnessDoesNotTrustCapturedUnsupportedAdmission()
    {
        var state = State([
            Item("bay", "left", 300, identifier: "machine_bay", types: ["STORAGE"], tags: ["CONTAINER_TAG"]),
            Item("fridge", "ground", 100, types: ["MACHINE"], tags: ["CONTAINER_TAG"])
        ], Inventories().Append(new InventoryState("inside", new InventoryLayout.Grid(ShapeState.Rectangle(6, 8, 0, 1)),
            new(AdmissionKind.Unsupported), "bay", unsupportedTransfer: "Equipment or unclassified inventory is not packing storage.")).ToArray());
        var plan = new PackPlan(state.StateId, [Place("fridge", "inside", new Placement.Grid(3, 4))]);
        Assert.Throws<InvalidOperationException>(() => PlanReplay.Replay(state, plan));
        var model = new ModelPackReplay(state);

        model.Execute(plan);

        Assert.Same(model.Inventories["inside"], model.Items["fridge"].parentInventory);
        Assert.Equal(plan.Steps[0].Destination, model.Snapshot().Items.Single(i => i.Id == "fridge").Location);
        Assert.Equal(400, model.ReturnedValue);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void ExecutesTheExactRequestedGridTransform(int rotation, bool flipped)
    {
        var state = State([
            Item("bag", "back", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("loot", "ground", 10, shape: new ShapeState(2, 2, [1, 1, 1, 0]))
        ], Inventories().Append(new InventoryState("inside", new InventoryLayout.Grid(ShapeState.Rectangle(4, 4, 0, 1)),
            new(AdmissionKind.Container), "bag")).ToArray());
        var destination = new ItemLocation("inside", new Placement.Grid(1, 2, rotation, flipped));
        var model = new ModelPackReplay(state);
        var instance = model.Items["loot"];

        model.Execute(new(state.StateId, [new TransferStep("loot", destination)]));

        Assert.Same(instance, Assert.Single(model.Inventories["inside"].childItems));
        Assert.Equal((1, 2, rotation, flipped), (instance.modifiedShape.minX, instance.modifiedShape.minY,
            instance.modifiedShape.orientation, instance.modifiedShape.flipped));
        Assert.Equal(destination, model.Snapshot().Items.Single(i => i.Id == "loot").Location);
        Assert.Empty(model.Inventories["ground"].childItems);
        Assert.Equal(11, model.ReturnedValue);
    }

    [Fact]
    public void RejectsAnOccupiedDestinationRatherThanPretendingTheMoveHappened()
    {
        var state = State([Item("loot", "ground", 10), Item("blocker", "ground", 1, 2)]);
        var model = new ModelPackReplay(state);

        Assert.ThrowsAny<XunitException>(() => model.Execute(new(state.StateId, [Place("loot", "ground", new Placement.Grid(2))])));

        Assert.Equal(0, model.Items["loot"].modifiedShape.minX);
        Assert.Equal(2, model.Items["blocker"].modifiedShape.minX);
        Assert.Equal(2, model.Inventories["ground"].childItems.Count);
    }
}
