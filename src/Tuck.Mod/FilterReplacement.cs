using System.Collections.Generic;
using HarmonyLib;

namespace Tuck.Mod;

/// <summary>Adds the battery-style drag interaction to installed water filters.</summary>
[HarmonyPatch(typeof(GameItem))]
internal static class FilterReplacement
{
    private static Options? _options;
    public static void Register(Options options) => _options = options;
    public static void Unregister()
    {
        _options = null;
        FilterSwapCandidates.Clear();
    }

    private static bool IsFilterReplacement(GameItem source, GameItem target) =>
        _options?.ReplaceFilters.Value == true && source != null && target != null && source != target &&
        source.IsTag("WATER_FILTER") && target.IsTag("WATER_FILTER") &&
        target.parentInventory is GameSlotInventory && source.parentInventory != target.parentInventory;

    [HarmonyPrefix, HarmonyPatch(nameof(GameItem.MayTarget))]
    internal static bool MayTargetPrefix(GameItem __instance, GameItem targetItem, ref bool __result)
    {
        if (!IsFilterReplacement(__instance, targetItem)) return true;
        __result = CanReplace(__instance, targetItem);
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(nameof(GameItem.CanTarget))]
    internal static bool CanTargetPrefix(GameItem __instance, GameItem targetItem, ref bool __result) =>
        MayTargetPrefix(__instance, targetItem, ref __result);

    [HarmonyPrefix, HarmonyPatch(nameof(GameItem.Target))]
    internal static bool TargetPrefix(GameItem __instance, GameItem targetItem)
    {
        if (!IsFilterReplacement(__instance, targetItem)) return true;
        // Recheck at drop time. Do not invoke tool actions for this interaction: UV filters
        // normally disinfect liquid targets, but replacing a filter must not disinfect anything.
        TryReplace(__instance, targetItem);
        return false;
    }

    internal static bool CanReplace(GameItem source, GameItem target) =>
        TryGetPlacements(source, target, out _, out _);

    private static bool TryGetPlacements(GameItem source, GameItem target,
        out SlotMarker sourcePlacement, out SlotMarker targetPlacement)
    {
        sourcePlacement = targetPlacement = null!;
        if (!IsFilterReplacement(source, target)) return false;
        if (source.parentInventory == null || target.MayStack(source) || source.forceDisableUse ||
            (!source.canUseOutsideCombat && !GameMaster.current.InCombat())) return false;

        if (!CanRemove(source) || !CanRemove(target)) return false;
        return TryFitInPlace(source, target, out sourcePlacement) &&
            TryFitInPlace(target, source, out targetPlacement);
    }

    internal static bool TryReplace(GameItem source, GameItem target, GameSlotInventory? buffer = null)
    {
        // Validate the destinations and remember the original placements for best-effort recovery.
        if (!TryGetPlacements(source, target, out var sourceDestination, out var targetDestination)) return false;
        var sourceOriginal = Placement(source, source.parentInventory, source.modifiedShape);
        var targetOriginal = Placement(target, target.parentInventory, target.modifiedShape);

        // Prepare an empty temporary buffer, as in the vanilla swap.
        buffer ??= EmporiumEntry.Instance != null ? EmporiumEntry.Instance.swapBufferElement : null;
        if (buffer == null) return false;
        if (buffer.childItem != null)
        {
            if (EmporiumEntry.Instance == null) return false;
            GraphUtils.TryAcceptOnce(EmporiumEntry.Instance.invElement, buffer.childItem);
            if (buffer.childItem != null) return false;
        }

        // 1. Move the source into the buffer, freeing its inventory space.
        int remaining = GraphUtils.TryAcceptOnce(buffer, source);
        if (remaining > 0) return false;

        // 2. Move the target into that space using the validated rotation and offset.
        if (targetDestination.TryAcceptOnce() < targetDestination.numTransfer)
        {
            // Return the source from the buffer to its original placement.
            sourceOriginal.TryAcceptOnce();
            return false;
        }

        // 3. Move the source from the buffer into the machine slot.
        if (sourceDestination.TryAcceptOnce() < sourceDestination.numTransfer)
        {
            // Restore the target first to make room for the source.
            targetOriginal.TryAcceptOnce();
            sourceOriginal.TryAcceptOnce();
            return false;
        }
        return true;
    }

    private static SlotMarker Placement(GameItem item, GameInventory inventory, GridShape shape) =>
        new() { item = item, inventory = inventory, itemGridShape = shape, numTransfer = item.unitCount };

    private static bool CanRemove(GameItem item) =>
        GeneralHelper.IsItemEffectivelyOwned(item) && !ItemPinHelper.IsPinned(item) &&
        item.unitCount > 0 && item.MayRemove() && item.MaxNumRemove() >= item.unitCount;

    private static bool TryFitInPlace(GameItem incoming, GameItem outgoing, out SlotMarker placement)
    {
        placement = null!;
        var inventory = outgoing.parentInventory;
        if (!GeneralHelper.MayPlayerInsertInto(inventory) || !inventory.MayHaveValidInventorySlot(incoming)) return false;

        if (inventory is not GameGridInventory &&
            (inventory is not GameSlotInventory slot || slot.childItem != outgoing)) return false;

        if (inventory is not GameGridInventory grid)
            return TryPlacement(incoming, inventory,
                new GridShapeBuilder(incoming.modifiedShape).SetTransform(outgoing.modifiedShape), out placement);

        // Cache relative geometry only. Occupancy and game rules are evaluated afresh, including on drop.
        var ignored = new List<GameItem> { outgoing };
        var neighbors = grid.childItems;
        foreach (var variants in FilterSwapCandidates.Get(incoming.modifiedShape, outgoing.modifiedShape))
        {
            var footprint = AtOutgoingPosition(variants[0], outgoing.modifiedShape);
            if (!footprint.FullyIntersects(grid.inventoryShape, (a, b) => a != 1 || b == 0)) continue;
            bool blocked = false;
            foreach (var neighbor in neighbors)
                if (neighbor != incoming && neighbor != outgoing && footprint.Intersects(neighbor.modifiedShape))
                {
                    blocked = true;
                    break;
                }
            if (blocked) continue;

            foreach (var variant in variants)
            {
                var shape = AtOutgoingPosition(variant, outgoing.modifiedShape);
                // Keep the game's check even after the cheap geometry prefilter, including custom rules.
                if (!grid.CheckShapePlacement(incoming, shape, ignored)) continue;
                if (TryPlacement(incoming, inventory, shape, out placement)) return true;
            }
        }
        return false;
    }

    private static GridShape AtOutgoingPosition(GridShape relative, GridShape outgoing) =>
        new GridShapeBuilder(relative).SetPosition(outgoing.minX + relative.minX, outgoing.minY + relative.minY);

    private static bool TryPlacement(GameItem incoming, GameInventory inventory, GridShape shape,
        out SlotMarker placement)
    {
        var marker = Placement(incoming, inventory, shape);
        placement = null!;
        if (!incoming.CheckIsStateValid(inventory, marker, shape, incoming.modifiedState) ||
            !inventory.CheckAcceptParentShapeStateModifier(incoming, marker, shape, incoming.modifiedState) ||
            incoming.MaxNumAdd(marker) < incoming.unitCount) return false;
        placement = marker;
        return true;
    }
}
