using System.Collections.Generic;
using System.Reflection;
using static Tuck.Mod.ItemWarnings;

namespace Tuck.Mod;

internal static class MissingIngredients
{
    // childItems returns a fresh copy. Borrow the grid's list for this synchronous read-only pass.
    private static readonly FieldInfo GridItems = typeof(GameGridInventory).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly Dictionary<string, int> RequiredItems = new();

    internal static bool Supports(GameItem item) => item.identifier is
        "hydroponic" or "printer" or "furnace" or "plant_dna_manipulator" or "desequencer" or
        "moisture_farm" or "water_purifier" or "evaporator" or "ras";

    internal static bool Applies(GameItem item) => Evaluate(item, true, false) != Warning.None;

    internal static Warning Evaluate(GameItem item, bool ingredients, bool water)
    {
        if ((!ingredients && !water) || !Supports(item) || item.contentWindow == null || item.IsTag("BROKEN_MACHINE_TAG") ||
            (item.IsTag(MachineryHelper.MACHINERY_TAG) && !MachineryHelper.IsMachineryToggleOn(item))) return Warning.None;
        switch (item.identifier)
        {
            case "hydroponic":
                if (Slot(item, 2) is not { } supply || Slot(item, 3) is not { } seed) return Warning.None;
                // Without a seed, test emptiness only (including liquid amounts below one ml).
                int supplyMl = water && supply.childItem != null ? (seed.childItem == null
                    ? (WaterHelper.IsEmpty(supply.childItem) ? 0 : 1)
                    : WaterHelper.GetCurrentCapacityML(supply.childItem)) : 0;
                return HydroponicWarnings(seed.childItem != null, supply.childItem != null,
                    supplyMl,
                    water && seed.childItem != null && supply.childItem != null ? HydroponicHelper.GetWaterUsePerCycle(seed.childItem) : 0,
                    ingredients, water);
            case "ras":
                if (MachineRAS.GetWaterSlot(item) is not { } tank || MachineRAS.GetTankGrid(item) is not { } fish) return Warning.None;
                int need = 0;
                bool livestock = false;
                foreach (var animal in Items(fish))
                    if (AquacultureHelper.IsLiveStock(animal))
                    {
                        livestock = true;
                        if (!water) break;
                        need += AquacultureHelper.GetWaterNeedMl(animal);
                    }
                int available = water ? AquacultureHelper.GetWaterMl(tank.childItem) : 0;
                return RasWarnings(livestock, available, need, ingredients, water);
            case "water_purifier":
                return water && Slot(item, 2) is { } bottle && Empty(bottle.childItem) ? Warning.MissingWater : Warning.None;
            default:
                return ingredients && GenericInputsMissing(item) ? Warning.MissingIngredients : Warning.None;
        }
    }

    internal static Warning HydroponicWarnings(bool seed, bool container, int availableMl, int requiredMl, bool ingredients, bool water) =>
        (ingredients && !seed ? Warning.MissingIngredients : Warning.None) |
        (water && (!container || (seed ? availableMl < requiredMl : availableMl <= 0)) ? Warning.MissingWater : Warning.None);

    internal static Warning RasWarnings(bool livestock, int availableMl, int requiredMl, bool ingredients, bool water) =>
        (ingredients && !livestock ? Warning.MissingIngredients : Warning.None) |
        (water && (availableMl <= 0 || availableMl < requiredMl) ? Warning.MissingWater : Warning.None);

    private static bool GenericInputsMissing(GameItem item)
    {
        // Check inputs only. Full outputs and insufficient power have different causes.
        switch (item.identifier)
        {
            case "printer":
                if (Working(item) || Slot(item, 3) is not { } chip || Inventory(item, 2) is not { } ingredients ||
                    Inventory(item, 1) is not { } modules) return false;
                if (chip.childItem == null) return true;
                if (!HasPrinterInputs(chip.childItem, Items(ingredients))) return true;
                foreach (string tag in MachineProgressHelper.GetRequiredMachineTags(chip.childItem))
                {
                    // Vanilla refreshes these two tags at night. Read the installed modules for an immediate preview.
                    bool present = tag switch
                    {
                        "Metal Printing Module" => Items(modules).Exists(m => m.IsID("printer_module_metal")),
                        "CNC Module" => Items(modules).Exists(m => m.IsID("printer_module_cnc")),
                        _ => item.IsTag(tag)
                    };
                    if (!present) return true;
                }
                return false;
            case "furnace":
                return Furnace(item);
            case "plant_dna_manipulator":
                return !Working(item) && DnaInputsMissing(item);
            case "desequencer":
                if (item.GetTagReadonly("DESEQUENCER_STATE_TAG").GetString() != "STATE_READY" ||
                    Slot(item, 2) is not { } program || Slot(item, 3) is not { } card) return false;
                return program.childItem == null || card.childItem == null || !card.childItem.IsGameItemType("ACCESS_CARD");
            case "moisture_farm":
                return Slot(item, 2) is { childItem: null };
            case "evaporator":
                return Slot(item, 2) is { } liquid && !MachineEvaporator.CanEvaporateContainer(liquid.childItem);
            default:
                return false;
        }
    }

    internal static List<GameItem> Items(GameInventory inventory) =>
        inventory is GameGridInventory grid ? (List<GameItem>)GridItems.GetValue(grid) : inventory.childItems;

    private static bool DnaInputsMissing(GameItem machine)
    {
        if (Slot(machine, 2) is not { } inputSlot || Slot(machine, 3) is not { } seedSlot ||
            Slot(machine, 4) is not { } outputSlot) return false;
        var seed = seedSlot.childItem;
        if (seed == null) return true;
        var input = inputSlot.childItem;
        var output = outputSlot.childItem;
        // PlanOperation also calculates progress/quality and repeatedly walks the module graph.
        if (PlantGeneticsHelper.IsWrittenDisk(input))
            return !PlantGeneticsHelper.CanApplyTrait(seed, PlantGeneticsHelper.GetDiskTrait(input), out _);
        if (!PlantGeneticsHelper.IsBlankDisk(output))
            return !PlantGeneticsHelper.IsWrittenDisk(output) || !PlantGeneticsHelper.CanExtractOntoDisk(seed, output, out _);

        PlantTrait? precision = null;
        bool positive = false, negative = false;
        if (Inventory(machine, 1) is { } modules)
            foreach (var module in Items(modules))
            {
                if (module.identifier == MachineDnaManipulator.POSITIVE_FILTER_MODULE_ID) positive = true;
                if (module.identifier == MachineDnaManipulator.NEGATIVE_FILTER_MODULE_ID) negative = true;
                if (precision == null && module.identifier == MachineDnaManipulator.PRECISION_MODULE_ID)
                    precision = PlantTraitRegistry.Get(module.GetTagReadonly(MachineDnaManipulator.PRECISION_TRAIT_STRING).GetString());
            }
        foreach (var trait in PlantTraitRegistry.All)
        {
            if (trait.locked) continue;
            if (precision != null ? trait.id != precision.id : (positive && trait.IsNegative) || (negative && !trait.IsNegative)) continue;
            if (PlantGeneticsHelper.GetTraitRank(seed, trait.id) > 0) return false;
        }
        return true;
    }

    // Match vanilla's duplicate-recipe aggregation and item (not stack-unit) counts without two new dictionaries.
    private static bool HasPrinterInputs(GameItem recipe, List<GameItem> input)
    {
        if (!recipe.IsTag(MachineProgressHelper.USE_ADDITIONAL_ITEMS_TAG)) return true;
        RequiredItems.Clear();
        int count = recipe.IsTag("REQUIRED_ITEM_TYPE_COUNT_TAG") ? recipe.GetTagReadonly("REQUIRED_ITEM_TYPE_COUNT_TAG").GetInt() : int.MaxValue;
        for (int i = 0; i < count; i++)
        {
            string idTag = "REQUIRED_ITEM_ID_" + i, countTag = "REQUIRED_ITEM_COUNT_" + i;
            if (!recipe.IsTag(idTag) || !recipe.IsTag(countTag)) break;
            string id = recipe.GetTagReadonly(idTag).GetString();
            RequiredItems.TryGetValue(id, out int required);
            RequiredItems[id] = required + recipe.GetTagReadonly(countTag).GetInt();
        }
        foreach (var requirement in RequiredItems)
        {
            int provided = 0;
            foreach (var material in input)
                if (material.identifier == requirement.Key) provided++;
            if (provided == 0 || provided < requirement.Value) return false;
        }
        return true;
    }

    private static bool Working(GameItem item) => MachineProgressHelper.GetMachineState(item) == MachineProgressHelper.STATE_WORKING;
    private static bool Empty(GameItem? item) => item == null || WaterHelper.IsEmpty(item);
    private static GameInventory? Inventory(GameItem item, int index) => MachineHelper.GetBodyStorage(item, index) as GameInventory;
    private static GameSlotInventory? Slot(GameItem item, int index) => MachineHelper.GetBodyStorage(item, index) as GameSlotInventory;

    private static bool Furnace(GameItem item)
    {
        if (Inventory(item, 1) is not { } modules || Inventory(item, 2) is not { } input) return false;
        bool blast = Items(modules).Exists(i => i.IsID("furnace_module_blast"));
        bool junk = Items(modules).Exists(i => i.IsID("furnace_module_junk"));
        int metalCount = 0, junkCount = 0;
        // These input bays accept materials directly, never nested containers.
        foreach (var material in Items(input))
        {
            if (material.identifier is "scrap_metal" or "common_ore") metalCount++;
            else if (material.IsID("junk")) junkCount++;
        }
        // Vanilla counts material items, not stack units; flux improves quality but is optional.
        return metalCount < (blast ? 3 : 2) && !(junk && junkCount >= 6);
    }
}
