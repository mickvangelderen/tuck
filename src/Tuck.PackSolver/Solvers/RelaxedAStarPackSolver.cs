using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Tuck.PackSolver.Solvers;

/// <summary>Bounded weighted A* over whole-item placements, with an occupancy relaxation.</summary>
/// <remarks>
/// Searches toward returning every item, retaining the best value/compactness/length incumbent even when that
/// goal is infeasible. The relaxed distance orders exploration; it never rejects a legal state.
/// At the value bound, refinement stops after 32 expansions without an improved incumbent.
/// This is a satisficing planner, not an optimal packing or infeasibility certificate.
/// </remarks>
public sealed class RelaxedAStarPackSolver : IPackSolver
{
    public PlanningResult Solve(PackProblem problem, PlanningOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (problem == null) throw new ArgumentNullException(nameof(problem));
        return new Search(problem.State, options ?? new(), cancellationToken).Run();
    }

    private sealed class Search
    {
        private readonly PackState initial;
        private readonly PlanningOptions limits;
        private readonly CancellationToken cancellation;
        private readonly SortedSet<Node> open = new(new NodeOrder());
        private readonly Dictionary<string, int> distances = new(StringComparer.Ordinal);
        private readonly string[] itemIds;
        private readonly Dictionary<string, long> capacities;
        private string[] actionOrder = Array.Empty<string>();
        private InventoryState[] inventoryOrder = Array.Empty<InventoryState>();
        private HashSet<string>? wanted;
        private int checks, expanded, duplicates, estimates;
        private long serial;
        private bool exhausted;
        private long valueBound;
        private long capacityBound;
        private int minimumHands;
        private Node best = null!;

        internal Search(PackState initial, PlanningOptions limits, CancellationToken cancellation)
        {
            this.initial = initial; this.limits = limits; this.cancellation = cancellation;
            itemIds = initial.Items.Select(i => i.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            capacities = initial.Inventories.Where(i => i.OwnerItemId != null && TransferSimulation.IsPackingStorage(i))
                .GroupBy(i => i.OwnerItemId!).ToDictionary(g => g.Key,
                    g => g.Sum(i => i.Layout is InventoryLayout.Grid grid ? (long)grid.Cells.UsableArea : 1));
        }

        internal PlanningResult Run()
        {
            var simulation = new TransferSimulation(initial);
            try
            {
                // Derive every item, including ground items, before admitting a search state.
                foreach (var item in simulation.Items.Values) _ = simulation.View.Effective(item.Id);
                best = new(simulation, null, null, 0, serial++, Key(simulation), capacities);
                valueBound = ValueBound(simulation);
            }
            catch (UnsupportedRuleException error)
            {
                return new(new(initial.StateId, Array.Empty<TransferStep>()), PlanningStatus.Unsupported,
                    null, null, new[] { error.Message });
            }
            var baseline = best;
            int lastImprovement = -1;
            if (!cancellation.IsCancellationRequested && limits.MaxSequenceNodes > 0 && limits.MaxPlacementChecks > 0)
            {
                minimumHands = simulation.Items.Values.Count(i =>
                    (i.Location.InventoryId == initial.Roots.Left || i.Location.InventoryId == initial.Roots.Right)
                    && simulation.Removable(i) < i.UnitCount);
                var equipped = simulation.View.Contents(initial.Roots.Backpack).FirstOrDefault();
                capacityBound = equipped != null && simulation.Removable(equipped) < equipped.UnitCount ? best.Capacity
                    : simulation.Items.Values.Where(i => i.Location.InventoryId == initial.Roots.Backpack
                        || simulation.Removable(i) >= i.UnitCount && simulation.Admits(i, simulation.Inventories[initial.Roots.Backpack])
                        && simulation.Addable(i, simulation.Inventories[initial.Roots.Backpack]) >= i.UnitCount)
                        .Select(i => capacities.TryGetValue(i.Id, out long capacity) ? capacity : 0).DefaultIfEmpty().Max();
                try
                {
                    if (RelaxedPackingBound.TryCreate(simulation, itemIds, cancellation, out long bound, out var targets, out int hands))
                    { valueBound = bound; wanted = targets; minimumHands = hands; }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                best.Estimate = Estimate(best);
            }
            actionOrder = itemIds.OrderByDescending(id => (decimal)simulation.View.PackUnitValue(id) * simulation.Items[id].UnitCount).ThenBy(id => id, StringComparer.Ordinal).ToArray();
            inventoryOrder = simulation.Inventories.Values.OrderBy(i => i.Id, StringComparer.Ordinal).ToArray();
            open.Add(best);
            distances.Add(best.Key, 0);
            while (open.Count != 0 && !cancellation.IsCancellationRequested)
            {
                if (expanded >= limits.MaxSequenceNodes || checks >= limits.MaxPlacementChecks)
                { exhausted = true; break; }
                var node = open.Min!;
                open.Remove(node);
                if (distances[node.Key] < node.Depth) continue;
                if (lastImprovement < 0 && node.Value == valueBound) lastImprovement = expanded;
                // The capacity ceiling and hand floor need not be jointly reachable.
                // The allowance is a normal heuristic stop, not a compactness certificate.
                if (lastImprovement >= 0 && (best.Hands == minimumHands && best.Capacity == capacityBound
                    || expanded - lastImprovement >= 32)) break;
                if (!node.BuffersPending) expanded++;
                foreach (var step in Actions(node.State, node.BuffersPending))
                {
                    if (cancellation.IsCancellationRequested) break;
                    if (checks >= limits.MaxPlacementChecks) { exhausted = true; break; }
                    checks++;
                    if (!node.State.TryCreateSuccessor(step, out var next, out _)) continue;
                    int depth = node.Depth + 1;
                    string key = Key(next);
                    if (distances.TryGetValue(key, out int previous) && previous <= depth) { duplicates++; continue; }
                    var child = new Node(next, node, step, depth, serial++, key, capacities);
                    distances[key] = depth;
                    if (child.BetterThan(best))
                    { best = child; if (lastImprovement >= 0) lastImprovement = expanded; }
                    // Moving a whole ground item within the ground changes neither containment
                    // nor any return-inventory obstacle used by the relaxation.
                    child.Estimate = step.Destination.InventoryId == initial.Roots.Ground
                        && node.State.Items[step.SourceItemId].Location.InventoryId == initial.Roots.Ground
                        ? node.Estimate : Estimate(child);
                    open.Add(child);
                }
                if (exhausted) break;
                if (!node.BuffersPending)
                {
                    // Preferred operators first, deferred buffer successors second. This keeps a
                    // large ground grid from consuming the entire budget before a short in-bag
                    // rearrangement is tried. Both phases use the same frontier and visited table.
                    node.BuffersPending = true;
                    open.Add(node);
                }
            }
            var steps = new List<TransferStep>();
            for (var node = best; node.Step != null; node = node.Parent!) steps.Add(node.Step);
            steps.Reverse();
            var status = cancellation.IsCancellationRequested ? PlanningStatus.Interrupted
                : exhausted ? PlanningStatus.SearchLimit
                : best.BetterThan(baseline) ? PlanningStatus.Improved : PlanningStatus.Unchanged;
            var unsupported = initial.Definitions.Values.Where(d => d.Rules.UnsupportedTransfer != null)
                .Select(d => d.Identifier + ": " + d.Rules.UnsupportedTransfer)
                .Concat(initial.Inventories.Where(i => i.UnsupportedTransfer != null)
                    .Select(i => i.Id + ": " + i.UnsupportedTransfer));
            return new(new(initial.StateId, steps), status, baseline.Value, best.Value, unsupported,
                new(expanded, checks, distances.Count, duplicates, estimates, wanted == null ? null : valueBound));
        }

        private IEnumerable<TransferStep> Actions(TransferSimulation state, bool buffers)
        {
            // Each captured stack is one indivisible item. Returned items can still move to
            // make space, but neither their quantities nor their intrinsic attributes change.
            foreach (var id in actionOrder)
            {
                var item = state.Items[id];
                if (state.Removable(item) < item.UnitCount) continue;
                foreach (var inventory in inventoryOrder)
                {
                    if (Returned(state, inventory) == buffers) continue;
                    if (!state.Admits(item, inventory) || state.Addable(item, inventory) < item.UnitCount) continue;
                    foreach (var position in state.Positions(item, inventory))
                    {
                        if (cancellation.IsCancellationRequested) yield break;
                        var location = new ItemLocation(inventory.Id, position);
                        if (!location.Equals(item.Location))
                            yield return new(item.Id, location);
                    }
                }
            }
        }

        private static bool Returned(TransferSimulation state, InventoryState inventory) =>
            state.Roots.IsReturn(inventory.Id) || inventory.OwnerItemId != null && state.IsReturned(inventory.OwnerItemId);

        private static long ValueBound(TransferSimulation state)
        {
            // Whole-item moves preserve each item's value. Ignoring capacity and admission,
            // packing every positive-value item bounds the value of any reachable packing.
            long total = 0;
            foreach (var item in state.Items.Values)
            {
                long value = Math.Max(0, state.View.PackUnitValue(item.Id));
                if (value > (long.MaxValue - total) / item.UnitCount) return long.MaxValue;
                total += value * item.UnitCount;
            }
            return total;
        }

        private (int Distance, double Freedom, int BufferDelay) Estimate(Node node)
        {
            estimates++;
            var state = node.State;
            int outsideRoots = 0, hardest = 0, replacements = 0;
            double freedom = 0;
            foreach (var item in state.Items.Values)
            {
                if (state.IsReturned(item.Id))
                {
                    // A chosen capacity subset can require replacing held low-value items.
                    // Reward that enabling move instead of treating every discard as regression.
                    // This is ordering only: unselected items and all their moves remain legal.
                    if (wanted != null && !wanted.Contains(item.Id) && state.Removable(item) >= item.UnitCount) replacements++;
                    continue;
                }
                if (wanted != null && !wanted.Contains(item.Id)) continue;
                // One transfer can return a whole containment subtree. Do not count its children
                // as mandatory independent packing moves.
                if (state.Inventories[item.Location.InventoryId].OwnerItemId != null) continue;
                outsideRoots++;
                int cheapest = int.MaxValue, positions = 0;
                double opportunities = 0;
                foreach (var inventory in state.Inventories.Values)
                {
                    if (!Returned(state, inventory) || !state.Admits(item, inventory)) continue;
                    if (state.Addable(item, inventory) >= item.UnitCount)
                        foreach (var position in state.Positions(item, inventory))
                        {
                            if (cancellation.IsCancellationRequested) return (outsideRoots, freedom, 1);
                            int blockers = Blockers(state, item, inventory, position);
                            if (blockers < 0) continue;
                            cheapest = Math.Min(cheapest, blockers + 1);
                            opportunities += 1.0 / ((blockers + 1.0) * (blockers + 1.0));
                            positions++;
                        }
                }
                // No relaxed destination is a heuristic plateau, not proof of a dead end:
                // moving a container/stack or changing an ancestor may create a destination.
                hardest = Math.Max(hardest, cheapest == int.MaxValue ? 2 : cheapest);
                if (positions > 0) freedom += opportunities / positions;
            }
            // Discarding an excluded returned item is progress toward the selected subset.
            // Do not additionally postpone that phase behind arbitrary in-bag permutations.
            int valueDistance = node.Value == valueBound ? 0 : Math.Max(outsideRoots + replacements, hardest);
            int handDistance = Math.Max(0, node.Hands - minimumHands);
            // With both hands occupied, a swap also needs a hand cleared (or a ground
            // buffer and reacquisition). Recognize that enabling move before the swap.
            int equipDistance = node.Capacity == capacityBound ? 0
                : !state.View.Contents(state.Roots.Backpack).Any() ? 1 : node.Hands == 2 ? 3 : 2;
            // Hand clearing and equipping can overlap. Add their estimated work to
            // acquisition so collecting the last items still makes progress. This is
            // search guidance, not an admissible bound or a prescribed container layout.
            return (valueDistance + Math.Max(handDistance, equipDistance), freedom, replacements > 0 ? 0 : 1);
        }

        private static int Blockers(TransferSimulation state, ItemState item, InventoryState inventory, Placement position)
        {
            PlacedShape? footprint = null;
            if (inventory.Layout is InventoryLayout.Grid grid)
            {
                footprint = state.View.Effective(item.Id).Place(item.Shape, position, inventory.Layout);
                if (!footprint.Fits(grid.Cells, checkBounds: false)) return -1;
            }
            int blockers = 0;
            foreach (var occupant in state.View.Contents(inventory.Id))
                if (occupant.Id != item.Id && (footprint == null || footprint.Intersects(state.View.Placed(occupant.Id))))
                    blockers++;
            return blockers;
        }

        private string Key(TransferSimulation state)
        {
            var key = new StringBuilder();
            // IDs, counts and intrinsic facts are fixed. Only locations vary, in a fixed item
            // order. The same configuration at another depth is the same search state.
            foreach (var id in itemIds)
            {
                var location = state.Items[id].Location;
                key.Append(location.InventoryId.Length).Append(':').Append(location.InventoryId);
                if (location.Placement is Placement.Grid p)
                    key.Append(p.X).Append(',').Append(p.Y).Append(',').Append(p.Orientation).Append(',').Append(p.Flipped);
                else key.Append('s');
                key.Append(';');
            }
            return key.ToString();
        }
    }

    private sealed class Node
    {
        internal readonly TransferSimulation State;
        internal readonly Node? Parent;
        internal readonly TransferStep? Step;
        internal readonly int Depth, Hands;
        internal readonly long Serial, Value, Capacity;
        internal readonly string Key;
        internal (int Distance, double Freedom, int BufferDelay) Estimate;
        internal bool BuffersPending;
        internal Node(TransferSimulation state, Node? parent, TransferStep? step, int depth, long serial, string key,
            Dictionary<string, long> capacities)
        {
            State = state; Parent = parent; Step = step; Depth = depth; Serial = serial; Key = key; Value = state.ReturnedValue;
            Hands = state.View.Contents(state.Roots.Left).Count() + state.View.Contents(state.Roots.Right).Count();
            var bag = state.View.Contents(state.Roots.Backpack).FirstOrDefault();
            Capacity = bag != null && capacities.TryGetValue(bag.Id, out long capacity) ? capacity : 0;
        }
        internal bool BetterThan(Node other) => Value != other.Value ? Value > other.Value
            : Hands != other.Hands ? Hands < other.Hands
            : Capacity != other.Capacity ? Capacity > other.Capacity : Depth < other.Depth;
    }

    private sealed class NodeOrder : IComparer<Node>
    {
        public int Compare(Node? x, Node? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;
            int result = Priority(x).CompareTo(Priority(y));
            if (result == 0) result = y.Value.CompareTo(x.Value);
            if (result == 0) result = y.Estimate.Freedom.CompareTo(x.Estimate.Freedom);
            return result != 0 ? result : x.Serial.CompareTo(y.Serial);
        }
        private static long Priority(Node node) => node.Depth + 2L * (node.Estimate.Distance + (node.BuffersPending ? node.Estimate.BufferDelay : 0));
    }
}
