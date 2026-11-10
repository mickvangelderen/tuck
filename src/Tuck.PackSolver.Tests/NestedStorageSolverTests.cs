using global::Tuck.PackSolver;
using static Tuck.PackSolver.Tests.PackSolverTests;

namespace Tuck.PackSolver.Tests;

[Trait("Category", "SolverContract")]
public class NestedStorageSolverTests
{
    [Theory]
    [SolverData]
    public void ReturnsLootUsingAPackInsideAToolboxInsideABackpack(string solverName)
    {
        var roots = Inventories();
        roots[0] = new("ground", new InventoryLayout.Grid(ShapeState.Rectangle(12, 6, 0, 1)), new(AdmissionKind.Ground));
        var state = State([
            Item("bag", "back", 5, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("box", "ground", 15, tags: ["CONTAINER_TAG", "NESTABLE_DEVICE_TAG"], shape: ShapeState.Rectangle(2, 3)),
            Item("pack", "ground", 20, 4, tags: ["CONTAINER_TAG", "TOOLBOX_ITEM_TAG"], shape: ShapeState.Rectangle(3, 3)),
            Item("loot", "ground", 40, 8, shape: ShapeState.Rectangle(2, 2)), Item("extra", "ground", 9, 11),
            Item("left-pin", "left", 1, tags: ["PINNED_TAG"]), Item("right-pin", "right", 1, tags: ["PINNED_TAG"])
        ], roots.Concat([
            new InventoryState("bag-grid", new InventoryLayout.Grid(ShapeState.Rectangle(3, 3, 0, 1)), new(AdmissionKind.Container), "bag"),
            new InventoryState("box-slot", new InventoryLayout.Slot(), new(AdmissionKind.Toolbox, slotIndex: 0), "box"),
            new InventoryState("pack-grid", new InventoryLayout.Grid(ShapeState.Rectangle(2, 2, 0, 1)), new(AdmissionKind.Container), "pack")
        ]).ToArray());
        var witness = new PackPlan(state.StateId,
        [
            Place("pack", "box-slot", new Placement.Slot()), Place("loot", "pack-grid"),
            Place("box", "bag-grid"), Place("extra", "bag-grid", new Placement.Grid(2))
        ]);
        AssertAllReturned(PlanReplay.Replay(state, witness));

        var result = SolverUnderTest.Solve(solverName, new(state));
        Assert.Equal(91, result.FinalValue);
        AssertAllReturned(PlanReplay.Replay(state, result.Plan));
    }

    [Theory]
    [SolverData]
    public void StorageInAContainerThatCannotBeCarriedDoesNotReturnItsContents(string solverName)
    {
        var roots = Inventories();
        roots[0] = new("ground", new InventoryLayout.Grid(ShapeState.Rectangle(5, 3, 0, 1)), new(AdmissionKind.Ground));
        var state = State([
            Item("bag", "back", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("box", "ground", 5, tags: ["CONTAINER_TAG", "NESTABLE_DEVICE_TAG"], shape: ShapeState.Rectangle(3, 3)),
            Item("loot", "ground", 100, 4),
            Item("left-pin", "left", 1, tags: ["PINNED_TAG"]), Item("right-pin", "right", 1, tags: ["PINNED_TAG"])
        ], roots.Concat([
            new InventoryState("bag-grid", new InventoryLayout.Grid(ShapeState.Rectangle(1, 1, 0, 1)),
                new(AdmissionKind.Container, allowedTags: ["NESTABLE_DEVICE_TAG"]), "bag"),
            new InventoryState("box-grid", new InventoryLayout.Grid(ShapeState.Rectangle(3, 3, 0, 1)), new(AdmissionKind.Container), "box")
        ]).ToArray());
        var stored = PlanReplay.Replay(state, new(state.StateId, [Place("loot", "box-grid")]));
        Assert.False(PlanReplay.IsReturned(stored, "loot"));

        var result = SolverUnderTest.Solve(solverName, new(state));
        Assert.Equal(3, result.FinalValue);
        Assert.Empty(result.Plan.Steps);
    }

    private static void AssertAllReturned(PackState state) =>
        Assert.All(state.Items, i => Assert.True(PlanReplay.IsReturned(state, i.Id), i.Id));
}
