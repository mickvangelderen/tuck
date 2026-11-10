using System.Threading;
using global::Tuck.PackSolver.Solvers;

namespace Tuck.PackSolver;

/// <summary>The production entry point using relaxed A*. Alternative solvers can be invoked directly through <see cref="IPackSolver"/>.</summary>
public static class PackSolver
{
    private static readonly IPackSolver DefaultSolver = new RelaxedAStarPackSolver();

    public static PlanningResult Solve(PackProblem problem, PlanningOptions? options = null,
        CancellationToken cancellationToken = default) => DefaultSolver.Solve(problem, options, cancellationToken);
}
