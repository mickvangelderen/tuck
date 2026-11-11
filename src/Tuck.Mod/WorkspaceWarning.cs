using HarmonyLib;
using global::Tuck.BitBoard;

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
        var free = Mask(shape, 0);
        long total = free.Count;
        foreach (var item in items)
        {
            var footprint = item.modifiedShape;
            if (footprint.GetLocal(-1, -1) == 1)
            {
                // Unusual shapes can occupy space outside their rectangle. Sample only this workspace.
                for (int y = 0; y < free.Height; y++)
                    for (int x = 0; x < free.Width; x++)
                        if (footprint.Intersects(x + shape.minX, y + shape.minY)) free[x, y] = false;
            }
            else free.Subtract(Mask(footprint, 1), footprint.minX - shape.minX, footprint.minY - shape.minY);
        }
        long occupiedCells = total - free.Count;

        return total == 0 || occupiedCells * 100d > total * (double)occupiedThreshold;
    }

    private static BitBoardMut Mask(GridShape shape, byte included)
    {
        var board = new BitBoardMut(shape.width, shape.height);
        for (int y = 0; y < shape.height; y++)
            for (int x = 0; x < shape.width; x++) board[x, y] = shape.GetLocal(x, y) == included;
        if (shape.flipped) board.FlipH();
        board.Rotate(shape.orientation);
        return board;
    }
}
