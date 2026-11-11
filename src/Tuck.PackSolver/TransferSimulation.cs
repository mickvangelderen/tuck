using System;
using System.Collections.Generic;
using System.Linq;
using Board = global::Tuck.BitBoard.BitBoard;

namespace Tuck.PackSolver;

public static class PlanReplay
{
    public static PackState Replay(PackState initial, PackPlan plan)
    {
        if (initial.StateId != plan.InitialStateId) throw new ArgumentException("Plan belongs to another capture.");
        var simulation = new TransferSimulation(initial);
        for (int index = 0; index < plan.Steps.Count; index++)
            if (!simulation.TryApply(plan.Steps[index], out var reason))
                throw new InvalidOperationException($"Step {index}: {reason}");
        return simulation.Snapshot();
    }
    public static long ReturnedValue(PackState state) => new StateView(state).ReturnedValue;
    public static bool IsReturned(PackState state, string itemId) => new StateView(state).IsReturned(itemId);
}

/// <summary>
/// Per-search working copy enforcing whole-item packing. Stack manipulation belongs to the user,
/// even when captured game rules permit it. Each accepted placement replaces the derived view.
/// </summary>
internal sealed class TransferSimulation
{
    private readonly PackState _initial;
    private readonly bool _fixedGeometry;
    private readonly Dictionary<(string Item, string Inventory), Placement[]> _positions;
    private bool? _validGeometry;
    private StateView? _view;
    internal Dictionary<string, ItemState> Items { get; private set; }
    internal Dictionary<string, InventoryState> Inventories { get; }
    internal PackRoots Roots => _initial.Roots;
    internal bool FixedGeometry => _fixedGeometry;
    internal StateView View => _view ??= new(_initial, Items, Inventories);
    internal ItemDefinition Definition(ItemState item) => _initial.Definitions[item.Identifier];
    internal TransferSimulation(PackState initial, bool incremental = true)
    {
        _initial = initial; Items = initial.Items.ToDictionary(i => i.Id); Inventories = initial.Inventories.ToDictionary(i => i.Id);
        // Conservative certificate: unusual parent modifiers keep the full derivation path.
        _fixedGeometry = incremental && initial.Definitions.Values.All(d => d.Rules.ParentShape == ParentShapeRule.None && !d.Rules.UnsupportedParentState)
            && initial.Inventories.All(i => !i.UnsupportedParentEffects);
        _positions = new();
    }
    private TransferSimulation(TransferSimulation other)
    {
        _initial = other._initial; Items = new(other.Items); Inventories = other.Inventories;
        _view = other._view;
        _fixedGeometry = other._fixedGeometry; _positions = other._positions; _validGeometry = other._validGeometry;
    }
    internal TransferSimulation Clone() => new(this);
    internal PackState Snapshot() => new(_initial.StateId, Roots, Items.Values.OrderBy(i => i.Id, StringComparer.Ordinal),
        Inventories.Values, _initial.Definitions.Values);
    internal long ReturnedValue => View.ReturnedValue;
    internal bool IsReturned(string itemId) => View.IsReturned(itemId);
    internal bool IsDescendant(string itemId, string ancestor) => View.IsDescendant(itemId, ancestor);
    internal ItemFacts Facts(ItemState item) => View.Effective(item.Id).State;
    internal bool Protected(string id) => Items.Values.Any(i => Facts(i).IsTag("PINNED_TAG") && IsDescendant(i.Id, id));
    // Only explicitly modeled storage exposes nested capacity to packing. Equipment sockets
    // and unclassified inventories retain their contents, even without callbacks or locks.
    internal static bool IsPackingStorage(InventoryState inventory) => inventory.OwnerItemId == null
        || inventory.Admission.Kind is AdmissionKind.Container or AdmissionKind.Toolbox;
    internal int Removable(ItemState item)
    {
        var source = Inventories[item.Location.InventoryId];
        var rule = Definition(item).Rules;
        var facts = Facts(item);
        if (!IsPackingStorage(source) || rule.UnsupportedTransfer != null || source.UnsupportedTransfer != null || source.OverrideLockRemove || Protected(item.Id)
            || !View.IsEffectivelyOwned(item.Id) || facts.IsTag("LOCKED_TAG") || facts.IsTag("MODULE_STUCK_TAG")
            || facts.IsTag("DEBRIS_PILE_TAG") || facts.IsTag("ITEM_BEHIND_LOCK_TAG")) return 0;
        if (source.OwnerItemId != null && !Facts(Items[source.OwnerItemId]).IsTag("IS_OWNED_TAG")) return 0;
        int removable = Math.Max(0, Math.Min(item.UnitCount - Math.Max(rule.Quantity.Minimum, source.Quantity.Minimum),
            Math.Min(rule.Quantity.MaxRemove, source.Quantity.MaxRemove)));
        return removable >= item.UnitCount ? item.UnitCount : 0;
    }
    internal bool Admits(ItemState item, InventoryState inventory, bool ignoreCurrentContainment = false)
    {
        var facts = Facts(item);
        if (!IsPackingStorage(inventory) || inventory.UnsupportedTransfer != null || inventory.OverrideLockInsert || !facts.IsTag("IS_OWNED_TAG")) return false;
        ItemState? owner = inventory.OwnerItemId == null ? null : Items[inventory.OwnerItemId];
        var ownerFacts = owner == null ? null : Facts(owner);
        if (owner != null && (!ownerFacts!.IsTag("IS_OWNED_TAG") || !ignoreCurrentContainment && IsDescendant(owner.Id, item.Id))) return false;
        var rule = inventory.Admission;
        switch (rule.Kind)
        {
            case AdmissionKind.Owned: return true;
            case AdmissionKind.Ground: return rule.AllowsHazardousWaste || !facts.IsTag("HAZARDOUS_WASTE_TAG");
            case AdmissionKind.Backpack: return facts.IsTag("BACKPACK_TAG");
            case AdmissionKind.Container:
                bool nestable = facts.IsTag("NESTABLE_DEVICE_TAG")
                    || facts.IsTag("STORABLE_PACKAGE_TAG") && !facts.IsTag("BACKPACK_TAG") && ownerFacts != null && ownerFacts.Types.Contains("STORAGE");
                if ((facts.IsTag("BACKPACK_TAG") || facts.IsTag("CONTAINER_TAG")) && !nestable) return false;
                if (facts.Types.Contains("MACHINE") && !facts.IsTag("NESTABLE_DEVICE_TAG")) return false;
                return rule.AllowedTags == null || rule.AllowedTags.Any(facts.IsTag)
                    || rule.AllowedIdentifiers != null && rule.AllowedIdentifiers.Contains(item.Identifier);
            case AdmissionKind.Tagged: return rule.AllowedTags != null && rule.AllowedTags.Any(facts.IsTag);
            case AdmissionKind.Toolbox:
                return ownerFacts != null && rule.SlotIndex < Math.Clamp(ownerFacts.Int("TOOLBOX_UNLOCKED_SLOTS_INT"), 3, 9) &&
                    (facts.Types.Contains("TOOL") || facts.Types.Contains("MELEE_WEAPON") || facts.IsTag("CAN_USE_ON_NON_OWNED_TAG")
                     || facts.IsTag("TOOLBOX_ITEM_TAG") || facts.IsTag("LIQUID_CONTAINER_DROPPER_TAG")
                     || item.Identifier is "labeler" or "labeler_blue" or "surgery_tool" or "uv_filter");
            default: return false;
        }
    }
    internal int Addable(ItemState item, InventoryState inventory, ItemState? target = null)
    {
        // A target denotes stacking, which is never a packing action. Report zero so candidate
        // generators cannot mistake game stack capacity for an available packing transition.
        if (target != null) return 0;
        var quantity = Definition(item).Rules.Quantity;
        int maximum = quantity.Maximum == null && inventory.Quantity.Maximum == null ? 1
            : Math.Min(quantity.Maximum ?? int.MaxValue, inventory.Quantity.Maximum ?? int.MaxValue);
        int addable = Math.Min(maximum, Math.Min(quantity.MaxAdd, inventory.Quantity.MaxAdd));
        return addable >= item.UnitCount ? item.UnitCount : 0;
    }
    internal bool Fits(ItemState item, InventoryState inventory, Placement placement, bool whole)
    {
        if (!TryPlace(item, inventory, placement, out var placed)) return false;
        foreach (var occupant in View.Contents(inventory.Id))
            if ((!whole || occupant.Id != item.Id) && (placed == null || placed.Intersects(View.Placed(occupant.Id)))) return false;
        return true;
    }

    // Candidate-discovery hints only: an empty result does not establish transfer legality.
    // Quantity/admission failures and changed ancestor geometry need not identify an obstruction.
    internal List<ItemState>? Obstructions(TransferStep step)
    {
        if (!Items.TryGetValue(step.SourceItemId, out var item)
            || !Inventories.TryGetValue(step.Destination.InventoryId, out var inventory)
            || Removable(item) < item.UnitCount || !Admits(item, inventory) || Addable(item, inventory) < item.UnitCount
            || !TryPlace(item, inventory, step.Destination.Placement, out var placed)) return null;
        var blockers = new List<ItemState>();
        foreach (var occupant in View.Contents(inventory.Id))
            if (occupant.Id != item.Id && (placed == null || placed.Intersects(View.Placed(occupant.Id))))
            {
                if (Removable(occupant) < occupant.UnitCount) return null;
                blockers.Add(occupant);
            }
        return blockers;
    }

    private bool TryPlace(ItemState item, InventoryState inventory, Placement placement, out PlacedShape? placed)
    {
        placed = null;
        if (inventory.Layout is InventoryLayout.Slot) return placement is Placement.Slot;
        if (placement is not Placement.Grid) return false;
        placed = View.Effective(item.Id).Place(item.Shape, placement, inventory.Layout);
        var grid = ((InventoryLayout.Grid)inventory.Layout).Cells;
        if ((placed.Width > grid.Width || placed.Height > grid.Height) && (placed.Width > grid.Height || placed.Height > grid.Width)) return false;
        return placed.Fits(grid);
    }
    internal IEnumerable<Placement> Positions(ItemState item, InventoryState inventory)
    {
        // Large grids remain streamed: caching must not turn a bounded search into unbounded setup.
        if (!_fixedGeometry || inventory.Layout is InventoryLayout.Grid grid && (long)grid.Cells.Width * grid.Cells.Height > 4096)
            return EnumeratePositions(item, inventory);
        var key = (item.Id, inventory.Id);
        if (!_positions.TryGetValue(key, out var positions))
            _positions.Add(key, positions = EnumeratePositions(item, inventory).ToArray());
        return positions;
    }
    private IEnumerable<Placement> EnumeratePositions(ItemState item, InventoryState inventory)
    {
        if (inventory.Layout is InventoryLayout.Slot) { yield return new Placement.Slot(); yield break; }
        var grid = ((InventoryLayout.Grid)inventory.Layout).Cells;
        if (grid.UsableArea == 0) yield break;
        var (minX, minY, maxX, maxY) = Bounds(grid.Usable);
        var effective = View.Effective(item.Id);
        if (effective.Shape.Area == 0) yield break;
        var binaryShapes = new HashSet<Board>();
        var shapes = new HashSet<string>();
        for (int flip = 0; flip < 2; flip++)
            for (int rotation = 0; rotation < 4; rotation++)
            {
                var occupied = effective.Shape.Occupied(rotation, flip != 0);
                // Non-1 labels also collide. Occupied-cell symmetry alone is not equivalence.
                // With mutable parent shapes, an apparently equivalent orientation can diverge later.
                if (_fixedGeometry)
                {
                    if (effective.Shape.IsBinary)
                    {
                        if (!binaryShapes.Add(occupied)) continue;
                    }
                    else
                    {
                        var transformed = new PlacedShape(effective.Shape, new(orientation: rotation, flipped: flip != 0));
                        var signature = new System.Text.StringBuilder().Append(transformed.GlobalWidth).Append(':').Append(transformed.GlobalHeight).Append(':');
                        for (int row = 0; row < transformed.GlobalHeight; row++)
                            for (int column = 0; column < transformed.GlobalWidth; column++) signature.Append(transformed.Get(column, row)).Append(',');
                        if (!shapes.Add(signature.ToString())) continue;
                    }
                }
                var bounds = Bounds(occupied);
                for (int y = minY - bounds.MinY; y <= maxY - bounds.MaxY; y++)
                    for (int x = minX - bounds.MinX; x <= maxX - bounds.MaxX; x++)
                        yield return effective.FromEffective(item.Shape, new(x, y, rotation, flip != 0));
            }
    }

    private static (int MinX, int MinY, int MaxX, int MaxY) Bounds(Board board)
    {
        int minX = board.Width, minY = board.Height, maxX = -1, maxY = -1;
        foreach (var (x, y) in board.Cells)
        {
            minX = Math.Min(minX, x); minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
        }
        return (minX, minY, maxX, maxY);
    }
    internal bool TryApply(TransferStep step, out string reason)
    {
        if (!TryCreateSuccessor(step, out var next, out reason)) return false;
        Items = next.Items; _view = next._view; _validGeometry = next._validGeometry;
        return true;
    }
    internal bool TryCreateSuccessor(TransferStep step, out TransferSimulation next, out string reason)
    {
        next = null!;
        reason = "";
        if (!Items.TryGetValue(step.SourceItemId, out var item)) return Fail("Missing source.", out reason);
        var location = step.Destination;
        if (!Inventories.TryGetValue(location.InventoryId, out var destination)) return Fail("Missing destination.", out reason);
        if (!IsPackingStorage(Inventories[item.Location.InventoryId]) || !IsPackingStorage(destination))
            return Fail("Packing cannot change equipment or unclassified inventory contents.", out reason);
        if (item.UnitCount > Removable(item)) return Fail("Source is protected or quantity cannot be removed.", out reason);
        if (!Admits(item, destination)) return Fail("Destination does not admit the item.", out reason);
        if (item.UnitCount > Addable(item, destination)) return Fail("Destination quantity limit.", out reason);
        if (!Fits(item, destination, location.Placement, whole: true)) return Fail("Invalid placement.", out reason);
        bool localCheckSuffices = _fixedGeometry && (_validGeometry ??= ValidGeometry());
        next = Clone();
        next.Items[item.Id] = item.At(location);
        next._view = new StateView(View, next.Items, _fixedGeometry);
        if (localCheckSuffices) { next._validGeometry = true; return true; }
        // Re-derive after the tree changes. This checks changed ancestors as well as the destination.
        try
        {
            foreach (var changed in next.Items.Values)
            {
                _ = next.View.Effective(changed.Id);
                if (!next.Fits(changed, next.Inventories[changed.Location.InventoryId], changed.Location.Placement, whole: true))
                    return Fail("Transfer would invalidate an item's or ancestor's geometry.", out reason);
                if (Items.TryGetValue(changed.Id, out var old) && !StateComparison.SameShape(View.Effective(old.Id).Shape, next.View.Effective(changed.Id).Shape)
                    && (Protected(changed.Id) || Definition(changed).Rules.UnsupportedTransfer != null
                        || !next.Admits(changed, next.Inventories[changed.Location.InventoryId])))
                    return Fail("Transfer changes a protected or unsupported ancestor.", out reason);
            }
        }
        catch (UnsupportedRuleException error) { return Fail(error.Message, out reason); }
        next._validGeometry = true;
        return true;
    }
    private bool ValidGeometry()
    {
        foreach (var item in Items.Values)
            if (!Fits(item, Inventories[item.Location.InventoryId], item.Location.Placement, whole: true)) return false;
        return true;
    }
    private static bool Fail(string message, out string reason) { reason = message; return false; }
    internal string Key() => string.Join("|", Items.Values.OrderBy(i => i.Id, StringComparer.Ordinal).Select(i =>
        $"{i.Id}:{i.Location.InventoryId}:{i.UnitCount}:" + (i.Location.Placement is Placement.Grid p ? $"{p.X},{p.Y},{p.Orientation},{p.Flipped}" : "slot")));
}
