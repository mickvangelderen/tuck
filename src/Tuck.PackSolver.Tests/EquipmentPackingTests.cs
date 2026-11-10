using global::Tuck.PackSolver;
using static Tuck.PackSolver.Tests.PackSolverTests;

namespace Tuck.PackSolver.Tests;

public class EquipmentPackingTests
{
    [Theory]
    [SolverData(false)]
    [SolverData(true)]
    [Trait("Category", "SolverContract")]
    public void DoesNotUseEquipmentSocketsAsSpareStorage(string solverName, bool grid)
    {
        var state = State([Item("machine", "left", 100), Item("fixed", "right", 1, tags: ["PINNED_TAG"]),
            Item("loot", "ground", 50)], Inventories().Append(Equipment(grid)).ToArray());
        var result = SolverUnderTest.Solve(solverName, new(state));
        Assert.Equal(101, result.FinalValue);
        Assert.Empty(result.Plan.Steps);
    }

    [Theory]
    [SolverData("battery")]
    [SolverData("evaporator_module_solvent_recovery")]
    [SolverData("stock")]
    [Trait("Category", "SolverContract")]
    public void DoesNotRemoveInstalledComponents(string solverName, string identifier)
    {
        var state = State([Item("machine", "ground", 100, tags: ["PINNED_TAG"]),
            Item("component", "equipment", 50, identifier: identifier)], Inventories().Append(Equipment(true)).ToArray());
        var result = SolverUnderTest.Solve(solverName, new(state));
        Assert.Equal(0, result.FinalValue);
        Assert.Empty(result.Plan.Steps);
    }

    [Theory]
    [SolverData(false)]
    [SolverData(true)]
    [Trait("Category", "SolverContract")]
    public void CarriesTheWholeMachineWithItsInstalledModule(string solverName, bool stuck)
    {
        var state = State([Item("machine", "ground", 100), Item("component", "equipment", 50,
            identifier: "evaporator_module_solvent_recovery", tags: stuck ? ["MODULE_STUCK_TAG"] : [])],
            Inventories().Append(Equipment(true)).ToArray());
        var result = SolverUnderTest.Solve(solverName, new(state));
        Assert.Equal(150, result.FinalValue);
        var final = PlanReplay.Replay(state, result.Plan);
        Assert.True(PlanReplay.IsReturned(final, "machine"));
        Assert.Equal("equipment", final.Items.Single(i => i.Id == "component").Location.InventoryId);
    }

    [Theory]
    [SolverData("battery")]
    [SolverData("evaporator_module_solvent_recovery")]
    [SolverData("stock")]
    [Trait("Category", "SolverContract")]
    public void LooseComponentsCanStillBePackedInOrdinaryStorage(string solverName, string identifier)
    {
        var state = State([
            Item("bag", "back", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("left", "left", 1, tags: ["PINNED_TAG"]), Item("right", "right", 1, tags: ["PINNED_TAG"]),
            Item("component", "ground", 50, identifier: identifier)
        ], Inventories().Append(new InventoryState("inside", new InventoryLayout.Grid(ShapeState.Rectangle(1, 1, 0, 1)),
            new(AdmissionKind.Container), "bag")).ToArray());
        var result = SolverUnderTest.Solve(solverName, new(state));
        Assert.Equal(53, result.FinalValue);
        Assert.Equal("inside", PlanReplay.Replay(state, result.Plan).Items.Single(i => i.Id == "component").Location.InventoryId);
    }

    [Fact]
    [Trait("Category", "PackingPolicy")]
    public void ReplayRejectsInsertionRemovalAndRearrangementWithinEquipment()
    {
        var state = State([Item("machine", "ground", 100), Item("component", "equipment", 50),
            Item("loose", "ground", 10, 1)], Inventories().Append(Equipment(true)).ToArray());
        foreach (var step in new[] { Place("component", "left"), Place("component", "equipment", placement: new Placement.Grid(1)),
                     Place("loose", "equipment", placement: new Placement.Grid(1)) })
        {
            var error = Assert.Throws<InvalidOperationException>(() => PlanReplay.Replay(state, new(state.StateId, [step])));
            Assert.Contains("equipment", error.Message);
        }
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void PacksAnIrregularShapeIntoAnOrdinaryMaskedGrid(string solverName)
    {
        var state = State([
            Item("bag", "back", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("left", "left", 1, tags: ["PINNED_TAG"]), Item("right", "right", 1, tags: ["PINNED_TAG"]),
            Item("loot", "ground", 10, shape: new(2, 2, [1, 0, 1, 1]))
        ], Inventories().Append(new InventoryState("inside", new InventoryLayout.Grid(new(2, 2, [0, 1, 0, 0], 1)),
            new(AdmissionKind.Container), "bag")).ToArray());
        var result = SolverUnderTest.Solve(solverName, new(state));
        Assert.Equal(13, result.FinalValue);
        Assert.True(PlanReplay.IsReturned(PlanReplay.Replay(state, result.Plan), "loot"));
    }

    // No lock, stuck tag or unsupported callback is needed: equipment is excluded by policy.
    private static InventoryState Equipment(bool grid) => new("equipment", grid
        ? new InventoryLayout.Grid(ShapeState.Rectangle(2, 1, 0, 1)) : new InventoryLayout.Slot(),
        new(AdmissionKind.Owned), "machine");
}
