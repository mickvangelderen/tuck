using System;
using System.Collections.Generic;
using System.Linq;
using global::Tuck.PackSolver;

namespace Tuck.Mod.Packing;

/// <summary>Explicit mappings from audited vanilla rules, without inspecting delegate methods or closures.</summary>
internal static class GameRuleCapture
{
    // ContainerItemDirectory and ShipSystemDirectory: ordinary InitBackpackItem/InitContainerItem grids.
    private static readonly HashSet<string> OrdinaryContainers = new(StringComparer.Ordinal)
    {
        "fanny_pack", "satchel", "kids_backpack", "kids_backpack_large", "backpack_small", "backpack_medium",
        "backpack_medium_military", "backpack_large", "backpack_large_military",
        "makeshift_storage_bay", "storage_bay", "storage_bay_large"
    };
    internal static readonly string[] Tags =
    {
        "IS_OWNED_TAG", "PINNED_TAG", "LOCKED_TAG", "MODULE_STUCK_TAG", "DEBRIS_PILE_TAG", "ITEM_BEHIND_LOCK_TAG",
        "BACKPACK_TAG", "CONTAINER_TAG", "NESTABLE_DEVICE_TAG", "STORABLE_PACKAGE_TAG", "HAZARDOUS_WASTE_TAG",
        "CAN_USE_ON_NON_OWNED_TAG", "TOOLBOX_ITEM_TAG", "LIQUID_CONTAINER_DROPPER_TAG",
        "FIREARM", "GUN_MOD", "AMMUNITION", "CHEMICAL_SUPPLIES", "LIQUID_CONTAINER_TAG", "NARCOTIC", "MEDICAL",
        "WATER_PURIFICATION_SUPPLY", "CHEMICAL_PRODUCT_TAG", "power_source_item", "armor_item", "ORGAN_ITEM_TAG", "not_purchased"
    };
    internal static readonly string[] IntTags = { "power_source_item_energy", "POWER_SOURCE_ITEM_VALUE_PERCENT_INT",
        "amenities", "amenities_use", "current_armor_value", "current_max_armor_value", "armor_efficiency",
        "LIQUID_CONTAINER_CURRENT", "ORGAN_HEALTH_INT", "ORGAN_HEALTH_MAX_INT", "TOOLBOX_UNLOCKED_SLOTS_INT" };
    internal static readonly string[] Types = { "STORAGE", "MACHINE", "TOOL", "MELEE_WEAPON" };

    internal static ItemDefinition Definition(GameItem item) => new(item.identifier, rules: new ItemRules(
        parentShape: item.parentItemShapeModifierForItemFunc == null ? ParentShapeRule.None
            : GunAttachments.Supports(item.identifier) ? ParentShapeRule.GunAttachment : ParentShapeRule.Unsupported,
        unsupportedParentState: item.parentItemStateModifierForItemFunc != null, unsupportedTransfer: ItemUnsupported(item)));

    internal static string? ItemUnsupported(GameItem item)
    {
        if (item.parentItemStateModifierForItemFunc != null || item.parentItemShapeModifierForItemFunc != null && !GunAttachments.Supports(item.identifier))
            return "Parent modifier is not represented.";
        // ModuleHelper.InitModuleItem's single removal callback checks MODULE_STUCK_TAG, already
        // represented by the global removal check. Additional callbacks still require a mapping.
        bool knownModuleRemoval = item.identifier == "system_module_ruined"
            && item.mayInventoryRemoveItemFunc?.GetInvocationList().Length == 1;
        if (item.mayInventoryAddItemFunc != null || item.mayInventoryRemoveItemFunc != null && !knownModuleRemoval || item.isItemStateValidFunc != null)
            return "Item admission/removal/state callback is not represented.";
        // MayStack only governs merges. Whole-item packing neither captures nor invokes it.
        if (item.maxSlotAmountItemFunc != null || item.minSlotAmountItemFunc != null || item.maxSlotAddItemFunc != null
            || item.maxSlotRemoveItemFunc != null || item.onChangeAmountItemFunc != null || item.onSlotChangeAmountItemFunc != null)
            return "Quantity/amount-change rules are not mapped for this definition.";
        // GameItem's single constructor callback removes selling-only features. Additional handlers need an audit.
        if ((item.onSlotAddItemFunc?.GetInvocationList().Length ?? 0) > 1 || item.onSlotRemoveItemFunc != null)
            return "Additional item transfer effects are not represented.";
        return null;
    }

    internal static (AdmissionRule Rule, string? Unsupported) InventoryRule(GameInventory inventory, GameItem? owner, string id,
        PackRoots roots, bool allowsHazardousWaste)
    {
        string? unsupported = null;
        if (inventory.parentItemShapeModifierForItemFunc != null || inventory.parentItemStateModifierForItemFunc != null
            || inventory.isItemStateValidFunc != null || inventory.mayInventoryRemoveItemFunc != null
            || inventory.maxSlotAmountItemFunc != null || inventory.minSlotAmountItemFunc != null
            || inventory.maxSlotAddItemFunc != null || inventory.maxSlotRemoveItemFunc != null
            || inventory.onSlotAddItemFunc != null || inventory.onSlotRemoveItemFunc != null
            || inventory.onSlotAddItemEndFunc != null || inventory.onSlotRemoveItemEndFunc != null
            || inventory.onSlotAddItemLateFunc != null || inventory.onSlotRemoveItemLateFunc != null
            || inventory.onSlotChangeAmountItemFunc != null)
            unsupported = "Inventory transfer effects or quantity rules are not represented.";
        if (unsupported != null) return (new(AdmissionKind.Unsupported), unsupported);
        if (id == roots.Backpack) return (new(AdmissionKind.Backpack), null);
        if (id == roots.Ground) return (new(AdmissionKind.Ground, allowsHazardousWaste: allowsHazardousWaste), null);
        if (id == roots.Left || id == roots.Right) return (new(AdmissionKind.Owned), null);
        if (owner?.identifier == "toolbox" && inventory is GameSlotInventory
            && inventory.identifier != null && inventory.identifier.StartsWith("toolbox_slot_", StringComparison.Ordinal)
            && int.TryParse(inventory.identifier.Substring("toolbox_slot_".Length), out int slot) && slot >= 0 && slot < 9)
            return (new(AdmissionKind.Toolbox, slotIndex: slot), null);
        // Only the content grid is covered; an examine/attachment inventory is a different rule family.
        bool contentGrid = owner?.contentWindow?.child == inventory && inventory is GameGridInventory;
        if (contentGrid && OrdinaryContainers.Contains(owner!.identifier)) return (new(AdmissionKind.Container), null);
        if (contentGrid && owner!.identifier == "storage_bay_gun")
            return (new(AdmissionKind.Container, new[] { "FIREARM", "GUN_MOD", "AMMUNITION" },
                new[] { "gun_part", "printer_plastic", "screwdriver", "tazer", "stun_gun" }), null);
        if (contentGrid && owner!.identifier == "storage_bay_chem")
            return (new(AdmissionKind.Container, new[] { "CHEMICAL_SUPPLIES", "LIQUID_CONTAINER_TAG", "NARCOTIC", "MEDICAL",
                "WATER_PURIFICATION_SUPPLY", "CHEMICAL_PRODUCT_TAG" }, new[] { "aquascan", "chem_scanner", "chem_module" }), null);
        // An empty callback list does not make a battery/module/attachment socket storage.
        return (new(AdmissionKind.Unsupported), "Equipment or unclassified inventory is not packing storage.");
    }
}
