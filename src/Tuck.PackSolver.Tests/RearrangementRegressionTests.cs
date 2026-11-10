using global::Tuck.PackSolver;
using static Tuck.PackSolver.Tests.PackSolverTests;

namespace Tuck.PackSolver.Tests;

public class RearrangementRegressionTests
{
    [Fact]
    public void ResolvesAnotherCycleToCreateSpaceForTheFirstCycle()
    {
        var inventories = Inventories();
        inventories[0] = Ground(3);
        var state = State([
            Item("a", "left", 10, shape: ShapeState.Rectangle(2, 1)),
            Item("b", "right", 10, shape: ShapeState.Rectangle(2, 1)),
            Item("c", "ground", 2, shape: ShapeState.Rectangle(2, 1)),
            Item("d", "inside", 1),
            Item("bag", "back", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG"])
        ], inventories.Append(new InventoryState("inside", new InventoryLayout.Grid(ShapeState.Rectangle(2, 1, 0, 1)),
            new(AdmissionKind.Container), "bag")).ToArray());
        TransferStep[] targets = [Place("a", "right"), Place("b", "left"), Place("c", "inside"), Place("d", "ground")];

        // The hand-swap cycle is listed first but has no 2x1 buffer. The separate c/d cycle
        // can use the one free cell; resolving it frees two adjacent cells for the hand swap.
        var witness = Replay(state,
        [
            Place("d", "ground", placement: new Placement.Grid(2)), targets[2], targets[3],
            Place("a", "ground", placement: new Placement.Grid(1)), targets[1], targets[0]
        ]);
        AssertTargets(witness, targets);

        var simulation = new TransferSimulation(state);
        var search = new SequenceSearch(targets.ToDictionary(t => t.SourceItemId), [], new(), default);
        var steps = search.Run(simulation);
        Assert.Null(StateComparison.Difference(state, simulation.Snapshot()));
        Assert.NotNull(steps);
        AssertTargets(Replay(state, steps), targets);
    }

    [Theory]
    [InlineData(64, 2000)]
    [InlineData(5000, 200000)]
    public void MovesGroundClutterToMakeRoomForAHandSwap(int nodes, int checks)
    {
        var inventories = Inventories();
        inventories[0] = Ground(3);
        var state = State([
            Item("a", "left", 10, shape: ShapeState.Rectangle(2, 1)),
            Item("b", "right", 10, shape: ShapeState.Rectangle(2, 1)),
            Item("clutter", "ground", 1, 1)
        ], inventories);
        TransferStep[] targets = [Place("a", "right"), Place("b", "left")];
        // Ground starts [. C .]. C blocks no hand destination, but moving it to [C . .]
        // creates the only 2x1 buffer. Clutter has no required final placement.
        var witness = new List<TransferStep>
        {
            Place("clutter", "ground"),
            Place("a", "ground", placement: new Placement.Grid(1)),
            Place("b", "left"), Place("a", "right")
        };
        AssertTargets(Replay(state, witness), targets);

        var simulation = new TransferSimulation(state);
        var search = new SequenceSearch(targets.ToDictionary(t => t.SourceItemId), [], new(nodes, checks), default);
        var steps = search.Run(simulation);
        Assert.Null(StateComparison.Difference(state, simulation.Snapshot()));
        Assert.False(search.Limited);
        Assert.NotNull(steps);
        AssertTargets(Replay(state, steps), targets);
    }

    [Theory]
    [InlineData(64, 2000)]
    [InlineData(5000, 200000)]
    public void RearrangesGroundBeforeDiscardingAnOccupiedHand(int nodes, int checks)
    {
        var inventories = Inventories();
        inventories[0] = Ground(5);
        var state = State([
            Item("loot", "ground", 20, shape: ShapeState.Rectangle(2, 1)),
            Item("clutter", "ground", 1, 3),
            Item("cheap", "left", 2, shape: ShapeState.Rectangle(2, 1)),
            Item("fixed", "right", 1, tags: ["PINNED_TAG"])
        ], inventories);
        TransferStep[] targets = [Place("loot", "left")];

        // [L L . C .] has two free cells but no 2x1 discard position. Move C to the end
        // before dropping the cheap item. Discarding must use real space, not delete it.
        var witness = Replay(state,
        [
            Place("clutter", "ground", placement: new Placement.Grid(4)),
            Place("cheap", "ground", placement: new Placement.Grid(2)),
            targets[0]
        ]);
        AssertTargets(witness, targets);
        Assert.Equal("ground", witness.Items.Single(i => i.Id == "cheap").Location.InventoryId);

        var simulation = new TransferSimulation(state);
        var search = new SequenceSearch(targets.ToDictionary(t => t.SourceItemId), ["cheap"], new(nodes, checks), default);
        var steps = search.Run(simulation);
        Assert.Null(StateComparison.Difference(state, simulation.Snapshot()));
        Assert.False(search.Limited);
        Assert.NotNull(steps);
        var final = Replay(state, steps);
        AssertTargets(final, targets);
        Assert.Equal("ground", final.Items.Single(i => i.Id == "cheap").Location.InventoryId);
        Assert.Equal(21, PlanReplay.ReturnedValue(final));
    }

    private static InventoryState Ground(int width) => new("ground",
        new InventoryLayout.Grid(ShapeState.Rectangle(width, 1, 0, 1)), new(AdmissionKind.Ground));

    private static PackState Replay(PackState initial, IEnumerable<TransferStep> steps)
    {
        var final = PlanReplay.Replay(initial, new(initial.StateId, steps));
        // Every original instance survives, including items left on the ground.
        Assert.Equal(initial.Items.OrderBy(i => i.Id).Select(i => (i.Id, i.UnitCount)),
            final.Items.OrderBy(i => i.Id).Select(i => (i.Id, i.UnitCount)));
        Assert.Equal(Quantities(initial), Quantities(final));
        return final;
    }

    private static IEnumerable<(string Identifier, int Count)> Quantities(PackState state) =>
        state.Items.GroupBy(i => i.Identifier).Select(g => (g.Key, g.Sum(i => i.UnitCount))).OrderBy(g => g.Key);

    private static void AssertTargets(PackState final, IEnumerable<TransferStep> targets)
    {
        foreach (var target in targets)
            Assert.Equal(target.Destination,
                final.Items.Single(i => i.Id == target.SourceItemId).Location);
    }
}
