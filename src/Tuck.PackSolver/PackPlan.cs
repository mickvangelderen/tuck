using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Tuck.PackSolver;

/// <summary>A detached capture shared by all solvers, with stable identities for replay.</summary>
/// <remarks>
/// Solvers may compile invariant facts into invocation-local tables and encode search states as
/// locations in those tables. Those indexes and caches are implementation details: the public model
/// retains the captured facts and identities needed to validate and execute a plan independently.
/// </remarks>
public sealed class PackProblem
{
    public PackState State { get; }
    public PackProblem(PackState state) => State = state ?? throw new ArgumentNullException(nameof(state));
}

/// <summary>
/// Places an existing item intact. Its captured identity, count and intrinsic facts are preserved.
/// Quantity is deliberately absent: packing chooses locations, while stack preparation belongs to
/// the user. A destination is a location, so merges and partial transfers cannot be represented.
/// </summary>
public sealed class TransferStep
{
    public string SourceItemId { get; }
    public ItemLocation Destination { get; }
    public TransferStep(string sourceItemId, ItemLocation destination)
    {
        if (string.IsNullOrWhiteSpace(sourceItemId)) throw new ArgumentException("Missing source item identity.", nameof(sourceItemId));
        SourceItemId = sourceItemId; Destination = destination ?? throw new ArgumentNullException(nameof(destination));
    }
}

public sealed class PackPlan
{
    public string InitialStateId { get; }
    public ReadOnlyCollection<TransferStep> Steps { get; }
    public PackPlan(string initialStateId, IEnumerable<TransferStep> steps)
    { InitialStateId = initialStateId; Steps = Array.AsReadOnly(steps.ToArray()); }
}

public sealed class PlanningOptions
{
    public int MaxSequenceNodes { get; }
    public int MaxPlacementChecks { get; }
    public int MaxContainerArrangements { get; }
    public PlanningOptions(int maxSequenceNodes = 5000, int maxPlacementChecks = 200000, int maxContainerArrangements = 128)
    {
        if (maxSequenceNodes < 0 || maxPlacementChecks < 0 || maxContainerArrangements < 0)
            throw new ArgumentOutOfRangeException(nameof(maxSequenceNodes));
        MaxSequenceNodes = maxSequenceNodes; MaxPlacementChecks = maxPlacementChecks;
        MaxContainerArrangements = maxContainerArrangements;
    }
}

public enum PlanningStatus { Improved, Unchanged, SearchLimit, Interrupted, Unsupported }

/// <summary>Optional per-invocation work counters. Meanings are documented by the emitting solver.</summary>
public sealed class PlanningMetrics
{
    public int ExpandedNodes { get; }
    public int PlacementChecks { get; }
    public int UniqueStates { get; }
    public int DuplicateStates { get; }
    public int HeuristicEvaluations { get; }
    /// <summary>
    /// Optional upper bound on reachable returned value, not an estimate of a feasible layout.
    /// It must cover the initial state and every legal result, including contents carried inside
    /// an acquired container. Reaching it certifies value only, not minimum transfer count.
    /// </summary>
    public long? RelaxedValueBound { get; }
    internal PlanningMetrics(int expanded, int checks, int unique, int duplicates, int estimates, long? bound)
    { ExpandedNodes = expanded; PlacementChecks = checks; UniqueStates = unique; DuplicateStates = duplicates; HeuristicEvaluations = estimates; RelaxedValueBound = bound; }
}

public sealed class PlanningResult
{
    public PackPlan Plan { get; }
    public PlanningStatus Status { get; }
    public long? InitialValue { get; }
    public long? FinalValue { get; }
    public ReadOnlyCollection<string> UnsupportedRules { get; }
    public PlanningMetrics? Metrics { get; }
    internal PlanningResult(PackPlan plan, PlanningStatus status, long? initialValue, long? finalValue, IEnumerable<string> unsupported, PlanningMetrics? metrics = null)
    { Plan = plan; Status = status; InitialValue = initialValue; FinalValue = finalValue; UnsupportedRules = Array.AsReadOnly(unsupported.ToArray()); Metrics = metrics; }
}
