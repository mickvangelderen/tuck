using global::Tuck.PackSolver;

namespace Tuck.PackSolver.Tests;

[Trait("Category", "RecordedGame")]
[Collection("Recorded packing budgets")]
public class RecordedPackTests
{
    public static IEnumerable<object[]> Cases() =>
        RecordedPackCases.All.Keys.Select(name => new object[] { name });

    public static IEnumerable<object[]> SolverCases() =>
        from solver in SolverUnderTest.Discover()
        from name in RecordedPackCases.All.Keys
        select new object[] { solver.FullName!, name };

    [Theory]
    [MemberData(nameof(SolverCases))]
    [Trait("Category", "SolverContract")]
    public void ReturnsExpectedValue(string solverName, string caseName)
    {
        var test = RecordedPackCases.All[caseName];
        var solver = SolverUnderTest.Create(solverName);
        // Match PackEngine's default work limits and cooperative deadline.
        var options = new PlanningOptions();
        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        // Also validates plan replay, item conservation and the reported value.
        var result = SolverUnderTest.Solve(solver, new(test.State), options, budget.Token);
        Assert.True(result.FinalValue >= test.MinimumReturnedValue,
            $"{caseName}: expected at least {test.MinimumReturnedValue}, returned {result.FinalValue} ({result.Status}).");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ExpectedValueHasALegalWitness(string caseName)
    {
        var test = RecordedPackCases.All[caseName];
        var final = PlanReplay.Replay(test.State, new(test.State.StateId, test.Witness));
        Assert.Equal(test.MinimumReturnedValue, PlanReplay.ReturnedValue(final));
    }

}

// Keep other test classes from competing for CPU during the game's time budget.
[CollectionDefinition("Recorded packing budgets", DisableParallelization = true)]
public class RecordedPackingBudgetCollection;
