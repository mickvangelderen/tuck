using global::Tuck.PackSolver;
using static Tuck.PackSolver.Tests.PackSolverTests;

namespace Tuck.PackSolver.Tests;

public class SolverSearchTests
{
    [Theory]
    [SolverData(32, 2000)]
    [SolverData(5000, 200000)]
    [Trait("Category", "SolverContract")]
    public void SelectsTheBestSubsetAndReplacesCheapHeldItems(string solverName, int nodes, int checks)
    {
        var inventories = Inventories(9).Append(new InventoryState("inside",
            new InventoryLayout.Grid(ShapeState.Rectangle(3, 1, 0, 1)), new(AdmissionKind.Container), "bag")).ToArray();
        var items = Enumerable.Range(0, 7).Select(i => Item($"loot-{i}", "ground", (i * 17 % 23) + 1, i))
            .Concat([Item("bag", "back", 2, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]), Item("left", "left", 1), Item("right", "right", 2)]).ToArray();
        // Three grid cells and two hands admit exactly five loot items. The backpack shell adds 2.
        var result = Solve(solverName, State(items, inventories), new(nodes, checks));
        Assert.Equal(83, result.FinalValue);
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void KeepsOrientationsWhoseOtherCollisionLabelsDiffer(string solverName)
    {
        var inventories = Inventories().Append(new InventoryState("inside",
            new InventoryLayout.Grid(ShapeState.Rectangle(3, 1, 0, 1)), new(AdmissionKind.Container), "bag")).ToArray();
        var state = State([Item("loot", "ground", 50, shape: new(3, 1, new byte[] { 2, 1, 0 })),
            Item("bag", "back", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG", "PINNED_TAG"]),
            Item("left", "left", 1, tags: ["PINNED_TAG"]), Item("right", "right", 1, tags: ["PINNED_TAG"]),
            Item("obstacle", "inside", 1, shape: new(3, 1, new byte[] { 2, 0, 1 }), tags: ["PINNED_TAG"])], inventories);
        var result = Solve(solverName, state);
        Assert.Equal(54, result.FinalValue);
    }

    [Theory]
    [SolverData(false)]
    [SolverData(true)]
    [Trait("Category", "SolverContract")]
    public void PickingUpAContainerCarriesExistingContentsEvenWhenInsertionIsLocked(string solverName, bool insertLocked)
    {
        var inventories = Inventories(2).Append(new InventoryState("inside",
            new InventoryLayout.Grid(ShapeState.Rectangle(1, 1, 0, 1)), new(AdmissionKind.Container),
            "bag", overrideLockInsert: insertLocked)).ToArray();
        var state = State([Item("bag", "ground", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("valuable", "inside", 100), Item("left-pin", "left", 1, tags: ["PINNED_TAG"]),
            Item("right-pin", "right", 1, tags: ["PINNED_TAG"])], inventories);

        // An independently replayed witness establishes a reachable value. An upper bound
        // must include the contents even before their enclosing container is returned.
        long reachable = PlanReplay.ReturnedValue(PlanReplay.Replay(state, new(state.StateId, [Place("bag", "back")])));
        Assert.Equal(103, reachable);
        var result = Solve(solverName, state);
        Assert.Equal(reachable, result.FinalValue);
        var final = PlanReplay.Replay(state, result.Plan);
        Assert.True(PlanReplay.IsReturned(final, "bag"));
        Assert.Equal(state.Items.Single(i => i.Id == "valuable").Location,
            final.Items.Single(i => i.Id == "valuable").Location);
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    [Trait("Category", "PackingPolicy")]
    public void DoesNotDivideAStackAcrossTwoSmallerDestinations(string solverName)
    {
        var inventories = Inventories();
        inventories[2] = new("left", new InventoryLayout.Slot(), new(AdmissionKind.Owned), quantity: new(3));
        inventories[3] = new("right", new InventoryLayout.Slot(), new(AdmissionKind.Owned), quantity: new(3));
        var state = State([Item("source", "ground", 10, count: 4, identifier: "coins")], inventories,
            new ItemDefinition("coins", new(new(4))));
        var result = Solve(solverName, state);
        Assert.Equal(0, result.FinalValue);
        Assert.Empty(result.Plan.Steps);
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void FullGroundCannotBeUsedAsImaginaryDiscardSpace(string solverName)
    {
        var inventories = Inventories();
        inventories[0] = new("ground", new InventoryLayout.Grid(ShapeState.Rectangle(1, 1, 0, 1)), new(AdmissionKind.Ground));
        var state = State([Item("loot", "ground", 50), Item("cheap", "left", 1), Item("keep", "right", 10)], inventories);
        var result = Solve(solverName, state);
        Assert.Equal(PlanningStatus.Unchanged, result.Status);
        Assert.Equal(11, result.FinalValue);
        Assert.Empty(result.Plan.Steps);
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void AdmissionAndProtectionApplyToTemporaryTransfers(string solverName)
    {
        var state = State([Item("loot", "ground", 50), Item("waste", "left", 1, tags: ["HAZARDOUS_WASTE_TAG"]),
            Item("fixed", "right", 1, tags: ["PINNED_TAG"])]);
        var result = Solve(solverName, state);
        Assert.Equal(PlanningStatus.Unchanged, result.Status);
        Assert.Equal(2, result.FinalValue);
        Assert.Empty(result.Plan.Steps);
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void DoesNotDetachAnAttachmentToMakeItsParentFit(string solverName)
    {
        var inventories = Inventories(12).Concat([
            new InventoryState("inside", new InventoryLayout.Grid(ShapeState.Rectangle(7, 2, 0, 1)), new(AdmissionKind.Container), "bag"),
            new InventoryState("mods", new InventoryLayout.Slot(), new(AdmissionKind.Owned), "gun")]).ToArray();
        var state = State([Item("bag", "back", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("fixed-left", "left", 1, tags: ["PINNED_TAG"]), Item("fixed-right", "right", 1, tags: ["PINNED_TAG"]),
            Item("gun", "ground", 100, 2, shape: ShapeState.Rectangle(7, 2), identifier: "smg"),
            new ItemState("stock", "stock", new("mods", new Placement.Slot()), 1, 10, ShapeState.Rectangle(2, 1), Facts())],
            inventories, new ItemDefinition("stock", rules: new(parentShape: ParentShapeRule.GunAttachment)));
        var result = Solve(solverName, state);
        Assert.Equal(3, result.FinalValue);
        Assert.Empty(result.Plan.Steps);
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void PacksAContainerAndItsContents(string solverName)
    {
        var state = State([Item("bag", "ground", 2, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]), Item("loot", "ground", 50, 1),
            Item("left", "left", 1, tags: ["PINNED_TAG"]), Item("right", "right", 1, tags: ["PINNED_TAG"])],
            Inventories().Append(new InventoryState("inside", new InventoryLayout.Grid(ShapeState.Rectangle(1, 1, 0, 1)),
                new(AdmissionKind.Container), "bag")).ToArray());
        // Filling a ground container before acquiring it is legal; neither this order
        // nor an exact transfer count is required of the solver.
        var witness = new PackPlan(state.StateId, [Place("loot", "inside"), Place("bag", "back")]);
        Assert.Equal(54, PlanReplay.ReturnedValue(PlanReplay.Replay(state, witness)));
        var result = Solve(solverName, state);
        Assert.Equal(54, result.FinalValue);
        var final = PlanReplay.Replay(state, result.Plan);
        Assert.All(final.Items, i => Assert.True(PlanReplay.IsReturned(final, i.Id), i.Id));
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void ClearsGroundSpaceBeforeReplacingAHand(string solverName)
    {
        var inventories = Inventories();
        inventories[0] = new("ground", new InventoryLayout.Grid(ShapeState.Rectangle(5, 1, 0, 1)), new(AdmissionKind.Ground));
        var state = State([Item("loot", "ground", 20, shape: ShapeState.Rectangle(2, 1)),
            Item("clutter", "ground", 1, 3), Item("cheap", "left", 2, shape: ShapeState.Rectangle(2, 1)),
            Item("fixed", "right", 1, tags: ["PINNED_TAG"])], inventories);
        var result = Solve(solverName, state);
        Assert.Equal(21, result.FinalValue);
        var final = PlanReplay.Replay(state, result.Plan);
        Assert.True(PlanReplay.IsReturned(final, "loot"));
        Assert.False(PlanReplay.IsReturned(final, "cheap"));
        Assert.Equal("right", final.Items.Single(i => i.Id == "fixed").Location.InventoryId);
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    [Trait("Category", "PackingPolicy")]
    public void FreesAHandBeforePackingAnIntactStack(string solverName)
    {
        var inventories = Inventories();
        inventories[2] = new("left", new InventoryLayout.Slot(), new(AdmissionKind.Owned), quantity: new(5));
        var state = State([Item("coins", "ground", 10, count: 5), Item("cheap", "left", 5),
            Item("fixed", "right", 1, tags: ["PINNED_TAG"])], inventories,
            new ItemDefinition("coins", new(new(10))));
        var result = Solve(solverName, state);
        Assert.Equal(51, result.FinalValue);
        var final = PlanReplay.Replay(state, result.Plan);
        Assert.True(PlanReplay.IsReturned(final, "coins"));
        Assert.False(PlanReplay.IsReturned(final, "cheap"));
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    [Trait("Category", "PackingPolicy")]
    public void PacksValuableLootWhileKeepingStacksSeparate(string solverName)
    {
        var inventories = Inventories();
        inventories[2] = new("left", new InventoryLayout.Slot(), new(AdmissionKind.Owned), quantity: new(5));
        var state = State([Item("source", "ground", 10, count: 5, identifier: "coins"), Item("loot", "ground", 1000, 1),
            Item("held", "left", 10, count: 5, identifier: "coins"), Item("fixed", "right", 1, tags: ["PINNED_TAG"]),
            Item("bag", "back", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG"])],
            inventories.Append(new InventoryState("inside", new InventoryLayout.Grid(ShapeState.Rectangle(1, 1, 0, 1)),
                new(AdmissionKind.Container), "bag")).ToArray(), new ItemDefinition("coins", rules: new(new(10))));
        var result = Solve(solverName, state);
        Assert.Equal(1052, result.FinalValue);
        var final = PlanReplay.Replay(state, result.Plan);
        Assert.True(PlanReplay.IsReturned(final, "loot"));
        Assert.Equal(2, final.Items.Count(i => i.Identifier == "coins"));
        Assert.Single(final.Items, i => i.Identifier == "coins" && !PlanReplay.IsReturned(final, i.Id));
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    [Trait("Category", "PackingPolicy")]
    public void AlreadyReturnedStackNeedsNoMoves(string solverName)
    {
        var state = State([Item("source", "left", 1, count: 2, identifier: "coins")], null,
            new ItemDefinition("coins", new(new(2))));
        var result = Solve(solverName, state);
        Assert.Equal(2, result.FinalValue);
        Assert.Empty(result.Plan.Steps);
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void UnknownParentRulesReturnUnsupportedEvenWhenEverythingIsOnTheGround(string solverName)
    {
        var state = State([Item("bag", "ground", 1), Item("child", "inside", 90)],
            Inventories().Append(new InventoryState("inside", new InventoryLayout.Grid(ShapeState.Rectangle(1, 1, 0, 1)),
                new(AdmissionKind.Owned), "bag")).ToArray(), new ItemDefinition("child", rules: new(unsupportedParentState: true)));
        var result = SolverUnderTest.Solve(solverName, new(state));
        Assert.Equal(PlanningStatus.Unsupported, result.Status);
        Assert.Null(result.FinalValue);
        Assert.Empty(result.Plan.Steps);
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void CancellationAndReuseDoNotShareSearchState(string solverName)
    {
        var state = State([Item("loot", "ground", 10)]);
        var solver = SolverUnderTest.Create(solverName);
        var interrupted = SolverUnderTest.Solve(solver, new(state), cancellationToken: new(true));
        Assert.Equal(PlanningStatus.Interrupted, interrupted.Status);
        Assert.Empty(interrupted.Plan.Steps);
        var result = SolverUnderTest.Solve(solver, new(state));
        Assert.Equal(10, result.FinalValue);
        Assert.True(PlanReplay.IsReturned(PlanReplay.Replay(state, result.Plan), "loot"));
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void CannotCreateReturnSpaceByRearrangingGroundWhenBothHandsArePinned(string solverName)
    {
        var inventories = Inventories();
        inventories[0] = new("ground", new InventoryLayout.Grid(ShapeState.Rectangle(3, 1, 0, 1)), new(AdmissionKind.Ground));
        var state = State([Item("$split:0", "ground", 10), Item("loose", "ground", 1, 1),
            Item("left", "left", 1, tags: ["PINNED_TAG"]), Item("right", "right", 1, tags: ["PINNED_TAG"])], inventories);
        var result = Solve(solverName, state);

        Assert.Equal(2, result.FinalValue);
        Assert.Empty(result.Plan.Steps);
    }

    [Theory]
    [SolverData(1, 2, 3)]
    [SolverData(3, 2, 1)]
    [SolverData(2, 2, 2)]
    [Trait("Category", "SolverContract")]
    public void AgreesWithExhaustiveSearchOnSmallInventoryGraphs(string solverName, int left, int right, int ground)
    {
        var inventories = Inventories();
        inventories[0] = new("ground", new InventoryLayout.Grid(ShapeState.Rectangle(2, 1, 0, 1)), new(AdmissionKind.Ground));
        var state = State([Item("a", "left", left), Item("b", "right", right), Item("c", "ground", ground)], inventories);
        var expected = ExhaustiveWholeItems(state);
        var result = Solve(solverName, state);
        Assert.NotEqual(PlanningStatus.SearchLimit, result.Status);
        Assert.Equal(expected.Value, result.FinalValue);
        Assert.Equal(expected.Moves, result.Plan.Steps.Count);
    }

    private static PlanningResult Solve(string solverName, PackState state, PlanningOptions? options = null) =>
        SolverUnderTest.Solve(solverName, new(state), options);

    // An independent breadth-first oracle, without the solver's ordering, pruning or canonical keys.
    private static (long Value, int Moves) ExhaustiveWholeItems(PackState capture)
    {
        var start = new TransferSimulation(capture);
        var seen = new HashSet<string> { start.Key() };
        var queue = new Queue<(TransferSimulation State, int Depth)>();
        queue.Enqueue((start, 0));
        (long Value, int Moves) best = (start.ReturnedValue, 0);
        while (queue.TryDequeue(out var node))
        {
            if (node.State.ReturnedValue > best.Value) best = (node.State.ReturnedValue, node.Depth);
            foreach (var item in node.State.Items.Values)
                foreach (var inventory in node.State.Inventories.Values)
                    foreach (var position in node.State.Positions(item, inventory))
                    {
                        var next = node.State.Clone();
                        if (next.TryApply(new(item.Id, new(inventory.Id, position)), out _)
                            && seen.Add(next.Key())) queue.Enqueue((next, node.Depth + 1));
                    }
        }
        return best;
    }
}
