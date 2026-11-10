using System.Collections.Generic;
using HarmonyLib;

namespace Tuck.Mod;

/// <summary>Adds the battery-style drag interaction to installed water filters.</summary>
[HarmonyPatch(typeof(GameItem))]
internal static class FilterReplacement
{
    private static Options? _options;
    public static void Register(Options options) => _options = options;
    public static void Unregister() => _options = null;

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
        if (CanReplace(__instance, targetItem)) GraphUtils.SwapItemsInPlace(__instance, targetItem);
        return false;
    }

    internal static bool CanReplace(GameItem source, GameItem target)
    {
        if (!IsFilterReplacement(source, target)) return false;
        if (source.parentInventory == null || target.MayStack(source) || source.forceDisableUse ||
            (!source.canUseOutsideCombat && !GameMaster.current.InCombat())) return false;

        if (!CanRemove(source) || !CanRemove(target)) return false;
        return FitsInPlace(source, target) && FitsInPlace(target, source);
    }

    private static bool CanRemove(GameItem item) =>
        GeneralHelper.IsItemEffectivelyOwned(item) && !ItemPinHelper.IsPinned(item) &&
        item.unitCount > 0 && item.MayRemove() && item.MaxNumRemove() >= item.unitCount;

    private static bool FitsInPlace(GameItem incoming, GameItem outgoing)
    {
        var inventory = outgoing.parentInventory;
        if (!GeneralHelper.MayPlayerInsertInto(inventory) || !inventory.MayHaveValidInventorySlot(incoming)) return false;

        // The vanilla swap helper places unchecked. Probe both destinations without moving items,
        // ignoring only the filter that will vacate this position when checking grid occupancy.
        var shape = new GridShapeBuilder(incoming.modifiedShape).SetTransform(outgoing.modifiedShape);
        if (inventory is GameGridInventory grid)
        {
            if (!grid.CheckShapePlacement(incoming, shape, new List<GameItem> { outgoing })) return false;
        }
        else if (inventory is not GameSlotInventory slot || slot.childItem != outgoing) return false;

        var marker = new SlotMarker { item = incoming, inventory = inventory, itemGridShape = shape };
        return incoming.CheckIsStateValid(inventory, marker, shape, incoming.modifiedState) &&
            inventory.CheckAcceptParentShapeStateModifier(incoming, marker, shape, incoming.modifiedState) &&
            incoming.MaxNumAdd(marker) >= incoming.unitCount;
    }
}
