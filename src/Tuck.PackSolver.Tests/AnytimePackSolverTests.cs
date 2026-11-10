using global::Tuck.PackSolver;
using global::Tuck.PackSolver.Solvers;
using static Tuck.PackSolver.Tests.PackSolverTests;

namespace Tuck.PackSolver.Tests;

public class AnytimePackSolverTests
{
    // Unsupported geometry and exact-limit completion are specific to this implementation.
    // Packing outcomes and safe budget exhaustion are covered by the shared solver tests.
    private readonly IPackSolver _solver = new AnytimePackSolver();

    [Fact]
    public void UnboundedEmptyFootprintsAreExplicitlyUnsupported()
    {
        var state = State([Item("empty", "ground", 10, shape: ShapeState.Rectangle(1, 1, 0))]);
        var result = Solve(state);
        Assert.Equal(PlanningStatus.Unsupported, result.Status);
        Assert.Single(result.UnsupportedRules);
        Assert.Empty(result.Plan.Steps);
    }

    [Fact]
    public void FinishingAtTheCheckLimitDoesNotReportUnfinishedSearch()
    {
        var result = Solve(State([Item("loot", "ground", 10)]), new(maxPlacementChecks: 1));
        Assert.Equal(PlanningStatus.Improved, result.Status);
        Assert.Equal(10, result.FinalValue);
    }

    private PlanningResult Solve(PackState state, PlanningOptions? options = null) =>
        SolverUnderTest.Solve(_solver, new(state), options);
}
