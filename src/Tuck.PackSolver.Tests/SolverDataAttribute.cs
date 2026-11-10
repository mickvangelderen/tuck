using System.Reflection;
using global::Tuck.PackSolver;
using Xunit.Sdk;

namespace Tuck.PackSolver.Tests;

/// <summary>Runs the same contract case against each concrete IPackSolver, with its name in the test result.</summary>
/// <remarks>
/// New solvers with public parameterless constructors are discovered automatically. No production
/// default or solver implementation needs editing to select a test subject. By default all solvers
/// run. To run just one implementation:
/// dotnet test Tuck.slnx --filter Category=SolverContract --environment PACK_SOLVER=AnytimePackSolver
/// PACK_SOLVER also accepts a fully qualified type name. An unknown selection fails discovery rather
/// than silently running zero cases. Shared cases assert outcomes; budget limits do not imply
/// identical search work or exploration order across implementations.
/// Simulator, serialization-only and algorithm-internal tests do not use this attribute.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class SolverDataAttribute(params object[] arguments) : DataAttribute
{
    // Split a solver into its own data attribute to give only its rows a Skip reason.
    public Type? Only { get; set; }
    public Type? Except { get; set; }

    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        string? selected = Environment.GetEnvironmentVariable("PACK_SOLVER");
        var solvers = typeof(IPackSolver).Assembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && !t.ContainsGenericParameters && typeof(IPackSolver).IsAssignableFrom(t))
            .Where(t => string.IsNullOrWhiteSpace(selected) || t.Name == selected || t.FullName == selected)
            .OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();
        if (solvers.Length == 0) throw new InvalidOperationException($"No IPackSolver matches PACK_SOLVER='{selected}'.");
        foreach (var solver in solvers.Where(t => (Only == null || t == Only) && t != Except))
        {
            if (solver.GetConstructor(Type.EmptyTypes) == null)
                throw new InvalidOperationException($"{solver.FullName} needs a public parameterless constructor for solver contract tests.");
            yield return new object[] { solver.FullName! }.Concat(arguments).ToArray();
        }
    }
}

internal static class SolverUnderTest
{
    internal static IPackSolver Create(string solverName)
    {
        var type = typeof(IPackSolver).Assembly.GetType(solverName, throwOnError: true)!;
        return (IPackSolver)Activator.CreateInstance(type)!;
    }

    /// <summary>Common validation in addition to each case's behavioral and quality assertions.</summary>
    internal static PlanningResult Solve(string solverName, PackProblem problem, PlanningOptions? options = null,
        CancellationToken cancellationToken = default) => Solve(Create(solverName), problem, options, cancellationToken);

    internal static PlanningResult Solve(IPackSolver solver, PackProblem problem, PlanningOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var before = new PackState(problem.State.StateId, problem.State.Roots, problem.State.Items,
            problem.State.Inventories, problem.State.Definitions.Values);
        var result = solver.Solve(problem, options, cancellationToken);
        Assert.Null(StateComparison.Difference(before, problem.State));
        var originals = before.Items.ToDictionary(i => i.Id);
        Assert.All(result.Plan.Steps, step =>
        {
            Assert.True(originals.ContainsKey(step.SourceItemId), "Packing cannot create item identities.");
        });
        var final = PlanReplay.Replay(problem.State, result.Plan);
        Assert.Equal(originals.Keys.OrderBy(id => id), final.Items.Select(i => i.Id).OrderBy(id => id));
        foreach (var item in final.Items)
        {
            var original = originals[item.Id];
            Assert.Equal(original.Identifier, item.Identifier);
            Assert.Equal(original.UnitCount, item.UnitCount);
            Assert.Equal(original.UnitValue, item.UnitValue);
            Assert.Equal(original.UnitBaseValue, item.UnitBaseValue);
            Assert.True(StateComparison.SameShape(original.Shape, item.Shape));
            Assert.True(StateComparison.SameFacts(original.State, item.State));
        }
        if (result.Status == PlanningStatus.Unsupported)
        {
            Assert.Empty(result.Plan.Steps);
            Assert.NotEmpty(result.UnsupportedRules);
        }
        else
        {
            Assert.Equal(PlanReplay.ReturnedValue(problem.State), result.InitialValue);
            Assert.Equal(PlanReplay.ReturnedValue(final), result.FinalValue);
            Assert.True(result.FinalValue >= result.InitialValue, "A solver must retain a feasible incumbent at least as valuable as the input.");
            if (result.Metrics?.RelaxedValueBound is long bound)
                Assert.True(bound >= result.FinalValue, $"The reported upper bound {bound} is below the replayed value {result.FinalValue}.");
        }
        return result;
    }

}
