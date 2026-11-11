using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Tuck.PackSolver.Solvers;

/// <summary>A final container hierarchy and a geometry-free, whole-item allocation used only as guidance.</summary>
internal sealed class ContainerAssignment
{
    internal readonly Dictionary<string, string> Parents;
    internal readonly HashSet<string> Storage;
    internal readonly Dictionary<string, string> Allocation;
    internal readonly Dictionary<string, ItemLocation> ContainerLocations;
    internal readonly decimal Value;
    internal readonly int Hands, Changes;
    internal readonly long Capacity;

    internal ContainerAssignment(Dictionary<string, string> parents, HashSet<string> storage,
        Dictionary<string, string> allocation, Dictionary<string, ItemLocation> containerLocations,
        decimal value, int hands, long capacity, int changes)
    { Parents = parents; Storage = storage; Allocation = allocation; ContainerLocations = containerLocations;
        Value = value; Hands = hands; Capacity = capacity; Changes = changes; }

    internal bool Matches(TransferSimulation state) => Parents.All(p => state.Items[p.Key].Location.InventoryId == p.Value);
}

/// <summary>
/// Enumerate acyclic container parents, then score each with an affinity-constrained multiple knapsack DP.
/// Each bin keeps its own cell/slot capacity; each item is used once. Shells consume space in their parent.
/// Equipment travels bundled with its owner. No speculative hierarchy is applied to the simulator.
/// </summary>
internal sealed class ContainerAssignments
{
    private readonly TransferSimulation state;
    private readonly CancellationToken cancel;
    private readonly int limit;
    private readonly InventoryState[] inventories;
    private readonly Dictionary<string, long> capacities;
    private readonly Dictionary<string, decimal> values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> areas = new(StringComparer.Ordinal);
    private readonly HashSet<string> movable;
    private readonly string[] owners;
    private readonly Dictionary<string, string[]> choices;
    private readonly List<ContainerAssignment> results = new();
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);
    private int branches;
    private readonly int maxPlacementChecks;
    internal int PlacementChecks { get; private set; }
    internal bool Limited { get; private set; }

    internal ContainerAssignments(TransferSimulation state, Dictionary<string, long> capacities, int limit, CancellationToken cancel,
        int maxPlacementChecks = int.MaxValue)
    {
        this.state = state; this.capacities = capacities; this.limit = limit; this.cancel = cancel;
        this.maxPlacementChecks = maxPlacementChecks;
        inventories = state.Inventories.Values.Where(TransferSimulation.IsPackingStorage).OrderBy(i => i.Id, StringComparer.Ordinal).ToArray();
        movable = new(state.Items.Values.Where(i => state.Removable(i) >= i.UnitCount).Select(i => i.Id), StringComparer.Ordinal);
        foreach (var item in state.Items.Values)
        {
            cancel.ThrowIfCancellationRequested();
            var unit = item;
            while (!TransferSimulation.IsPackingStorage(state.Inventories[unit.Location.InventoryId]))
                unit = state.Items[state.Inventories[unit.Location.InventoryId].OwnerItemId!];
            values.TryGetValue(unit.Id, out decimal value);
            values[unit.Id] = value + (decimal)state.View.PackUnitValue(item.Id) * item.UnitCount;
            areas[item.Id] = state.View.Effective(item.Id).Shape.Area;
        }
        owners = capacities.Keys.OrderByDescending(id => capacities[id]).ThenBy(id => id, StringComparer.Ordinal).ToArray();
        choices = owners.ToDictionary(id => id, id => !movable.Contains(id) ? new[] { state.Items[id].Location.InventoryId }
            : inventories.Where(i => i.OwnerItemId != id
                && (i.Id == state.Items[id].Location.InventoryId || state.Admits(state.Items[id], i, ignoreCurrentContainment: true)
                    && state.Addable(state.Items[id], i) >= state.Items[id].UnitCount))
            .OrderBy(i => i.Id == state.Roots.Backpack ? 0 : i.OwnerItemId != null ? 1
                : i.Id == state.Roots.Ground ? 3 : 2)
            .ThenBy(i => i.Id != state.Items[id].Location.InventoryId).ThenBy(i => i.Id, StringComparer.Ordinal)
            .Select(i => i.Id).ToArray(), StringComparer.Ordinal);
    }

    internal List<ContainerAssignment> Run()
    {
        if (limit == 0) { Limited = true; return results; }
        // Include the current hierarchy even if the enumeration budget ends before reaching it.
        Evaluate(owners.ToDictionary(id => id, id => state.Items[id].Location.InventoryId, StringComparer.Ordinal));
        Visit(0, new(StringComparer.Ordinal));
        return results.OrderByDescending(a => a.Value).ThenBy(a => a.Hands).ThenByDescending(a => a.Capacity)
            .ThenBy(a => a.Changes).ToList();
    }

    private void Visit(int index, Dictionary<string, string> parents)
    {
        cancel.ThrowIfCancellationRequested();
        // Bound rejected partial branches too, not just completed arrangements.
        if (Limited || seen.Count >= limit || branches++ >= Math.Max(1L, limit) * Math.Max(16, owners.Length * 16L))
        { Limited = true; return; }
        if (index == owners.Length) { Evaluate(parents); return; }
        string id = owners[index];
        foreach (string inventory in choices[id])
        {
            parents[id] = inventory;
            if (!HasCycle(id, parents) && FitsArea(parents)) Visit(index + 1, parents);
            parents.Remove(id);
            if (Limited) break;
        }
    }

    private bool HasCycle(string id, Dictionary<string, string> parents)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(id))
        {
            if (!parents.TryGetValue(id, out string? inventory)) return false;
            string? owner = state.Inventories[inventory].OwnerItemId;
            if (owner == null) return false;
            id = owner;
        }
        return true;
    }

    private int Cost(string id, InventoryState inventory) => inventory.Layout is InventoryLayout.Slot ? 1 : areas[id];
    private static int Cells(InventoryState inventory) => inventory.Layout is InventoryLayout.Grid grid ? grid.Cells.UsableArea : 1;

    private bool FitsArea(Dictionary<string, string> parents)
    {
        foreach (var group in parents.Where(p => p.Value != state.Roots.Ground).GroupBy(p => p.Value))
        {
            var inventory = state.Inventories[group.Key];
            long used = group.Sum(p => (long)Cost(p.Key, inventory));
            used += state.View.Contents(inventory.Id).Where(i => !parents.ContainsKey(i.Id) && !capacities.ContainsKey(i.Id)
                && !movable.Contains(i.Id)).Sum(i => (long)Cost(i.Id, inventory));
            if (used > Cells(inventory)) return false;
        }
        return true;
    }

    private bool Returned(string inventory, Dictionary<string, string> parents)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(inventory))
        {
            var owner = state.Inventories[inventory].OwnerItemId;
            if (owner == null) return state.Roots.IsReturn(inventory);
            inventory = parents.TryGetValue(owner, out string? parent) ? parent : state.Items[owner].Location.InventoryId;
        }
        return false;
    }

    private void Evaluate(Dictionary<string, string> parents)
    {
        cancel.ThrowIfCancellationRequested();
        string key = string.Concat(owners.Select(id => parents[id].Length + ":" + parents[id]));
        if (!seen.Add(key)) return;
        var containerLocations = PlaceContainers(parents);
        if (containerLocations == null) return;
        var storage = inventories.Where(i => Returned(i.Id, parents)).ToArray();
        var remaining = storage.Select(Cells).ToArray();
        var fixedAllocation = new Dictionary<string, string>(parents, StringComparer.Ordinal);
        decimal fixedValue = 0;
        foreach (var entry in values)
        {
            if (!parents.ContainsKey(entry.Key) && movable.Contains(entry.Key)) continue;
            string destination = parents.TryGetValue(entry.Key, out string? parent) ? parent : state.Items[entry.Key].Location.InventoryId;
            fixedAllocation[entry.Key] = destination;
            int bin = Array.FindIndex(storage, i => i.Id == destination);
            if (bin < 0) continue;
            fixedValue += entry.Value;
            remaining[bin] -= Cost(entry.Key, storage[bin]);
        }
        if (remaining.Any(c => c < 0)) return;
        var optional = values.Where(p => !fixedAllocation.ContainsKey(p.Key)).OrderByDescending(p => p.Value)
            .ThenBy(p => p.Key, StringComparer.Ordinal).ToArray();
        var admissions = optional.ToDictionary(p => p.Key, p => Enumerable.Range(0, storage.Length)
            .Where(b => storage[b].Id == state.Items[p.Key].Location.InventoryId
                || state.Admits(state.Items[p.Key], storage[b], ignoreCurrentContainment: true)
                    && state.Addable(state.Items[p.Key], storage[b]) >= state.Items[p.Key].UnitCount).ToArray());
        // Equivalent slots share one capacity dimension. Their identities remain in traceback;
        // merging can change a movement tie, but cannot change achievable relaxed value or hand count.
        var dimensions = Enumerable.Range(0, storage.Length).GroupBy(b => storage[b].Layout is InventoryLayout.Slot
            ? "slot:" + (storage[b].Id == state.Roots.Left || storage[b].Id == state.Roots.Right) + ":"
                + string.Concat(optional.Select(p => admissions[p.Key].Contains(b) ? '1' : '0'))
            : "grid:" + b).Select(g => g.ToArray()).ToArray();
        var labels = new List<Allocation> { new(remaining, fixedValue, 0, null, null, null) };
        foreach (var entry in optional)
        {
            cancel.ThrowIfCancellationRequested();
            var item = state.Items[entry.Key];
            var bins = admissions[item.Id];
            var next = new Dictionary<string, Allocation>(StringComparer.Ordinal);
            foreach (var label in labels)
            {
                cancel.ThrowIfCancellationRequested();
                Keep(new(label.Remaining, label.Value, label.Changes + (state.IsReturned(item.Id) ? 1 : 0), label, null, null));
                foreach (int bin in bins)
                {
                    int cost = Cost(item.Id, storage[bin]);
                    if (label.Remaining[bin] < cost) continue;
                    var free = (int[])label.Remaining.Clone(); free[bin] -= cost;
                    Keep(new(free, label.Value + entry.Value, label.Changes + (item.Location.InventoryId == storage[bin].Id ? 0 : 1),
                        label, item.Id, storage[bin].Id));
                }
            }
            // shortcut: cap the capacity-vector frontier; widen it if captures show poor hierarchy ranking.
            labels = next.Values.OrderByDescending(a => a.Value).ThenBy(a => Hands(a.Remaining)).ThenBy(a => a.Changes).Take(512).ToList();

            void Keep(Allocation allocation)
            {
                string capacityKey = string.Join(",", dimensions.Select(group => group.Sum(b => allocation.Remaining[b])));
                if (!next.TryGetValue(capacityKey, out var old) || allocation.Value > old.Value
                    || allocation.Value == old.Value && allocation.Changes < old.Changes) next[capacityKey] = allocation;
            }
        }
        var best = labels.OrderByDescending(a => a.Value).ThenBy(a => Hands(a.Remaining)).ThenBy(a => a.Changes).First();
        for (var label = best; label != null; label = label.Previous)
            if (label.Item != null) fixedAllocation[label.Item] = label.Inventory!;
        string? equipped = parents.FirstOrDefault(p => p.Value == state.Roots.Backpack).Key;
        results.Add(new(new(parents, StringComparer.Ordinal), new(storage.Select(i => i.Id), StringComparer.Ordinal), fixedAllocation, containerLocations,
            best.Value, Hands(best.Remaining), equipped != null ? capacities[equipped] : 0,
            best.Changes + parents.Count(p => p.Value != state.Items[p.Key].Location.InventoryId)));

        int Hands(int[] free) => Enumerable.Range(0, storage.Length)
            .Count(b => (storage[b].Id == state.Roots.Left || storage[b].Id == state.Roots.Right) && free[b] == 0);
    }

    // Establish a geometric witness before valuing loose loot. Fixed contents reserve their exact
    // footprints; movable loose items are omitted. This proves a layout, not an executable sequence.
    private Dictionary<string, ItemLocation>? PlaceContainers(Dictionary<string, string> parents)
    {
        var locations = new Dictionary<string, ItemLocation>(StringComparer.Ordinal);
        foreach (var group in parents.GroupBy(p => p.Value))
        {
            cancel.ThrowIfCancellationRequested();
            var inventory = state.Inventories[group.Key];
            var occupied = state.View.Contents(inventory.Id).Where(i => !movable.Contains(i.Id))
                .Select(i => state.View.Placed(i.Id)).ToList();
            var pending = group.Select(p => state.Items[p.Key]).Where(i => movable.Contains(i.Id))
                .OrderByDescending(i => areas[i.Id]).ThenBy(i => i.Id, StringComparer.Ordinal).ToArray();
            foreach (var entry in group.Where(p => !movable.Contains(p.Key))) locations[entry.Key] = state.Items[entry.Key].Location;
            if (!Place(0)) return null;

            bool Place(int index)
            {
                cancel.ThrowIfCancellationRequested();
                if (index == pending.Length) return true;
                var item = pending[index];
                var positions = state.Positions(item, inventory);
                if (item.Location.InventoryId == inventory.Id) positions = new[] { item.Location.Placement }.Concat(positions).Distinct();
                foreach (var position in positions)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (PlacementChecks >= maxPlacementChecks) { Limited = true; return false; }
                    PlacementChecks++;
                    var shape = state.View.Effective(item.Id).Place(item.Shape, position, inventory.Layout);
                    if (inventory.Layout is InventoryLayout.Slot ? occupied.Count != 0
                        : !shape.Fits(((InventoryLayout.Grid)inventory.Layout).Cells)
                            || occupied.Any(shape.Intersects)) continue;
                    occupied.Add(shape); locations[item.Id] = new(inventory.Id, position);
                    if (Place(index + 1)) return true;
                    occupied.RemoveAt(occupied.Count - 1); locations.Remove(item.Id);
                    if (Limited) return false;
                }
                return false;
            }
        }
        return locations;
    }

    private sealed class Allocation
    {
        internal readonly int[] Remaining;
        internal readonly decimal Value;
        internal readonly int Changes;
        internal readonly Allocation? Previous;
        internal readonly string? Item, Inventory;
        internal Allocation(int[] remaining, decimal value, int changes, Allocation? previous, string? item, string? inventory)
        { Remaining = remaining; Value = value; Changes = changes; Previous = previous; Item = item; Inventory = inventory; }
    }
}
