using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using A = global::Tuck.PackSolver.Algorithms;

namespace Tuck.PackSolver.Solvers;

/// <summary>The existing layout-and-sequence algorithm, retained as a comparison baseline.</summary>
public sealed class BaselinePackSolver : IPackSolver
{
    public PlanningResult Solve(PackProblem problem, PlanningOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (problem == null) throw new ArgumentNullException(nameof(problem));
        options ??= new PlanningOptions();
        var initial = new TransferSimulation(problem.State);
        try { foreach (var item in problem.State.Items) _ = initial.View.Effective(item.Id); }
        catch (UnsupportedRuleException error)
        { return new(new(problem.State.StateId, Array.Empty<TransferStep>()), PlanningStatus.Unsupported, null, null, new[] { error.Message }); }
        var best = initial;
        var bestSteps = new List<TransferStep>();
        bool limited = false, interrupted = false;
        try
        {
            var seed = initial.Clone();
            var seedSteps = new List<TransferStep>();
            try { AcquireDirect(seed, seedSteps, options.MaxPlacementChecks, cancellationToken); }
            finally { Accept(seed, seedSteps); }
            var working = initial.Clone();
            var prefix = new List<TransferStep>();
            var view = new AlgorithmView(working);
            var result = A.PackPlanner.Plan(view.Problem,
                new A.PackingOptions(maxPlacementChecks: options.MaxPlacementChecks,
                    maxContainerArrangements: options.MaxContainerArrangements), cancellationToken);
            limited |= result.SearchBudgetExhausted;
            if (result.Plan != null && result.Plan.TotalValue > best.ReturnedValue)
            {
                var sequence = new SequenceSearch(view.Targets(result.Plan, working), result.Plan.LeftItems.Select(i => i.Id), options, cancellationToken);
                var steps = sequence.Run(working);
                limited |= sequence.Limited;
                if (steps != null)
                {
                    foreach (var step in steps)
                        if (!working.TryApply(step, out var reason)) throw new InvalidOperationException(reason);
                    prefix.AddRange(steps);
                    Accept(working, prefix);
                }
            }
            // Useful feasible incumbent when the best target layout could not be sequenced.
            working = best.Clone();
            prefix = new List<TransferStep>(bestSteps);
            try { AcquireDirect(working, prefix, options.MaxPlacementChecks, cancellationToken); }
            finally { Accept(working, prefix); }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { interrupted = true; }
        var plan = new PackPlan(problem.State.StateId, bestSteps);
        var replay = PlanReplay.Replay(problem.State, plan);
        long finalValue = PlanReplay.ReturnedValue(replay);
        var unsupported = problem.State.Definitions.Values.Where(i => i.Rules.UnsupportedTransfer != null).Select(i => $"{i.Identifier}: {i.Rules.UnsupportedTransfer}")
            .Concat(problem.State.Inventories.Where(i => i.UnsupportedTransfer != null).Select(i => $"{i.Id}: {i.UnsupportedTransfer}"));
        return new PlanningResult(plan, interrupted ? PlanningStatus.Interrupted : limited ? PlanningStatus.SearchLimit
            : finalValue > initial.ReturnedValue ? PlanningStatus.Improved : PlanningStatus.Unchanged,
            initial.ReturnedValue, finalValue, unsupported);

        void Accept(TransferSimulation state, List<TransferStep> steps)
        {
            if (state.ReturnedValue > best.ReturnedValue || state.ReturnedValue == best.ReturnedValue && steps.Count < bestSteps.Count)
            { best = state.Clone(); bestSteps = new List<TransferStep>(steps); }
        }
    }

    private static void AcquireDirect(TransferSimulation state, List<TransferStep> steps, int budget, CancellationToken cancellation)
    {
        foreach (string id in state.Items.Values.Where(i => !state.IsReturned(i.Id)).OrderByDescending(i => state.View.PackUnitValue(i.Id) * i.UnitCount)
                     .ThenBy(i => i.Id, StringComparer.Ordinal).Select(i => i.Id).ToArray())
        {
            cancellation.ThrowIfCancellationRequested();
            if (!state.Items.TryGetValue(id, out var item) || state.IsReturned(id)) continue;
            bool moved = false;
            foreach (var inventory in state.Inventories.Values.Where(i => state.Roots.IsReturn(i.Id)
                         || i.OwnerItemId != null && state.IsReturned(i.OwnerItemId)))
            {
                if (!state.Admits(item, inventory)) continue;
                int quantity = Math.Min(state.Removable(item), state.Addable(item, inventory));
                if (quantity != item.UnitCount) continue;
                foreach (var placement in state.Positions(item, inventory))
                {
                    if (--budget < 0) return;
                    cancellation.ThrowIfCancellationRequested();
                    var step = new TransferStep(id, new(inventory.Id, placement));
                    if (!state.TryApply(step, out _)) continue;
                    steps.Add(step); moved = true; break;
                }
                if (moved) break;
            }
        }
    }
}
