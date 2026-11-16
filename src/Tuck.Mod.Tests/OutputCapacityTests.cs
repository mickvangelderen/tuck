using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using static Tuck.Mod.ItemWarnings;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class OutputCapacityTests
{
    [Theory]
    [InlineData(0, 100, 0, false)]
    [InlineData(1, 1000000, 0, true)]
    [InlineData(1, 21, 5, false)]
    [InlineData(1, 20, 5, true)]
    [InlineData(79, 100, 80, false)]
    [InlineData(4, 5, 80, true)]
    [InlineData(799999, 1000000, 80, false)]
    [InlineData(800000, 1000000, 80, true)]
    [InlineData(999999, 1000000, 100, false)]
    [InlineData(1000000, 1000000, 100, true)]
    [InlineData(1200000, 1000000, 100, true)]
    [InlineData(1, 0, 0, false)]
    public void ThresholdsCompareExactRatiosWithoutRoundingUp(long occupied, long total, int threshold, bool warning) =>
        Assert.Equal(warning, OutputCapacity.AtThreshold(occupied, total, threshold));

    [Fact]
    public void GridCountsRotatedFlippedOccupiedCellsWithoutDoubleCountingOverlap()
    {
        var shape = new GridShapeBuilder().SetData([1, 0, 1, 1], 2);
        var item = new Item("mod_added_product") { modifiedShape = shape };
        var grid = Grid(2, 2, item, item);
        for (int rotation = 0; rotation < 4; rotation++)
        foreach (bool flip in new[] { false, true })
        {
            shape.SetTransform(0, 0, flip, rotation);
            Assert.True(OutputCapacity.GridAtThreshold(grid, 75));
            Assert.False(OutputCapacity.GridAtThreshold(grid, 80));
            Assert.False(OutputCapacity.GridAtThreshold(grid, 100));
            Assert.Equal((flip, rotation), (shape.flipped, shape.orientation));
        }
        Assert.Equal(new GameItem[] { item, item }, MissingIngredients.Items(grid));
    }

    [Fact]
    public void BlockedCellsAreExcludedAndOnlyActualContentsCountRegardlessOfAcceptanceRules()
    {
        var item = new Item("bag") { modifiedShape = new GridShapeBuilder().SetData([1, 1, 1, 0], 2) };
        item.mayStackItemFunc = (_, _) => throw new InvalidOperationException("No stack probes.");
        Set(item, nameof(GameItem.contentWindow), Window(Grid(10, 10)));
        var grid = Grid(2, 2, item);
        grid.mayInventoryAddItemFunc = (_, _) => throw new InvalidOperationException("No admission probes.");
        Set(grid, nameof(GameGridInventory.inventoryShape), new GridShapeBuilder().SetData([0, 1, 0, 0], 2).SetDataOutside(1));
        Assert.True(OutputCapacity.GridAtThreshold(grid, 65)); // Two of three usable cells.
        Assert.False(OutputCapacity.GridAtThreshold(grid, 70));
        MissingIngredients.Items(grid).Add(new Item("plug") { modifiedShape = new GridShapeBuilder(1, 1).SetDataFill(1).SetPosition(1, 1) });
        Assert.True(OutputCapacity.GridAtThreshold(grid, 100));
        Assert.False(OutputCapacity.GridAtThreshold(Grid(1, 1), 0));
        Assert.False(OutputCapacity.GridAtThreshold(Grid(0, 0), 100));
        Set(grid, nameof(GameGridInventory.inventoryShape), new GridShapeBuilder(2, 2).SetDataFill(1).SetDataOutside(1));
        Assert.False(OutputCapacity.GridAtThreshold(grid, 0));
        Assert.False(OutputCapacity.GridAtThreshold(grid, 100));
    }

    [Fact]
    public void TransformedGridBoundsRemainAccurateWhenTheNativeFullOnlyShortcutDoesNotApply()
    {
        var item = new Item("product") { modifiedShape = new GridShapeBuilder(1, 2).SetDataFill(1).SetPosition(4, 3) };
        var grid = Grid(2, 1, item);
        Set(grid, nameof(GameGridInventory.inventoryShape), new GridShapeBuilder(2, 1).SetDataFill(0).SetDataOutside(1).SetTransform(4, 3, true, 1));
        Assert.True(OutputCapacity.GridAtThreshold(grid, 100));
        item.modifiedShape = new GridShapeBuilder(1, 1).SetDataFill(1).SetPosition(4, 3);
        Assert.True(OutputCapacity.GridAtThreshold(grid, 50));
        Assert.False(OutputCapacity.GridAtThreshold(grid, 100));
    }

    [Theory]
    [InlineData("printer")]
    [InlineData("furnace")]
    [InlineData("hydroponic")]
    [InlineData("mod_overnight_machine")]
    public void StandardOutputRoleWorksThroughNativeIntakeWithoutRecipesOrPositionalIndices(string id)
    {
        var output = Grid(5, 1, new Item("arbitrary_product") { modifiedShape = new GridShapeBuilder(4, 1).SetDataFill(1) });
        var intake = Raw<MachineryIntake>();
        Set(intake, nameof(MachineryIntake.content), output);
        var machine = Machine(id, ("note", Slot(new Item("note"))), ("output", intake), ("input", Grid(8, 8)));
        Assert.True(OutputCapacity.Applies(machine, 80));
        Assert.False(OutputCapacity.Applies(machine, 85));
        MissingIngredients.Items(output).Clear();
        Assert.False(OutputCapacity.Applies(machine, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(80)]
    [InlineData(100)]
    public void SolidOutputSlotIsEmptyOrFull(int threshold)
    {
        var slot = Slot();
        var machine = Machine("desequencer", ("output", slot));
        Assert.False(OutputCapacity.Applies(machine, threshold));
        Put(slot, new Item("any_card"));
        Assert.True(OutputCapacity.Applies(machine, threshold));
        Put(slot, Bottle(1000, 0)); // A bottle produced as an item still occupies a solid-output slot.
        Assert.True(OutputCapacity.Applies(machine, threshold));
    }

    [Theory]
    [InlineData(0, 1000000, 0, false)]
    [InlineData(1, 1000000, 0, true)]
    [InlineData(49999, 1000000, 5, false)]
    [InlineData(50000, 1000000, 5, true)]
    [InlineData(799999, 1000000, 80, false)]
    [InlineData(800000, 1000000, 80, true)]
    [InlineData(999999, 1000000, 100, false)]
    [InlineData(1000000, 1000000, 100, true)]
    [InlineData(1100000, 1000000, 100, true)]
    [InlineData(1, 0, 0, false)]
    public void LiquidCapacityRatiosIncludeOverfullStates(int volume, int total, int threshold, bool warning) =>
        Assert.Equal(warning, OutputCapacity.LiquidAtThreshold(total, total - volume, threshold));

    [Fact]
    public void MissingInvalidAndInputContainersDoNotMasqueradeAsFullOutputs()
    {
        var receiver = Slot();
        var machine = Machine("evaporator", ("container", Slot(Bottle(1000, 1000))), ("distillate", receiver));
        Assert.False(OutputCapacity.Applies(machine, 0));
        Put(receiver, new Item("not_a_bottle"));
        Assert.False(OutputCapacity.Applies(machine, 0));
        Put(receiver, Bottle(0, 1000));
        Assert.False(OutputCapacity.Applies(machine, 0));
        Assert.False(OutputCapacity.Applies(Machine("moisture_farm", ("container", Slot())), 0));
        Assert.False(OutputCapacity.Applies(Machine("other_overnight_machine", ("container", Slot(Bottle(1, 1)))), 0));
        Assert.False(OutputCapacity.Applies(Machine("water_purifier", ("container", Slot(Bottle(1, 1)))), 0));
        Assert.False(OutputCapacity.Applies(Machine("wine_rack", ("wine", Slot(new Item("wine")))), 0));
    }

    [Fact]
    public void LiquidReceiverUsesGameCapacityHelpersRatherThanReimplementingLiquidComposition()
    {
        // Liquid's registry initializer needs Unity localization. Check this boundary without constructing fake Unity services.
        using var module = DseCecil.ModuleDefinition.ReadModule(typeof(OutputCapacity).Assembly.Location);
        var method = module.Types.Single(t => t.FullName == typeof(OutputCapacity).FullName).Methods
            .Single(m => m.Name == nameof(OutputCapacity.LiquidAtThreshold) && m.Parameters[0].ParameterType.Name == nameof(GameItem));
        var calls = method.Body.Instructions.Select(i => i.Operand).OfType<DseCecil.MethodReference>()
            .Select(m => m.DeclaringType.Name + "." + m.Name).ToArray();
        Assert.Contains("WaterHelper.GetTotalCapacity", calls);
        Assert.Contains("WaterHelper.GetFreeCapacity", calls);
    }

    [Fact]
    public void DnaUsesOnlyTheEjectTrayAndRespectsKeepDiskWithoutRequiringAnActiveOperation()
    {
        var output = Grid(1, 1, new Item("disk") { modifiedShape = new GridShapeBuilder(1, 1).SetDataFill(1) });
        var machine = Machine("plant_dna_manipulator", ("output_disk", Slot(new Item("blank"))), ("eject", output));
        Assert.True(OutputCapacity.Applies(machine, 80));
        machine.Tag(MachineDnaManipulator.KEEP_DISK_BOOL, true);
        Assert.False(OutputCapacity.Applies(machine, 80));
        machine.Tag(MachineDnaManipulator.KEEP_DISK_BOOL, false);
        MissingIngredients.Items(output).Clear();
        Assert.False(OutputCapacity.Applies(machine, 0));
    }

    [Theory]
    [InlineData("chem_finisher")]
    [InlineData("blender")]
    [InlineData("heating_plate")]
    [InlineData("bottle_printer")]
    [InlineData("recharger")]
    [InlineData("ras")]
    public void ExcludedMachinesDoNotWarnEvenWithAnOutputRoleAndNightlyCallback(string id) =>
        Assert.False(OutputCapacity.Applies(Machine(id, ("output", Slot(new Item("contents")))), 0));

    [Fact]
    public void NightlyMachineryEligibilityAndIndependentToggleAreRequired()
    {
        var machine = Machine("printer", ("output", Slot(new Item("contents"))));
        Assert.Equal(Warning.None, Applicable(machine, StolenWarningMode.Disabled, false, false, false, outputOccupiedPercent: 50));
        Assert.Equal(Warning.Spill, Applicable(machine, StolenWarningMode.Disabled, false, false, false, spillEnabled: true, outputOccupiedPercent: 0));
        machine.Tag(MachineryHelper.IS_MACHINERY_POWERED, false);
        Assert.False(OutputCapacity.Applies(machine, 0));
        machine.Tag(MachineryHelper.IS_MACHINERY_POWERED, true);
        machine.modifiedState.EnableTag("BROKEN_MACHINE_TAG");
        Assert.False(OutputCapacity.Applies(machine, 0));
        machine.modifiedState.DisableTag("BROKEN_MACHINE_TAG");
        machine.onCycleEndSlotItemFunc = null;
        Assert.False(OutputCapacity.Applies(machine, 0));
        machine.onCycleEndLateSlotItemFunc = (_, _, _) => throw new InvalidOperationException("Do not run production.");
        Assert.True(OutputCapacity.Applies(machine, 0));
        machine.modifiedState.DisableTag(MachineryHelper.MACHINERY_TAG);
        Assert.False(OutputCapacity.Applies(machine, 0));
    }

    [Fact]
    public void FullnessSliderDefaultsToFiftyAndStoresZeroToHundredInFivePointSteps()
    {
        string id = "tuck-fullness-test-" + Guid.NewGuid().ToString("N");
        var configs = (Dictionary<string, ModConfig>)AccessTools.Field(typeof(ModConfig), "configs").GetValue(null)!;
        var config = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.NonPublic, null, [id], null)!;
        configs.Add(id, config);
        var options = new Options(new ModManifest { ID = id, Name = "Tuck" }, (key, args) => key == "x_percent" ? args[0] + "%" : key);
        var entry = (ConfigEntry<float>)config.Get("output_occupied_percent");
        try
        {
            options.SpillWarnings.Register();
            options.OutputOccupiedPercent.Register();
            var slider = GameOptionRegistry.Find(ModHelper.GameOptionId(id, entry.Key));
            Assert.Equal((50f, 0f, 100f), (entry.Default, entry.Min, entry.Max));
            Assert.False(entry.RequiresRestart);
            Assert.Equal(GameOptionRegistry.Kind.Slider, slider.kind);
            Assert.True(slider.wholeNumbers);
            Assert.Equal((0f, 20f, 10f), (slider.min, slider.max, slider.getFloat()));
            AccessTools.Property(typeof(ConfigEntry), nameof(ConfigEntry.Config)).SetValue(entry, null);
            for (int tick = 0; tick <= 20; tick++)
            {
                slider.setFloat(tick);
                Assert.Equal(tick * 5f, options.OutputOccupiedPercent.Value);
                Assert.Equal(tick * 5 + "%", slider.valueText(tick));
                Assert.True(options.SpillWarnings.Value); // Neither endpoint disables the independent toggle.
            }
            entry.Set(79);
            Assert.Equal(80f, options.OutputOccupiedPercent.Value);
            entry.Set(float.NaN);
            Assert.Equal(50f, options.OutputOccupiedPercent.Value);
        }
        finally
        {
            AccessTools.Property(typeof(ConfigEntry), nameof(ConfigEntry.Config)).SetValue(entry, config);
            options.OutputOccupiedPercent.Unregister();
            options.SpillWarnings.Unregister();
            configs.Remove(id);
        }
    }

    private static Item Bottle(int capacity, int volume)
    {
        var bottle = new Item("bottle");
        bottle.modifiedState.EnableTag(WaterHelper.LIQUID_CONTAINER_TAG);
        bottle.Tag(WaterHelper.LIQUID_CONTAINER_CAPACITY, capacity);
        bottle.Tag("CURRENT_PART_WATER", volume);
        return bottle;
    }
    private static Item Machine(string id, params (string Role, PixelElement Content)[] contents)
    {
        var panels = contents.Select(pair =>
        {
            var panel = Raw<MachineryPanel>();
            panel.role = pair.Role;
            Set(panel, nameof(MachineryPanel.content), pair.Content);
            return panel;
        }).ToList();
        var row = Raw<MachineryPanelRow>();
        AccessTools.Field(typeof(MachineryPanelRow), "panels").SetValue(row, panels);
        var item = new Item(id);
        item.modifiedState.EnableTag(MachineryHelper.MACHINERY_TAG);
        item.Tag(MachineryHelper.IS_MACHINERY_POWERED, true);
        item.onCycleEndSlotItemFunc = (_, _, _) => throw new InvalidOperationException("Do not run production.");
        Set(item, nameof(GameItem.contentWindow), Window(row));
        return item;
    }
    private static PixelWindow Window(PixelElement content)
    {
        var window = Raw<PixelWindow>();
        Set(window, nameof(PixelWindow.childElement), content);
        return window;
    }
    private static GameSlotInventory Slot(Item? item = null)
    {
        var slot = Raw<GameSlotInventory>();
        Put(slot, item);
        return slot;
    }
    private static void Put(GameSlotInventory slot, Item? item) => AccessTools.Field(typeof(GameSlotInventory), "currentItem").SetValue(slot, item);
    private static GameGridInventory Grid(int width, int height, params Item[] items)
    {
        var grid = Raw<GameGridInventory>();
        AccessTools.Field(typeof(GameGridInventory), "items").SetValue(grid, items.Cast<GameItem>().ToList());
        Set(grid, nameof(GameGridInventory.inventoryShape), new GridShapeBuilder(width, height).SetDataFill(0).SetDataOutside(1));
        return grid;
    }
    private static void Set(object instance, string property, object value) => AccessTools.Property(instance.GetType(), property).SetValue(instance, value);
    private static T Raw<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private sealed class Item(string id) : GameItem(id)
    {
        internal void Tag(string key, int value) => modifiedState.GetTag(key).Enable().SetInt(value);
        internal void Tag(string key, bool value) => modifiedState.GetTag(key).Enable().SetBool(value);
        public override void Validate() { }
        public override void Destroy() => throw new InvalidOperationException("Fullness checks must not consume items.");
    }
}
