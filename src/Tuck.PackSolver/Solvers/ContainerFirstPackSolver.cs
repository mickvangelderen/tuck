using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Tuck.PackSolver.Solvers;

/// <summary>Choose container parents using relaxed allocation, then search exact transfers for each fixed hierarchy.</summary>
/// <remarks>
/// The master/subproblem split is inspired by Hooker's logic-based Benders decomposition
/// (https://johnhooker.tepper.cmu.edu/encyLBBD.pdf), without inference cuts or an optimality certificate.
/// Hierarchy scoring is multiple knapsack with affinities and per-destination cell/slot costs
/// (https://developers.google.com/optimization/pack/multiple_knapsack).
/// Before scoring, a geometric witness places the containers around pinned/otherwise fixed contents.
/// Following weighted/anytime A* (https://arxiv.org/abs/1110.2737), depth plus weighted estimated
/// remaining work orders legal transitions; a separate incumbent survives interruption.
/// Like RelaxedAStarPackSolver, buffers are deferred and whole-item simulation validates each step.
/// Container parents are final goals, not coordinates or restrictions on temporary transfers.
/// Loose-item assignments only guide search. Scores are NOT upper bounds; geometric failures do not
/// prove a hierarchy infeasible. A bounded run may return a better feasible intermediate prefix.
/// </remarks>
public sealed class ContainerFirstPackSolver : IPackSolver
{
    /// <summary>
    /// MaxContainerArrangements bounds scored hierarchies (including the current one).
    /// MaxSequenceNodes bounds total expansions; MaxPlacementChecks bounds container layout checks
    /// plus attempted transfers across hierarchies. Allocation keeps at most 512 capacity-vector labels.
    /// Metrics aggregate expansions, attempts, unique search labels, duplicates and heuristic evaluations.
    /// </summary>
    public PlanningResult Solve(PackProblem problem, PlanningOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (problem == null) throw new ArgumentNullException(nameof(problem));
        return new Search(problem.State, options ?? new(), cancellationToken).Run();
    }

    private sealed class Search
    {
        private readonly PackState capture;
        private readonly PlanningOptions limits;
        private readonly CancellationToken cancel;
        private readonly Dictionary<string, long> capacities;
        private PackingSearchData data = null!;
        private Node best = null!;
        private int expanded, checks, unique, duplicates, estimates;
        private long serial;
        private bool limited;

        internal Search(PackState capture, PlanningOptions limits, CancellationToken cancel)
        {
            this.capture = capture; this.limits = limits; this.cancel = cancel;
            capacities = capture.Inventories.Where(i => i.OwnerItemId != null && TransferSimulation.IsPackingStorage(i))
                .GroupBy(i => i.OwnerItemId!).ToDictionary(g => g.Key,
                    g => g.Sum(i => i.Layout is InventoryLayout.Grid grid ? (long)grid.Cells.Cells.Count(c => c == 0) : 1));
        }

        internal PlanningResult Run()
        {
            var initial = new TransferSimulation(capture);
            try
            {
                foreach (var item in initial.Items.Values)
                    if (!item.Shape.Cells.Contains((byte)1) || !initial.View.Effective(item.Id).Shape.Cells.Contains((byte)1))
                        throw new UnsupportedRuleException($"{item.Id}: finite grid search requires an occupied shape cell.");
                data = new(initial);
                best = new(initial, null, null, serial++, data.Key(initial), capacities);
            }
            catch (UnsupportedRuleException error)
            { return new(new(capture.StateId, Array.Empty<TransferStep>()), PlanningStatus.Unsupported, null, null, new[] { error.Message }); }
            var baseline = best;
            try
            {
                cancel.ThrowIfCancellationRequested();
                if (limits.MaxSequenceNodes == 0 || limits.MaxPlacementChecks == 0 || limits.MaxContainerArrangements == 0) limited = true;
                else
                {
                    var master = new ContainerAssignments(initial, capacities, limits.MaxContainerArrangements, cancel, limits.MaxPlacementChecks);
                    List<ContainerAssignment> arrangements;
                    try { arrangements = master.Run(); }
                    finally { checks += master.PlacementChecks; }
                    limited |= master.Limited;
                    var current = arrangements.FirstOrDefault(a => a.Matches(initial));
                    if (current != null && current != arrangements.FirstOrDefault())
                    {
                        // A short pass in the existing hierarchy gives an executable incumbent before
                        // a more valuable hierarchy asks for a multi-step container swap.
                        Realize(initial, current, Math.Min(16, limits.MaxSequenceNodes / 4),
                            checks + Math.Min(4096, Math.Max(0, limits.MaxPlacementChecks - checks) / 4));
                    }
                    for (int index = 0; index < arrangements.Count; index++)
                    {
                        cancel.ThrowIfCancellationRequested();
                        if (expanded >= limits.MaxSequenceNodes || checks >= limits.MaxPlacementChecks) { limited = true; break; }
                        var arrangement = arrangements[index];
                        // Reserve work for alternative hierarchies if the first relaxed winner cannot be realized.
                        int shares = Math.Min(2, arrangements.Count - index);
                        int nodeEnd = expanded + Math.Max(1, (limits.MaxSequenceNodes - expanded) / shares);
                        int checkEnd = checks + Math.Max(1, (limits.MaxPlacementChecks - checks) / shares);
                        Realize(initial, arrangement, nodeEnd, checkEnd);
                        if (best.Value == data.PositiveValueBound && best.Hands == 0
                            && best.Capacity == capacities.Values.DefaultIfEmpty().Max()) break;
                    }
                }
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            var steps = new List<TransferStep>();
            for (var node = best; node.Step != null; node = node.Parent!) steps.Add(node.Step);
            steps.Reverse();
            var diagnostics = capture.Definitions.Values.Where(d => d.Rules.UnsupportedTransfer != null)
                .Select(d => d.Identifier + ": " + d.Rules.UnsupportedTransfer)
                .Concat(capture.Inventories.Where(i => i.UnsupportedTransfer != null).Select(i => i.Id + ": " + i.UnsupportedTransfer));
            return new(new(capture.StateId, steps), cancel.IsCancellationRequested ? PlanningStatus.Interrupted
                : limited ? PlanningStatus.SearchLimit : best.BetterThan(baseline) ? PlanningStatus.Improved : PlanningStatus.Unchanged,
                baseline.Value, best.Value, diagnostics, new(expanded, checks, unique, duplicates, estimates, null));
        }

        private void Realize(TransferSimulation initial, ContainerAssignment assignment, int nodeEnd, int checkEnd)
        {
            var order = new NodeOrder();
            var open = new SortedSet<Node>(order);
            var distances = new Dictionary<PackingStateKey, int>();
            var root = new Node(initial, null, null, serial++, data.Key(initial), capacities);
            root.Estimate = Estimate(root, assignment);
            open.Add(root); distances.Add(root.Key, 0); unique++;
            var started = new List<Node>();
            try
            {
                while (open.Count != 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    var node = open.Min!; open.Remove(node);
                    if (distances[node.Key] < node.Depth) continue;
                    if (assignment.Matches(node.State) && node.Value >= assignment.Value
                        && node.Hands <= assignment.Hands && node.Capacity >= assignment.Capacity) return;
                    if (expanded >= nodeEnd || checks >= checkEnd) { limited = true; return; }
                    if (node.Candidates == null && !node.Buffers) { expanded++; started.Add(node); }
                    var candidates = node.Candidates ??= Actions(node.State, assignment, node.Buffers).GetEnumerator();
                    bool exhausted = false;
                    // Partial expansion lets an enabling move continue before thousands of siblings.
                    for (int batch = 0; batch < 16; batch++)
                    {
                        cancel.ThrowIfCancellationRequested();
                        if (!candidates.MoveNext())
                        {
                            candidates.Dispose(); node.Candidates = null;
                            if (node.Buffers) exhausted = true;
                            else node.Buffers = true;
                            break;
                        }
                        if (checks >= checkEnd) { limited = true; return; }
                        checks++;
                        var step = candidates.Current;
                        if (!node.State.TryCreateSuccessor(step, out var next, out _)) continue;
                        var key = data.Key(next);
                        if (distances.TryGetValue(key, out int depth) && depth <= node.Depth + 1) { duplicates++; continue; }
                        var child = new Node(next, node, step, serial++, key, capacities);
                        if (!distances.ContainsKey(key)) unique++;
                        distances[key] = child.Depth;
                        if (child.BetterThan(best)) best = child;
                        child.Estimate = Estimate(child, assignment);
                        open.Add(child);
                        if (order.Compare(child, node) < 0) break;
                    }
                    if (!exhausted) open.Add(node);
                }
            }
            finally { foreach (var node in started) node.Candidates?.Dispose(); }
        }

        private IEnumerable<TransferStep> Actions(TransferSimulation state, ContainerAssignment assignment, bool buffers)
        {
            foreach (string id in data.ActionOrder.OrderBy(id => assignment.Parents.TryGetValue(id, out var parent)
                ? state.Items[id].Location.InventoryId != parent ? 0 : 3
                : assignment.Allocation.TryGetValue(id, out var destination)
                    ? !state.IsReturned(id) ? 1 : state.Items[id].Location.InventoryId != destination ? 2 : 3 : 3))
            {
                var item = state.Items[id];
                if (state.Removable(item) < item.UnitCount) continue;
                foreach (var inventory in data.Inventories.OrderBy(i => assignment.Allocation.TryGetValue(id, out var destination) && i.Id == destination ? 0 : 1))
                {
                    bool preferred = assignment.Parents.TryGetValue(id, out string? parent)
                        ? inventory.Id == parent : assignment.Storage.Contains(inventory.Id);
                    var source = state.Inventories[item.Location.InventoryId];
                    if (parent == null && source.OwnerItemId != null && source.OwnerItemId == inventory.OwnerItemId
                        && source.Layout is InventoryLayout.Slot && inventory.Layout is InventoryLayout.Slot
                        && !assignment.Parents.Values.Contains(source.Id)) preferred = false;
                    if (preferred == buffers || !state.Admits(item, inventory) || state.Addable(item, inventory) < item.UnitCount) continue;
                    var positions = state.Positions(item, inventory);
                    if (assignment.ContainerLocations.TryGetValue(id, out var target) && target.InventoryId == inventory.Id)
                        positions = new[] { target.Placement }.Concat(positions).Distinct();
                    foreach (var position in positions)
                    {
                        cancel.ThrowIfCancellationRequested();
                        var destination = new ItemLocation(inventory.Id, position);
                        if (!destination.Equals(item.Location)) yield return new(id, destination);
                    }
                }
            }
        }

        private (int Distance, double Freedom, int BufferDelay) Estimate(Node node, ContainerAssignment assignment)
        {
            estimates++;
            var state = node.State;
            int remaining = 0, hardest = 0, discards = 0;
            double freedom = 0;
            foreach (string id in data.ItemIds)
            {
                var item = state.Items[id];
                if (assignment.Parents.TryGetValue(id, out string? parent))
                {
                    if (item.Location.InventoryId == parent) continue;
                }
                else
                {
                    // Existing membership in any selected storage is satisfactory: no forced same-rank swaps.
                    if (assignment.Allocation.ContainsKey(id))
                    {
                        if (assignment.Storage.Contains(item.Location.InventoryId)
                            || !TransferSimulation.IsPackingStorage(state.Inventories[item.Location.InventoryId])) continue;
                    }
                    else
                    {
                        if (assignment.Storage.Contains(item.Location.InventoryId) && state.Removable(item) >= item.UnitCount) discards++;
                        continue;
                    }
                }
                remaining++;
                int cheapest = int.MaxValue, positions = 0;
                double opportunities = 0;
                foreach (var inventory in data.Inventories)
                {
                    if (parent != null ? inventory.Id != parent : !assignment.Storage.Contains(inventory.Id)) continue;
                    if (!state.Admits(item, inventory) || state.Addable(item, inventory) < item.UnitCount) continue;
                    foreach (var position in state.Positions(item, inventory))
                    {
                        cancel.ThrowIfCancellationRequested();
                        int blockers = Blockers(state, item, inventory, position);
                        if (blockers < 0) continue;
                        cheapest = Math.Min(cheapest, blockers + 1);
                        opportunities += 1.0 / ((blockers + 1.0) * (blockers + 1.0));
                        positions++;
                    }
                }
                hardest = Math.Max(hardest, cheapest == int.MaxValue ? 2 : cheapest);
                if (positions > 0) freedom += opportunities / positions;
            }
            int distance = remaining + discards + Math.Max(0, hardest - 1) + Math.Max(0, node.Hands - assignment.Hands);
            return (distance, freedom, discards > 0 ? 0 : 1);
        }

        private static int Blockers(TransferSimulation state, ItemState item, InventoryState inventory, Placement position)
        {
            PlacedShape? footprint = null;
            if (inventory.Layout is InventoryLayout.Grid grid)
            {
                footprint = state.View.Effective(item.Id).Place(item.Shape, position, inventory.Layout);
                if (footprint.Positions(1).Any(c => grid.Cells.GetLocal(c.X, c.Y) != 0)) return -1;
            }
            int blockers = 0;
            foreach (var occupant in state.View.Contents(inventory.Id))
                if (occupant.Id != item.Id && (footprint == null || footprint.Intersects(state.View.Placed(occupant.Id)))) blockers++;
            return blockers;
        }
    }

    private sealed class Node
    {
        internal readonly TransferSimulation State;
        internal readonly Node? Parent;
        internal readonly TransferStep? Step;
        internal readonly PackingStateKey Key;
        internal readonly int Depth, Hands;
        internal readonly long Serial, Value, Capacity;
        internal bool Buffers;
        internal IEnumerator<TransferStep>? Candidates;
        internal (int Distance, double Freedom, int BufferDelay) Estimate;
        internal Node(TransferSimulation state, Node? parent, TransferStep? step, long serial, PackingStateKey key, Dictionary<string, long> capacities)
        {
            State = state; Parent = parent; Step = step; Serial = serial; Key = key; Depth = parent == null ? 0 : parent.Depth + 1;
            Value = state.ReturnedValue;
            Hands = state.View.Contents(state.Roots.Left).Count() + state.View.Contents(state.Roots.Right).Count();
            var bag = state.View.Contents(state.Roots.Backpack).FirstOrDefault();
            Capacity = bag != null && capacities.TryGetValue(bag.Id, out long capacity) ? capacity : 0;
        }
        internal bool BetterThan(Node other) => Value != other.Value ? Value > other.Value
            : Hands != other.Hands ? Hands < other.Hands : Capacity != other.Capacity ? Capacity > other.Capacity : Depth < other.Depth;
    }

    private sealed class NodeOrder : IComparer<Node>
    {
        public int Compare(Node? x, Node? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;
            int order = Priority(x).CompareTo(Priority(y));
            if (order == 0) order = y.Value.CompareTo(x.Value);
            if (order == 0) order = y.Estimate.Freedom.CompareTo(x.Estimate.Freedom);
            return order != 0 ? order : x.Serial.CompareTo(y.Serial);
        }
        private static long Priority(Node node) => node.Depth + 2L * (node.Estimate.Distance + (node.Buffers ? node.Estimate.BufferDelay : 0));
    }
}
