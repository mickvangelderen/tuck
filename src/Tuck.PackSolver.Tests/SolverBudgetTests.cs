using global::Tuck.PackSolver;
using static Tuck.PackSolver.Tests.PackSolverTests;

namespace Tuck.PackSolver.Tests;

[Trait("Category", "SolverContract")]
public class SolverBudgetTests
{
    [Theory]
    [SolverData(0)]
    [SolverData(100)]
    public void NoPlacementBudgetLeavesTheLoadoutUntouched(string solverName, int nodes)
    {
        var state = State([Item("held", "left", 10), Item("loot", "ground", 20)]);
        var result = SolverUnderTest.Solve(solverName, new(state), new(nodes, 0));

        Assert.Equal(PlanningStatus.SearchLimit, result.Status);
        Assert.Equal(10, result.FinalValue);
        Assert.Empty(result.Plan.Steps);
    }

    [Theory]
    [SolverData(0, 100)]
    [SolverData(1, 1)]
    [SolverData(32, 300)]
    public void LimitedSearchPreservesAFeasibleLoadout(string solverName, int nodes, int checks)
    {
        var state = State([
            Item("held", "left", 100), Item("valuable", "ground", 20), Item("cheap", "ground", 10, 1)
        ]);
        // Node budgets have different meanings across implementations. Validate the
        // returned loadout, not which search expansion or direct pickup produced it.
        var result = SolverUnderTest.Solve(solverName, new(state), new(nodes, checks));
        var final = PlanReplay.Replay(state, result.Plan);

        Assert.NotEqual(PlanningStatus.Unsupported, result.Status);
        Assert.True(PlanReplay.IsReturned(final, "held"));
        Assert.InRange(result.FinalValue!.Value, 100, 120);
    }

    [Theory]
    [SolverData]
    public void KeepsCollectedLootWhenFurtherSearchIsLimited(string solverName)
    {
        var state = State([Item("a", "ground", 10), Item("b", "ground", 20, 1)]);
        var result = SolverUnderTest.Solve(solverName, new(state), new(maxPlacementChecks: 1));

        Assert.Equal(PlanningStatus.SearchLimit, result.Status);
        Assert.True(result.FinalValue > result.InitialValue);
        Assert.Contains(PlanReplay.Replay(state, result.Plan).Items,
            i => i.Location.InventoryId is "left" or "right");
    }
}
