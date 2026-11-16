using System.Runtime.CompilerServices;
using HarmonyLib;
using static Tuck.Mod.ItemWarnings;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class MissingIngredientsTests
{
    [Fact]
    public void HydroponicsSeparatesWaterFromSeedsAndSupportsIndependentOptions()
    {
        var water = Slot();
        var seedSlot = Slot();
        var machine = Machine("hydroponic", Slot(), Grid(), water, seedSlot, Grid());
        Assert.Equal(Warning.MissingWater | Warning.MissingIngredients, MissingIngredients.Evaluate(machine, true, true));
        Assert.Equal(Warning.MissingWater, MissingIngredients.Evaluate(machine, false, true));
        Assert.Equal(Warning.MissingIngredients, MissingIngredients.Evaluate(machine, true, false));
        Assert.Equal(Warning.None, MissingIngredients.Evaluate(machine, false, false));
        Assert.Equal(Warning.MissingWater, Applicable(machine, StolenWarningMode.Disabled, false, false, false, waterEnabled: true, outputOccupiedPercent: 50));
        Put(seedSlot, new Item("seed"));
        Assert.False(MissingIngredients.Applies(machine));
        Assert.Equal(Warning.MissingIngredients, MissingIngredients.HydroponicWarnings(false, true, 1, 0, true, true));
        Assert.Equal(Warning.MissingWater, MissingIngredients.HydroponicWarnings(true, true, 99, 100, true, true));
        Assert.Equal(Warning.None, MissingIngredients.HydroponicWarnings(true, true, 100, 100, true, true));
        Assert.Equal(Warning.None, MissingIngredients.HydroponicWarnings(true, true, 0, 0, true, true));
        Assert.Equal(Warning.MissingWater, MissingIngredients.HydroponicWarnings(true, false, 0, 0, true, true));
        Assert.Equal(Warning.None, MissingIngredients.HydroponicWarnings(true, true, 99, 100, true, false));
    }

    [Fact]
    public void RasComparesCombinedLiveStockDemandInMillilitres()
    {
        var fish = Grid();
        var machine = Machine("ras", Slot(), Slot(), Grid(), Grid(), fish);
        Assert.Equal(Warning.MissingWater | Warning.MissingIngredients, MissingIngredients.Evaluate(machine, true, true));
        var first = new Item("fish");
        first.modifiedState.EnableTag(AquacultureHelper.FISH_ITEM_TAG);
        first.Set(AquacultureHelper.FISH_SPECIES_TAG, FishSpecies.Tilapia.speciesID);
        var second = new Item("fish");
        second.modifiedState.EnableTag(AquacultureHelper.FISH_ITEM_TAG);
        second.Set(AquacultureHelper.FISH_SPECIES_TAG, FishSpecies.Tilapia.speciesID);
        Items(fish).AddRange(new[] { first, second });
        int required = AquacultureHelper.GetTotalWaterNeedMl(fish);
        Assert.Equal(AquacultureHelper.GetWaterNeedMl(first) * 2, required);
        Assert.True(required > 1);
        Assert.Equal(Warning.MissingWater, MissingIngredients.RasWarnings(true, required - 1, required, true, true));
        Assert.Equal(Warning.None, MissingIngredients.RasWarnings(true, required, required, true, true));
        Assert.Equal(Warning.MissingWater, MissingIngredients.Evaluate(machine, true, true));
        Assert.Equal(Warning.None, MissingIngredients.Evaluate(machine, true, false));
        Assert.Equal(Warning.MissingIngredients, MissingIngredients.RasWarnings(false, 1, 0, true, true));
        Assert.Equal(Warning.MissingWater, MissingIngredients.RasWarnings(false, 0, 0, false, true));
        second.modifiedState.EnableTag(AquacultureHelper.FISH_IS_DEAD_TAG);
        Assert.Equal(AquacultureHelper.GetWaterNeedMl(first), AquacultureHelper.GetTotalWaterNeedMl(fish));
    }

    [Fact]
    public void PurifierWaterWarningsRespectEligibilityWithoutRedundantGear()
    {
        var water = Slot();
        var machine = Machine("water_purifier", Slot(), Grid(), water);
        Assert.Equal(Warning.MissingWater, MissingIngredients.Evaluate(machine, true, true));
        Assert.False(MissingIngredients.Applies(machine));
        Assert.Equal(Warning.None, MissingIngredients.Evaluate(machine, true, false));
        Put(water, new Item("empty_bottle"));
        Assert.Equal(Warning.MissingWater, MissingIngredients.Evaluate(machine, true, true));
        machine.modifiedState.EnableTag(MachineryHelper.MACHINERY_TAG);
        machine.Set(MachineryHelper.IS_MACHINERY_POWERED, false);
        Assert.Equal(Warning.None, MissingIngredients.Evaluate(machine, true, true));
        machine.Set(MachineryHelper.IS_MACHINERY_POWERED, true);
        machine.modifiedState.EnableTag("BROKEN_MACHINE_TAG");
        Assert.Equal(Warning.None, MissingIngredients.Evaluate(machine, true, true));
    }

    [Fact]
    public void PrinterCountsRecipeInputsAndDoesNotRequireThemAgainAfterStarting()
    {
        var chip = new Item("recipe");
        chip.modifiedState.EnableTag(MachineProgressHelper.USE_ADDITIONAL_ITEMS_TAG);
        chip.Set("REQUIRED_ITEM_TYPE_COUNT_TAG", 2);
        // Duplicate ingredient entries must be added together, as vanilla does.
        for (int i = 0; i < 2; i++)
        {
            chip.Set("REQUIRED_ITEM_ID_" + i, "plastic");
            chip.Set("REQUIRED_ITEM_COUNT_" + i, 1);
        }
        var plastic = new Item("plastic") { unitCount = 50 };
        var input = Grid(plastic);
        var machine = Machine("printer", Slot(), Grid(), input, Slot(chip), Grid());
        Assert.True(MissingIngredients.Applies(machine));
        Items(input).Add(new Item("plastic"));
        Assert.False(MissingIngredients.Applies(machine));
        Assert.Equal(2, input.childItems.Count);
        Assert.Equal(50, plastic.unitCount);
        Items(input).Clear();
        Assert.True(MissingIngredients.Applies(machine));
        machine.Set(MachineProgressHelper.MACHINE_STATE_TAG, MachineProgressHelper.STATE_WORKING);
        Assert.False(MissingIngredients.Applies(machine));
    }

    [Fact]
    public void PrinterUsesInstalledModulesInsteadOfLastNightsCachedTags()
    {
        var chip = new Item("recipe");
        chip.Set(MachineProgressHelper.PROGRESS_ITEM_REQUIRED_MACHINE_TAG, "Metal Printing Module");
        var modules = Grid();
        var machine = Machine("printer", Slot(), modules, Grid(), Slot(chip));
        machine.modifiedState.EnableTag("Metal Printing Module");
        Assert.True(MissingIngredients.Applies(machine));
        Items(modules).Add(new Item("printer_module_metal"));
        machine.modifiedState.DisableTag("Metal Printing Module");
        Assert.False(MissingIngredients.Applies(machine));
        Assert.False(machine.IsTag("Metal Printing Module"));
    }

    [Theory]
    [InlineData(false, 1, true)]
    [InlineData(false, 2, false)]
    [InlineData(true, 2, true)]
    [InlineData(true, 3, false)]
    public void FurnaceRequiresEnoughMaterialForInstalledModule(bool blast, int count, bool warning)
    {
        var input = Grid(Enumerable.Range(0, count).Select(i => new Item(i % 2 == 0 ? "scrap_metal" : "common_ore")).ToArray());
        var machine = Machine("furnace", Slot(), blast ? Grid(new Item("furnace_module_blast")) : Grid(), input, Grid());
        Assert.Equal(warning, MissingIngredients.Applies(machine));
        Assert.Equal(count, input.childItems.Count); // A preview never consumes anything.
    }

    [Fact]
    public void FurnaceAcceptsSixJunkOnlyWithJunkModuleAndFluxIsOptional()
    {
        var modules = Grid();
        var input = Grid(Enumerable.Range(0, 6).Select(_ => new Item("junk")).ToArray());
        var machine = Machine("furnace", Slot(), modules, input, Grid());
        Assert.True(MissingIngredients.Applies(machine));
        Items(modules).Add(new Item("furnace_module_junk"));
        Assert.False(MissingIngredients.Applies(machine));
        Items(input).RemoveAt(0);
        Assert.True(MissingIngredients.Applies(machine));
    }

    [Theory]
    [InlineData("hydroponic")]
    [InlineData("printer")]
    [InlineData("furnace")]
    [InlineData("plant_dna_manipulator")]
    [InlineData("desequencer")]
    [InlineData("moisture_farm")]
    [InlineData("water_purifier")]
    [InlineData("evaporator")]
    [InlineData("ras")]
    public void SupportedMachinesWaitForTheirInventoryToBeConstructed(string id)
    {
        var machine = new Item(id);
        Assert.True(MissingIngredients.Supports(machine));
        Assert.False(MissingIngredients.Applies(machine));
        Assert.False(MissingIngredients.Applies(Machine(id)));
    }

    [Fact]
    public void BrokenAndPoweredOffMachinesDoNotWarnAndToggleIsIndependentOfInspection()
    {
        var machine = Machine("hydroponic", Slot(), Grid(), Slot(), Slot());
        Assert.Equal(ItemWarnings.Warning.MissingIngredients, ItemWarnings.Applicable(machine, StolenWarningMode.Disabled, false, true, false, outputOccupiedPercent: 50));
        Assert.Equal(ItemWarnings.Warning.None, ItemWarnings.Applicable(machine, StolenWarningMode.Disabled, false, false, true, outputOccupiedPercent: 50));
        machine.modifiedState.EnableTag(MachineryHelper.MACHINERY_TAG);
        machine.Set(MachineryHelper.IS_MACHINERY_POWERED, false);
        Assert.False(MissingIngredients.Applies(machine));
        machine.Set(MachineryHelper.IS_MACHINERY_POWERED, true);
        Assert.True(MissingIngredients.Applies(machine));
        machine.modifiedState.EnableTag("BROKEN_MACHINE_TAG");
        Assert.False(MissingIngredients.Applies(machine));
    }

    [Fact]
    public void DnaManipulatorDoesNotRequestNewInputsDuringAnOperation()
    {
        var machine = Machine("plant_dna_manipulator", Slot(), Grid(), Slot(), Slot(), Slot());
        Assert.True(MissingIngredients.Applies(machine));
        machine.Set(MachineProgressHelper.MACHINE_STATE_TAG, MachineProgressHelper.STATE_WORKING);
        Assert.False(MissingIngredients.Applies(machine));
    }

    [Fact]
    public void DnaExtractionRespectsInstalledFiltersAndPrecisionOverrides()
    {
        var seed = new Item("seed");
        var disk = new Item("blank_disk");
        disk.modifiedState.EnableTag("DNA_DISK_TAG");
        var modules = Grid();
        var machine = Machine("plant_dna_manipulator", Slot(), modules, Slot(), Slot(seed), Slot(disk));
        Assert.True(MissingIngredients.Applies(machine));
        var trait = PlantTraitRegistry.All.First(t => !t.locked && !t.IsNegative);
        seed.Set(PlantGeneticsHelper.TraitTag(trait.id), 1);
        Assert.False(MissingIngredients.Applies(machine));
        Items(modules).Add(new Item(MachineDnaManipulator.NEGATIVE_FILTER_MODULE_ID));
        Assert.True(MissingIngredients.Applies(machine));
        Items(modules).Add(new Item(MachineDnaManipulator.POSITIVE_FILTER_MODULE_ID));
        Assert.True(MissingIngredients.Applies(machine));
        var precision = new Item(MachineDnaManipulator.PRECISION_MODULE_ID);
        precision.Set(MachineDnaManipulator.PRECISION_TRAIT_STRING, trait.id);
        Items(modules).Add(precision);
        Assert.False(MissingIngredients.Applies(machine));
        seed.Set(PlantGeneticsHelper.TraitTag(trait.id), 0);
        Assert.True(MissingIngredients.Applies(machine));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PresetDnaSuppliesAllowNativeApplicationAndExtractionWithoutChangingTheSeed(bool nutrifruitFreed)
    {
        // Use the registry's native factory genetics, without invoking Unity sprite/UI creation.
        var seed = new Item("nutrifruit_seed");
        seed.modifiedState.EnableTag(HydroponicHelper.SEED_ITEM_TAG);
        seed.Set(HydroponicHelper.PLANT_ID_STRING, seed.identifier);
        var plant = PlantRegistry.Get(seed.identifier);
        foreach (var trait in plant.innateTraits)
            seed.Set(PlantGeneticsHelper.TraitTag(trait.Key), nutrifruitFreed && trait.Key == PlantTraitList.INFERTILE ? 0 : trait.Value);
        var blank = new Item("plant_dna_disk");
        blank.modifiedState.EnableTag("DNA_DISK_TAG");
        var written = new Item("plant_dna_disk");
        written.modifiedState.EnableTag("DNA_DISK_TAG");
        written.Set("DNA_DISK_TRAIT_STRING", ItemWarningPreset.DnaTrait);
        written.Set("DNA_DISK_RANK_INT", 1);
        written.Set("DNA_DISK_USES_INT", 1);

        Assert.True(PlantGeneticsHelper.IsBlankDisk(blank));
        Assert.True(PlantGeneticsHelper.IsWrittenDisk(written));
        Assert.Equal(1, PlantGeneticsHelper.GetDiskRank(written));
        Assert.Equal(1, PlantGeneticsHelper.GetDiskUses(written));
        Assert.Equal(2, PlantGeneticsHelper.GetTraitRank(seed, ItemWarningPreset.DnaTrait));
        Assert.True(PlantGeneticsHelper.IsDiskCompatibleWithSeed(written, seed));
        Assert.True(PlantGeneticsHelper.CanApplyTrait(seed, PlantGeneticsHelper.GetDiskTrait(written), out _));
        Assert.True(PlantGeneticsHelper.HasExtractableTrait(seed));
        Assert.True(PlantGeneticsHelper.CanExtractOntoDisk(seed, blank, out _));
        Assert.Same(plant, HydroponicHelper.GetPlant(seed));

        var input = Slot();
        var target = Slot();
        var output = Slot();
        var machine = Machine("plant_dna_manipulator", Slot(), Grid(), input, target, output);
        Assert.True(MissingIngredients.Applies(machine));
        Put(target, seed);
        Assert.True(MissingIngredients.Applies(machine));
        Put(input, written);
        var apply = MachineDnaManipulator.PlanOperation(machine);
        Assert.Equal(MachineDnaManipulator.OPERATION_APPLY, apply.operation);
        Assert.True(apply.isStacking);
        Assert.Equal(ItemWarningPreset.DnaTrait, apply.trait.id);
        Assert.Empty(apply.blockedReason);
        Assert.False(MissingIngredients.Applies(machine));

        Put(input, null);
        Assert.True(MissingIngredients.Applies(machine));
        Put(output, blank);
        var extract = MachineDnaManipulator.PlanOperation(machine);
        Assert.Equal(MachineDnaManipulator.OPERATION_EXTRACT, extract.operation);
        Assert.Empty(extract.blockedReason);
        Assert.False(MissingIngredients.Applies(machine));
        Put(target, null);
        Assert.True(MissingIngredients.Applies(machine));
        Assert.Equal(2, PlantGeneticsHelper.GetTraitRank(seed, ItemWarningPreset.DnaTrait));
    }

    [Fact]
    public void DesequencerRequiresProgramAndAccessCardOnlyWhenReady()
    {
        var program = Slot();
        var card = Slot();
        var machine = Machine("desequencer", Slot(), Grid(), program, card);
        machine.Set("DESEQUENCER_STATE_TAG", "STATE_READY");
        Assert.True(MissingIngredients.Applies(machine));
        Put(program, new Item("program"));
        Put(card, new Item("wrong_input"));
        Assert.True(MissingIngredients.Applies(machine));
        card.childItem.itemTypes.Add("ACCESS_CARD");
        Assert.False(MissingIngredients.Applies(machine));
        Put(program, null);
        machine.Set("DESEQUENCER_STATE_TAG", "STATE_DESEQUENCING");
        Assert.False(MissingIngredients.Applies(machine));
    }

    [Theory]
    [InlineData("moisture_farm")]
    [InlineData("evaporator")]
    public void LiquidMachinesNeedAContainer(string id) =>
        Assert.True(MissingIngredients.Applies(Machine(id, Slot(), Grid(), Slot())));

    [Fact]
    public void MoistureFarmDoesNotNeedLiquidInItsReceivingBottle()
    {
        Assert.False(MissingIngredients.Applies(Machine("moisture_farm", Slot(), Grid(), Slot(new Item("bottle")))));
    }

    [Fact]
    public void AgewellNeverNeedsIngredientWarningsRegardlessOfContents()
    {
        Assert.False(MissingIngredients.Applies(Machine("wine_rack", Slot(), Grid(), Grid())));
        var wine = new Item("wine");
        var machine = Machine("wine_rack", Slot(), Grid(), Grid(wine));
        Assert.False(MissingIngredients.Supports(machine));
        Assert.False(MissingIngredients.Applies(machine));
        wine.modifiedState.EnableTag("WINE_CONTAINER_TAG");
        wine.Set("WINE_CONTAINER_STATE", 2);
        Assert.False(MissingIngredients.Applies(machine));
        wine.modifiedState.EnableTag("WINE_COUNTERFEIT_TAG");
        Assert.False(MissingIngredients.Applies(machine));
    }

    [Theory]
    [InlineData("blender")]
    [InlineData("heating_plate")]
    [InlineData("chem_finisher")]
    public void ManualMachinesNeverGetMissingInputWarningsEvenWhenConfigured(string id)
    {
        var machine = Machine(id, Grid(), Slot(), Grid());
        machine.Set(MachineHeatingPlate.HEATING_PLATE_STATE, 1);
        machine.Set(MachineChemFinisher.SELECTED_CHEM_ID_TAG, "dream_dust");
        Assert.False(MissingIngredients.Supports(machine));
        Assert.Equal(Warning.None, MissingIngredients.Evaluate(machine, true, true));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 100)]
    [InlineData(3, 0)]
    [InlineData(4, 0)]
    [InlineData(4, 100)]
    public void RechargerNeverNeedsIngredientOrWaterOverlaysRegardlessOfOccupancyOrCharge(int count, int charge)
    {
        var batteries = Enumerable.Range(0, count).Select(x =>
        {
            var battery = new Item("energy_credit") { modifiedShape = new GridShapeBuilder(1, 2).SetDataFill(1).SetPosition(x, 0) };
            battery.Set(PowerHelper.POWER_SOURCE_ITEM_ENERGY, charge);
            return battery;
        }).ToArray();
        var machine = Machine("recharger", Grid(batteries), Grid());
        Assert.False(MissingIngredients.Supports(machine));
        Assert.Equal(Warning.None, MissingIngredients.Evaluate(machine, true, true));
        Assert.All(batteries, b => Assert.Equal(charge, b.GetTagReadonly(PowerHelper.POWER_SOURCE_ITEM_ENERGY).GetInt()));
        machine.modifiedState.EnableTag("BROKEN_MACHINE_TAG");
        Assert.False(MissingIngredients.Applies(machine));
        machine.modifiedState.DisableTag("BROKEN_MACHINE_TAG");
        machine.modifiedState.EnableTag(MachineryHelper.MACHINERY_TAG);
        machine.Set(MachineryHelper.IS_MACHINERY_POWERED, false);
        Assert.False(MissingIngredients.Applies(machine));
    }

    private static Item Machine(string id, params GameInventory[] inventories)
    {
        var panels = inventories.Select(inventory =>
        {
            var panel = Raw<MachineryPanel>();
            AccessTools.Property(typeof(MachineryPanel), nameof(MachineryPanel.content)).SetValue(panel, inventory);
            return panel;
        }).ToList();
        var row = Raw<MachineryPanelRow>();
        AccessTools.Field(typeof(MachineryPanelRow), "panels").SetValue(row, panels);
        var window = Raw<PixelWindow>();
        AccessTools.Property(typeof(PixelWindow), nameof(PixelWindow.childElement)).SetValue(window, row);
        var item = new Item(id);
        AccessTools.Property(typeof(GameItem), nameof(GameItem.contentWindow)).SetValue(item, window);
        return item;
    }

    private static GameSlotInventory Slot(Item? item = null)
    {
        var slot = Raw<GameSlotInventory>();
        Put(slot, item);
        return slot;
    }

    private static void Put(GameSlotInventory slot, Item? item) =>
        AccessTools.Field(typeof(GameSlotInventory), "currentItem").SetValue(slot, item);

    private static GameGridInventory Grid(params Item[] items)
    {
        var grid = Raw<GameGridInventory>();
        AccessTools.Field(typeof(GameGridInventory), "items").SetValue(grid, items.Cast<GameItem>().ToList());
        AccessTools.Property(typeof(GameGridInventory), nameof(GameGridInventory.inventoryShape)).SetValue(grid,
            new GridShapeBuilder(4, 2).SetDataFill(0).SetDataOutside(1));
        return grid;
    }

    private static T Raw<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static List<GameItem> Items(GameGridInventory grid) =>
        (List<GameItem>)AccessTools.Field(typeof(GameGridInventory), "items").GetValue(grid)!;

    private sealed class Item(string id) : GameItem(id)
    {
        internal void Set(string tag, int value) => modifiedState.GetTag(tag).Enable().SetInt(value);
        internal void Set(string tag, bool value) => modifiedState.GetTag(tag).Enable().SetBool(value);
        internal void Set(string tag, string value) => modifiedState.GetTag(tag).Enable().SetString(value);
        public override void Validate() { }
        public override void Destroy() => throw new InvalidOperationException("Warnings must not consume inputs.");
    }
}
