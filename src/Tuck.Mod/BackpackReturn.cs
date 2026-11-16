using System.Linq;
using HarmonyLib;

namespace Tuck.Mod;

/// <summary>Returns the selected backpack to its previous placement when that space is still available.</summary>
[HarmonyPatch]
internal static class BackpackReturn
{
    private static Options? _options;
    private static (GameItem Item, GameInventory Inventory, GridShape Shape)? _placement;

    public static void Register(Options options)
    {
        _options = options;
        options.ReturnBackpack.OnChanged += OnSettingChanged;
        ModHook.OnGameLoadedInit += ClearMemory;
    }

    public static void Unregister()
    {
        if (_options != null) _options.ReturnBackpack.OnChanged -= OnSettingChanged;
        ModHook.OnGameLoadedInit -= ClearMemory;
        _options = null;
        ClearMemory();
    }

    private static void OnSettingChanged(bool enabled)
    {
        if (!enabled) ClearMemory();
    }

    private static void ClearMemory() => _placement = null;

    [HarmonyPrefix, HarmonyPatch(typeof(EmporiumEntry), nameof(EmporiumEntry.TransferBackpackToInv))]
    internal static void Remember(EmporiumEntry __instance)
    {
        ClearMemory();
        var bag = __instance.selectedBackpack;
        if (_options?.ReturnBackpack.Value == true && bag?.parentInventory != null)
            _placement = (bag, bag.parentInventory, bag.modifiedShape.Clone());
    }

    [HarmonyPrefix, HarmonyPatch(typeof(EmporiumEntry), nameof(EmporiumEntry.TransferAfterhourToInv))]
    internal static void BeforeReturn(EmporiumEntry __instance)
    {
        TryRestore(__instance);
        ClearMemory();
    }

    [HarmonyPostfix, HarmonyPatch(typeof(MapUIManager), nameof(MapUIManager.TransferCounterToBackInv))]
    internal static void AfterCounterRestored()
    {
        // Dr. Jackson restores the shop counter here, before placing purchases and ground loot.
        TryRestore(EmporiumEntry.Instance);
    }

    private static void TryRestore(EmporiumEntry entry)
    {
        if (_options?.ReturnBackpack.Value != true || !_placement.HasValue) return;
        var (bag, inventory, previousShape) = _placement.Value;
        // Never recover a bag left on the ground, or substitute another bag of the same kind.
        if ((!GraphUtils.FindParentEquals(bag, entry.afterhourPocketSlotInvBackpack)
             && !GraphUtils.FindParentEquals(bag, entry.afterhourPocketSlotInvLeft)
             && !GraphUtils.FindParentEquals(bag, entry.afterhourPocketSlotInvRight))
            || !IsStoreInventory(entry, inventory) || !bag.MayRemove() || inventory.IsInsertLocked()) return;

        var shape = new GridShapeBuilder(bag.modifiedShape).SetTransform(previousShape);
        var slot = inventory.TryInventorySlot(bag, bag.unitCount, shape);
        if (slot == null || slot.numTransfer < bag.unitCount || slot.targetItem != null) return;
        if (slot.TryAcceptOnce(bag.unitCount) == bag.unitCount) ClearMemory();
    }

    private static bool IsStoreInventory(EmporiumEntry entry, GameInventory inventory) =>
        new GameInventory[] { entry.invElement, entry.backInvinvElement, entry.frontInvinvElement, entry.showcaseElement,
            entry.docInvElement, entry.trashInvElement, entry.vendingFountainElement, entry.vendingMachineElement,
            entry.hiddenElement }
        .Any(root => root != null && (inventory == root || GraphUtils.FindParentEquals(inventory, root)));
}
