using System;
using System.Collections.Generic;
using System.Linq;
using global::Tuck.PackSolver.Algorithms;

namespace Tuck.PackSolver;

/// <summary>
/// Schedules stable whole-item transfers by start/goal dependencies. Scratch positions are allocated
/// only for cycle members and must avoid all final footprints, so parked items introduce no new goal
/// dependencies. Concrete buffer positions remain part of the memo key: free space is finite.
/// Cases needing buffers on future goals, clearing buffer occupants, discards, moving storage
/// owners or changing shapes use the general sequence search. Half of its work budget is reserved for that fallback.
/// </summary>
internal sealed class DependencyScheduler
{
    private readonly TransferSimulation _initial;
    private readonly TransferStep[] _targets;
    private readonly ItemState[] _items;
    private readonly SequenceBudget _budget;
    private readonly int _nodeLimit, _checkLimit;
    private readonly Dictionary<string, ulong[]> _finalOccupancy = new();
    private readonly List<Footprint>[] _positions;
    private readonly bool[] _buffersGenerated;
    private readonly IEnumerator<Footprint>?[] _bufferCandidates;
    private int[][] _dependencies = Array.Empty<int[]>();
    private readonly HashSet<PositionKey> _visited = new();
    private bool _bufferBoundReached;

    private DependencyScheduler(TransferSimulation initial, TransferStep[] targets, SequenceBudget budget)
    {
        _initial = initial; _targets = targets; _budget = budget;
        _items = targets.Select(t => initial.Items[t.SourceItemId]).ToArray();
        _positions = _items.Select(_ => new List<Footprint>()).ToArray();
        _buffersGenerated = new bool[_items.Length];
        _bufferCandidates = new IEnumerator<Footprint>?[_items.Length];
        _nodeLimit = budget.Nodes + (budget.Options.MaxSequenceNodes - budget.Nodes) / 2;
        _checkLimit = budget.Checks + (budget.Options.MaxPlacementChecks - budget.Checks) / 2;
    }

    internal static List<TransferStep>? TryRun(TransferSimulation state,
        Dictionary<string, TransferStep> targets, HashSet<string> unwanted, SequenceBudget budget)
    {
        // These conditions make geometry, admission and ancestry independent of transfer order.
        if (unwanted.Any(id => state.Items.ContainsKey(id) && state.IsReturned(id))
            || state.Inventories.Values.Any(i => i.UnsupportedParentEffects)
            || state.Items.Values.Any(i => state.Definition(i).Rules.ParentShape != ParentShapeRule.None
                || state.Definition(i).Rules.UnsupportedParentState || i.Shape.OutsideBounds != 0
                || i.Shape.Cells.Any(b => b > 1))) return null;
        var moving = new List<TransferStep>();
        foreach (var target in targets.Values)
        {
            budget.Cancellation.ThrowIfCancellationRequested();
            if (!state.Items.TryGetValue(target.SourceItemId, out var item)) return null;
            if (item.Location.Equals(target.Destination)) continue;
            if (state.Inventories.Values.Any(i => i.OwnerItemId == item.Id)
                || !state.Inventories.TryGetValue(target.Destination.InventoryId, out var inventory)
                || state.Removable(item) != item.UnitCount || !state.Admits(item, inventory)
                || state.Addable(item, inventory) < item.UnitCount) return null;
            moving.Add(target);
        }
        if (moving.Count == 0) return new();
        var scheduler = new DependencyScheduler(state, moving.ToArray(), budget);
        try
        {
            if (!scheduler.HasBudget || !scheduler.Initialize()) return null;
            // Iterative buffer bounds favor schedules requiring little temporary space. A failed bounded
            // search is not a proof that the concrete inventory problem is infeasible.
            for (int buffers = 0; buffers <= moving.Count && scheduler.HasBudget; buffers++)
            {
                scheduler._visited.Clear();
                scheduler._bufferBoundReached = false;
                var result = scheduler.Search(state.Clone(), new int[moving.Count], buffers);
                if (result != null) return result;
                if (!scheduler._bufferBoundReached) break; // More buffers cannot fix exhausted geometric choices.
            }
            return null;
        }
        finally { foreach (var candidates in scheduler._bufferCandidates) candidates?.Dispose(); }
    }

    private bool HasBudget
    {
        get
        {
            _budget.Cancellation.ThrowIfCancellationRequested();
            return _budget.Nodes < _nodeLimit && _budget.Checks < _checkLimit;
        }
    }
    private bool Check()
    {
        _budget.Cancellation.ThrowIfCancellationRequested();
        return _budget.Checks < _checkLimit && _budget.Check();
    }

    private bool Initialize()
    {
        var byId = _items.Select((item, i) => (item.Id, i)).ToDictionary(p => p.Id, p => p.i);
        foreach (var item in _initial.Items.Values)
        {
            if (!Check()) return false;
            bool moving = byId.TryGetValue(item.Id, out int i);
            var location = moving
                ? _targets[i].Destination : item.Location;
            var goal = FootprintAt(item, location);
            if (goal == null) return false;
            if (!_finalOccupancy.TryGetValue(location.InventoryId, out var occupied))
                _finalOccupancy.Add(location.InventoryId, occupied = new ulong[goal.Cells.Length]);
            if (CellBits.Overlap(occupied, goal.Cells)) return false;
            for (int w = 0; w < occupied.Length; w++) occupied[w] |= goal.Cells[w];
            if (!moving) continue;
            var start = FootprintAt(item, item.Location);
            if (start == null) return false;
            _positions[i].Add(start); // 0 = start, 1 = goal, 2+ = concrete buffer placements.
            _positions[i].Add(goal);
        }
        _dependencies = new int[_items.Length][];
        for (int i = 0; i < _items.Length; i++)
        {
            var blockers = new List<int>();
            for (int j = 0; j < _items.Length; j++)
            {
                if (!Check()) return false;
                if (i != j && Overlap(_positions[i][1], _positions[j][0])) blockers.Add(j);
            }
            _dependencies[i] = blockers.ToArray();
        }
        return true;
    }

    private List<TransferStep>? Search(TransferSimulation state, int[] positions, int bufferLimit)
    {
        if (!HasBudget || !_visited.Add(new PositionKey(positions))) return null;
        if (!_budget.Node()) return null;
        var steps = new List<TransferStep>();

        // Goal footprints are disjoint, shapes and owners are stable, and buffers avoid every goal.
        // Thus these moves commute; no search over permutations of ready moves is needed.
        var dependents = Enumerable.Range(0, _items.Length).Select(_ => new List<int>()).ToArray();
        var remaining = new int[_items.Length];
        var ready = new Queue<int>();
        for (int i = 0; i < _items.Length; i++)
        {
            if (positions[i] == 1) continue;
            foreach (int blocker in _dependencies[i])
                if (positions[blocker] == 0) { remaining[i]++; dependents[blocker].Add(i); }
            if (remaining[i] == 0) ready.Enqueue(i);
        }
        while (ready.Count != 0)
        {
            int i = ready.Dequeue();
            if (!Check() || !state.TryApply(_targets[i], out _)) return null;
            bool vacatedStart = positions[i] == 0;
            positions[i] = 1;
            steps.Add(_targets[i]);
            if (vacatedStart)
                foreach (int dependent in dependents[i])
                    if (--remaining[dependent] == 0) ready.Enqueue(dependent);
        }
        if (positions.All(p => p == 1)) return steps;
        if (!HasBudget) return null;
        if (positions.Count(p => p >= 2) >= bufferLimit) { _bufferBoundReached = true; return null; }

        // Branch only on a dependency SCC with no unresolved prerequisites outside it.
        // Relocating a nonmoving occupant can create buffer space without removing a
        // dependency edge; the fallback handles that case.
        var component = DependencyGraph.BlockedComponent(_dependencies, positions);
        foreach (int i in component)
        {
            for (int p = 2; TryBuffer(i, p, out var buffer); p++)
            {
                if (!HasBudget || !Check()) return null;
                bool occupied = false;
                for (int j = 0; j < _items.Length && !occupied; j++)
                    if (i != j) occupied = Overlap(buffer, _positions[j][positions[j]]);
                // Nonmoving items are already covered by _finalOccupancy in BufferCandidates.
                if (occupied) continue;
                var move = new TransferStep(_items[i].Id, buffer.Location);
                var next = state.Clone();
                if (!next.TryApply(move, out _)) continue;
                var nextPositions = (int[])positions.Clone();
                nextPositions[i] = p;
                var tail = Search(next, nextPositions, bufferLimit);
                if (tail == null) continue;
                steps.Add(move); steps.AddRange(tail); return steps;
            }
        }
        return null;
    }

    private bool TryBuffer(int i, int p, out Footprint buffer)
    {
        if (p < _positions[i].Count) { buffer = _positions[i][p]; return true; }
        buffer = null!;
        if (_buffersGenerated[i] || !HasBudget) return false;
        var candidates = _bufferCandidates[i] ??= BufferCandidates(i).GetEnumerator();
        if (candidates.MoveNext())
        {
            buffer = candidates.Current;
            _positions[i].Add(buffer);
            return true;
        }
        candidates.Dispose(); _bufferCandidates[i] = null;
        _buffersGenerated[i] = true;
        return false;
    }

    private IEnumerable<Footprint> BufferCandidates(int i)
    {
        var item = _items[i];
        foreach (var inventory in _initial.Inventories.Values.OrderBy(inv => inv.Id == _initial.Roots.Ground ? 0 : 1))
        {
            if (!_initial.Admits(item, inventory) || _initial.Addable(item, inventory) < item.UnitCount) continue;
            foreach (var placement in _initial.Positions(item, inventory))
            {
                if (!Check()) yield break;
                var location = new ItemLocation(inventory.Id, placement);
                if (location.Equals(item.Location)) continue;
                var footprint = FootprintAt(item, location);
                if (footprint == null || _finalOccupancy.TryGetValue(inventory.Id, out var goals)
                    && CellBits.Overlap(footprint.Cells, goals)) continue;
                yield return footprint;
            }
        }
    }

    private Footprint? FootprintAt(ItemState item, ItemLocation location)
    {
        var inventory = _initial.Inventories[location.InventoryId];
        if (inventory.Layout is InventoryLayout.Slot)
            return location.Placement is Placement.Slot ? new(location, new ulong[] { 1 }) : null;
        if (location.Placement is not Placement.Grid) return null;
        var grid = ((InventoryLayout.Grid)inventory.Layout).Cells;
        var cells = CellBits.Create(grid.Width, grid.Height);
        var placed = _initial.View.Effective(item.Id).Place(item.Shape, location.Placement, inventory.Layout);
        foreach (var (x, y) in placed.Positions(1))
        {
            if (x < 0 || x >= grid.Width || y < 0 || y >= grid.Height || grid.GetLocal(x, y) != 0) return null;
            CellBits.Set(cells, y * grid.Width + x);
        }
        return new(location, cells);
    }
    private static bool Overlap(Footprint a, Footprint b) =>
        a.Location.InventoryId == b.Location.InventoryId && CellBits.Overlap(a.Cells, b.Cells);
    private sealed class Footprint
    {
        internal ItemLocation Location { get; }
        internal ulong[] Cells { get; }
        internal Footprint(ItemLocation location, ulong[] cells) { Location = location; Cells = cells; }
    }
    private sealed class PositionKey : IEquatable<PositionKey>
    {
        private readonly int[] _positions;
        private readonly int _hash;
        internal PositionKey(int[] positions)
        {
            _positions = (int[])positions.Clone();
            var hash = new HashCode();
            foreach (int p in positions) hash.Add(p);
            _hash = hash.ToHashCode();
        }
        public bool Equals(PositionKey? other) => other != null && _positions.SequenceEqual(other._positions);
        public override bool Equals(object? obj) => obj is PositionKey other && Equals(other);
        public override int GetHashCode() => _hash;
    }
}

internal static class DependencyGraph
{
    // Tarjan's SCC algorithm on the unresolved start poses. Edges point to prerequisites.
    internal static int[] BlockedComponent(int[][] dependencies, int[] positions)
    {
        int next = 0;
        var indices = Enumerable.Repeat(-1, positions.Length).ToArray();
        var low = new int[positions.Length];
        var onStack = new bool[positions.Length];
        var stack = new Stack<int>();
        var components = new List<int[]>();
        for (int i = 0; i < positions.Length; i++)
            if (positions[i] == 0 && indices[i] < 0) Visit(i);
        foreach (var component in components)
        {
            var members = new HashSet<int>(component);
            if (component.Length > 1 && component.All(i => dependencies[i].All(j => positions[j] != 0 || members.Contains(j))))
                return component.OrderBy(i => i).ToArray();
        }
        return Array.Empty<int>();

        void Visit(int i)
        {
            indices[i] = low[i] = next++;
            stack.Push(i); onStack[i] = true;
            foreach (int j in dependencies[i])
            {
                if (positions[j] != 0) continue;
                if (indices[j] < 0) { Visit(j); low[i] = Math.Min(low[i], low[j]); }
                else if (onStack[j]) low[i] = Math.Min(low[i], indices[j]);
            }
            if (low[i] != indices[i]) return;
            var component = new List<int>();
            int member;
            do { member = stack.Pop(); onStack[member] = false; component.Add(member); } while (member != i);
            components.Add(component.ToArray());
        }
    }
}
