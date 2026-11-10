using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Tuck.PackSolver.Solvers;

/// <summary>Anytime forward search over whole-item placements, including temporary moves.</summary>
/// <remarks>
/// Uses the incumbent-and-bound separation of Hansen and Zhou, Anytime Heuristic Search (2007),
/// https://arxiv.org/abs/1110.2737. Every reached state is a possible stopping point, following the
/// soft-goal interpretation of Keyder and Geffner (2009), https://arxiv.org/abs/1401.3491.
/// Compare returned value, fewer occupied hands, larger equipped capacity, then fewer transfers.
/// The optimistic packing estimate orders exploration only; it is NOT an admissible pruning bound.
/// All legal candidates remain available, including moves of ground clutter and already returned items.
/// A pooled-capacity relaxation supplies an upper bound and a preferred subset, never an action filter.
/// When available, that subset defines packing/discard progress for weighted best-first ordering;
/// the independent packing-value estimate breaks ties. Without it, value remains the primary guide.
/// At the value bound, refinement stops after 32 expansions without an improved incumbent.
/// Deferred buffer moves compete on the same frontier after return-storage moves.
/// This is a best-first branch-and-bound adaptation, not a claim to implement weighted A*'s error bound.
/// Finite grid enumeration requires at least one occupied (value 1) cell in item shapes
/// and their effective shapes. Shapes outside that domain are reported as unsupported rather than silently
/// omitting their unbounded placements. Supported shape changes are re-derived after every transfer.
/// </remarks>
public sealed class AnytimePackSolver : IPackSolver
{
    /// <summary>
    /// MaxSequenceNodes bounds retained search labels (including the initial state), and
    /// MaxPlacementChecks bounds attempted transitions. MaxContainerArrangements is unused:
    /// containers are moved in the same search as other items. No separate layout solver is invoked.
    /// Metrics count distinct labels expanded, transition attempts, retained unique keys, dominated
    /// duplicates, and heuristic evaluations. RelaxedValueBound is present only for the capacity DP.
    /// </summary>
    public PlanningResult Solve(PackProblem problem, PlanningOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (problem == null) throw new ArgumentNullException(nameof(problem));
        return new Search(problem.State, options ?? new(), cancellationToken).Run();
    }

    private sealed class Search
    {
        private readonly PackState _capture;
        private readonly PlanningOptions _options;
        private readonly CancellationToken _cancel;
        private readonly Dictionary<PackingStateKey, int> _distance = new();
        private readonly SortedSet<Work> _open = new(new WorkOrder());
        private readonly List<Node> _retained = new();
        private long _serial;
        private int _checks, _expanded, _duplicates, _estimates;
        private bool _limited, _interrupted;
        private Node _best = null!;
        private double _movePenalty;
        private PackingSearchData _data = null!;
        private decimal _upperValue;
        private long? _relaxedBound;
        private HashSet<string>? _wanted;
        private Dictionary<string, long> _capacities = null!;
        private long _capacityBound;
        private int _minimumHands, _lastImprovement = -1;

        internal Search(PackState capture, PlanningOptions options, CancellationToken cancel)
        { _capture = capture; _options = options; _cancel = cancel; }

        internal PlanningResult Run()
        {
            var initial = new TransferSimulation(_capture);
            long initialValue;
            try
            {
                foreach (var item in initial.Items.Values)
                {
                    var effective = initial.View.Effective(item.Id);
                    if (!item.Shape.Cells.Contains((byte)1) || !effective.Shape.Cells.Contains((byte)1))
                        throw new UnsupportedRuleException($"{item.Id}: finite grid search requires an occupied shape cell.");
                }
                initialValue = initial.ReturnedValue;
            }
            catch (UnsupportedRuleException error)
            { return new(new(_capture.StateId, Array.Empty<TransferStep>()), PlanningStatus.Unsupported, null, null, new[] { error.Message }); }
            _data = new(initial);
            _upperValue = _data.PositiveValueBound;
            _capacities = _capture.Inventories.Where(i => i.OwnerItemId != null && TransferSimulation.IsPackingStorage(i))
                .GroupBy(i => i.OwnerItemId!).ToDictionary(g => g.Key,
                    g => g.Sum(i => i.Layout is InventoryLayout.Grid grid ? (long)grid.Cells.Cells.Count(c => c == 0) : 1));
            _best = new(initial, null, null, _data.Key(initial), _capacities);
            var baseline = _best;
            _movePenalty = Math.Max(1d, Math.Abs((double)initialValue)) / 1000d;
            try
            {
                _cancel.ThrowIfCancellationRequested();
                _minimumHands = initial.Items.Values.Count(i =>
                    (i.Location.InventoryId == initial.Roots.Left || i.Location.InventoryId == initial.Roots.Right)
                    && initial.Removable(i) < i.UnitCount);
                var equipped = initial.View.Contents(initial.Roots.Backpack).FirstOrDefault();
                _capacityBound = equipped != null && initial.Removable(equipped) < equipped.UnitCount ? _best.Capacity
                    : initial.Items.Values.Where(i => i.Location.InventoryId == initial.Roots.Backpack
                        || initial.Removable(i) >= i.UnitCount && initial.Admits(i, initial.Inventories[initial.Roots.Backpack])
                        && initial.Addable(i, initial.Inventories[initial.Roots.Backpack]) >= i.UnitCount)
                        .Select(i => _capacities.TryGetValue(i.Id, out long capacity) ? capacity : 0).DefaultIfEmpty().Max();
                if (_options.MaxSequenceNodes > 0 && _options.MaxPlacementChecks > 0
                    && RelaxedPackingBound.TryCreate(initial, _data.ItemIds, _cancel, out long bound, out var wanted, out int hands))
                { _upperValue = bound; _relaxedBound = bound; _wanted = wanted; _minimumHands = hands; }
                if (_best.Value == _upperValue) _lastImprovement = _expanded;
                if (CanImprove())
                {
                    if (_options.MaxSequenceNodes == 0 || _options.MaxPlacementChecks == 0) _limited = true;
                    else
                    {
                        _distance.Add(_best.Key, 0);
                        _retained.Add(_best);
                        Enqueue(_best);
                    }
                }
                while (_open.Count != 0 && !_limited && CanImprove())
                {
                    _cancel.ThrowIfCancellationRequested();
                    var work = _open.Min!;
                    _open.Remove(work);
                    var node = work.Node;
                    if (_distance[node.Key] < node.Depth) continue;
                    if (node.Candidates == null && !node.BuffersPending) _expanded++;
                    var candidates = node.Candidates ??= Actions(node).GetEnumerator();
                    // Partial expansion bounds work before reconsidering the frontier. The iterator
                    // stays with its state; deferring a candidate never deletes that search branch.
                    for (int batch = 0; batch < 16; batch++)
                    {
                        _cancel.ThrowIfCancellationRequested();
                        if (!CanImprove()) break;
                        if (!candidates.MoveNext())
                        {
                            candidates.Dispose(); node.Candidates = null;
                            if (node.BuffersPending) node.Exhausted = true;
                            else node.BuffersPending = true;
                            break;
                        }
                        if (_checks >= _options.MaxPlacementChecks) { _limited = true; break; }
                        _checks++;
                        var step = candidates.Current;
                        if (!node.State.TryCreateSuccessor(step, out var next, out _)) continue;
                        int depth = node.Depth + 1;
                        var key = _data.Key(next);
                        if (_distance.TryGetValue(key, out int previous) && previous <= depth) { _duplicates++; continue; }
                        var child = new Node(next, node, step, key, _capacities);
                        // The estimate only inspects returned storage and membership. Ground-to-ground
                        // moves leave both unchanged, even when they unblock an essential discard.
                        if (step.Destination.InventoryId == _capture.Roots.Ground
                            && node.State.Items[step.SourceItemId].Location.InventoryId == _capture.Roots.Ground)
                        {
                            child.Estimate = node.Estimate;
                            child.Remaining = node.Remaining;
                        }
                        if (child.BetterThan(_best))
                        {
                            _best = child;
                            if (_best.Value == _upperValue) _lastImprovement = _expanded;
                        }
                        if (!CanImprove()) break;
                        if (_retained.Count >= _options.MaxSequenceNodes) { _limited = true; break; }
                        _distance[key] = depth;
                        _retained.Add(child);
                        // Yield a partial expansion as soon as a successor outranks its parent.
                        // Keeping a fixed batch of siblings first can consume the label budget on
                        // equivalent choices before any promising path gets another transfer.
                        var queued = Enqueue(child);
                        if (queued.Priority > work.Priority || queued.Priority == work.Priority && queued.Estimate > work.Estimate) break;
                    }
                    if (!node.Exhausted && !_limited && CanImprove()) Enqueue(node);
                }
            }
            catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { _interrupted = true; }
            finally { foreach (var node in _retained) node.Candidates?.Dispose(); }

            var steps = new List<TransferStep>();
            for (var node = _best; node.Step != null; node = node.Parent!) steps.Add(node.Step);
            steps.Reverse();
            var plan = new PackPlan(_capture.StateId, steps);
            long finalValue = PlanReplay.ReturnedValue(PlanReplay.Replay(_capture, plan));
            var diagnostics = _capture.Definitions.Values.Where(d => d.Rules.UnsupportedTransfer != null)
                .Select(d => $"{d.Identifier}: {d.Rules.UnsupportedTransfer}")
                .Concat(_capture.Inventories.Where(i => i.UnsupportedTransfer != null).Select(i => $"{i.Id}: {i.UnsupportedTransfer}"));
            return new(plan, _interrupted ? PlanningStatus.Interrupted : _limited ? PlanningStatus.SearchLimit
                : _best.BetterThan(baseline) ? PlanningStatus.Improved : PlanningStatus.Unchanged, initialValue, finalValue, diagnostics,
                new(_expanded, _checks, _distance.Count, _duplicates, _estimates, _relaxedBound));
        }

        private bool CanImprove()
        {
            if (_upperValue > _best.Value) return true;
            // The hand floor and capacity ceiling need not be jointly attainable. Bound the
            // extra exploration instead of trying to certify compactness or shortest length.
            return (_best.Hands > _minimumHands || _best.Capacity < _capacityBound)
                && _expanded - _lastImprovement < 32;
        }

        private Work Enqueue(Node node)
        {
            node.Estimate ??= Estimate(node.State);
            // Independent packing estimates often credit the same free cell to many items. Their
            // sum can remain flat after a useful packing. Count outstanding preferred packing moves and
            // discards as well, so a path that realizes that value outranks its unexpanded ancestor.
            // Contained items may travel together, so this is ordering guidance, not a move bound.
            node.Remaining ??= node.Value == _upperValue ? 0 : _data.ItemIds.Count(id => node.State.IsReturned(id)
                ? _wanted != null ? !_wanted.Contains(id) : _data.Value(id) < 0
                : _wanted != null ? _wanted.Contains(id) : _data.Value(id) > 0);
            // Two transfers of scheduling delay prevent a large buffer grid from hiding short
            // rearrangements. When the preferred subset excludes returned items, discards are
            // already part of the relaxed solution and need no extra delay. No phase is omitted.
            node.BufferDelay ??= _wanted != null && _data.ItemIds.Any(id => node.State.IsReturned(id) && !_wanted.Contains(id))
                ? 0 : 2;
            int handDistance = Math.Max(0, node.Hands - _minimumHands);
            // A bag swap needs a free hand or a temporary ground placement and reacquisition.
            int equipDistance = node.Capacity == _capacityBound ? 0
                : !node.State.View.Contents(node.State.Roots.Backpack).Any() ? 1 : node.Hands == 2 ? 3 : 2;
            // Hand clearing and equipping can overlap. This guides exploration without
            // prescribing which bag holds loose items or rejecting any legal move.
            double work = node.Depth + 2d * (node.Remaining.Value + Math.Max(handDistance, equipDistance))
                + (node.BuffersPending ? node.BufferDelay.Value : 0);
            // With a capacity-selected target, order by progress toward that target and use the
            // packing-value estimate to break ties. Otherwise the same empty cell is credited to
            // every packing and shallow states can outrank a nearly completed target indefinitely.
            // In the general domain, retain the soft-goal value estimate as the primary guide.
            double priority = _wanted == null ? node.Estimate.Value - work * _movePenalty : -work;
            var queued = new Work(node, priority, node.Estimate.Value, _serial++);
            _open.Add(queued);
            return queued;
        }

        private double Estimate(TransferSimulation state)
        {
            _estimates++;
            // A relaxed packing estimate: subtract occupants that have no currently available return
            // placement, then discount by the number of transfers needed to clear the destination.
            // Relocations are checked independently, so they may compete for the same space. This
            // estimate may over/underestimate and is used exclusively for ordering, never pruning.
            double value = _wanted == null ? state.ReturnedValue
                : _data.ItemIds.Where(id => _wanted.Contains(id) && state.IsReturned(id)).Sum(_data.Value);
            var alternatives = new Dictionary<string, List<ItemLocation>>(StringComparer.Ordinal);
            foreach (var id in _data.ItemIds)
            {
                var item = state.Items[id];
                _cancel.ThrowIfCancellationRequested();
                if (_wanted != null && !_wanted.Contains(id)) continue;
                if (state.IsReturned(item.Id)) continue;
                if (state.Removable(item) != item.UnitCount) continue;
                double best = 0;
                foreach (var inventory in _data.Inventories)
                {
                    if (!Returned(state, inventory) || !state.Admits(item, inventory)
                        || state.Addable(item, inventory) < item.UnitCount) continue;
                    // This cap limits heuristic work only, never the actual action enumeration.
                    foreach (var placement in state.Positions(item, inventory).Take(32))
                    {
                        _cancel.ThrowIfCancellationRequested();
                        var shape = state.View.Effective(item.Id).Place(item.Shape, placement, inventory.Layout);
                        var blockers = state.View.Contents(inventory.Id).Where(i => i.Id != item.Id
                            && (inventory.Layout is InventoryLayout.Slot || shape.Intersects(state.View.Placed(i.Id)))).ToArray();
                        double lost = 0;
                        foreach (var blocker in blockers)
                        {
                            // Items outside the relaxed subset can be discarded without losing
                            // target value. This affects guidance only; actual moves still replay.
                            if (_wanted != null && !_wanted.Contains(blocker.Id)) continue;
                            if (!alternatives.TryGetValue(blocker.Id, out var places))
                            {
                                places = new();
                                alternatives.Add(blocker.Id, places);
                                if (state.Removable(blocker) == blocker.UnitCount)
                                    foreach (var storage in _data.Inventories)
                                    {
                                        if (!Returned(state, storage) || !state.Admits(blocker, storage)
                                            || state.Addable(blocker, storage) < blocker.UnitCount) continue;
                                        foreach (var alternative in state.Positions(blocker, storage).Take(32))
                                        {
                                            _cancel.ThrowIfCancellationRequested();
                                            if (state.Fits(blocker, storage, alternative, whole: true)) places.Add(new(storage.Id, alternative));
                                        }
                                    }
                            }
                            if (!places.Any(p => p.InventoryId != inventory.Id || inventory.Layout is not InventoryLayout.Slot
                                    && !shape.Intersects(state.View.Effective(blocker.Id).Place(blocker.Shape, p.Placement, inventory.Layout))))
                                lost += _data.Value(blocker.Id);
                        }
                        best = Math.Max(best, (_data.Value(item.Id) - lost) / (1 + blockers.Length));
                        if (blockers.Length == 0) break;
                    }
                }
                value += Math.Max(0, best);
            }
            return value;
        }

        private IEnumerable<TransferStep> Actions(Node node)
        {
            var state = node.State;
            // The second phase remains pending on the frontier, rather than immediately walking
            // every ground coordinate at the original node's preferred priority.
            foreach (var id in ActionIds(node))
            {
                var item = state.Items[id];
                _cancel.ThrowIfCancellationRequested();
                if (state.Removable(item) != item.UnitCount) continue;
                foreach (var inventory in _data.Inventories)
                {
                    if (Returned(state, inventory) == node.BuffersPending || !state.Admits(item, inventory)
                        || state.Addable(item, inventory) < item.UnitCount) continue;
                    foreach (var placement in state.Positions(item, inventory))
                    {
                        _cancel.ThrowIfCancellationRequested();
                        if (item.Location.InventoryId == inventory.Id && item.Location.Placement.Equals(placement)) continue;
                        yield return new(item.Id, new(inventory.Id, placement));
                    }
                }
            }
        }

        private IEnumerable<string> ActionIds(Node node)
        {
            bool preferDiscards = node.BuffersPending && _wanted != null;
            // The relaxation identifies which returned items may need to leave. Try those buffer
            // moves before displacing desired cargo, without removing either group from the search.
            for (int group = 0; group < (preferDiscards ? 2 : 1); group++)
                foreach (var id in _data.ActionOrder)
                {
                    if (preferDiscards)
                    {
                        bool discard = !_wanted!.Contains(id) && node.State.IsReturned(id);
                        if (discard != (group == 0)) continue;
                    }
                    yield return id;
                }
        }

        private static bool Returned(TransferSimulation state, InventoryState inventory) => state.Roots.IsReturn(inventory.Id)
            || inventory.OwnerItemId != null && state.IsReturned(inventory.OwnerItemId);
    }

    private sealed class Node
    {
        internal readonly TransferSimulation State;
        internal readonly Node? Parent;
        internal readonly TransferStep? Step;
        internal readonly PackingStateKey Key;
        internal readonly long Value, Capacity;
        internal readonly int Depth, Hands;
        internal double? Estimate, BufferDelay;
        internal int? Remaining;
        internal IEnumerator<TransferStep>? Candidates;
        internal bool Exhausted, BuffersPending;
        internal Node(TransferSimulation state, Node? parent, TransferStep? step, PackingStateKey key,
            Dictionary<string, long> capacities)
        {
            State = state; Parent = parent; Step = step; Key = key; Value = state.ReturnedValue; Depth = parent == null ? 0 : parent.Depth + 1;
            Hands = state.View.Contents(state.Roots.Left).Count() + state.View.Contents(state.Roots.Right).Count();
            var bag = state.View.Contents(state.Roots.Backpack).FirstOrDefault();
            Capacity = bag != null && capacities.TryGetValue(bag.Id, out long capacity) ? capacity : 0;
        }
        internal bool BetterThan(Node other) => Value != other.Value ? Value > other.Value
            : Hands != other.Hands ? Hands < other.Hands
            : Capacity != other.Capacity ? Capacity > other.Capacity : Depth < other.Depth;
    }
    private sealed class Work
    {
        internal readonly Node Node;
        internal readonly double Priority;
        internal readonly double Estimate;
        internal readonly long Serial;
        internal Work(Node node, double priority, double estimate, long serial)
        { Node = node; Priority = priority; Estimate = estimate; Serial = serial; }
    }
    private sealed class WorkOrder : IComparer<Work>
    {
        public int Compare(Work? a, Work? b)
        {
            int order = b!.Priority.CompareTo(a!.Priority);
            if (order == 0) order = b.Estimate.CompareTo(a.Estimate);
            return order != 0 ? order : a.Serial.CompareTo(b.Serial);
        }
    }
}

