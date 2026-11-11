using System;
using System.Collections.Generic;
using System.Linq;
using A = global::Tuck.PackSolver.Algorithms;

namespace Tuck.PackSolver;

/// <summary>Lossy, private whole-item view for the existing geometric optimizer. Never recorded.</summary>
internal sealed class AlgorithmView
{
    internal A.PackProblem Problem { get; }
    private readonly Dictionary<string, (int X, int Y)> _origins = new();
    internal AlgorithmView(TransferSimulation state)
    {
        var movable = state.Items.Values.Where(i => state.Removable(i) == i.UnitCount && state.View.Effective(i.Id).Shape.Area > 0
                && state.View.Effective(i.Id).Shape.IsBinary && state.View.Effective(i.Id).Shape.OutsideBounds == 0)
            .OrderByDescending(i => state.IsReturned(i.Id)).ThenBy(i => i.Id, StringComparer.Ordinal).ToDictionary(i => i.Id);
        string? MovableOwner(string inventoryId)
        {
            for (var inventory = state.Inventories[inventoryId]; inventory.OwnerItemId != null;
                 inventory = state.Inventories[state.Items[inventory.OwnerItemId].Location.InventoryId])
                if (movable.ContainsKey(inventory.OwnerItemId)) return inventory.OwnerItemId;
            return null;
        }
        bool ReturnedInventory(InventoryState inventory) => state.Roots.IsReturn(inventory.Id)
            || inventory.OwnerItemId != null && state.IsReturned(inventory.OwnerItemId);
        var values = movable.Values.ToDictionary(i => i.Id, i => checked(state.View.PackUnitValue(i.Id) * i.UnitCount));
        long fixedValue = 0;
        foreach (var item in state.Items.Values.Where(i => !movable.ContainsKey(i.Id)))
        {
            long value = checked(state.View.PackUnitValue(item.Id) * item.UnitCount);
            var owner = MovableOwner(item.Location.InventoryId);
            if (owner != null) values[owner] = checked(values[owner] + value);
            else if (state.IsReturned(item.Id)) fixedValue = checked(fixedValue + value);
        }
        var containers = new List<A.PackingContainer>();
        foreach (var inventory in state.Inventories.Values)
        {
            if (inventory.Id == state.Roots.Ground || inventory.OverrideLockInsert || inventory.UnsupportedTransfer != null) continue;
            var owner = MovableOwner(inventory.Id);
            if (owner == null && !ReturnedInventory(inventory)) continue;
            var occupants = state.Items.Values.Where(i => i.Location.InventoryId == inventory.Id && !movable.ContainsKey(i.Id)).ToArray();
            if (inventory.Layout is InventoryLayout.Slot)
            { containers.Add(A.PackingContainer.Slot(inventory.Id, owner, occupants.Length != 0)); continue; }
            var cells = ((InventoryLayout.Grid)inventory.Layout).Cells.Usable.Cells.ToArray();
            if (cells.Length == 0) continue;
            int x = cells.Min(c => c.X), y = cells.Min(c => c.Y);
            _origins[inventory.Id] = (x, y);
            var usable = new A.CellMask(cells.Max(c => c.X) - x + 1, cells.Max(c => c.Y) - y + 1,
                cells.Select(c => new A.GridCell(c.X - x, c.Y - y)));
            var occupied = occupants.SelectMany(i => state.View.Placed(i.Id).Positions(1)).Distinct()
                .Select(c => new A.GridCell(c.X - x, c.Y - y)).Where(c => usable.Contains(c.X, c.Y));
            containers.Add(new A.PackingContainer(inventory.Id, usable, occupied, owner));
        }
        var items = movable.Values.Select(i => new A.PackingItem(i.Id, values[i.Id],
            A.PackingOrientation.FromShape(new A.CellMask(state.View.Effective(i.Id).Shape.Occupied()), allowReflection: true),
            canUseHand: false, allowedContainerIds: containers.Where(c => state.Admits(i, state.Inventories[c.Id], ignoreCurrentContainment: true)
                && state.Addable(i, state.Inventories[c.Id]) >= i.UnitCount).Select(c => c.Id)));
        Problem = new A.PackProblem(items, containers, availableHands: 0, fixedValue);
    }
    internal Dictionary<string, TransferStep> Targets(A.PackPlan layout, TransferSimulation state) =>
        layout.Placements.ToDictionary(p => p.Item.Id, p =>
        {
            var inventory = state.Inventories[p.Container.Id];
            var origin = _origins.TryGetValue(inventory.Id, out var point) ? point : (0, 0);
            var item = state.Items[p.Item.Id];
            Placement placement = inventory.Layout is InventoryLayout.Slot ? new Placement.Slot()
                : state.View.Effective(item.Id).FromEffective(item.Shape,
                    new Placement.Grid(p.X + origin.Item1, p.Y + origin.Item2, p.Orientation.QuarterTurns, p.Orientation.Flipped));
            return new TransferStep(p.Item.Id, new(inventory.Id, placement));
        });
}
