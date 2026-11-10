using System.Threading;

namespace Tuck.PackSolver;

/// <summary>Computes an executable packing plan from a detached inventory capture.</summary>
/// <remarks>
/// <para>
/// The dedicated problem and result models let alternative algorithms be developed and evaluated
/// side by side in this repository. Each implementation solves the same problem independently;
/// selecting an implementation does not require chaining competing solvers as fallbacks.
/// Keep implementations under Solvers, with their own search representations, heuristics and caches.
/// </para>
/// <para>
/// Share game semantics rather than search internals: use TransferSimulation for legal transitions
/// and the shared value calculation for scoring. Every returned plan must replay through
/// <see cref="PlanReplay.Replay"/> against the input capture. Solving must not mutate that capture
/// or execute transfers in the live game. Keep mutable search state local to each invocation so
/// implementations can be reused for independent runs.
/// Reuse semantic placement enumeration, validated successor construction, and optional compiled
/// invariant facts where useful. Search ordering stays solver-specific. Fixed identities and counts
/// permit exact location-only keys; a hash alone is never sufficient state equality. Public models
/// need not carry a solver's indexes or caches to make its inner loop efficient.
/// </para>
/// <para>
/// Packing moves existing items intact, including stacks prepared by the user. Every step must
/// name an existing item and its destination; the captured count travels with that item, so no
/// quantity is stored in the output step. Merging stacks, splitting quantities,
/// creating or consuming items, and transferring liquid or consumable contents are outside this
/// interface. Item identities, counts and intrinsic facts must survive unchanged. Placements and
/// containment may change only through root inventories and explicitly modeled container/toolbox
/// storage. Equipment sockets and unclassified inventories retain their contents: do not install,
/// remove or rearrange batteries, machine modules or gun attachments. Equipment may travel intact
/// with those contents when otherwise legal. Stack preparation and equipment configuration are
/// the user's job. This removes quantity partitions and equipment configurations from the search.
/// The models omit factory defaults, merge destinations and quantity choices. TransferSimulation
/// and plan replay enforce intact-item movement for every solver.
/// </para>
/// <para>
/// Compare solutions by returned value first. Relaxed A* and Anytime then prefer fewer occupied hands,
/// larger equipped-backpack capacity, and fewer transfers; Baseline still uses
/// transfer count alone at equal value. Compactness does not prescribe container nesting. An empty
/// plan is a valid baseline; interruption or exhausted search budgets should preserve the best
/// feasible plan found. A result need not be globally optimal, and failure to find an improvement
/// within a budget is not proof that none exists.
/// </para>
/// <para>
/// Evaluate alternatives on the same captures using replay validity, returned value, transfer
/// count, elapsed time and memory. Work counters remain useful diagnostics, but equal node or
/// placement-check limits need not represent equal work across different algorithms.
/// </para>
/// </remarks>
public interface IPackSolver
{
    /// <summary>Searches for an improved packing and returns its explicit transfer sequence.</summary>
    /// <param name="problem">The shared input capture, left unchanged by the search.</param>
    /// <param name="options">Search limits, or null to use the default limits.</param>
    /// <param name="cancellationToken">Requests interruption and return of a feasible incumbent.</param>
    /// <returns>
    /// A replayable plan and its termination status. Unsupported state rules may prevent valuation;
    /// in that case return an empty plan with <see cref="PlanningStatus.Unsupported"/> and diagnostics.
    /// </returns>
    PlanningResult Solve(PackProblem problem, PlanningOptions? options = null,
        CancellationToken cancellationToken = default);
}
