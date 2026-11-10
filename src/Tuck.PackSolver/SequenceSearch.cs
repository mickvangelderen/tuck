using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Tuck.PackSolver;

/// <summary>Finds the intermediate transfers on detached state, including finite ground storage.</summary>
internal sealed class SequenceSearch
{
    private readonly Dictionary<string, TransferStep> _targets;
    private readonly HashSet<string> _unwanted;
    private readonly CancellationToken _cancellation;
    private readonly Dictionary<string, int> _visited = new();
    private readonly SequenceBudget _budget;
    internal bool Limited => _budget.Limited;
    internal int ExpandedNodes => _budget.Nodes;
    internal int PlacementChecks => _budget.Checks;
    internal SequenceSearch(Dictionary<string, TransferStep> targets, IEnumerable<string> unwanted,
        PlanningOptions options, CancellationToken cancellation)
    {
        _targets = targets; _unwanted = new(unwanted); _cancellation = cancellation;
        _budget = new(options, cancellation);
    }
    internal List<TransferStep>? Run(TransferSimulation state)
    {
        _cancellation.ThrowIfCancellationRequested();
        var scheduled = DependencyScheduler.TryRun(state, _targets, _unwanted, _budget);
        return scheduled ?? Search(state, 0, Math.Min(256, state.Items.Count * 4 + 8));
    }
    private bool AtTarget(TransferSimulation state, TransferStep target) => state.Items.TryGetValue(target.SourceItemId, out var item)
        && item.Location.Equals(target.Destination);
    private List<TransferStep>? Search(TransferSimulation state, int depth, int maxDepth)
    {
        _cancellation.ThrowIfCancellationRequested();
        var pending = _targets.Values.Where(t => !AtTarget(state, t)).ToArray();
        var discarded = _unwanted.Where(id => state.Items.ContainsKey(id) && state.IsReturned(id)).ToArray();
        if (pending.Length == 0 && discarded.Length == 0) return new();
        if (depth >= maxDepth) return null;
        string key = state.Key();
        if (_visited.TryGetValue(key, out int previous) && previous <= depth) return null;
        if (!_budget.Node()) return null;
        _visited[key] = depth;
        // Rebuild relevance from this state: inheriting it from a parent would make
        // state-only deduplication depend on the path used to reach the state.
        var candidates = new List<string>();
        var discovered = new HashSet<string>(StringComparer.Ordinal);
        // Exact goal moves first. A container may be filled while it is still on the ground.
        foreach (var step in pending)
        {
            var result = Try(step);
            if (result != null) return result;
            if (Limited) return null;
        }
        // Discards have an exact, legal ground placement too; they are not item destruction.
        foreach (string id in discarded)
            foreach (var position in state.Positions(state.Items[id], state.Inventories[state.Roots.Ground]))
            {
                var result = Try(new TransferStep(id, new(state.Roots.Ground, position)));
                if (result != null) return result;
                if (Limited) return null;
            }
        // Include obstructions discovered by goal/discard attempts in the usual ordering.
        // Later discoveries append to the same worklist, including blockers of buffers.
        var blockers = state.Items.Values.Where(i => pending.Any(t => t.Destination.InventoryId == i.Location.InventoryId)
            || pending.Any(t => t.SourceItemId == i.Id) || discarded.Contains(i.Id));
        foreach (var item in blockers) Enqueue(item.Id);
        candidates = candidates.OrderBy(id => _targets.ContainsKey(id)).ThenBy(id => id, StringComparer.Ordinal).ToList();
        for (int index = 0; index < candidates.Count; index++)
        {
            var item = state.Items[candidates[index]];
            if (state.Removable(item) != item.UnitCount) continue;
            foreach (var inventory in state.Inventories.Values.OrderBy(i => i.Id == state.Roots.Ground ? 0 : 1))
            {
                if (!state.Admits(item, inventory) || state.Addable(item, inventory) < item.UnitCount) continue;
                foreach (var position in state.Positions(item, inventory))
                {
                    if (item.Location.InventoryId == inventory.Id && item.Location.Placement.Equals(position)) continue;
                    var result = Try(new TransferStep(item.Id, new(inventory.Id, position)));
                    if (result != null) return result;
                    if (Limited) return null;
                }
            }
        }
        return null;

        void Enqueue(string id)
        {
            if (discovered.Add(id)) candidates.Add(id);
        }

        List<TransferStep>? Try(TransferStep step)
        {
            if (!_budget.Check()) return null;
            if (!state.TryCreateSuccessor(step, out var next, out _))
            {
                // This scan belongs to the charged placement attempt, just like Fits.
                // Ground and container obstructions use identical geometry and rules.
                var obstructions = state.Obstructions(step);
                if (obstructions != null)
                    foreach (var item in obstructions) Enqueue(item.Id);
                return null;
            }
            var tail = Search(next, depth + 1, maxDepth);
            if (tail == null) return null;
            tail.Insert(0, step);
            return tail;
        }
    }
}

/// <summary>One work budget shared by the dependency scheduler and the general fallback.</summary>
internal sealed class SequenceBudget
{
    internal PlanningOptions Options { get; }
    internal CancellationToken Cancellation { get; }
    internal int Nodes { get; private set; }
    internal int Checks { get; private set; }
    internal bool Limited { get; private set; }
    internal SequenceBudget(PlanningOptions options, CancellationToken cancellation)
    { Options = options; Cancellation = cancellation; }
    internal bool Node()
    {
        Cancellation.ThrowIfCancellationRequested();
        if (Nodes >= Options.MaxSequenceNodes) { Limited = true; return false; }
        Nodes++; return true;
    }
    internal bool Check()
    {
        Cancellation.ThrowIfCancellationRequested();
        if (Checks >= Options.MaxPlacementChecks) { Limited = true; return false; }
        Checks++; return true;
    }
}
