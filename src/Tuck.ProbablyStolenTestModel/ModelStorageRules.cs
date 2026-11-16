namespace Tuck.ProbablyStolenTestModel;

// Admission callbacks ported from ContainerHelper, ShipSystemDirectory and ToolboxHelper.
// These use vanilla item state, independently of packing snapshots and solver rule enums.
public static class ModelStorageRules
{
    public static bool IsNestingAllowed(GameItem addedItem, GameItem? parentItem)
    {
        if (addedItem.IsTag("NESTABLE_DEVICE_TAG")) return true;
        if (addedItem.IsTag("STORABLE_PACKAGE_TAG") && !addedItem.IsTag("BACKPACK_TAG") && parentItem != null)
            return parentItem.IsGameItemType("STORAGE");
        return false;
    }

    public static void InitContainerItem(GameInventory inventory, GameItem item,
        List<string>? allowedTags = null, List<string>? allowedIds = null)
    {
        item.EnableTag("IMPORTANT_TAG");
        item.EnableTag("CONTAINER_TAG");
        inventory.mayInventoryAddItemFunc += (addedItem, inv) =>
        {
            if (!GeneralHelper.IsItemOwned(addedItem)) return false;
            var parentItem = inv.GetParentItem();
            if (parentItem != null && !GeneralHelper.IsItemOwned(parentItem)) return false;
            if ((addedItem.IsTag("BACKPACK_TAG") || addedItem.IsTag("CONTAINER_TAG"))
                && !IsNestingAllowed(addedItem, parentItem)) return false;
            if (addedItem.IsGameItemType("MACHINE") && !addedItem.IsTag("NESTABLE_DEVICE_TAG")) return false;
            if (allowedTags != null && addedItem.IsOneOfTags(allowedTags)) return true;
            if (allowedIds != null && allowedIds.Contains(addedItem.identifier)) return true;
            return allowedTags == null;
        };
    }

    public static void InitMachineBay(GameInventory inventory, GameItem item)
    {
        InitContainerItem(inventory, item);
        item.SetGameItemType("STORAGE");
        // Both vanilla bay factories replace, rather than append to, the ordinary container callback.
        inventory.mayInventoryAddItemFunc = (addedItem, inv) =>
        {
            if (!GeneralHelper.IsItemOwned(addedItem)) return false;
            var parentItem = inv.GetParentItem();
            if (parentItem != null && !GeneralHelper.IsItemOwned(parentItem)) return false;
            if (addedItem.IsTag("BACKPACK_TAG")) return false;
            return !addedItem.IsTag("STORAGE") || IsNestingAllowed(addedItem, parentItem);
        };
    }

    public static void InitToolboxSlot(GameInventory inventory, GameItem box, int slotIndex)
    {
        inventory.slotPriorityFunc = (_, _, _) => 5 + (8 - slotIndex);
        inventory.mayInventoryAddItemFunc += (item, _) =>
        {
            if (slotIndex >= Math.Clamp(box.GetTagReadonly("TOOLBOX_UNLOCKED_SLOTS_INT").GetInt(), 3, 9))
                return false;
            return GeneralHelper.IsItemOwned(item) &&
                (item.IsGameItemType("TOOL") || item.IsGameItemType("MELEE_WEAPON")
                || item.IsTag("CAN_USE_ON_NON_OWNED_TAG") || item.IsTag("TOOLBOX_ITEM_TAG")
                || item.IsTag("LIQUID_CONTAINER_DROPPER_TAG")
                || item.identifier is "labeler" or "labeler_blue" or "surgery_tool" or "uv_filter");
        };
    }
}
