using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Tuck.Mod;

internal static partial class DragAmounts
{
    private static bool RotationEnabled => _options?.RotateDraggedGroups.Value == true;

    private static void RotationSettingChanged(bool enabled)
    {
        if (enabled || _session == null || _session.QuarterTurns == 0 || !_session.Group.IsGroupDragging) return;
        ModReflect.Call(_session.Group, "PruneSelection");
        _session?.Rotate(-_session.QuarterTurns);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(ItemMultiSelectHandler), "keyListeners", MethodType.Getter)]
    private static void RotationKeys(ref List<KeyCode> __result)
    {
        if (!RotationEnabled) return;
        __result.Add(KeyBindHelper.Get("rotate_cw"));
        __result.Add(KeyBindHelper.Get("rotate_ccw"));
        __result.Add(KeyBindHelper.Get("reset_orientation"));
        __result.Add(KeyBindHelper.Get("secondary_click"));
    }

    [HarmonyPrefix, HarmonyPatch(typeof(ItemMultiSelectHandler), "OnEventPress")]
    private static void RotateGroup(ItemMultiSelectHandler __instance, ISet<KeyCode> keysPressed)
    {
        if (!RotationEnabled || !__instance.IsGroupDragging || _session?.Group != __instance) return;
        ModReflect.Call(__instance, "PruneSelection");
        if (_session?.Group != __instance) return;
        var items = Items(__instance);
        if (items.Count == 0 || !items.All(item => item.parentInventory != null && Eligible(item, item.parentInventory))) return;
        int turns = 0;
        if (keysPressed.Contains(KeyBindHelper.Get("rotate_cw"))
            || (EmporiumOption.Instance != null && EmporiumOption.Instance.swapDragRotateFlip
                && keysPressed.Contains(KeyBindHelper.Get("secondary_click")))) turns--;
        if (keysPressed.Contains(KeyBindHelper.Get("rotate_ccw"))) turns++;
        if (keysPressed.Contains(KeyBindHelper.Get("reset_orientation"))) turns = -_session.QuarterTurns;
        _session.Rotate(turns);
    }

    // Overlay coordinates grow upward; grid coordinates grow downward.
    internal static Vector2 RotateOffset(Vector2 offset, int turns)
    {
        for (int i = 0; i < (turns % 4 + 4) % 4; i++) offset = new Vector2(-offset.y, offset.x);
        return offset;
    }

    internal static GridShape RotateShape(GridShape source, int turns)
    {
        var result = new GridShapeBuilder(source);
        for (int i = 0; i < (turns % 4 + 4) % 4; i++)
            result.SetTransform(result.minY, -result.maxX, result.flipped, result.orientation + 1);
        return result.Build();
    }

    [HarmonyPostfix, HarmonyPatch(typeof(ItemMultiSelectHandler), "TryGetPreciseDelta")]
    private static void RotatedDelta(ItemMultiSelectHandler __instance, bool __result, ref int deltaX, ref int deltaY)
    {
        if (!RotationEnabled || !__result || _session?.Group != __instance || _session.QuarterTurns == 0) return;
        var source = Items(__instance)[0].modifiedShape;
        var rotated = RotateShape(source, _session.QuarterTurns);
        deltaX += source.minX - rotated.minX;
        deltaY += source.minY - rotated.minY;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(ItemMultiSelectHandler), "TranslatedShape")]
    private static bool RotatedPlacement(GameItemElement element, int deltaX, int deltaY, ref GridShape __result)
    {
        if (!RotationEnabled || _session == null || _session.QuarterTurns == 0 || !Items(_session.Group).Contains(element)) return true;
        var rotated = RotateShape(element.modifiedShape, _session.QuarterTurns);
        __result = new GridShapeBuilder(rotated).SetPosition(rotated.minX + deltaX, rotated.minY + deltaY).Build();
        return false;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(ItemMultiSelectHandler), "IsFormationValid")]
    private static void RotationEligibility(ItemMultiSelectHandler __instance, ref bool __result)
    {
        if (!RotationEnabled || !__result || _session?.Group != __instance || _session.QuarterTurns == 0) return;
        __result = Items(__instance).All(item => Eligible(item, item.parentInventory));
    }

    [HarmonyPostfix, HarmonyPatch(typeof(GameGridInventory), "TryFindOneValidInventorySlot", new[] { typeof(GameItem), typeof(bool) })]
    private static void PreferDraggedOrientation(GameGridInventory __instance, GameItem item, ref SlotMarker? __result)
    {
        if (!RotationEnabled || _session == null || _session.QuarterTurns == 0 || !_session.Dropping
            || item is not GameItemElement element || !Items(_session.Group).Contains(element)) return;
        // Keep native stacking and searches through nested containers ahead of empty grid cells.
        if (__result != null && (__result.targetItem != null || __result.inventory != __instance)) return;
        __result = FindRotatedSlot(__instance, item, element.tempShape) ?? __result;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(ItemMultiSelectHandler), "EndGroupDrag")]
    private static void BeginRotatedDrop(ItemMultiSelectHandler __instance)
    {
        if (RotationEnabled && _session?.Group == __instance) _session.Dropping = true;
    }

    internal static SlotMarker? FindRotatedSlot(GameGridInventory inventory, GameItem item, GridShape orientation)
    {
        int amount = item.MaxNumRemove();
        if (amount < 1 || item.parentInventory == null || !Eligible(item, item.parentInventory)
            || !inventory.MayHaveValidInventorySlot(item)) return null;
        var candidate = new GridShapeBuilder(item.modifiedShape)
            .SetOrientation(orientation.orientation).SetFlipped(orientation.flipped);
        for (int y = 0; y < inventory.inventoryShape.height; y++)
            for (int x = 0; x < inventory.inventoryShape.width; x++)
            {
                candidate.SetPosition(x, y);
                var slot = inventory.TryInventorySlot(item, amount, candidate);
                if (slot.numTransfer > 0) return slot;
            }
        return null;
    }
}
