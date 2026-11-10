using System.Collections.Generic;
using System.Linq;

namespace Tuck.PackSolver;

public static class StateComparison
{
    public static bool SameShape(ShapeState a, ShapeState b) => a.Width == b.Width && a.Height == b.Height
        && a.OutsideBounds == b.OutsideBounds && a.Cells.SequenceEqual(b.Cells);
    public static bool SameFacts(ItemFacts a, ItemFacts b) => a.Types.SequenceEqual(b.Types) && a.Tags.Count == b.Tags.Count
        && a.Tags.All(p => b.Tags.TryGetValue(p.Key, out var value) && p.Value.Equals(value));
    private static bool SameQuantity(QuantityRules a, QuantityRules b) => a.Maximum == b.Maximum && a.Minimum == b.Minimum
        && a.MaxAdd == b.MaxAdd && a.MaxRemove == b.MaxRemove;
    public static bool SameDefinition(ItemDefinition a, ItemDefinition b)
    {
        var x = a.Rules; var y = b.Rules;
        return a.Identifier == b.Identifier && SameQuantity(x.Quantity, y.Quantity)
            && x.ParentShape == y.ParentShape && x.UnsupportedParentState == y.UnsupportedParentState && x.UnsupportedTransfer == y.UnsupportedTransfer;
    }
    public static string? Difference(PackState expected, PackState actual)
    {
        if (expected.Roots.Ground != actual.Roots.Ground || expected.Roots.Backpack != actual.Roots.Backpack
            || expected.Roots.Left != actual.Roots.Left || expected.Roots.Right != actual.Roots.Right) return "Packing roots changed.";
        // Captures can include definitions that no current item uses.
        foreach (var id in expected.Items.Select(i => i.Identifier).Union(actual.Items.Select(i => i.Identifier)))
            if (!expected.Definitions.TryGetValue(id, out var a) || !actual.Definitions.TryGetValue(id, out var b) || !SameDefinition(a, b))
                return $"Definition {id} changed.";
        var items = actual.Items.ToDictionary(i => i.Id);
        if (items.Count != expected.Items.Count) return "Item membership changed.";
        foreach (var a in expected.Items)
        {
            if (!items.TryGetValue(a.Id, out var b)) return $"Missing item {a.Id}.";
            if (a.Identifier != b.Identifier || !a.Location.Equals(b.Location) || a.UnitCount != b.UnitCount
                || a.UnitValue != b.UnitValue || a.UnitBaseValue != b.UnitBaseValue || !SameShape(a.Shape, b.Shape) || !SameFacts(a.State, b.State))
                return $"Item {a.Id} differs from the expected state.";
        }
        var inventories = actual.Inventories.ToDictionary(i => i.Id);
        if (inventories.Count != expected.Inventories.Count) return "Inventory membership changed.";
        foreach (var a in expected.Inventories)
        {
            if (!inventories.TryGetValue(a.Id, out var b)) return $"Missing inventory {a.Id}.";
            bool sameLayout = a.Layout is InventoryLayout.Grid ag && b.Layout is InventoryLayout.Grid bg && SameShape(ag.Cells, bg.Cells)
                || a.Layout is InventoryLayout.Slot ass && b.Layout is InventoryLayout.Slot bs && ass.Orientation == bs.Orientation;
            if (a.OwnerItemId != b.OwnerItemId || !sameLayout || a.OverrideLockInsert != b.OverrideLockInsert
                || a.OverrideLockRemove != b.OverrideLockRemove || !SameQuantity(a.Quantity, b.Quantity)
                || a.UnsupportedTransfer != b.UnsupportedTransfer || a.UnsupportedParentEffects != b.UnsupportedParentEffects
                || a.Admission.Kind != b.Admission.Kind || a.Admission.SlotIndex != b.Admission.SlotIndex
                || a.Admission.AllowsHazardousWaste != b.Admission.AllowsHazardousWaste
                || !SameList(a.Admission.AllowedTags, b.Admission.AllowedTags) || !SameList(a.Admission.AllowedIdentifiers, b.Admission.AllowedIdentifiers))
                return $"Inventory {a.Id} differs from the expected state.";
        }
        return null;
    }
    private static bool SameList(IEnumerable<string>? a, IEnumerable<string>? b) => a == null ? b == null : b != null && a.SequenceEqual(b);
}
