using global::Tuck.PackSolver;
using static Tuck.PackSolver.Tests.PackSolverTests;

namespace Tuck.PackSolver.Tests;

public class CounterPackingTests
{
    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void CollectsBoughtMachinesFromBothCountersWithEmptyGroundAndHands(string solverName)
    {
        var stock = new ItemState("stock", "stock", new("front", new Placement.Grid(2)), 1, 5000,
            ShapeState.Rectangle(1, 1), new(types: ["MACHINE"]));
        var state = State([
            Item("machine1", "front", 100, types: ["MACHINE"]),
            Item("machine2", "front", 400, 1, types: ["MACHINE"]),
            Item("machine3", "back-counter", 300, types: ["MACHINE"]),
            Item("machine4", "back-counter", 200, 1, types: ["MACHINE"]),
            Item("pinned", "back-counter", 6000, 2, tags: ["PINNED_TAG"], types: ["MACHINE"]),
            stock
        ], Inventories().Concat([Counter("front"), Counter("back-counter")]).ToArray());

        var result = SolverUnderTest.Solve(solverName, new(state));
        var final = PlanReplay.Replay(state, result.Plan);
        Assert.Equal(0, result.InitialValue);
        Assert.Equal(700, result.FinalValue);
        Assert.True(PlanReplay.IsReturned(final, "machine2"));
        Assert.True(PlanReplay.IsReturned(final, "machine3"));
        Assert.Equal(stock.Location, final.Items.Single(i => i.Id == "stock").Location);
        Assert.Equal("back-counter", final.Items.Single(i => i.Id == "pinned").Location.InventoryId);
        Assert.DoesNotContain(result.Plan.Steps, step => step.Destination.InventoryId is "front" or "back-counter");
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void CarriesCounterMachineWithInstalledEquipmentAndPacksLoosePurchasesInBag(string solverName)
    {
        var state = State([
            Item("bag", "back", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("machine", "counter", 400, types: ["MACHINE"], tags: ["CONTAINER_TAG"]),
            new("installed", "battery", new("socket", new Placement.Slot()), 1, 20, ShapeState.Rectangle(1, 1), Facts()),
            Item("loose-module", "counter", 30, 1),
            Item("other-machine", "counter", 500, 2, types: ["MACHINE"])
        ], Inventories().Concat([
            Counter("counter"),
            new InventoryState("bag-grid", new InventoryLayout.Grid(ShapeState.Rectangle(2, 2, 0, 1)), new(AdmissionKind.Container), "bag"),
            new InventoryState("socket", new InventoryLayout.Slot(), new(AdmissionKind.Unsupported), "machine",
                unsupportedTransfer: "Equipment is not packing storage.")
        ]).ToArray());

        var result = SolverUnderTest.Solve(solverName, new(state));
        var final = PlanReplay.Replay(state, result.Plan);
        Assert.Equal(951, result.FinalValue);
        Assert.True(PlanReplay.IsReturned(final, "machine"));
        Assert.Equal("socket", final.Items.Single(i => i.Id == "installed").Location.InventoryId);
        Assert.Equal("bag-grid", final.Items.Single(i => i.Id == "loose-module").Location.InventoryId);
    }

    [Fact]
    public void CountersCannotBeUsedAsPackingDestinations()
    {
        var state = State([Item("held", "left", 5)], Inventories().Append(Counter("counter")).ToArray());
        Assert.Throws<InvalidOperationException>(() => PlanReplay.Replay(state,
            new(state.StateId, [Place("held", "counter")])));
    }

    [Fact]
    public void CounterAreaIsNotCountedAsCarryCapacityByTheRelaxedBound()
    {
        var state = State([
            Item("bag", "back", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("machine1", "counter", 100, types: ["MACHINE"]),
            Item("machine2", "counter", 90, 1, types: ["MACHINE"]),
            Item("machine3", "counter", 80, 2, types: ["MACHINE"])
        ], Inventories().Concat([
            Counter("counter"),
            new InventoryState("bag-grid", new InventoryLayout.Grid(ShapeState.Rectangle(10, 10, 0, 1)), new(AdmissionKind.Container), "bag")
        ]).ToArray());
        var simulation = new TransferSimulation(state);
        Assert.True(global::Tuck.PackSolver.Solvers.RelaxedPackingBound.TryCreate(simulation,
            state.Items.Select(i => i.Id).ToArray(), default, out long bound, out _));
        Assert.Equal(191, bound);
    }

    private static InventoryState Counter(string id) => new(id,
        new InventoryLayout.Grid(ShapeState.Rectangle(4, 2, 0, 1)), new(AdmissionKind.SourceOnly));
}
