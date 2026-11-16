using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Tuck.Mod.Tests;

[Collection("Drag amounts")]
public class DragRotationTests : IDisposable
{
    private readonly UISettings _previousSettings = UISettings.current;
    private readonly string _id = "tuck-rotation-test-" + Guid.NewGuid().ToString("N");
    private readonly ModConfig _config;
    private readonly Options _options;
    private readonly ConfigEntry<bool> _entry;
    private static Dictionary<string, ModConfig> Configs =>
        (Dictionary<string, ModConfig>)typeof(ModConfig).GetField("configs", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;

    public DragRotationTests()
    {
        UISettings.current = (UISettings)RuntimeHelpers.GetUninitializedObject(typeof(UISettings));
        UISettings.current.gridSize = 16;
        _config = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: [_id], culture: null)!;
        Configs.Add(_id, _config);
        _options = new Options(new ModManifest { ID = _id, Name = "Tuck" });
        _entry = (ConfigEntry<bool>)_config.Get("rotate_dragged_groups");
        _options.RotateDraggedGroups.Register();
        DragAmounts.Register(_options);
    }

    public void Dispose()
    {
        DragAmounts.Unregister();
        typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!.SetValue(_entry, _config);
        _options.RotateDraggedGroups.Unregister();
        Configs.Remove(_id);
        UISettings.current = _previousSettings;
    }

    [Fact]
    public void RotationToggleDefaultsOnAndDisablesEveryRotationPatchImmediately()
    {
        var row = GameOptionRegistry.Find(ModHelper.GameOptionId(_id, _entry.Key));
        Assert.Equal(GameOptionRegistry.Kind.Toggle, row.kind);
        Assert.True(_entry.Default);
        Assert.False(_entry.RequiresRestart);
        Assert.True(row.getBool());
        // Exercise the live menu callback without saving test settings.
        typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!.SetValue(_entry, null);
        row.setBool(false);
        Assert.False(_options.RotateDraggedGroups.Value);
        CheckDisabled();
        row.setBool(true);
        Assert.True(_options.RotateDraggedGroups.Value);
        DragAmounts.Unregister();
        CheckDisabled();
        // Re-registering the feature uses the current setting.
        DragAmounts.Register(_options);
        row.setBool(false);
        CheckDisabled();

        static void CheckDisabled()
        {
            var keys = new List<KeyCode> { KeyCode.Mouse0 };
            Invoke("RotationKeys", [keys]);
            Assert.Equal([KeyCode.Mouse0], keys);
            Invoke("RotateGroup", [null, new HashSet<KeyCode> { KeyCode.E }]);
            object?[] delta = [null, true, 3, 4];
            Invoke("RotatedDelta", delta);
            Assert.Equal(3, delta[2]);
            Assert.Equal(4, delta[3]);
            GridShape shape = new GridShapeBuilder(2, 1);
            object?[] placement = [null, 3, 4, shape];
            Assert.Equal(true, Invoke("RotatedPlacement", placement));
            Assert.Same(shape, placement[3]);
            object?[] valid = [null, true];
            Invoke("RotationEligibility", valid);
            Assert.Equal(true, valid[1]);
            var slot = new SlotMarker();
            object?[] search = [null, null, slot];
            Invoke("PreferDraggedOrientation", search);
            Assert.Same(slot, search[2]);
            Invoke("BeginRotatedDrop", [null]);
        }

        static object? Invoke(string name, object?[] args) =>
            typeof(DragAmounts).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RotatesEveryOccupiedCellWithoutChangingSource(int turns)
    {
        for (int orientation = 0; orientation < 4; orientation++)
        foreach (bool flipped in new[] { false, true })
        {
            GridShape source = new GridShapeBuilder().SetData([1, 0, 1, 1, 1, 0], 3)
                .SetTransform(5, -2, flipped, orientation);
            var before = Cells(source);
            var expected = before;
            for (int i = 0; i < (turns % 4 + 4) % 4; i++)
                expected = expected.Select(cell => (cell.Y, -cell.X)).ToHashSet();
            var rotated = DragAmounts.RotateShape(source, turns);
            Assert.True(expected.SetEquals(Cells(rotated)));
            Assert.True(before.SetEquals(Cells(source)));
            Assert.Equal((5, -2, flipped, orientation), (source.minX, source.minY, source.flipped, source.orientation));
            Assert.True(before.SetEquals(Cells(DragAmounts.RotateShape(rotated, -turns))));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MixedShapesKeepTheirFormationAndPreviewOffsets(int turns)
    {
        GridShape first = new GridShapeBuilder(3, 1).SetDataFill(1).SetTransform(2, 3, true, 1);
        GridShape second = new GridShapeBuilder(2, 2).SetData([1, 0, 1, 1], 2).SetPosition(5, 4);
        var rotatedFirst = DragAmounts.RotateShape(first, turns);
        var rotatedSecond = DragAmounts.RotateShape(second, turns);
        Assert.False(rotatedFirst.Intersects(rotatedSecond));
        var originalOffset = Center(second) - Center(first);
        var rotatedOffset = DragAmounts.RotateOffset(originalOffset, turns);
        var actualOffset = Center(rotatedSecond) - Center(rotatedFirst);
        Assert.Equal((rotatedOffset.x, rotatedOffset.y), (actualOffset.x, actualOffset.y));

        // A new item joining an already-rotated drag uses this same relative offset.
        var anchorOffset = new Vector2(17, -9);
        var expandedOffset = DragAmounts.RotateOffset(anchorOffset, turns) + rotatedOffset;
        var expectedOffset = DragAmounts.RotateOffset(anchorOffset + originalOffset, turns);
        Assert.Equal((expectedOffset.x, expectedOffset.y), (expandedOffset.x, expandedOffset.y));

        // Anchor the first rotated shape at the hovered cell, then translate every item equally.
        int deltaX = 7 - rotatedFirst.minX, deltaY = 8 - rotatedFirst.minY;
        GridShape placed = new GridShapeBuilder(rotatedFirst).SetPosition(rotatedFirst.minX + deltaX, rotatedFirst.minY + deltaY);
        Assert.Equal((7, 8), (placed.minX, placed.minY));
        Assert.Equal(rotatedFirst.orientation, placed.orientation);
    }

    [Fact]
    public void FormationHooksUseTheSameTransformForPreviewAndCommitAndStopAfterReset()
    {
        var group = (ItemMultiSelectHandler)RuntimeHelpers.GetUninitializedObject(typeof(ItemMultiSelectHandler));
        var first = (GameItemElement)RuntimeHelpers.GetUninitializedObject(typeof(GameItemElement));
        var second = (GameItemElement)RuntimeHelpers.GetUninitializedObject(typeof(GameItemElement));
        first.modifiedShape = new GridShapeBuilder(2, 1).SetDataFill(1).SetPosition(3, 4);
        second.modifiedShape = new GridShapeBuilder(1, 1).SetDataFill(1).SetPosition(5, 4);
        var firstSource = first.modifiedShape;
        var secondSource = second.modifiedShape;
        typeof(ItemMultiSelectHandler).GetField("selectedItems", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(group, new List<GameItemElement> { first, second });
        var sessionType = typeof(DragAmounts).GetNestedType("Session", BindingFlags.NonPublic)!;
        var session = RuntimeHelpers.GetUninitializedObject(sessionType);
        sessionType.GetField("Group", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, group);
        sessionType.GetField("QuarterTurns", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, 1);
        var sessionField = typeof(DragAmounts).GetField("_session", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = sessionField.GetValue(null);
        var deltaHook = typeof(DragAmounts).GetMethod("RotatedDelta", BindingFlags.Static | BindingFlags.NonPublic)!;
        var placementHook = typeof(DragAmounts).GetMethod("RotatedPlacement", BindingFlags.Static | BindingFlags.NonPublic)!;
        try
        {
            sessionField.SetValue(null, session);
            // Native code computes a delta from the original first item's position.
            object[] deltaArgs = [group, true, 7 - firstSource.minX, 8 - firstSource.minY];
            deltaHook.Invoke(null, deltaArgs);
            object?[] firstArgs = [first, deltaArgs[2], deltaArgs[3], null];
            object?[] secondArgs = [second, deltaArgs[2], deltaArgs[3], null];
            Assert.Equal(false, placementHook.Invoke(null, firstArgs));
            Assert.Equal(false, placementHook.Invoke(null, secondArgs));
            var firstPlacement = Assert.IsAssignableFrom<GridShape>(firstArgs[3]);
            var secondPlacement = Assert.IsAssignableFrom<GridShape>(secondArgs[3]);
            Assert.Equal((7, 8, 1), (firstPlacement.minX, firstPlacement.minY, firstPlacement.orientation));
            Assert.Equal((7, 7, 1), (secondPlacement.minX, secondPlacement.minY, secondPlacement.orientation));
            Assert.Same(firstSource, first.modifiedShape);
            Assert.Same(secondSource, second.modifiedShape);

            // Native commit assigns each returned shape only after validating the whole formation.
            first.modifiedShape = firstPlacement;
            object?[] commitArgs = [second, deltaArgs[2], deltaArgs[3], null];
            Assert.Equal(false, placementHook.Invoke(null, commitArgs));
            Assert.True(Cells(secondPlacement).SetEquals(Cells(Assert.IsAssignableFrom<GridShape>(commitArgs[3]))));
            typeof(DragAmounts).GetMethod("GroupReset", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [group]);
            Assert.Null(sessionField.GetValue(null));
            Assert.Equal(true, placementHook.Invoke(null, secondArgs));
        }
        finally { sessionField.SetValue(null, previous); }
    }

    [Fact]
    public void LoosePlacementFindsPreviewOrientationWithoutMutatingSourceOrPreview()
    {
        var item = new Item("arbitrary_item", 3, 1);
        var home = Grid(3, 1, item);
        var source = item.modifiedShape;
        var preview = DragAmounts.RotateShape(source, 1);
        var target = Grid(1, 3);
        Assert.Null(DragAmounts.FindRotatedSlot(target, item, source));
        var slot = Assert.IsType<SlotMarker>(DragAmounts.FindRotatedSlot(target, item, preview));
        Assert.Equal((0, 0, 1, false), (slot.itemGridShape.minX, slot.itemGridShape.minY,
            slot.itemGridShape.orientation, slot.itemGridShape.flipped));
        Assert.Equal(1, slot.numTransfer);
        Assert.Same(home, item.parentInventory);
        Assert.Same(source, item.modifiedShape);
        Assert.Equal((0, 0, 0), (source.minX, source.minY, source.orientation));
        Assert.Equal((0, -2, 1), (preview.minX, preview.minY, preview.orientation));
        Assert.Empty(target.childItems);
    }

    [Fact]
    public void LoosePlacementHonorsObstaclesAdmissionAndItemState()
    {
        var item = new Item("bar", 2, 1);
        Grid(2, 1, item);
        var obstacle = new Item("obstacle", 1, 1);
        var target = Grid(2, 2, obstacle);
        var preview = DragAmounts.RotateShape(item.modifiedShape, 1);
        var slot = Assert.IsType<SlotMarker>(DragAmounts.FindRotatedSlot(target, item, preview));
        Assert.Equal((1, 0), (slot.itemGridShape.minX, slot.itemGridShape.minY));
        target.mayInventoryAddItemFunc = (_, _) => false;
        Assert.Null(DragAmounts.FindRotatedSlot(target, item, preview));
        target.mayInventoryAddItemFunc = null;
        item.isItemStateValidFunc = (_, _, _, _, _) => false;
        Assert.Null(DragAmounts.FindRotatedSlot(target, item, preview));
        item.isItemStateValidFunc = null;
        item.mayInventoryRemoveItemFunc = (_, _) => false;
        Assert.Null(DragAmounts.FindRotatedSlot(target, item, preview));
    }

    private static Vector2 Center(GridShape shape) =>
        new(shape.minX + shape.globalWidth / 2f, -shape.minY - shape.globalHeight / 2f);

    private static HashSet<(int X, int Y)> Cells(GridShape shape)
    {
        var cells = new HashSet<(int, int)>();
        for (int y = 0; y < shape.height; y++)
            for (int x = 0; x < shape.width; x++)
                if (shape.GetLocal(x, y) == 1) cells.Add(shape.ForwardTransform(x, y));
        return cells;
    }

    private sealed class Item : GameItem
    {
        internal Item(string id, int width, int height) : base(id)
        {
            state.EnableTag("IS_OWNED_TAG");
            modifiedState = state.Clone();
            shape = new GridShapeBuilder(width, height).SetDataFill(1);
            modifiedShape = shape.Clone();
        }
        public override void Validate() { }
        public override void Destroy() { }
    }

    private static GameGridInventory Grid(int width, int height, params GameItem[] items)
    {
        var grid = (GameGridInventory)RuntimeHelpers.GetUninitializedObject(typeof(GameGridInventory));
        typeof(GameGridInventory).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(grid, items.ToList());
        typeof(GameGridInventory).GetField("<inventoryShape>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(grid, new GridShapeBuilder(width, height).SetDataOutside(1));
        foreach (var item in items) item.parentInventory = grid;
        return grid;
    }
}
