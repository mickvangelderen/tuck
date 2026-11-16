using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using static Tuck.Mod.ItemWarnings;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class ItemWarningsTests
{
    [Theory]
    [InlineData(WarningAnimation.StolenFile)]
    [InlineData(WarningAnimation.BatteryFile)]
    [InlineData(WarningAnimation.IngredientsFile)]
    public void PackagedSheetsContainTwoNativeSixteenPixelFrames(string file)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "warnings", file));
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
        Assert.Equal(2, WarningAnimation.FrameCount(
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4))));
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(17, 16)]
    [InlineData(16, 32)]
    public void MalformedSheetsAreRejected(int width, int height) =>
        Assert.Throws<ArgumentException>(() => WarningAnimation.FrameCount(width, height));

    [Fact]
    public void BottommostRowWinsBeforeLeftmostColumnAndTransparentCellsAreSkipped()
    {
        var shape = new GridShapeBuilder().SetData([1, 1, 1, 0, 0, 1], 2);
        var cells = TestCells(shape, 0, false);
        Assert.Equal((1, 2), cells[0]);
        Assert.Equal((0, 1), cells[1]);
        Assert.Equal((0, 0), cells[2]);
        var position = Position(cells[0], 16);
        Assert.Equal(16, position.x);
        Assert.Equal(-32, position.y);
    }

    [Fact]
    public void PlacementMatchesGameGeometryForEveryRotationAndFlip()
    {
        var shape = new GridShapeBuilder().SetData([0, 1, 1, 1, 0, 1], 3).SetPosition(7, -4);
        for (int orientation = 0; orientation < 4; orientation++)
        foreach (bool flipped in new[] { false, true })
        {
            shape.SetOrientation(orientation);
            shape.flipped = flipped;
            GridShape view = shape;
            var expected = new List<(int, int)>();
            for (int y = view.maxY; y >= view.minY; y--)
            for (int x = view.minX; x <= view.maxX; x++)
                if (view.Get(x, y) != 0) expected.Add((x - view.minX, y - view.minY));
            var actual = TestCells(view, orientation, flipped);
            Assert.Equal(expected[0], actual[0]);
            Assert.Equal(expected[1], actual[1]);
            Assert.Equal(expected[2], actual[2]);
            Assert.Equal(expected[3], actual[3]);
        }
    }

    [Fact]
    public void SingleCellCentersSixteenPixelSpriteWithNoInsetOrExtraBadgeCells()
    {
        var cells = TestCells(new GridShapeBuilder(1, 1).SetDataFill(1), 0, false);
        Assert.Equal((0, 0), cells[0]);
        Assert.Single(cells);
        Assert.Equal(0, Position(cells[0], 16).x);
        Assert.Equal(0, Position(cells[0], 16).y);
        Assert.Empty(TestCells(new GridShapeBuilder(1, 1), 0, false));
    }

    [Fact]
    public void WarningsUseSeparateCellsAndOverflowSharesTheLastCellInPriorityOrder()
    {
        var all = Warning.Stolen | Warning.LowBattery | Warning.MissingIngredients;
        Assert.Equal(all, Sequence(all, 1, 0));
        Assert.Equal(Warning.None, Sequence(all, 1, 1));
        Assert.Equal(Warning.Stolen, Sequence(all, 2, 0));
        Assert.Equal(Warning.LowBattery | Warning.MissingIngredients, Sequence(all, 2, 1));
        Assert.Equal(Warning.Stolen, Sequence(all, 3, 0));
        Assert.Equal(Warning.LowBattery, Sequence(all, 3, 1));
        Assert.Equal(Warning.MissingIngredients, Sequence(all, 3, 2));
        Assert.Equal(Warning.LowBattery, Sequence(Warning.LowBattery, 3, 0));
        Assert.Equal(Warning.None, Sequence(Warning.LowBattery, 3, 1));
        Assert.Equal(Warning.None, Sequence(all, 0, 0));
        Assert.Equal(Warning.None, Sequence(Warning.None, 3, 0));
        // Removing a lower-priority warning must not restart a cell whose sequence stayed the same.
        Assert.Equal(Sequence(all, 3, 0), Sequence(all & ~Warning.LowBattery, 3, 0));
    }

    [Fact]
    public void SharedCellPlaysEachCompleteAnimationAtOneFramePerSecond()
    {
        var fields = new[] { "_stolen", "_battery", "_water", "_ingredients", "_spill", "_filter" }.Select(n => AccessTools.Field(typeof(ItemWarnings), n)).ToArray();
        var previous = fields.Select(f => f.GetValue(null)).ToArray();
        var sheets = new[] { 2, 3, 5, 4, 6, 7 }.Select(count => Enumerable.Range(0, count).Select(_ => Raw<UnityEngine.Sprite>()).ToArray()).ToArray();
        try
        {
            for (int i = 0; i < fields.Length; i++)
            {
                var animation = Raw<WarningAnimation>();
                AccessTools.Property(typeof(WarningAnimation), nameof(WarningAnimation.Frames)).SetValue(animation, sheets[i]);
                fields[i].SetValue(null, animation);
            }
            var frames = new List<UnityEngine.Sprite>();
            FillFrames(Warning.Stolen | Warning.LowBattery | Warning.MissingWater | Warning.MissingIngredients | Warning.Spill | Warning.FilterDurability, frames);
            var expected = sheets.SelectMany(s => s).ToArray();
            Assert.Equal(expected.Length, frames.Count);
            for (int cycle = 0; cycle < 2; cycle++)
            for (int i = 0; i < expected.Length; i++)
                Assert.Same(expected[i], frames[WarningAnimation.FrameAt(cycle * expected.Length + i + 0.9, frames.Count)]);
            FillFrames(Warning.LowBattery | Warning.MissingWater | Warning.MissingIngredients, frames);
            Assert.Equal(12, frames.Count);
            Assert.Same(sheets[1][0], frames[0]);
            Assert.Same(sheets[2][0], frames[3]);
            Assert.Same(sheets[3][0], frames[8]);
            FillFrames(Warning.None, frames);
            Assert.Empty(frames);
        }
        finally
        {
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, previous[i]);
        }
    }

    [Fact]
    public void FrameSelectionUsesSharedScaledTimeWithoutVisibilityOrStartHistory()
    {
        const int frames = 7;
        var continuous = new List<int>();
        for (int second = 0; second <= 10; second++)
            continuous.Add(WarningAnimation.FrameAt(second + 0.75, frames));
        Assert.Equal(continuous[10], WarningAnimation.FrameAt(10.75, frames)); // Newly opened overlay.
        Assert.Equal(continuous[2], WarningAnimation.FrameAt(2.75, frames));
        Assert.Equal(continuous[10], WarningAnimation.FrameAt(10.75, frames)); // Hidden between these samples.
        for (int refresh = 0; refresh < 10; refresh++)
            Assert.Equal(3, WarningAnimation.FrameAt(10.75, frames)); // Unchanged scaled time freezes.
        Assert.Equal(3, WarningAnimation.FrameAt(10.999, frames));
        Assert.Equal(4, WarningAnimation.FrameAt(11, frames));
        Assert.Equal(0, WarningAnimation.FrameAt(14, frames));
        Assert.Equal(0, WarningAnimation.FrameAt(10.75, 1));
        Assert.Equal(1, WarningAnimation.FrameAt(10.75, 3)); // A changed sequence also joins the shared phase.
        Assert.Equal(2, WarningAnimation.FrameAt(4_294_967_298.75, 4)); // Do not truncate time to an int first.
    }

    [Fact]
    public void EarlyWarningChecksNeverCreateThePlayerStore()
    {
        var field = AccessTools.Field(typeof(PlayerStore), "instance");
        var previous = field.GetValue(null);
        try
        {
            field.SetValue(null, null);
            Assert.False(InspectionActive());
            var machine = Machine(0, out var source);
            source!.modifiedState.EnableTag(CurrentConverterHelper.CURRENT_CONVERTER_TAG);
            source.Set(CurrentConverterHelper.CURRENT_CONVERTER_ON_BOOL, true);
            Assert.True(HasLowBattery(machine));
            Assert.Null(field.GetValue(null));
        }
        finally { field.SetValue(null, previous); }
    }

    [Theory]
    [InlineData(-1, true)] // Missing battery.
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(6, false)] // Exactly one night is enough.
    [InlineData(7, false)]
    public void BatteryWarningUsesInstalledSourceAndCurrentNightlyDemand(int charge, bool warning)
    {
        var machine = Machine(charge, out var battery);
        Assert.Equal(warning, HasLowBattery(machine));
        if (battery == null) return;
        Assert.Equal(charge, battery.GetTagReadonly(PowerHelper.POWER_SOURCE_ITEM_ENERGY).GetInt());
        machine.Set(MachineryHelper.MACHINERY_DRAWN_POWER, charge + 1);
        Assert.True(HasLowBattery(machine));
        machine.Set(MachineryHelper.MACHINERY_DRAWN_POWER, charge);
        Assert.False(HasLowBattery(machine));
    }

    [Fact]
    public void PoweredOffBrokenAndManualOnlyMachinesDoNotWarn()
    {
        var machine = Machine(0, out _);
        machine.Set(MachineryHelper.IS_MACHINERY_POWERED, false);
        Assert.False(HasLowBattery(machine));
        machine.Set(MachineryHelper.IS_MACHINERY_POWERED, true);
        machine.modifiedState.EnableTag("BROKEN_MACHINE_TAG");
        Assert.False(HasLowBattery(machine));
        machine.modifiedState.DisableTag("BROKEN_MACHINE_TAG");
        machine.onCycleEndSlotItemFunc = null;
        Assert.False(HasLowBattery(machine));
        machine.onCycleEndEarlySlotItemFunc = (_, _, _) => { };
        Assert.True(HasLowBattery(machine));
        Assert.False(HasLowBattery(new Item("loose_battery")));
    }

    [Theory]
    [InlineData("bottle_printer", -1)]
    [InlineData("bottle_printer", 0)]
    [InlineData("bottle_printer", 100)]
    [InlineData("recharger", -1)]
    [InlineData("recharger", 0)]
    [InlineData("recharger", 100)]
    [InlineData("chem_finisher", -1)]
    [InlineData("chem_finisher", 0)]
    [InlineData("chem_finisher", 100)]
    [InlineData("blender", 0)]
    [InlineData("heating_plate", 0)]
    public void ExcludedMachinesNeverGetEnergyWarningButKeepInspectionWarning(string id, int charge)
    {
        var machine = Machine(charge, out _);
        Assert.Equal(charge < 6, HasLowBattery(machine));
        machine.identifier = id;
        Assert.False(HasLowBattery(machine));
        Assert.Equal(Warning.None, Applicable(machine, StolenWarningMode.OnInspection, true, true, false, waterEnabled: true, spillEnabled: true, outputOccupiedPercent: 50));
        machine.Set(MachineryHelper.IS_MACHINERY_POWERED, false);
        Assert.False(HasLowBattery(machine));
        machine.modifiedState.EnableTag(StolenHelper.STOLEN_TAG);
        Assert.Equal(Warning.Stolen, Applicable(machine, StolenWarningMode.OnInspection, true, true, true, waterEnabled: true, spillEnabled: true, outputOccupiedPercent: 50));
        Assert.Equal(Warning.None, Applicable(machine, StolenWarningMode.OnInspection, true, true, false, waterEnabled: true, spillEnabled: true, outputOccupiedPercent: 50));
    }

    [Fact]
    public void HypertechSourcesUseTheirOutputLimitRatherThanAChargePercentage()
    {
        var machine = Machine(0, out var battery);
        battery!.modifiedState.EnableTag(HypertechBatteryHelper.HYPERTECH_BATTERY_TAG);
        battery.Set(HypertechBatteryHelper.HYPERTECH_BATTERY_OUTPUT_INT, 6);
        Assert.False(HasLowBattery(machine));
        battery.Set(HypertechBatteryHelper.HYPERTECH_BATTERY_OUTPUT_INT, 5);
        Assert.True(HasLowBattery(machine));
    }

    [Fact]
    public void ConverterWarningUsesNightlyServiceInsteadOfDaytimeCash()
    {
        var storeField = AccessTools.Field(typeof(PlayerStore), "instance");
        var previousStore = storeField.GetValue(null);
        try
        {
            var store = Raw<PlayerStore>();
            store.storeService = [new StoreService { id = "CURRENT_CONVERTER_SERVICE", unlocked = true, isUsing = true }];
            store.playerCash = 0;
            storeField.SetValue(null, store);
            var machine = Machine(0, out var source);
            source!.modifiedState.EnableTag(CurrentConverterHelper.CURRENT_CONVERTER_TAG);
            source.Set(CurrentConverterHelper.CURRENT_CONVERTER_ON_BOOL, true);
            Assert.False(HasLowBattery(machine));
            store.storeService[0].isUsing = false;
            store.playerCash = 1000;
            Assert.True(HasLowBattery(machine));
            store.storeService[0].isUsing = true;
            source.Set(CurrentConverterHelper.CURRENT_CONVERTER_ON_BOOL, false);
            Assert.True(HasLowBattery(machine));
        }
        finally { storeField.SetValue(null, previousStore); }
    }

    [Fact]
    public void OnInspectionModeRequiresAnInspectorAndOtherWarningsRemainIndependent()
    {
        var item = Machine(0, out _);
        item.modifiedState.EnableTag(StolenHelper.STOLEN_TAG);
        Assert.False(IsInspection(null));
        var client = new StoreClient { clientIntent = StoreClient.ClientIntent.BUY };
        Assert.False(IsInspection(client));
        client.clientIntent = StoreClient.ClientIntent.INSPECTION;
        Assert.True(IsInspection(client));
        Assert.Equal(Warning.Stolen | Warning.LowBattery, Applicable(item, StolenWarningMode.OnInspection, true, false, true, outputOccupiedPercent: 50));
        Assert.Equal(Warning.LowBattery, Applicable(item, StolenWarningMode.OnInspection, true, false, false, outputOccupiedPercent: 50));
        Assert.Equal(Warning.LowBattery, Applicable(item, StolenWarningMode.Disabled, true, false, true, outputOccupiedPercent: 50));
        Assert.Equal(Warning.Stolen, Applicable(item, StolenWarningMode.OnInspection, false, false, true, outputOccupiedPercent: 50));
        Assert.Equal(Warning.None, Applicable(item, StolenWarningMode.Disabled, false, false, true, outputOccupiedPercent: 50));
        item.modifiedState.DisableTag(StolenHelper.STOLEN_TAG);
        Assert.Equal(Warning.None, Applicable(item, StolenWarningMode.OnInspection, false, false, true, outputOccupiedPercent: 50));
    }

    [Theory]
    [InlineData(StolenWarningMode.Disabled, false, false)]
    [InlineData(StolenWarningMode.Disabled, true, false)]
    [InlineData(StolenWarningMode.OnInspection, false, false)]
    [InlineData(StolenWarningMode.OnInspection, true, true)]
    [InlineData(StolenWarningMode.Always, false, true)]
    [InlineData(StolenWarningMode.Always, true, true)]
    public void AllStolenModesUseNativeStolenStateWithoutChangingIt(object selectedMode, bool inspection, bool shown)
    {
        var mode = (StolenWarningMode)selectedMode;
        var item = Machine(0, out _);
        // Heat alone does not make an item stolen; use the same predicate as the game.
        item.Set(StolenHelper.STOLEN_VALUE_INT, 100);
        Assert.False(StolenHelper.IsStolenItem(item));
        Assert.Equal(Warning.LowBattery, Applicable(item, mode, true, false, inspection, outputOccupiedPercent: 50));
        item.modifiedState.EnableTag(StolenHelper.STOLEN_TAG);
        item.Set(StolenHelper.STOLEN_VALUE_INT, 0);
        int tags = item.modifiedState.dict.Count;
        var expected = shown ? Warning.Stolen : Warning.None;
        Assert.Equal(expected, Applicable(item, mode, false, false, inspection, outputOccupiedPercent: 50));
        Assert.Equal(expected | Warning.LowBattery, Applicable(item, mode, true, false, inspection, outputOccupiedPercent: 50));
        Assert.True(StolenHelper.IsStolenItem(item));
        Assert.Equal(0, item.GetTagReadonly(StolenHelper.STOLEN_VALUE_INT).GetInt());
        Assert.Equal(tags, item.modifiedState.dict.Count);
    }

    [Fact]
    public void SettingsDefaultOnAndAssetLoadingWaitsForOurMod()
    {
        string id = "tuck-warnings-test-" + Guid.NewGuid().ToString("N");
        var configs = (Dictionary<string, ModConfig>)AccessTools.Field(typeof(ModConfig), "configs").GetValue(null)!;
        var config = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.NonPublic, null, [id], null)!;
        configs.Add(id, config);
        var hook = AccessTools.Field(typeof(ModHook), nameof(ModHook.OnModAssetsLoaded));
        var previousHandlers = hook.GetValue(null);
        var options = new Options(new ModManifest { ID = id, Name = "Tuck" });
        try
        {
            options.StolenWarnings.Register();
            options.LowBatteryWarnings.Register();
            options.MissingIngredientsWarnings.Register();
            options.MissingWaterWarnings.Register();
            options.SpillWarnings.Register();
            options.FilterDurabilityWarnings.Register();
            ItemWarnings.Register(options, id);
            foreach (string key in new[] { "low_battery_warnings", "missing_ingredients_warnings", "missing_water_warnings", "spill_warnings", "filter_durability_warnings" })
            {
                Assert.True(((ConfigEntry<bool>)config.Get(key)).Default);
                var row = GameOptionRegistry.Find(ModHelper.GameOptionId(id, key));
                Assert.Equal(GameOptionRegistry.Kind.Toggle, row.kind);
                Assert.True(row.getBool());
                var entry = (ConfigEntry<bool>)config.Get(key);
                AccessTools.Property(typeof(ConfigEntry), nameof(ConfigEntry.Config)).SetValue(entry, null);
                try
                {
                    row.setBool(false);
                    Assert.False(row.getBool());
                    row.setBool(true);
                    Assert.True(row.getBool());
                }
                finally { AccessTools.Property(typeof(ConfigEntry), nameof(ConfigEntry.Config)).SetValue(entry, config); }
            }
            var handlers = ((Action<LoadedMod>)hook.GetValue(null)!).GetInvocationList();
            var handler = Assert.Single(handlers, h => h.Method.DeclaringType == typeof(ItemWarnings));
            ((Action<LoadedMod>)handler)(new LoadedMod { Manifest = new ModManifest { ID = "unrelated" } });
            Assert.Null(AccessTools.Field(typeof(ItemWarnings), "_stolen").GetValue(null));
        }
        finally
        {
            ItemWarnings.Unregister();
            options.FilterDurabilityWarnings.Unregister();
            options.SpillWarnings.Unregister();
            options.MissingWaterWarnings.Unregister();
            options.MissingIngredientsWarnings.Unregister();
            options.LowBatteryWarnings.Unregister();
            options.StolenWarnings.Unregister();
            configs.Remove(id);
        }
        Assert.Equal(previousHandlers, hook.GetValue(null));
    }

    private static (int X, int Y)[] TestCells(GridShape shape, int orientation, bool flipped)
    {
        var cells = new (int X, int Y)[Kinds.Length];
        int count = Cells(shape, orientation, flipped, cells);
        return cells[..count];
    }

    [Theory]
    [InlineData(WarningAnimation.WaterFile)]
    [InlineData(WarningAnimation.SpillFile)]
    [InlineData(WarningAnimation.FilterFile)]
    public void NewStripsUseTheirActualFrameCount(string file)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "warnings", file));
        int width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        Assert.Equal(width / 16, WarningAnimation.FrameCount(width, height));
        Assert.True(width >= 16);
    }

    [Fact]
    public void SixKindsUseSixCellsAndPreservePriorityWhileOverflowCycles()
    {
        var all = Warning.Stolen | Warning.LowBattery | Warning.MissingWater | Warning.MissingIngredients | Warning.Spill | Warning.FilterDurability;
        Assert.Equal(new[] { Warning.Stolen, Warning.LowBattery, Warning.MissingWater, Warning.MissingIngredients, Warning.Spill, Warning.FilterDurability }, Kinds);
        for (int i = 0; i < Kinds.Length; i++) Assert.Equal(Kinds[i], Sequence(all, 6, i));
        Assert.Equal(Warning.Spill | Warning.FilterDurability, Sequence(all, 5, 4));
        Assert.Equal(Warning.MissingIngredients | Warning.Spill | Warning.FilterDurability, Sequence(all, 4, 3));
        Assert.Equal(Warning.MissingWater | Warning.MissingIngredients | Warning.Spill | Warning.FilterDurability, Sequence(all, 3, 2));
        Assert.Equal(all & ~Warning.Stolen, Sequence(all, 2, 1));
        Assert.Equal(all, Sequence(all, 1, 0));
        Assert.Equal(6, TestCells(new GridShapeBuilder(2, 3).SetDataFill(1), 0, false).Length);
    }

    [Theory]
    [InlineData("water_filter")]
    [InlineData("water_filter_adv")]
    [InlineData("hydroreed_filter")]
    [InlineData("kotton_fabric")]
    public void NativePitcherAndRasLayoutsWorkWithoutHostTagsAtZeroRealDurability(string id)
    {
        var filter = Filter(id);
        var pitcher = Pitcher(filter);
        var ras = Ras(filter);
        Assert.Empty(pitcher.modifiedState.dict);
        Assert.Empty(ras.modifiedState.dict);
        filter.Set(DurabilityHelper.DURABILITY_FAKE_TAG, 400);
        int tags = filter.modifiedState.dict.Count;
        Assert.True(HasOnlyDepletedFilters(pitcher));
        Assert.True(HasOnlyDepletedFilters(ras));
        Assert.Equal(tags, filter.modifiedState.dict.Count);
        filter.Set(DurabilityHelper.DURABILITY_TAG, 1);
        filter.Set(DurabilityHelper.DURABILITY_FAKE_TAG, 0);
        Assert.False(HasOnlyDepletedFilters(pitcher));
        Assert.False(HasOnlyDepletedFilters(ras));
        filter.Set(DurabilityHelper.DURABILITY_TAG, 400);
        Assert.False(HasOnlyDepletedFilters(pitcher));
        Assert.False(HasOnlyDepletedFilters(ras));
        filter.Set(DurabilityHelper.DURABILITY_TAG, 0);
        Assert.True(HasOnlyDepletedFilters(pitcher));
        Assert.True(HasOnlyDepletedFilters(ras));
    }

    [Fact]
    public void AnyHostWithItsOwnFilterSlotQualifiesButIndividualFiltersDoNot()
    {
        var filter = Filter();
        Assert.Equal(Warning.None, Applicable(filter, StolenWarningMode.Disabled, false, false, false, filterEnabled: true, outputOccupiedPercent: 50));
        filter.parentInventory = Raw<GameGridInventory>();
        Assert.False(HasOnlyDepletedFilters(filter));
        Assert.True(HasOnlyDepletedFilters(Pitcher(filter)));
        Assert.False(HasOnlyDepletedFilters(filter));
        Assert.True(HasOnlyDepletedFilters(Ras(filter)));
        Assert.False(HasOnlyDepletedFilters(filter));
        foreach (string id in new[] { "unknown_mod_item", "backpack", "pouch", "water_purifier" })
        {
            var other = new Item(id);
            SetContent(other, FilterSlot(filter));
            Assert.Empty(other.modifiedState.dict);
            Assert.True(HasOnlyDepletedFilters(other));
        }
    }

    [Fact]
    public void OwnSlotsRequireAnInstalledFilterAndExcludeNestedStoredItems()
    {
        var pitcher = Pitcher(null);
        Assert.False(HasOnlyDepletedFilters(pitcher));
        var filter = Filter();
        SetContent(pitcher, FilterSlot(filter));
        Assert.True(HasOnlyDepletedFilters(pitcher));
        AccessTools.Property(typeof(GameItem), nameof(GameItem.contentWindow)).SetValue(pitcher, null);
        Assert.False(HasOnlyDepletedFilters(pitcher));
        var bag = new Item("bag");
        SetContent(bag, FilterSlot(filter));
        SetContent(pitcher, FilterSlot(bag));
        Assert.False(HasOnlyDepletedFilters(pitcher));
    }

    [Fact]
    public void SlotGroupsNeedAtLeastOneFilterAndEveryParticipatingFilterMustBeDepleted()
    {
        Assert.False(HasOnlyDepletedFilters(Ras()));
        var first = Filter();
        var last = Filter();
        var ras = Ras(first, null, null, last);
        Assert.Equal(2, MachineRAS.GetInstalledFilterCount(ras));
        Assert.True(HasOnlyDepletedFilters(ras)); // Empty sockets are irrelevant, not depleted filters.
        last.Set(DurabilityHelper.DURABILITY_TAG, 1);
        Assert.False(HasOnlyDepletedFilters(ras));
        last.Set(DurabilityHelper.DURABILITY_TAG, 0);
        Assert.True(HasOnlyDepletedFilters(ras));
        first.Set(DurabilityHelper.DURABILITY_TAG, 400);
        Assert.False(HasOnlyDepletedFilters(ras));
        Assert.True(HasOnlyDepletedFilters(Ras(Filter(), Filter(), Filter(), Filter())));
    }

    [Fact]
    public void WorkingUvSuppressesHostWarningDespiteExhaustedDurableFilters()
    {
        var uv = new Item("uv_filter");
        uv.modifiedState.EnableTag(AmenitiesItemDirectory.WATER_FILTER);
        Assert.False(uv.IsTag(DurabilityHelper.DURABILITY_TAG));
        Assert.False(HasOnlyDepletedFilters(Ras(uv)));
        var uvSlot = FilterSlot(uv);
        var host = Host(Layout(FilterSlot(Filter()), FilterSlot(Filter()), FilterSlot(null), uvSlot));
        Assert.False(HasOnlyDepletedFilters(host));
        AccessTools.Field(typeof(GameSlotInventory), "currentItem").SetValue(uvSlot, null);
        Assert.True(HasOnlyDepletedFilters(host));
    }

    [Fact]
    public void WaterFilterTagAndEnabledRealDurabilityMatterNotItemType()
    {
        var filter = Filter();
        var pitcher = Pitcher(filter);
        filter.itemTypes.Clear();
        Assert.True(HasOnlyDepletedFilters(pitcher));
        filter.modifiedState.DisableTag(DurabilityHelper.DURABILITY_TAG);
        Assert.False(HasOnlyDepletedFilters(pitcher));
        Assert.False(HasOnlyDepletedFilters(Ras(Filter(), filter))); // Unknown nondurable filter is not assumed depleted.
        filter.Set(DurabilityHelper.DURABILITY_TAG, -1);
        Assert.False(HasOnlyDepletedFilters(pitcher)); // Exactly zero, not arbitrary nonpositive data.
        filter.Set(DurabilityHelper.DURABILITY_TAG, 0);
        filter.modifiedState.DisableTag(AmenitiesItemDirectory.WATER_FILTER);
        Assert.False(HasOnlyDepletedFilters(pitcher));
        Assert.False(HasOnlyDepletedFilters(Ras(filter)));
        Assert.True(HasOnlyDepletedFilters(Ras(Filter(), filter))); // An untagged occupant does not participate.
    }

    [Fact]
    public void NativeWrappersExposeAllHostSlotsIncludingUnselectedTabsAndClosedContent()
    {
        var working = Filter();
        working.Set(DurabilityHelper.DURABILITY_TAG, 1);
        var tabs = Tabs(FilterSlot(Filter()), Layout(FilterSlot(null), FilterSlot(working)));
        var stack = Raw<MachineryStack>();
        AccessTools.Field(typeof(MachineryStack), "children").SetValue(stack, new List<PixelElement>
        {
            FilterSlot(null), Wrap<ScrollElement>(nameof(ScrollElement.childElement),
                Wrap<MachineryPanel>(nameof(MachineryPanel.content), tabs))
        });
        var intake = Wrap<MachineryIntake>(nameof(MachineryIntake.content), stack);
        var host = Host(Row(intake));
        // No live UI/window was created or opened; inactive tab slots are still the host's slots.
        Assert.Null(host.contentWindow.handler);
        Assert.False(HasOnlyDepletedFilters(host));
        working.Set(DurabilityHelper.DURABILITY_TAG, 0);
        Assert.True(HasOnlyDepletedFilters(host));
        foreach (string role in new[] { "input", "output", "storage", "filters", "unknown" })
        {
            ((MachineryPanelRow)host.contentWindow.childElement).GetPanel(0).role = role;
            Assert.True(HasOnlyDepletedFilters(host)); // Panel roles are not filter eligibility rules.
        }
    }

    [Fact]
    public void LooseGridSparesAndStoredContainersNeverCountAsHostFilters()
    {
        var spare = Filter();
        spare.Set(DurabilityHelper.DURABILITY_TAG, 10);
        var storedHost = Host(FilterSlot(Filter()));
        var grid = Raw<GameGridInventory>();
        AccessTools.Field(typeof(GameGridInventory), "items").SetValue(grid, new List<GameItem> { spare, storedHost });
        Assert.False(HasOnlyDepletedFilters(Host(grid)));
        Assert.False(HasOnlyDepletedFilters(Host(FilterSlot(storedHost))));
        Assert.True(HasOnlyDepletedFilters(Host(Row(grid, FilterSlot(Filter()))))); // Loose working spares cannot clear it either.

        var childItem = Raw<GameItemElement>();
        SetContent(childItem, FilterSlot(Filter()));
        Assert.True(HasOnlyDepletedFilters(childItem));
        Assert.False(HasOnlyDepletedFilters(Host(Wrap<MachineryPanel>(nameof(MachineryPanel.content), childItem))));
    }

    [Fact]
    public void HostExamineSlotsAreIncludedWithoutEnteringChildItems()
    {
        var host = Host(FilterSlot(Filter()));
        var working = Filter();
        working.Set(DurabilityHelper.DURABILITY_TAG, 1);
        var examined = Host(FilterSlot(working));
        AccessTools.Property(typeof(GameItem), nameof(GameItem.examineWindow)).SetValue(host, examined.contentWindow);
        Assert.False(HasOnlyDepletedFilters(host));
        working.Set(DurabilityHelper.DURABILITY_TAG, 0);
        Assert.True(HasOnlyDepletedFilters(host));
        AccessTools.Property(typeof(GameItem), nameof(GameItem.contentWindow)).SetValue(host, null);
        Assert.True(HasOnlyDepletedFilters(host));
    }

    [Fact]
    public void FilterWarningToggleAndInspectionWarningAreIndependent()
    {
        var host = Host(FilterSlot(Filter()));
        host.modifiedState.EnableTag(StolenHelper.STOLEN_TAG);
        Assert.Equal(Warning.None, Applicable(host, StolenWarningMode.Disabled, false, false, false, outputOccupiedPercent: 50));
        Assert.Equal(Warning.FilterDurability, Applicable(host, StolenWarningMode.Disabled, false, false, false, filterEnabled: true, outputOccupiedPercent: 50));
        Assert.Equal(Warning.Stolen, Applicable(host, StolenWarningMode.OnInspection, false, false, true, outputOccupiedPercent: 50));
        var both = Warning.Stolen | Warning.FilterDurability;
        Assert.Equal(both, Applicable(host, StolenWarningMode.OnInspection, false, false, true, filterEnabled: true, outputOccupiedPercent: 50));
        Assert.Equal(both, Sequence(both, 1, 0));
        Assert.Equal(Warning.Stolen, Sequence(both, 2, 0));
        Assert.Equal(Warning.FilterDurability, Sequence(both, 2, 1));
        SetContent(host, FilterSlot(null));
        Assert.Equal(Warning.Stolen, Applicable(host, StolenWarningMode.OnInspection, false, false, true, filterEnabled: true, outputOccupiedPercent: 50));
    }

    private static Item Filter(string id = "water_filter")
    {
        var item = new Item(id);
        item.itemTypes.Add(TypeHelper.FILTER);
        item.modifiedState.EnableTag(AmenitiesItemDirectory.WATER_FILTER);
        item.Set(DurabilityHelper.DURABILITY_TAG, 0);
        return item;
    }

    private static GameSlotInventory FilterSlot(Item? item)
    {
        var slot = Raw<GameSlotInventory>();
        AccessTools.Property(typeof(TagSystem), nameof(TagSystem.dict)).SetValue(slot, new Dictionary<string, TagState>());
        AccessTools.Field(typeof(GameSlotInventory), "currentItem").SetValue(slot, item);
        if (item != null) item.parentInventory = slot;
        return slot;
    }

    private static Item Pitcher(Item? filter)
    {
        var pitcher = new Item("water_pitcher");
        SetContent(pitcher, FilterSlot(filter));
        return pitcher;
    }

    private static Item Ras(params Item?[] filters)
    {
        var ras = new Item("ras");
        var grid = Layout(Enumerable.Range(0, 4).Select(i => (PixelElement)FilterSlot(i < filters.Length ? filters[i] : null)).ToArray());
        SetContent(ras, Row(FilterSlot(null), FilterSlot(null), grid));
        return ras;
    }

    private static Item Host(PixelElement content)
    {
        var host = new Item("unknown_mod_host");
        SetContent(host, content);
        return host;
    }

    private static GridPixelElement Layout(params PixelElement[] cells)
    {
        var grid = Raw<GridPixelElement>();
        AccessTools.Field(typeof(GridPixelElement), nameof(GridPixelElement.gridWidth)).SetValue(grid, cells.Length);
        AccessTools.Field(typeof(GridPixelElement), nameof(GridPixelElement.gridHeight)).SetValue(grid, 1);
        AccessTools.Field(typeof(GridPixelElement), "childElements").SetValue(grid, cells);
        return grid;
    }

    private static MachineryPanelRow Row(params PixelElement[] contents)
    {
        var row = Raw<MachineryPanelRow>();
        var panels = contents.Select(content => Wrap<MachineryPanel>(nameof(MachineryPanel.content), content)).ToList();
        AccessTools.Field(typeof(MachineryPanelRow), "panels").SetValue(row, panels);
        return row;
    }

    private static T Wrap<T>(string property, PixelElement content)
    {
        var wrapper = Raw<T>();
        AccessTools.Property(typeof(T), property).SetValue(wrapper, content);
        return wrapper;
    }

    private static MachineryTabs Tabs(params PixelElement[] pages)
    {
        var tabs = Raw<MachineryTabs>();
        var tabType = typeof(MachineryTabs).GetNestedType("Tab", BindingFlags.NonPublic)!;
        var entries = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tabType))!;
        foreach (var page in pages)
        {
            var entry = RuntimeHelpers.GetUninitializedObject(tabType);
            AccessTools.Field(tabType, "page").SetValue(entry, page);
            entries.Add(entry);
        }
        AccessTools.Field(typeof(MachineryTabs), "tabs").SetValue(tabs, entries);
        AccessTools.Property(typeof(MachineryTabs), nameof(MachineryTabs.selected)).SetValue(tabs, 0);
        return tabs;
    }

    private static void SetContent(GameItem item, PixelElement content)
    {
        var window = Raw<PixelWindow>();
        AccessTools.Property(typeof(PixelWindow), nameof(PixelWindow.childElement)).SetValue(window, content);
        AccessTools.Property(typeof(GameItem), nameof(GameItem.contentWindow)).SetValue(item, window);
    }

    private static Item Machine(int charge, out Item? battery)
    {
        var machine = new Item("machine");
        machine.modifiedState.EnableTag(MachineHelper.MACHINE_RECHARGABLE_TAG);
        machine.modifiedState.EnableTag(MachineryHelper.MACHINERY_TAG);
        machine.Set(MachineryHelper.IS_MACHINERY_POWERED, true);
        machine.Set(MachineryHelper.MACHINERY_DRAWN_POWER, 6);
        machine.onCycleEndSlotItemFunc = (_, _, _) => { };
        battery = charge < 0 ? null : new Item("battery");
        if (battery != null)
        {
            battery.modifiedState.EnableTag(PowerHelper.POWER_SOURCE_ITEM_TAG);
            battery.Set(PowerHelper.POWER_SOURCE_ITEM_ENERGY, charge);
            battery.Set(PowerHelper.POWER_SOURCE_ITEM_MAX_ENERGY, 100);
        }
        var slot = Raw<GameSlotInventory>();
        AccessTools.Field(typeof(GameSlotInventory), "currentItem").SetValue(slot, battery);
        var panel = Raw<MachineryPanel>();
        AccessTools.Property(typeof(MachineryPanel), nameof(MachineryPanel.content)).SetValue(panel, slot);
        var row = Raw<MachineryPanelRow>();
        AccessTools.Field(typeof(MachineryPanelRow), "panels").SetValue(row, new List<MachineryPanel> { panel });
        var window = Raw<PixelWindow>();
        AccessTools.Property(typeof(PixelWindow), nameof(PixelWindow.childElement)).SetValue(window, row);
        AccessTools.Property(typeof(GameItem), nameof(GameItem.contentWindow)).SetValue(machine, window);
        return machine;
    }

    private static T Raw<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private sealed class Item(string id) : GameItem(id)
    {
        internal void Set(string tag, int value) => modifiedState.GetTag(tag).Enable().SetInt(value);
        internal void Set(string tag, bool value) => modifiedState.GetTag(tag).Enable().SetBool(value);
        public override void Validate() { }
        public override void Destroy() => throw new InvalidOperationException("Warnings must not destroy items.");
    }
}
