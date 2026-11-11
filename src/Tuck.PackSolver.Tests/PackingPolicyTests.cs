using global::Tuck.PackSolver;
using static Tuck.PackSolver.Tests.PackSolverTests;

namespace Tuck.PackSolver.Tests;

[Trait("Category", "PackingPolicy")]
public class PackingPolicyTests
{
    [Theory]
    [SolverData(false)]
    [SolverData(true)]
    [Trait("Category", "SolverContract")]
    public void StuckItemsStayInPlaceRegardlessOfItemType(string solverName, bool stuck)
    {
        var state = State([Item("ordinary_item", "ground", 70, tags: stuck ? ["MODULE_STUCK_TAG"] : [])]);
        var result = SolverUnderTest.Solve(solverName, new(state));
        Assert.Equal(stuck ? 0 : 70, result.FinalValue);
        if (stuck) Assert.Empty(result.Plan.Steps);
        else Assert.True(PlanReplay.IsReturned(PlanReplay.Replay(state, result.Plan), "ordinary_item"));
    }

    [Theory]
    [SolverData(false)]
    [SolverData(true)]
    [Trait("Category", "SolverContract")]
    public void LeavesAnOversizedStackIntactEvenWhenAHandCouldBeCleared(string solverName, bool occupied)
    {
        var inventories = Inventories();
        inventories[2] = new("left", new InventoryLayout.Slot(), new(AdmissionKind.Owned), quantity: new(3));
        var items = new List<ItemState>
        {
            Item("source", "ground", 10, count: 5, identifier: "coins"),
            Item("fixed", "right", 1, tags: ["PINNED_TAG"])
        };
        if (occupied) items.Add(Item("cheap", "left", 5));
        var state = State(items.ToArray(), inventories, new ItemDefinition("coins", new(new(10))));
        var result = SolverUnderTest.Solve(solverName, new(state));

        Assert.Equal(occupied ? 6 : 1, result.FinalValue);
        Assert.Empty(result.Plan.Steps);
    }

    [Theory]
    [SolverData(1, "prepared-stack", 10)]
    [SolverData(8, "prepared-stack", 10)]
    [SolverData(2, "$split:0", 2)]
    [Trait("Category", "SolverContract")]
    public void PacksAnExistingStackIntactIncludingLegacyIds(string solverName, int count, string id, int maximum)
    {
        var inventories = Inventories();
        inventories[0] = new("ground", new InventoryLayout.Grid(ShapeState.Rectangle(1, 1, 0, 1)), new(AdmissionKind.Ground));
        inventories[2] = new("left", new InventoryLayout.Slot(), new(AdmissionKind.Owned), quantity: new(maximum));
        inventories[3] = new("right", new InventoryLayout.Slot(), new(AdmissionKind.Owned), quantity: new(1));
        var state = State([Item(id, "ground", 7, count: count, identifier: "coins")], inventories,
            new ItemDefinition("coins", new(new(maximum))));
        var result = SolverUnderTest.Solve(solverName, new(state));

        Assert.Equal(7 * count, result.FinalValue);
        Assert.True(PlanReplay.IsReturned(PlanReplay.Replay(state, result.Plan), id));
    }

    [Theory]
    [SolverData(1, int.MaxValue, int.MaxValue)]
    [SolverData(0, 3, int.MaxValue)]
    [SolverData(0, int.MaxValue, 3)]
    [Trait("Category", "SolverContract")]
    public void LeavesStacksWhoseRulesOnlyAllowPartialTransfers(string solverName, int minimum, int maxRemove, int maxAdd)
    {
        var definition = new ItemDefinition("coins", new(new(10, minimum, maxAdd, maxRemove)));
        var state = State([Item("prepared-stack", "ground", 7, count: 5, identifier: "coins")], null, definition);
        var result = SolverUnderTest.Solve(solverName, new(state));
        Assert.Equal(0, result.FinalValue);
        Assert.Empty(result.Plan.Steps);
    }
}
