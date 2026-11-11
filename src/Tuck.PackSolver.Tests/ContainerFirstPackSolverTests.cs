using global::Tuck.PackSolver;
using global::Tuck.PackSolver.Solvers;
using static Tuck.PackSolver.Tests.PackSolverTests;

namespace Tuck.PackSolver.Tests;

public class ContainerFirstPackSolverTests
{
    [Theory]
    [InlineData(16, 0, false)]
    [InlineData(17, 0, true)]
    [InlineData(17, 2, false)]
    public void ContainerWitnessRespectsPartialTilesBoundsAndBlockedLabels(int width, byte edge, bool fits)
    {
        var cells = new byte[width * 2];
        cells[cells.Length - 1] = edge;
        var grid = new ShapeState(width, 2, cells); // Zero outside still cannot extend a legal placement.
        var state = State([
            Item("bag", "back", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("pin", "inside", 2, tags: ["PINNED_TAG"], shape: ShapeState.Rectangle(7, 2)),
            Item("box", "ground", 10, tags: ["CONTAINER_TAG", "NESTABLE_DEVICE_TAG"], shape: ShapeState.Rectangle(10, 2)),
            Item("left-pin", "left", 0, tags: ["PINNED_TAG"]), Item("right-pin", "right", 0, tags: ["PINNED_TAG"])
        ], Inventories(10).Concat([
            new InventoryState("inside", new InventoryLayout.Grid(grid), new(AdmissionKind.Container), "bag"),
            Grid("box-grid", "box", 1, 1)
        ]).ToArray());

        var assignments = Assignments(state).Where(a => a.Parents["box"] == "inside").ToArray();
        Assert.Equal(fits, assignments.Length != 0);
        foreach (var assignment in assignments)
        {
            var placement = Assert.IsType<Placement.Grid>(assignment.ContainerLocations["box"].Placement);
            var footprint = new PlacedShape(state.Items.Single(i => i.Id == "box").Shape, placement);
            Assert.All(footprint.Positions(1), cell =>
            {
                Assert.InRange(cell.X, 7, width - 1);
                Assert.InRange(cell.Y, 0, 1);
                Assert.Equal(0, grid.GetLocal(cell.X, cell.Y));
            });
        }
    }

    [Fact]
    public void RejectsContainerParentsWhoseFreeCellsAreFragmentedByPinnedContents()
    {
        var state = WithPinnedObstacle(3, ShapeState.Rectangle(2, 2));
        var assignments = Assignments(state);
        Assert.NotEmpty(assignments);
        Assert.All(assignments, a => Assert.NotEqual("inside", a.Parents["box"]));
        var result = SolverUnderTest.Solve(new ContainerFirstPackSolver(), new(state));
        Assert.Empty(result.Plan.Steps);
    }

    [Fact]
    public void RotatesContainerBesidePinnedContentsBeforeAllocatingItsLoot()
    {
        var state = WithPinnedObstacle(2, ShapeState.Rectangle(2, 1));
        var assignment = Assignments(state).First();
        Assert.Equal("inside", assignment.Parents["box"]);
        var target = Assert.IsType<Placement.Grid>(assignment.ContainerLocations["box"].Placement);
        Assert.Equal(1, target.Orientation % 2);
        var result = SolverUnderTest.Solve(new ContainerFirstPackSolver(), new(state));
        var final = PlanReplay.Replay(state, result.Plan);
        Assert.True(PlanReplay.IsReturned(final, "box"));
        Assert.Equal(state.Items.Single(i => i.Id == "pin").Location, final.Items.Single(i => i.Id == "pin").Location);
    }

    [Fact]
    public void AllocationRespectsAffinityWithoutDuplicatingItemsAcrossCarriedBags()
    {
        var state = State([
            Item("a", "back", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("b", "left", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("pin", "right", 0, tags: ["PINNED_TAG"]),
            Item("flexible", "ground", 10, tags: ["A", "B"]), Item("restricted", "ground", 9, 1, tags: ["A"])
        ], Inventories().Concat([
            Grid("a-grid", "a", 1, 1, ["A"]), Grid("b-grid", "b", 1, 1, ["B"])
        ]).ToArray());
        var assignment = Assignments(state).First();
        Assert.Equal(21, assignment.Value);
        Assert.Equal("a-grid", assignment.Allocation["restricted"]);
        Assert.Equal("b-grid", assignment.Allocation["flexible"]);
        Assert.Equal(21, SolverUnderTest.Solve(new ContainerFirstPackSolver(), new(state)).FinalValue);
    }

    [Fact]
    public void ContainerWitnessCanRepositionAnExistingContainer()
    {
        var state = State([
            Item("bag", "back", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("a", "inside", 2, 1, tags: ["CONTAINER_TAG", "NESTABLE_DEVICE_TAG"]),
            Item("b", "ground", 3, tags: ["CONTAINER_TAG", "NESTABLE_DEVICE_TAG"], shape: ShapeState.Rectangle(2, 1)),
            Item("left-pin", "left", 0, tags: ["PINNED_TAG"]), Item("right-pin", "right", 0, tags: ["PINNED_TAG"])
        ], Inventories().Concat([
            Grid("inside", "bag", 3, 1), Grid("a-grid", "a", 1, 1, ["LOOT"]), Grid("b-grid", "b", 1, 1, ["LOOT"])
        ]).ToArray());
        var assignment = Assignments(state).First();
        Assert.Equal("inside", assignment.Parents["a"]);
        Assert.Equal("inside", assignment.Parents["b"]);
        Assert.NotEqual(state.Items.Single(i => i.Id == "a").Location, assignment.ContainerLocations["a"]);
        Assert.Equal(6, SolverUnderTest.Solve(new ContainerFirstPackSolver(), new(state)).FinalValue);
    }

    [Fact]
    public void MutuallyCompatibleContainersNeverFormACycle()
    {
        var state = State([
            Item("a", "ground", 1, tags: ["CONTAINER_TAG", "NESTABLE_DEVICE_TAG"]),
            Item("b", "ground", 1, 1, tags: ["CONTAINER_TAG", "NESTABLE_DEVICE_TAG"])
        ], Inventories().Concat([Grid("a-grid", "a", 2, 1), Grid("b-grid", "b", 2, 1)]).ToArray());
        var assignments = Assignments(state);
        Assert.Contains(assignments, a => a.Parents["a"] == "b-grid");
        Assert.Contains(assignments, a => a.Parents["b"] == "a-grid");
        Assert.DoesNotContain(assignments, a => a.Parents["a"] == "b-grid" && a.Parents["b"] == "a-grid");
    }

    [Fact]
    public void RecordedToolboxHierarchyHasAGeometricWitnessAndUsefulAllocation()
    {
        var test = RecordedPackCases.All["electronics-toolbox"];
        var assignments = Assignments(test.State);
        Assert.True(assignments.First().Value >= test.MinimumReturnedValue);
        Assert.All(assignments, a => Assert.Equal(a.Parents.Count, a.ContainerLocations.Count));
    }

    [Fact]
    public void ZeroHierarchyBudgetReturnsTheOriginalLoadout()
    {
        var state = State([Item("loot", "ground", 10)]);
        var result = SolverUnderTest.Solve(new ContainerFirstPackSolver(), new(state), new(maxContainerArrangements: 0));
        Assert.Empty(result.Plan.Steps);
        Assert.Equal(PlanningStatus.SearchLimit, result.Status);
        Assert.Null(result.Metrics!.RelaxedValueBound);
    }

    [Fact]
    public void ContainerGeometryChecksRespectThePlacementBudget()
    {
        var state = WithPinnedObstacle(2, ShapeState.Rectangle(2, 1));
        var result = SolverUnderTest.Solve(new ContainerFirstPackSolver(), new(state), new(maxPlacementChecks: 1));
        Assert.Equal(PlanningStatus.SearchLimit, result.Status);
        Assert.InRange(result.Metrics!.PlacementChecks, 0, 1);
        Assert.InRange(result.Metrics.ExpandedNodes, 0, 5000);
    }

    private static List<ContainerAssignment> Assignments(PackState state)
    {
        var capacities = state.Inventories.Where(i => i.OwnerItemId != null && TransferSimulation.IsPackingStorage(i))
            .GroupBy(i => i.OwnerItemId!).ToDictionary(g => g.Key,
                g => g.Sum(i => i.Layout is InventoryLayout.Grid grid ? (long)grid.Cells.Cells.Count(c => c == 0) : 1));
        return new ContainerAssignments(new(state), capacities, 128, default, 200000).Run();
    }

    private static InventoryState Grid(string id, string owner, int width, int height, string[]? tags = null) =>
        new(id, new InventoryLayout.Grid(ShapeState.Rectangle(width, height, 0, 1)), new(AdmissionKind.Container, allowedTags: tags), owner);

    private static PackState WithPinnedObstacle(int width, ShapeState boxShape) => State([
        Item("bag", "back", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
        Item("pin", "inside", 2, 1, tags: ["PINNED_TAG"], shape: ShapeState.Rectangle(1, 2)),
        Item("box", "ground", 10, tags: ["CONTAINER_TAG", "NESTABLE_DEVICE_TAG"], shape: boxShape),
        Item("left-pin", "left", 0, tags: ["PINNED_TAG"]), Item("right-pin", "right", 0, tags: ["PINNED_TAG"])
    ], Inventories().Concat([Grid("inside", "bag", width, 2), Grid("box-grid", "box", 1, 1)]).ToArray());
}
