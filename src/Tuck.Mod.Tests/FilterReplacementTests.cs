using System.Reflection;
using System.Runtime.CompilerServices;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class FilterReplacementTests : IDisposable
{
    private readonly string _modId = "tuck-filter-test-" + Guid.NewGuid().ToString("N");
    private readonly ModConfig _config;
    private readonly Options _options;
    private readonly ConfigEntry<bool> _entry;
    private static Dictionary<string, ModConfig> Configs =>
        (Dictionary<string, ModConfig>)typeof(ModConfig).GetField("configs", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;

    public FilterReplacementTests()
    {
        _config = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: [_modId], culture: null)!;
        Configs.Add(_modId, _config);
        _options = new Options(new ModManifest { ID = _modId, Name = "Tuck" });
        _entry = (ConfigEntry<bool>)_config.Get("replace_filters");
        _options.ReplaceFilters.Register();
        FilterReplacement.Register(_options);
    }

    [Fact]
    public void DefaultEnabledToggleAppliesImmediatelyWithoutChangingItemHandlers()
    {
        Assert.True(_entry.Default);
        Assert.False(_entry.RequiresRestart);
        var row = GameOptionRegistry.Find(ModHelper.GameOptionId(_modId, _entry.Key));
        Assert.True(row.getBool());
        var (source, target) = Pair();
        Func<GameItem, GameItem, bool> other = (_, _) => true;
        Action<GameItem, GameItem> otherAction = (_, _) => { };
        source.mayThisTargetItemFunc += other;
        source.canThisTargetItemFunc += other;
        source.onThisTargetItemFunc += otherAction;
        bool result = false;
        Assert.False(FilterReplacement.MayTargetPrefix(source, target, ref result));
        Assert.True(result);
        Assert.True(FilterReplacement.CanReplace(source, target));

        WithoutSaving(() => row.setBool(false));
        Assert.False(_options.ReplaceFilters.Value);
        Assert.False(FilterReplacement.CanReplace(source, target));
        Assert.Equal(other, source.mayThisTargetItemFunc);
        Assert.Equal(other, source.canThisTargetItemFunc);
        Assert.Equal(otherAction, source.onThisTargetItemFunc);
        Assert.True(FilterReplacement.MayTargetPrefix(null!, null!, ref result));
        Assert.True(FilterReplacement.CanTargetPrefix(null!, null!, ref result));
        Assert.True(FilterReplacement.TargetPrefix(null!, null!));

        WithoutSaving(() => _entry.Set(true)); // Mods UI writes the same entry.
        Assert.True(row.getBool());
        Assert.True(FilterReplacement.CanReplace(source, target));
        FilterReplacement.Unregister();
        Assert.Equal(otherAction, source.onThisTargetItemFunc);
        Assert.False(FilterReplacement.CanReplace(source, target));
        Assert.True(FilterReplacement.MayTargetPrefix(null!, null!, ref result));
    }

    [Theory]
    [InlineData("water_filter")]
    [InlineData("water_filter_adv")]
    [InlineData("hydroreed_filter")]
    [InlineData("uv_filter")]
    public void InstalledMachineFiltersCanBeReplacedWithoutMutatingTheInventories(string id)
    {
        var (source, target) = Pair(id);
        var sourceInventory = source.parentInventory;
        var targetInventory = target.parentInventory;
        Assert.True(FilterReplacement.CanReplace(source, target));
        bool result = false;
        Assert.False(FilterReplacement.CanTargetPrefix(source, target, ref result));
        Assert.True(result);
        Assert.Same(sourceInventory, source.parentInventory);
        Assert.Same(targetInventory, target.parentInventory);
        Assert.Same(target, ((GameSlotInventory)targetInventory).childItem);
    }

    [Fact]
    public void PitcherAdmissionStillRejectsUvFilters()
    {
        var (source, target) = Pair("uv_filter");
        target.parentInventory.mayInventoryAddItemFunc = (item, _) =>
            GeneralHelper.IsItemOwned(item) && item.IsTag("WATER_FILTER") && item.identifier != "uv_filter";
        Assert.False(FilterReplacement.CanReplace(source, target));
        source.identifier = "water_filter_adv";
        Assert.True(FilterReplacement.CanReplace(source, target));
    }

    [Theory]
    [InlineData("pinned source")]
    [InlineData("pinned target")]
    [InlineData("unowned")]
    [InlineData("remove lock")]
    [InlineData("insert lock")]
    [InlineData("remove rule")]
    [InlineData("return admission")]
    [InlineData("quantity")]
    [InlineData("stacking")]
    [InlineData("not filter")]
    public void RestrictionsPreventReplacement(string restriction)
    {
        var (source, target) = Pair();
        switch (restriction)
        {
            case "pinned source": source.modifiedState.EnableTag(ItemPinHelper.PINNED_TAG); break;
            case "pinned target": target.modifiedState.EnableTag(ItemPinHelper.PINNED_TAG); break;
            case "unowned": target.modifiedState.DisableTag("IS_OWNED_TAG"); break;
            case "remove lock": target.parentInventory.overrideLockRemove = true; break;
            case "insert lock": target.parentInventory.overrideLockInsert = true; break;
            case "remove rule": target.mayInventoryRemoveItemFunc = (_, _) => false; break;
            case "return admission": source.parentInventory.mayInventoryAddItemFunc = (_, _) => false; break;
            case "quantity": target.minSlotAmountItemFunc = (_, _, _) => 1; break;
            case "stacking": source.mayStackItemFunc = target.mayStackItemFunc = (_, _) => true; break;
            case "not filter": source.modifiedState.DisableTag("WATER_FILTER"); break;
        }
        Assert.False(FilterReplacement.CanReplace(source, target));
    }

    [Fact]
    public void LargerRemovedFilterRequiresFreeSpaceAtTheOriginalPosition()
    {
        var (source, target) = Pair();
        target.shape = new GridShapeBuilder(2, 1).SetDataFill(1);
        target.modifiedShape = target.shape.Clone();
        Assert.False(FilterReplacement.CanReplace(source, target)); // Grid is only one cell wide.
        var grid = Grid(2, source);
        Assert.True(FilterReplacement.CanReplace(source, target));
        var neighbor = new Item("bolt");
        neighbor.modifiedShape = new GridShapeBuilder(neighbor.shape).SetPosition(1, 0);
        Field(grid, typeof(GameGridInventory), "items", new List<GameItem> { source, neighbor });
        neighbor.parentInventory = grid;
        Assert.False(FilterReplacement.CanReplace(source, target));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RemovedFilterRotatesToFitAndDropUsesTheProbedPlacement(int orientation)
    {
        var (source, target) = Pair();
        source.shape = new GridShapeBuilder(orientation % 2 == 0 ? 1 : 2, orientation % 2 == 0 ? 2 : 1).SetDataFill(1);
        source.modifiedShape = new GridShapeBuilder(source.shape).SetTransform(1, 1, true, orientation);
        target.shape = new GridShapeBuilder(2, 1).SetDataFill(1);
        target.modifiedShape = target.shape.Clone();
        var grid = Grid(2, source, 3); // Only a vertical filter fits at (1, 1).
        var slot = (GameSlotInventory)target.parentInventory;
        var sourceShape = source.modifiedShape;
        var targetShape = target.modifiedShape;

        bool result = false;
        Assert.False(FilterReplacement.MayTargetPrefix(source, target, ref result));
        Assert.True(result);
        Assert.Same(sourceShape, source.modifiedShape);
        Assert.Same(targetShape, target.modifiedShape);
        Assert.Same(source, Assert.Single(grid.childItems));
        Assert.Same(target, slot.childItem);

        var buffer = (Slot)RuntimeHelpers.GetUninitializedObject(typeof(Slot));
        Assert.True(FilterReplacement.TryReplace(source, target, buffer));
        Assert.Same(source, slot.childItem);
        Assert.Same(slot, source.parentInventory);
        Assert.Same(target, Assert.Single(grid.childItems));
        Assert.Same(grid, target.parentInventory);
        Assert.Null(buffer.childItem);
        Assert.Equal((1, 1, false, 1),
            (target.modifiedShape.minX, target.modifiedShape.minY,
                target.modifiedShape.flipped, target.modifiedShape.orientation));
        Assert.Equal((0, 0, false, 0), (source.modifiedShape.minX, source.modifiedShape.minY,
            source.modifiedShape.flipped, source.modifiedShape.orientation));
        Assert.True(grid.CheckShapePlacement(target, target.modifiedShape, []));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void RemovedFilterKeepsItsOwnTransformWhenItFits(int orientation, bool flipped)
    {
        var (source, target) = Pair();
        source.modifiedShape = new GridShapeBuilder(source.shape).SetTransform(1, 1, !flipped, (orientation + 1) % 4);
        target.modifiedShape = new GridShapeBuilder(2, 1).SetDataFill(1).SetTransform(0, 0, flipped, orientation);
        var grid = Grid(3, source, 3);
        var buffer = (Slot)RuntimeHelpers.GetUninitializedObject(typeof(Slot));

        Assert.True(FilterReplacement.TryReplace(source, target, buffer));
        Assert.Equal((1, 1, flipped, orientation),
            (target.modifiedShape.minX, target.modifiedShape.minY, target.modifiedShape.flipped, target.modifiedShape.orientation));
        Assert.True(grid.CheckShapePlacement(target, target.modifiedShape, []));
    }

    [Theory]
    [InlineData(0, 2, 3)]
    [InlineData(1, 3, 4)]
    [InlineData(2, 4, 1)]
    [InlineData(3, 1, 2)]
    public void ReturnedFilterUsesOccupiedCenterOfVacatedShape(int orientation, int x, int y)
    {
        var (source, target) = Pair();
        source.shape = new GridShapeBuilder().SetData("100001000011111", 5);
        source.modifiedShape = new GridShapeBuilder(source.shape).SetTransform(1, 1, false, orientation);
        Grid(8, source, 8);
        var buffer = (Slot)RuntimeHelpers.GetUninitializedObject(typeof(Slot));

        Assert.True(FilterReplacement.TryReplace(source, target, buffer));
        Assert.Equal((x, y), (target.modifiedShape.minX, target.modifiedShape.minY));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RepeatedSwapsBetweenEvenAndOddLengthsDoNotDrift(int orientation)
    {
        var (source, target) = Pair();
        source.modifiedShape = new GridShapeBuilder(1, 2).SetDataFill(1).SetTransform(4, 4, false, orientation);
        target.modifiedShape = new GridShapeBuilder(1, 3).SetDataFill(1).SetOrientation(orientation);
        Grid(10, source, 10);
        var buffer = (Slot)RuntimeHelpers.GetUninitializedObject(typeof(Slot));

        for (int swap = 0; swap < 6; swap++)
        {
            Assert.True(FilterReplacement.TryReplace(source, target, buffer));
            Assert.Equal((4, 4), (target.modifiedShape.minX, target.modifiedShape.minY));
            (source, target) = (target, source);
        }
    }

    [Fact]
    public void ReturnedFilterUsesItsOccupiedCenterAndPreferredTransformOnEqualDistances()
    {
        var (source, target) = Pair();
        source.modifiedShape = new GridShapeBuilder(source.shape).SetPosition(4, 3);
        target.modifiedShape = new GridShapeBuilder().SetData("100001000011111", 5);
        Grid(8, source, 8);
        var buffer = (Slot)RuntimeHelpers.GetUninitializedObject(typeof(Slot));

        Assert.True(FilterReplacement.TryReplace(source, target, buffer));
        // Rotations/reflections tie at this fractional centroid distance; preserve the original transform.
        Assert.Equal((3, 1, false, 0), (target.modifiedShape.minX, target.modifiedShape.minY,
            target.modifiedShape.flipped, target.modifiedShape.orientation));
    }

    [Fact]
    public void SurroundedFilterSwapChoosesClosestOccupiedCentersAcrossRotations()
    {
        var (source, target) = Pair();
        source.modifiedShape = new GridShapeBuilder(1, 2).SetDataFill(1).SetPosition(3, 3);
        target.modifiedShape = new GridShapeBuilder(3, 1).SetDataFill(1);
        var grid = Grid(7, source, 7);
        var items = new List<GameItem> { source };
        foreach (var (x, y) in new[] { (3, 2), (2, 3), (4, 3), (2, 4), (4, 4), (3, 5) })
            items.Add(new Item("blocker")
            {
                parentInventory = grid,
                modifiedShape = new GridShapeBuilder(1, 1).SetDataFill(1).SetPosition(x, y)
            });
        Field(grid, typeof(GameGridInventory), "items", items);
        var buffer = (Slot)RuntimeHelpers.GetUninitializedObject(typeof(Slot));

        Assert.True(FilterReplacement.TryReplace(source, target, buffer));
        // A vertical fit beside the enclosure is closer by center than the horizontal fit above it.
        Assert.Equal((1, 3, false, 1), (target.modifiedShape.minX, target.modifiedShape.minY,
            target.modifiedShape.flipped, target.modifiedShape.orientation));
        Assert.True(grid.CheckShapePlacement(target, target.modifiedShape, []));
    }

    [Fact]
    public void RejectedClosestPlacementDoesNotDiscardTheBestValidPlacement()
    {
        var (source, target) = Pair();
        source.modifiedShape = new GridShapeBuilder(source.shape).SetPosition(2, 2);
        var grid = Grid(5, source, 5);
        grid.isItemStateValidFunc = (_, _, _, shape, _) => shape.minX != 2 || shape.minY != 2;
        var buffer = (Slot)RuntimeHelpers.GetUninitializedObject(typeof(Slot));

        Assert.True(FilterReplacement.TryReplace(source, target, buffer));
        Assert.Equal((2, 1), (target.modifiedShape.minX, target.modifiedShape.minY));
    }

    [Fact]
    public void DropRechecksSpaceForRotatedFilter()
    {
        var (source, target) = Pair();
        target.modifiedShape = new GridShapeBuilder(2, 1).SetDataFill(1);
        var grid = Grid(1, source, 2);
        Assert.True(FilterReplacement.CanReplace(source, target));
        var neighbor = new Item("bolt") { parentInventory = grid };
        neighbor.modifiedShape = new GridShapeBuilder(neighbor.shape).SetPosition(0, 1);
        Field(grid, typeof(GameGridInventory), "items", new List<GameItem> { source, neighbor });

        var sourceShape = source.modifiedShape;
        var targetShape = target.modifiedShape;
        Assert.False(FilterReplacement.CanReplace(source, target));
        Assert.False(FilterReplacement.TargetPrefix(source, target));
        Assert.Same(grid, source.parentInventory);
        Assert.Same(target, ((GameSlotInventory)target.parentInventory).childItem);
        Assert.Same(sourceShape, source.modifiedShape);
        Assert.Same(targetShape, target.modifiedShape);
    }

    [Fact]
    public void StraightFilterFitsBottomRowOfVacatedTShape()
    {
        // Items: a T-shaped fresh filter and a vertical 1×3 installed filter.
        var freshFilter = new Item("water_filter")
        {
            shape = new GridShapeBuilder().SetData(
                "010" +
                "111", 3)
        };
        freshFilter.modifiedShape = new GridShapeBuilder(freshFilter.shape).SetPosition(1, 1);
        var installedFilter = new Item("water_filter")
        {
            shape = new GridShapeBuilder(1, 3).SetDataFill(1)
        };
        installedFilter.modifiedShape = installedFilter.shape.Clone();

        // Inventory: T = fresh filter, # = a separate one-cell blocker. No other space is free.
        string[] inventoryLayout =
        [
            "#####",
            "##T##",
            "#TTT#",
            "#####"
        ];
        var grid = Grid(inventoryLayout[0].Length, freshFilter, inventoryLayout.Length);
        var inventoryItems = new List<GameItem> { freshFilter };
        for (int y = 0; y < inventoryLayout.Length; y++)
            for (int x = 0; x < inventoryLayout[y].Length; x++)
            {
                if (inventoryLayout[y][x] != '#') continue;
                inventoryItems.Add(new Item("blocker")
                {
                    parentInventory = grid,
                    modifiedShape = new GridShapeBuilder(1, 1).SetDataFill(1).SetPosition(x, y)
                });
            }
        Field(grid, typeof(GameGridInventory), "items", inventoryItems);

        // Machine slot holds the installed filter; the empty buffer is used during the swap.
        var machineSlot = (Slot)RuntimeHelpers.GetUninitializedObject(typeof(Slot));
        Field(machineSlot, typeof(GameSlotInventory), "currentItem", installedFilter);
        installedFilter.parentInventory = machineSlot;
        var buffer = (Slot)RuntimeHelpers.GetUninitializedObject(typeof(Slot));

        // Hover accepts the swap without changing either item's placement.
        var freshPlacement = freshFilter.modifiedShape;
        var installedPlacement = installedFilter.modifiedShape;
        Assert.True(FilterReplacement.CanReplace(freshFilter, installedFilter));
        Assert.Same(freshPlacement, freshFilter.modifiedShape);
        Assert.Same(installedPlacement, installedFilter.modifiedShape);

        // Drop returns the installed filter horizontally into the T's bottom row.
        Assert.True(FilterReplacement.TryReplace(freshFilter, installedFilter, buffer));
        Assert.Equal(1, installedFilter.modifiedShape.orientation); // Quarter turn.
        Assert.Equal(3, installedFilter.modifiedShape.globalWidth);
        Assert.Equal(1, installedFilter.modifiedShape.globalHeight);
        Assert.Equal((1, 2), (installedFilter.modifiedShape.minX, installedFilter.modifiedShape.minY));
        Assert.True(grid.CheckShapePlacement(installedFilter, installedFilter.modifiedShape, []));
    }

    [Fact]
    public void LargerFilterCanExtendLeftOfTheVacatedPosition()
    {
        var (source, target) = Pair();
        source.modifiedShape = new GridShapeBuilder(source.shape).SetPosition(1, 0);
        target.modifiedShape = new GridShapeBuilder(2, 1).SetDataFill(1);
        var grid = Grid(2, source);
        var buffer = (Slot)RuntimeHelpers.GetUninitializedObject(typeof(Slot));
        Assert.True(FilterReplacement.TryReplace(source, target, buffer));
        Assert.Equal((0, 0, 2, 1), (target.modifiedShape.minX, target.modifiedShape.minY,
            target.modifiedShape.globalWidth, target.modifiedShape.globalHeight));
        Assert.True(grid.CheckShapePlacement(target, target.modifiedShape, []));
    }

    [Fact]
    public void RemovedFilterCanUseFreeSpaceElsewhereInTheInventory()
    {
        var (source, target) = Pair();
        target.modifiedShape = new GridShapeBuilder(2, 1).SetDataFill(1);
        var grid = Grid(4, source);
        var blocker = new Item("bolt") { parentInventory = grid };
        blocker.modifiedShape = new GridShapeBuilder(blocker.shape).SetPosition(1, 0);
        Field(grid, typeof(GameGridInventory), "items", new List<GameItem> { source, blocker });
        Assert.True(FilterReplacement.CanReplace(source, target));
        var buffer = (Slot)RuntimeHelpers.GetUninitializedObject(typeof(Slot));
        Assert.True(FilterReplacement.TryReplace(source, target, buffer));
        Assert.Equal((2, 0), (target.modifiedShape.minX, target.modifiedShape.minY));
        Assert.True(grid.CheckShapePlacement(target, target.modifiedShape, []));
    }

    [Fact]
    public void DistinctShapesKeepThePreferredFlipAndNearestRotation()
    {
        GridShape line = new GridShapeBuilder(1, 3).SetDataFill(1).SetTransform(10, 20, true, 2);
        var shapes = FilterSwapCandidates.Shapes(line);
        Assert.Equal(new[] { (true, 2), (true, 3) },
            shapes.Select(candidate => (candidate.Shape.flipped, candidate.Shape.orientation)));

        GridShape asymmetric = new GridShapeBuilder().SetData("101011", 2).SetTransform(10, 20, true, 2);
        shapes = FilterSwapCandidates.Shapes(asymmetric);
        Assert.Equal(new[] { (true, 2), (true, 3), (true, 1), (true, 0),
            (false, 2), (false, 3), (false, 1), (false, 0) },
            shapes.Select(candidate => (candidate.Shape.flipped, candidate.Shape.orientation)));
    }

    [Fact]
    public void EquivalentTransformsAreEvaluatedOnlyOnce()
    {
        var (source, target) = Pair();
        Assert.Single(FilterSwapCandidates.Shapes(target.modifiedShape));

        var grid = (GridInventory)source.parentInventory;
        grid.isItemStateValidFunc = (_, _, _, shape, _) => shape.orientation == 2;
        var buffer = (Slot)RuntimeHelpers.GetUninitializedObject(typeof(Slot));
        Assert.False(FilterReplacement.TryReplace(source, target, buffer));
        Assert.Equal(1, grid.PlacementChecks);
    }

    [Fact]
    public void ProbesUseCurrentAdmissionAndShapes()
    {
        var (source, target) = Pair();
        Assert.True(FilterReplacement.CanReplace(source, target));
        source.parentInventory.isItemStateValidFunc = (_, _, _, _, _) => false;
        Assert.False(FilterReplacement.CanReplace(source, target));
        source.parentInventory.isItemStateValidFunc = null;
        // Same item identities, but a modified shape that no longer fits.
        target.modifiedShape = new GridShapeBuilder(2, 1).SetDataFill(1);
        Assert.False(FilterReplacement.CanReplace(source, target));
        target.modifiedShape = target.shape.Clone();
        Assert.True(FilterReplacement.CanReplace(source, target));
    }

    [Fact]
    public void DeduplicationPreservesOtherCellValuesUsedByGameCollisionChecks()
    {
        // Four binary footprints, but eight distinct shapes once the cell marked 2 is included.
        GridShape incoming = new GridShapeBuilder().SetData("1200", 2);
        Assert.Equal(8, FilterSwapCandidates.Shapes(incoming).Count);
    }

    [Fact]
    public void RemovedFilterFlipsToFitAnIrregularInventory()
    {
        var (source, target) = Pair();
        source.modifiedShape = new GridShapeBuilder(source.shape).SetPosition(1, 0);
        target.modifiedShape = new GridShapeBuilder().SetData("101011", 2);
        var grid = (GridInventory)Grid(2, source, 3);
        Field(grid, typeof(GameGridInventory), "<inventoryShape>k__BackingField",
            new GridShapeBuilder { outside = 1 }.SetData("101000", 2));
        var buffer = (Slot)RuntimeHelpers.GetUninitializedObject(typeof(Slot));

        Assert.True(FilterReplacement.TryReplace(source, target, buffer));
        Assert.Equal((0, 0, true, 0), (target.modifiedShape.minX, target.modifiedShape.minY,
            target.modifiedShape.flipped, target.modifiedShape.orientation));
        Assert.Equal(1, grid.PlacementChecks); // Bitboards reject the other placements before game checks.
    }

    [Fact]
    public void FailedRotatedSwapRestoresOriginalTransforms()
    {
        var (source, target) = Pair();
        source.modifiedShape = new GridShapeBuilder(source.shape).SetOrientation(2);
        target.modifiedShape = new GridShapeBuilder(2, 1).SetDataFill(1);
        var grid = Grid(1, source, 2);
        var slot = (Slot)target.parentInventory;
        slot.RejectedItem = source;
        var buffer = (Slot)RuntimeHelpers.GetUninitializedObject(typeof(Slot));

        Assert.False(FilterReplacement.TryReplace(source, target, buffer));
        Assert.Same(source, Assert.Single(grid.childItems));
        Assert.Same(grid, source.parentInventory);
        Assert.Equal(2, source.modifiedShape.orientation);
        Assert.Same(target, slot.childItem);
        Assert.Same(slot, target.parentInventory);
        Assert.Equal(0, target.modifiedShape.orientation);
        Assert.Null(buffer.childItem);
    }

    [Fact]
    public void OnlyInstalledFiltersAreTargetsAndOtherInteractionsKeepTheirOriginalBehavior()
    {
        var (source, target) = Pair();
        Assert.False(FilterReplacement.CanReplace(source, source));
        bool result = false;
        var water = new Item("water_pitcher");
        water.modifiedState.DisableTag("WATER_FILTER");
        Assert.True(FilterReplacement.MayTargetPrefix(source, water, ref result));
        Assert.True(FilterReplacement.CanTargetPrefix(source, water, ref result));
        Assert.True(FilterReplacement.TargetPrefix(source, water));
        Grid(1, target);
        Assert.False(FilterReplacement.CanReplace(source, target));
    }

    [Fact]
    public void DropRechecksRestrictionsAfterHover()
    {
        var (source, target) = Pair();
        bool result = false;
        Assert.False(FilterReplacement.MayTargetPrefix(source, target, ref result));
        Assert.True(result);
        var inventory = target.parentInventory;
        inventory.overrideLockInsert = true;
        Assert.False(FilterReplacement.TargetPrefix(source, target));
        Assert.Same(inventory, target.parentInventory);
        Assert.Same(target, ((GameSlotInventory)inventory).childItem);
    }

    private static (Item Source, Item Target) Pair(string id = "water_filter")
    {
        var source = new Item(id);
        var target = new Item("water_filter");
        Grid(1, source);
        var slot = (Slot)RuntimeHelpers.GetUninitializedObject(typeof(Slot));
        Field(slot, typeof(GameSlotInventory), "currentItem", target);
        target.parentInventory = slot;
        slot.mayInventoryAddItemFunc = (item, _) => GeneralHelper.IsItemOwned(item) && item.IsTag("WATER_FILTER");
        return (source, target);
    }

    private static GameGridInventory Grid(int width, Item item, int height = 1)
    {
        var grid = (GridInventory)RuntimeHelpers.GetUninitializedObject(typeof(GridInventory));
        Field(grid, typeof(GameGridInventory), "items", new List<GameItem> { item });
        Field(grid, typeof(GameGridInventory), "<inventoryShape>k__BackingField",
            new GridShapeBuilder(width, height) { outside = 1 }.SetDataFill(0));
        item.parentInventory = grid;
        return grid;
    }

    private static void Field(object obj, Type type, string name, object value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);

    // Keep real shape checks and SlotMarker transfers, replacing inventory operations that require Unity UI.
    private sealed class GridInventory : GameGridInventory
    {
        internal int PlacementChecks;
        private GridInventory() : base(1, 1) { }
        private List<GameItem> Items => (List<GameItem>)typeof(GameGridInventory)
            .GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(this)!;
        public override bool CheckShapePlacement(GameItem item, GridShape shape, List<GameItem> ignoredItems)
        {
            PlacementChecks++;
            return base.CheckShapePlacement(item, shape, ignoredItems);
        }
        public override bool UncheckedAccept(GameItem item)
        {
            if (item.parentInventory != null) return false;
            Items.Add(item);
            item.parentInventory = this;
            return true;
        }
        public override bool Expel(GameItem item)
        {
            if (item.parentInventory != this || !Items.Remove(item)) return false;
            item.parentInventory = null;
            return true;
        }
    }

    private sealed class Slot : GameSlotInventory
    {
        internal GameItem? RejectedItem;
        public override SlotMarker TryFindOneValidInventorySlot(GameItem item) => new()
        {
            item = item, inventory = this, itemGridShape = item.modifiedShape,
            numTransfer = childItem == null ? item.unitCount : 0
        };
        public override bool UncheckedAccept(GameItem item)
        {
            if (item.parentInventory != null || childItem != null || item == RejectedItem) return false;
            Field(this, typeof(GameSlotInventory), "currentItem", item);
            item.parentInventory = this;
            return true;
        }
        public override bool Expel(GameItem item)
        {
            if (item.parentInventory != this || childItem != item) return false;
            Field(this, typeof(GameSlotInventory), "currentItem", null!);
            item.parentInventory = null;
            return true;
        }
    }

    private sealed class Item : GameItem
    {
        internal Item(string id) : base(id)
        {
            state.EnableTag("IS_OWNED_TAG");
            state.EnableTag("WATER_FILTER");
            modifiedState = state.Clone();
            shape = new GridShapeBuilder(1, 1).SetDataFill(1);
            modifiedShape = shape.Clone();
        }
        public override void Validate() { }
        public override void Destroy() { }
    }

    private void WithoutSaving(Action change)
    {
        var property = typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!;
        property.SetValue(_entry, null);
        try { change(); }
        finally { property.SetValue(_entry, _config); }
    }

    public void Dispose()
    {
        FilterReplacement.Unregister();
        _options.ReplaceFilters.Unregister();
        Configs.Remove(_modId);
    }
}
