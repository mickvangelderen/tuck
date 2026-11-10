using global::Tuck.PackSolver;
using static Tuck.PackSolver.Tests.PackSolverTests;

namespace Tuck.PackSolver.Tests;

public class SequenceSearchTests
{
    [Fact]
    public void DrainsALongDependencyChainWithoutSearchingMovePermutations()
    {
        const int count = 80; // Exercise graphs and cell masks beyond one 64-bit word.
        var state = BagState(Enumerable.Range(0, count).Select(i => Item("item-" + i, "inside", 1, i)), count + 1);
        var targets = Enumerable.Range(0, count).Select(i => Place("item-" + i, "inside", placement: new Placement.Grid(i + 1))).ToArray();
        var (_, steps) = Solve(state, targets, new(maxSequenceNodes: 4, maxPlacementChecks: 20000));

        Assert.Equal(count, steps.Count);
        Assert.Equal("item-79", steps[0].SourceItemId);
    }

    [Fact]
    public void IndependentCyclesReuseOnePhysicalBuffer()
    {
        var state = BagState([Item("a", "inside", 1), Item("b", "inside", 1, 1),
            Item("c", "inside", 1, 2), Item("d", "inside", 1, 3)], 4, groundWidth: 1);
        var (_, steps) = Solve(state, [Grid("a", 1), Grid("b", 0), Grid("c", 3), Grid("d", 2)]);

        Assert.Equal(6, steps.Count);
        Assert.Equal(1, PeakGroundOccupancy(state, steps));
    }

    [Fact]
    public void AllocatesTwoSimultaneousBuffersWhenOneCannotBreakAllDependencies()
    {
        var state = ThreeBars(groundHeight: 2);
        var (_, steps) = Solve(state, VerticalBars());

        Assert.Equal(5, steps.Count);
        Assert.Equal(2, PeakGroundOccupancy(state, steps));
    }

    [Fact]
    public void BacktracksBufferGeometryWhenTheFirstFitPreventsASecondBuffer()
    {
        var original = ThreeBars(groundHeight: 3);
        // H-shaped free space: the first horizontal placement fits but blocks both vertical buffers.
        var ground = new InventoryState("ground", new InventoryLayout.Grid(new ShapeState(3, 3,
            new byte[] { 0, 1, 0, 0, 0, 0, 0, 1, 0 }, outsideBounds: 1)), new(AdmissionKind.Ground));
        var state = new PackState(original.StateId, original.Roots, original.Items,
            original.Inventories.Select(i => i.Id == "ground" ? ground : i), original.Definitions.Values);
        var (_, steps) = Solve(state, VerticalBars());

        Assert.Equal(5, steps.Count);
        Assert.Equal(2, PeakGroundOccupancy(state, steps));
        var first = steps[0].Destination;
        Assert.Equal("ground", first.InventoryId);
        Assert.Equal(1, Assert.IsType<Placement.Grid>(first.Placement).Orientation);
    }

    [Fact]
    public void InsufficientBufferSpaceDoesNotProduceAnIllegalSequence()
    {
        var state = ThreeBars(groundHeight: 1);
        var simulation = new TransferSimulation(state);
        var options = new PlanningOptions(maxSequenceNodes: 100, maxPlacementChecks: 4000);
        var search = Search(VerticalBars(), options);

        Assert.Null(search.Run(simulation));
        Assert.Null(StateComparison.Difference(state, simulation.Snapshot()));
    }

    [Fact]
    public void DoesNotRelocateUnrelatedGroundLootWhileBreakingAHandSwapCycle()
    {
        var state = State([Item("a-ground", "ground", 1), Item("b-left", "left", 2), Item("c-right", "right", 3)]);
        var (_, steps) = Solve(state, [Place("b-left", "right"), Place("c-right", "left")],
            new(maxSequenceNodes: 16, maxPlacementChecks: 300));

        Assert.Equal(3, steps.Count);
        Assert.DoesNotContain(steps, s => s.SourceItemId == "a-ground");
    }

    [Fact]
    public void ClearsClutterFromAContainerUsedOnlyAsTemporaryStorage()
    {
        var state = State([
            Item("bag", "back", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("a", "left", 10, shape: ShapeState.Rectangle(2, 1), tags: ["HAZARDOUS_WASTE_TAG"]),
            Item("b", "right", 10, shape: ShapeState.Rectangle(2, 1), tags: ["HAZARDOUS_WASTE_TAG"]),
            Item("clutter", "inside", 1, 1, tags: ["HAZARDOUS_WASTE_TAG"])
        ], Inventories().Append(new InventoryState("inside", new InventoryLayout.Grid(ShapeState.Rectangle(3, 1, 0, 1)),
            new(AdmissionKind.Container), "bag")).ToArray());
        TransferStep[] targets = [Place("a", "right"), Place("b", "left")];
        // All three movable items are refused by the ground. The bag [. C .] must be rearranged.
        PlanReplay.Replay(state, new(state.StateId,
            [Grid("clutter", 0), Grid("a", 1), targets[1], targets[0]]));

        var (_, steps) = Solve(state, targets, new(64, 2000));

        Assert.Contains(steps, s => s.SourceItemId == "clutter");
        Assert.DoesNotContain(steps, s => s.Destination.InventoryId == "ground");
    }

    [Fact]
    public void DiscoversABlockerOfAnotherBlockersTemporaryPlacement()
    {
        var inventories = Inventories();
        inventories[0] = new("ground", new InventoryLayout.Grid(ShapeState.Rectangle(2, 1, 0, 1)), new(AdmissionKind.Ground));
        var state = State([
            Item("bag", "back", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("a", "left", 10, shape: ShapeState.Rectangle(3, 1)),
            Item("b", "right", 10, shape: ShapeState.Rectangle(3, 1)),
            Item("c", "inside", 1, 1, shape: ShapeState.Rectangle(2, 1)), Item("d", "ground", 1, 1)
        ], inventories.Append(new InventoryState("inside", new InventoryLayout.Grid(ShapeState.Rectangle(4, 1, 0, 1)),
            new(AdmissionKind.Container), "bag")).ToArray());
        TransferStep[] targets = [Place("a", "right"), Place("b", "left")];
        // Bag [. C C .], ground [. D]. Hand items cannot fit on the ground, so D is
        // discovered only when trying to relocate C. Neither clutter item has a goal.
        PlanReplay.Replay(state, new(state.StateId,
            [Grid("d", 0), Place("c", "ground"), Grid("a", 1), targets[1], targets[0]]));

        var (_, steps) = Solve(state, targets, new(200, 10000));

        Assert.Contains(steps, s => s.SourceItemId == "c");
        Assert.Contains(steps, s => s.SourceItemId == "d");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DoesNotTryToClearPinnedOrMaskedBufferSpace(bool masked)
    {
        var inventories = Inventories();
        inventories[0] = new("ground", new InventoryLayout.Grid(masked
            ? new ShapeState(5, 1, [0, 1, 0, 1, 0], 1) : ShapeState.Rectangle(3, 1, 0, 1)), new(AdmissionKind.Ground));
        var state = State([
            Item("a", "left", 10, shape: ShapeState.Rectangle(2, 1)),
            Item("b", "right", 10, shape: ShapeState.Rectangle(2, 1)),
            Item("clutter", "ground", 1, masked ? 4 : 1, tags: masked ? [] : ["PINNED_TAG"])
        ], inventories);
        var simulation = new TransferSimulation(state);
        var search = Search([Place("a", "right"), Place("b", "left")], new(64, 2000));

        Assert.Null(search.Run(simulation));
        Assert.False(search.Limited);
        Assert.Null(StateComparison.Difference(state, simulation.Snapshot()));
    }

    [Fact]
    public void LeavesUnrelatedClutterAloneWhenADiscardAlreadyHasSpace()
    {
        var state = State([Item("loot", "ground", 10), Item("clutter", "ground", 1, 1),
            Item("cheap", "left", 2), Item("fixed", "right", 1, tags: ["PINNED_TAG"])]);
        var target = Place("loot", "left");
        var simulation = new TransferSimulation(state);
        // A pending discard bypasses the fast dependency scheduler.
        var search = new SequenceSearch(new() { ["loot"] = target }, ["cheap"], new(64, 2000), default);

        var steps = Assert.IsType<List<TransferStep>>(search.Run(simulation));
        var final = PlanReplay.Replay(state, new(state.StateId, steps));

        Assert.False(search.Limited);
        Assert.Null(StateComparison.Difference(state, simulation.Snapshot()));
        Assert.Equal(target.Destination, final.Items.Single(i => i.Id == "loot").Location);
        Assert.False(PlanReplay.IsReturned(final, "cheap"));
        Assert.DoesNotContain(steps, s => s.SourceItemId == "clutter");
    }

    [Fact]
    public void ChoosesACycleMemberWhoseShapeFitsTheAvailableBuffer()
    {
        var inventories = Inventories();
        inventories[0] = new("ground", new InventoryLayout.Grid(ShapeState.Rectangle(3, 1, 0, 1)), new(AdmissionKind.Ground));
        var state = State([Item("obstacle", "ground", 1, 1, tags: ["PINNED_TAG"]),
            Item("large", "left", 2, shape: ShapeState.Rectangle(2, 1)), Item("small", "right", 1)], inventories);
        // Try the large item first: two free cells separated by the obstacle are not a 2x1 buffer.
        var (_, steps) = Solve(state, [Place("large", "right"), Place("small", "left")]);

        Assert.Equal(3, steps.Count);
        Assert.Equal("small", steps[0].SourceItemId);
    }

    [Fact]
    public void UsesAFutureGoalAsTemporarySpaceBeforeCompletingThatGoal()
    {
        var state = BagState([Item("a", "inside", 1, tags: ["HAZARDOUS_WASTE_TAG"]),
            Item("b", "inside", 1, 1, tags: ["HAZARDOUS_WASTE_TAG"]), Item("c", "ground", 1)], 3, groundWidth: 1);
        // a/b cannot go on the ground. The spare bag cell is also c's goal, so c must wait.
        var (_, steps) = Solve(state, [Grid("a", 1), Grid("b", 0), Grid("c", 2)]);

        Assert.Equal(4, steps.Count);
        Assert.Equal("c", steps[^1].SourceItemId);
    }

    [Fact]
    public void RejectsTargetsThatReconfigureEquipment()
    {
        var inventories = Inventories(12).Append(new InventoryState("mods", new InventoryLayout.Slot(), new(AdmissionKind.Owned), "gun")).ToArray();
        var state = State([Item("gun", "ground", 50, 2, shape: ShapeState.Rectangle(7, 2), identifier: "smg"),
            Item("stock", "left", 10, shape: ShapeState.Rectangle(2, 1)), Item("blocker", "ground", 1)], inventories,
            new ItemDefinition("stock", rules: new(parentShape: ParentShapeRule.GunAttachment)));
        var simulation = new TransferSimulation(state);
        var search = Search([Place("stock", "mods", placement: new Placement.Slot()), Place("blocker", "left")], new(200, 10000));
        Assert.Null(search.Run(simulation));
        Assert.Null(StateComparison.Difference(state, simulation.Snapshot()));
    }

    [Fact]
    public void CancellationAndZeroBudgetsLeaveTheInputUntouched()
    {
        var state = State([Item("a", "left", 1), Item("b", "right", 1)]);
        TransferStep[] targets = [Place("a", "right"), Place("b", "left")];
        // A legal witness distinguishes budget exhaustion from an impossible hand swap.
        var witness = PlanReplay.Replay(state, new(state.StateId,
            [Place("a", "ground"), Place("b", "left"), Place("a", "right")]));
        Assert.Equal("right", witness.Items.Single(i => i.Id == "a").Location.InventoryId);
        Assert.Equal("left", witness.Items.Single(i => i.Id == "b").Location.InventoryId);
        var simulation = new TransferSimulation(state);
        var zero = Search(targets, new(maxSequenceNodes: 0, maxPlacementChecks: 0));
        Assert.Null(zero.Run(simulation));
        Assert.True(zero.Limited);
        var cancelled = new SequenceSearch(targets.ToDictionary(t => t.SourceItemId), [], new(), new CancellationToken(true));
        Assert.Throws<OperationCanceledException>(() => cancelled.Run(simulation));
        Assert.Null(StateComparison.Difference(state, simulation.Snapshot()));
    }

    private static TransferStep Grid(string id, int x) => Place(id, "inside", placement: new Placement.Grid(x));
    private static TransferStep[] VerticalBars() => Enumerable.Range(0, 3)
        .Select(i => Place("bar-" + i, "inside", placement: new Placement.Grid(i, 0, orientation: 1))).ToArray();
    private static PackState ThreeBars(int groundHeight) => BagState(Enumerable.Range(0, 3)
        .Select(i => Item("bar-" + i, "inside", 1, 0, i, shape: ShapeState.Rectangle(3, 1))),
        3, 3, groundWidth: 3, groundHeight: groundHeight);
    private static PackState BagState(IEnumerable<ItemState> contents, int width, int height = 1,
        int groundWidth = 4, int groundHeight = 1)
    {
        var inventories = Inventories();
        inventories[0] = new("ground", new InventoryLayout.Grid(ShapeState.Rectangle(groundWidth, groundHeight, 0, 1)), new(AdmissionKind.Ground));
        return State([Item("bag", "back", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("fixed-left", "left", 1, tags: ["PINNED_TAG"]), Item("fixed-right", "right", 1, tags: ["PINNED_TAG"]), .. contents],
            inventories.Append(new InventoryState("inside", new InventoryLayout.Grid(ShapeState.Rectangle(width, height, 0, 1)),
                new(AdmissionKind.Container), "bag")).ToArray());
    }
    private static SequenceSearch Search(TransferStep[] targets, PlanningOptions options) =>
        new(targets.ToDictionary(t => t.SourceItemId), [], options, default);
    private static (SequenceSearch Search, List<TransferStep> Steps) Solve(PackState state, TransferStep[] targets, PlanningOptions? options = null)
    {
        var simulation = new TransferSimulation(state);
        var search = Search(targets, options ?? new(maxSequenceNodes: 200, maxPlacementChecks: 10000));
        var steps = Assert.IsType<List<TransferStep>>(search.Run(simulation));
        Assert.False(search.Limited);
        Assert.Null(StateComparison.Difference(state, simulation.Snapshot()));
        var final = PlanReplay.Replay(state, new(state.StateId, steps));
        foreach (var target in targets)
            Assert.Equal(target.Destination, final.Items.Single(i => i.Id == target.SourceItemId).Location);
        Assert.Equal(state.Items.Count, final.Items.Count);
        return (search, steps);
    }
    private static int PeakGroundOccupancy(PackState state, List<TransferStep> steps)
    {
        int peak = 0;
        for (int count = 1; count <= steps.Count; count++)
        {
            var partial = PlanReplay.Replay(state, new(state.StateId, steps.Take(count)));
            peak = Math.Max(peak, partial.Items.Count(i => i.Location.InventoryId == state.Roots.Ground));
        }
        return peak;
    }
}
