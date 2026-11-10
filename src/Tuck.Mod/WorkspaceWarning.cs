using HarmonyLib;

namespace Tuck.Mod;

/// <summary>Warns before going outside when workspace occupancy exceeds the configured threshold.</summary>
[HarmonyPatch(typeof(MapUIManager), nameof(MapUIManager.IsShowingGoOutsideConfirm))]
internal static class WorkspaceWarning
{
    // The loader owns the patch; leave it inert outside the mod's enabled lifetime.
    private static Options? _options;

    public static void Register(Options options) => _options = options;

    public static void Unregister() => _options = null;

    internal static bool Prefix(ref bool __result)
    {
        if (_options == null) return true;

        float threshold = _options.WorkspaceOccupiedPercent.Value;
        // The vanilla setting must preserve the original method, including other mods' changes.
        if (threshold <= 0f) return true;

        __result = RequiresWarning(EmporiumEntry.Instance.backInvinvElement, threshold);
        return false;
    }

    internal static bool RequiresWarning(GameGridInventory workspace, float occupiedThreshold)
    {
        if (occupiedThreshold >= 100f) return false;

        var items = workspace.childItems;
        // Vanilla warns whenever there is an item, even if its footprint has no occupied cells.
        if (occupiedThreshold <= 0f) return items.Count > 0;

        var shape = workspace.inventoryShape;
        int total = 0, occupiedCells = 0;
        for (int y = shape.minY; y <= shape.maxY; y++)
            for (int x = shape.minX; x <= shape.maxX; x++)
            {
                if (shape.Get(x, y) != 0) continue;
                total++;
                bool occupied = false;
                foreach (var item in items)
                {
                    // Includes rotation and holes; only the container's footprint occupies the workspace.
                    if (!item.modifiedShape.Intersects(x, y)) continue;
                    occupied = true;
                    break;
                }
                if (occupied) occupiedCells++;
            }

        return total == 0 || occupiedCells * 100d > total * (double)occupiedThreshold;
    }
}
