using global::Tuck.PackSolver;
using static Tuck.PackSolver.Tests.PackSolverTests;

namespace Tuck.PackSolver.Tests;

public class IncrementalSimulationTests
{
    [Fact]
    public void DirectFitChecksMatchFiniteCellSetsForLabeledShapes()
    {
        var inventory = new InventoryState("inside", new InventoryLayout.Grid(
            new ShapeState(4, 2, new byte[] { 0, 0, 1, 0, 0, 0, 0, 0 }, outsideBounds: 0)), new(AdmissionKind.Container), "bag");
        var shapes = new[] { ShapeState.Rectangle(2, 1), new ShapeState(3, 1, new byte[] { 2, 1, 0 }),
            new ShapeState(2, 2, new byte[] { 1, 0, 0, 1 }, outsideBounds: 2) };
        foreach (var shape in shapes)
        {
            var simulation = new TransferSimulation(State([Item("bag", "back", 1), Item("moving", "ground", 10, shape: shape),
                Item("obstacle", "inside", 1, 2, 1, shape: new ShapeState(2, 1, new byte[] { 2, 1 }))],
                Inventories(5).Append(inventory).ToArray()));
            var item = simulation.Items["moving"];
            for (int flip = 0; flip < 2; flip++)
                for (int rotation = 0; rotation < 4; rotation++)
                    for (int y = -2; y < 4; y++)
                        for (int x = -2; x < 6; x++)
                        {
                            var position = new Placement.Grid(x, y, rotation, flip != 0);
                            var placed = simulation.View.Effective(item.Id).Place(item.Shape, position, inventory.Layout);
                            var grid = ((InventoryLayout.Grid)inventory.Layout).Cells;
                            // Independent finite-set formulation retained from the original simulator.
                            bool expected = !((placed.Width > grid.Width || placed.Height > grid.Height)
                                    && (placed.Width > grid.Height || placed.Height > grid.Width))
                                && placed.Positions(1).ToHashSet().IsSubsetOf(new PlacedShape(grid, new()).Positions(0))
                                && !placed.Intersects(simulation.View.Placed("obstacle"));
                            Assert.Equal(expected, simulation.Fits(item, inventory, position, whole: true));
                        }
        }
    }

    [Fact]
    public void SuccessorsDoNotChangeTheirParentOrSiblings()
    {
        var parent = new TransferSimulation(State([Item("loot", "ground", 10)]));
        Assert.Equal(0, parent.ReturnedValue);
        Assert.True(parent.TryCreateSuccessor(Place("loot", "left"), out var left, out _));
        Assert.True(parent.TryCreateSuccessor(Place("loot", "right"), out var right, out _));
        Assert.Equal(0, parent.ReturnedValue);
        Assert.Equal("left", left.Items["loot"].Location.InventoryId);
        Assert.Equal("right", right.Items["loot"].Location.InventoryId);
        Assert.True(left.TryApply(Place("loot", "ground"), out _));
        Assert.Equal(10, right.ReturnedValue);
        Assert.Equal(0, parent.ReturnedValue);
    }

    [Fact]
    public void InvalidInitialGeometryCannotBypassGlobalValidation()
    {
        var parent = new TransferSimulation(State([Item("a", "ground", 10), Item("b", "ground", 20), Item("c", "ground", 30, 1)]));
        string before = parent.Key();
        Assert.False(parent.TryApply(Place("c", "left"), out _));
        Assert.Equal(before, parent.Key());
        Assert.True(parent.TryApply(Place("a", "left"), out _));
        Assert.True(parent.TryApply(Place("c", "right"), out _));
    }

    [Theory]
    [InlineData(1729)]
    [InlineData(991)]
    public void IncrementalTransitionsMatchFullRevalidation(int seed)
    {
        var inventories = Inventories(5).Concat([
            new InventoryState("inside", new InventoryLayout.Grid(new ShapeState(4, 2, new byte[] { 0, 0, 1, 0, 0, 0, 0, 0 }, 1)), new(AdmissionKind.Container), "bag"),
            new InventoryState("nested", new InventoryLayout.Grid(ShapeState.Rectangle(2, 1, 0, 1)), new(AdmissionKind.Container), "box")]).ToArray();
        var state = State([Item("bag", "back", 2, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("box", "ground", 3, tags: ["CONTAINER_TAG", "NESTABLE_DEVICE_TAG"]),
            Item("a", "ground", 10, 1, shape: ShapeState.Rectangle(2, 1)), Item("b", "left", 4),
            Item("c", "nested", 5)], inventories);
        var fast = new TransferSimulation(state);
        var reference = new TransferSimulation(state, incremental: false);
        var random = new Random(seed);
        var ids = state.Items.Select(i => i.Id).ToArray();
        int acceptedCount = 0;
        for (int n = 0; n < 1000; n++)
        {
            string id = ids[random.Next(ids.Length)];
            var inventory = inventories[random.Next(inventories.Length)];
            Placement position = inventory.Layout is InventoryLayout.Slot ? new Placement.Slot()
                : new Placement.Grid(random.Next(-1, 5), random.Next(-1, 3), random.Next(4), random.Next(2) == 1);
            var step = new TransferStep(id, new(inventory.Id, position));
            bool accepted = fast.TryApply(step, out _);
            if (accepted) acceptedCount++;
            Assert.Equal(reference.TryApply(step, out _), accepted);
            Assert.Null(StateComparison.Difference(reference.Snapshot(), fast.Snapshot()));
            Assert.Equal(reference.ReturnedValue, fast.ReturnedValue);
            foreach (var item in fast.Items.Values)
                Assert.Equal(reference.Removable(reference.Items[item.Id]), fast.Removable(item));
        }
        Assert.True(acceptedCount > 20, "The comparison must exercise changing states, not just reject invalid coordinates.");
    }

    [Fact]
    public void EmptyUnsupportedParentInventoryStillRejectsAnInsertion()
    {
        var state = State([Item("box", "left", 1), Item("loot", "ground", 10)],
            Inventories().Append(new InventoryState("inside", new InventoryLayout.Grid(ShapeState.Rectangle(2, 1, 0, 1)),
                new(AdmissionKind.Container), "box", unsupportedParentEffects: true)).ToArray());
        var simulation = new TransferSimulation(state);
        Assert.False(simulation.FixedGeometry);
        Assert.False(simulation.TryApply(Place("loot", "inside"), out _));
        Assert.Equal("ground", simulation.Items["loot"].Location.InventoryId);
    }
}
