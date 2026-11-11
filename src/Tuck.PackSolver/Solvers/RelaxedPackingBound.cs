using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Tuck.PackSolver.Solvers;

/// <summary>
/// A capacity relaxation for whole-item packing. Pool grid cells, retain the three root-slot
/// admission constraints, and grant all containers' capacity without requiring their acquisition.
/// Every real packing embeds in this relaxation. Its chosen subset guides search only; it never
/// restricts candidate moves. The optimum value is an upper bound, not a feasible layout.
/// </summary>
internal static class RelaxedPackingBound
{
    internal static bool TryCreate(TransferSimulation state, string[] ids, CancellationToken cancel,
        out long bound, out HashSet<string> wanted) => TryCreate(state, ids, cancel, out bound, out wanted, out _);

    internal static bool TryCreate(TransferSimulation state, string[] ids, CancellationToken cancel,
        out long bound, out HashSet<string> wanted, out int minimumHands)
    {
        bound = 0; wanted = new(StringComparer.Ordinal); minimumHands = 0;
        // Preserve the general semantic path for modifiers and nonbinary geometry. Equipment
        // descendants need bundled values; the simple independent-item relaxation does not model them.
        if (!state.FixedGeometry || state.Items.Values.Any(i => !TransferSimulation.IsPackingStorage(state.Inventories[i.Location.InventoryId]))) return false;
        int count = ids.Length;
        var cost = new int[count]; var slots = new int[count]; var value = new long[count];
        string[] roots = { state.Roots.Backpack, state.Roots.Left, state.Roots.Right };
        long capacity = 0, useful = 0;
        foreach (var inventory in state.Inventories.Values)
        {
            cancel.ThrowIfCancellationRequested();
            if (inventory.Id == state.Roots.Ground || inventory.OwnerItemId == null || !TransferSimulation.IsPackingStorage(inventory)) continue;
            capacity += inventory.Layout is InventoryLayout.Grid grid ? grid.Cells.UsableArea : 1;
        }
        decimal total = 0;
        for (int i = 0; i < count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var item = state.Items[ids[i]];
            var shape = state.View.Effective(item.Id).Shape;
            if (shape.OutsideBounds != 0 || !shape.IsBinary) return false;
            int area = shape.Area;
            if (area == 0) return false;
            decimal positive = (decimal)Math.Max(0, state.View.PackUnitValue(item.Id)) * item.UnitCount;
            total += positive;
            if (total > long.MaxValue) return false;
            value[i] = (long)positive;
            cost[i] = int.MaxValue;
            foreach (var inventory in state.Inventories.Values)
            {
                if (inventory.Id == state.Roots.Ground || !TransferSimulation.IsPackingStorage(inventory)) continue;
                // Keeping an existing occupant requires no insertion, including when its container
                // starts on the ground. Acquiring that container returns its contents intact.
                // Excluding such an option would make this relaxation smaller than the real problem.
                bool existing = item.Location.InventoryId == inventory.Id;
                if (!existing && (!state.Admits(item, inventory, ignoreCurrentContainment: true) || state.Addable(item, inventory) < item.UnitCount)) continue;
                int root = Array.IndexOf(roots, inventory.Id);
                if (root >= 0) slots[i] |= 1 << root;
                else cost[i] = Math.Min(cost[i], inventory.Layout is InventoryLayout.Slot ? 1 : area);
            }
            if (cost[i] != int.MaxValue) useful += cost[i];
        }
        capacity = Math.Min(capacity, useful);
        // Bound setup itself is bounded. Falling back keeps every legal action available.
        if (capacity > 4096 || (capacity + 1) * 8 * Math.Max(1, count) > 1_000_000) return false;
        int cells = (int)capacity, states = (cells + 1) * 8;
        var previous = new long[states]; var current = new long[states];
        var choices = new byte[count * states];
        for (int i = 0; i < count; i++)
        {
            for (int c = 0; c <= cells; c++)
            {
                if ((c & 127) == 0) cancel.ThrowIfCancellationRequested();
                for (int mask = 0; mask < 8; mask++)
                {
                    int index = c * 8 + mask;
                    long best = previous[index]; byte choice = 0;
                    if (c >= cost[i] && previous[(c - cost[i]) * 8 + mask] + value[i] > best)
                    { best = previous[(c - cost[i]) * 8 + mask] + value[i]; choice = 1; }
                    for (int slot = 0; slot < 3; slot++)
                        if ((mask & slots[i] & (1 << slot)) != 0 && previous[c * 8 + (mask ^ (1 << slot))] + value[i] > best)
                        { best = previous[c * 8 + (mask ^ (1 << slot))] + value[i]; choice = (byte)(slot + 2); }
                    current[index] = best; choices[i * states + index] = choice;
                }
            }
            (previous, current) = (current, previous);
        }
        bound = previous[states - 1];
        // A real packing attaining bound must also attain it in the relaxation with
        // just its occupied hands available. This lower bound is conditional on that value.
        int fixedHands = 0;
        for (int slot = 1; slot < 3; slot++)
            if (state.View.Contents(roots[slot]).Any(i => state.Removable(i) < i.UnitCount)) fixedHands |= 1 << slot;
        minimumHands = 2;
        for (int mask = 1; mask < 8; mask += 2)
            if ((mask & fixedHands) == fixedHands && previous[cells * 8 + mask] == bound)
                minimumHands = Math.Min(minimumHands, ((mask >> 1) & 1) + ((mask >> 2) & 1));
        int remaining = cells, freeSlots = 7;
        for (int i = count - 1; i >= 0; i--)
        {
            byte choice = choices[i * states + remaining * 8 + freeSlots];
            if (choice == 0) continue;
            wanted.Add(ids[i]);
            if (choice == 1) remaining -= cost[i];
            else freeSlots ^= 1 << (choice - 2);
        }
        return true;
    }
}
