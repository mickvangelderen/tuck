using System.Collections.Generic;

namespace Tuck.Mod;

internal static class OutputCapacity
{
    private static readonly List<GraphNodeStorage> Storage = new();

    internal static bool Applies(GameItem item, int occupiedPercent)
    {
        if (item.identifier is "chem_finisher" or "blender" or "heating_plate" or "bottle_printer" or "recharger" or "ras" ||
            item.IsTag("BROKEN_MACHINE_TAG") || !MachineryHelper.IsMachineryToggleOn(item) ||
            (item.onCycleEndEarlySlotItemFunc == null && item.onCycleEndSlotItemFunc == null && item.onCycleEndLateSlotItemFunc == null) ||
            item.contentWindow?.childElement is not MachineryPanelRow row) return false;
        if (item.identifier == "plant_dna_manipulator" && !MachineDnaManipulator.IsEjectEnabled(item)) return false;
        string role = item.identifier switch {
            "plant_dna_manipulator" => "eject", "evaporator" => "distillate", "moisture_farm" => "container", _ => "output"
        };
        var panel = row.GetPanel(role);
        if (panel == null) return false;
        bool liquid = item.identifier is "evaporator" or "moisture_farm";
        try
        {
            panel.CollectStorage(Storage);
            foreach (var storage in Storage)
            {
                if (liquid)
                {
                    if (storage is GameSlotInventory receiver && LiquidAtThreshold(receiver.childItem, occupiedPercent)) return true;
                }
                else if (storage is GameGridInventory grid ? GridAtThreshold(grid, occupiedPercent) :
                    storage is GameSlotInventory slot && slot.childItem != null) return true;
            }
            return false;
        }
        finally { Storage.Clear(); }
    }

    internal static bool AtThreshold(long occupied, long total, int percent) =>
        total > 0 && occupied > 0 && (percent == 0 || occupied * 100 >= total * percent);

    internal static bool LiquidAtThreshold(GameItem? container, int percent)
    {
        if (container == null || !container.IsTag(WaterHelper.LIQUID_CONTAINER_TAG)) return false;
        int total = WaterHelper.GetTotalCapacity(container);
        return total > 0 && LiquidAtThreshold(total, WaterHelper.GetFreeCapacity(container), percent);
    }

    internal static bool LiquidAtThreshold(int total, int free, int percent) => AtThreshold((long)total - free, total, percent);

    internal static bool GridAtThreshold(GameGridInventory grid, int percent)
    {
        var shape = grid.inventoryShape;
        if (shape == null) return false;
        // Native HasFreeCell uses untransformed grid bounds. Reuse it for ordinary full-only checks.
        bool fullOnly = percent == 100 && shape.minX == 0 && shape.minY == 0 && shape.orientation == 0 && !shape.flipped;
        if (fullOnly && grid.HasFreeCell()) return false;
        var items = fullOnly ? null : MissingIngredients.Items(grid);
        int total = 0, occupied = 0;
        // No native numeric occupancy API; count each usable cell once, using the same shape test as HasFreeCell.
        for (int y = shape.minY; y <= shape.maxY; y++)
        for (int x = shape.minX; x <= shape.maxX; x++)
        {
            if (shape.Get(x, y) != 0) continue;
            if (fullOnly) return true; // No free cells, and at least one usable cell exists.
            total++;
            foreach (var item in items!)
                if (item.modifiedShape.Intersects(x, y)) { occupied++; break; }
        }
        return AtThreshold(occupied, total, percent);
    }
}
