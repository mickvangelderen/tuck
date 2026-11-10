using System.Reflection;
using System.Runtime.CompilerServices;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class WorkspaceWarningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VanillaSettingRunsOriginalWithoutAccessingTheSceneOrChangingTheResult(bool previousResult)
    {
        string modId = "tuck-workspace-test-" + Guid.NewGuid().ToString("N");
        var configs = (Dictionary<string, ModConfig>)typeof(ModConfig)
            .GetField("configs", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var config = (ModConfig)Activator.CreateInstance(typeof(ModConfig),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [modId], null)!;
        configs.Add(modId, config);

        try
        {
            var options = new Options(new ModManifest { ID = modId, Name = "Tuck" });
            var entry = (ConfigEntry<float>)config.Get("workspace_clear_percent");
            // Use the live setting without writing a config file through Unity.
            typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!.SetValue(entry, null);
            WorkspaceWarning.Register(options);
            entry.Set(0f);

            // There is no game scene. A pass-through must leave any earlier patch's result alone.
            bool result = previousResult;
            Assert.True(WorkspaceWarning.Prefix(ref result));
            Assert.Equal(previousResult, result);
        }
        finally
        {
            WorkspaceWarning.Unregister();
            configs.Remove(modId);
        }
    }

    private sealed class FixtureItem : GameItem
    {
        public FixtureItem(GridShape footprint) : base("workspace-item") => modifiedShape = footprint;
        public override void Validate() { }
        public override void Destroy() { }
    }

    private static GameGridInventory Workspace(GridShape shape, params GridShape[] footprints)
    {
        // Bypass Unity rendering constructors, as in the other game-boundary fixtures.
        var grid = (GameGridInventory)RuntimeHelpers.GetUninitializedObject(typeof(GameGridInventory));
        var items = footprints.Select(footprint => (GameItem)new FixtureItem(footprint)).ToList();
        typeof(GameGridInventory).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(grid, items);
        typeof(GameGridInventory).GetField("<inventoryShape>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(grid, shape);
        return grid;
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(4, false)] // Exactly 40% occupied does not exceed the threshold.
    [InlineData(5, true)]
    public void WarnsOnlyAboveOccupiedPercentage(int occupied, bool expected)
    {
        var workspace = Workspace(new GridShapeBuilder(10, 1).SetDataFill(0),
            new GridShapeBuilder(occupied, 1).SetDataFill(1));
        Assert.Equal(expected, WorkspaceWarning.RequiresWarning(workspace, 40f));
    }

    [Theory]
    [InlineData(28, false)] // 28/72 is below 40% occupied.
    [InlineData(29, true)] // 29/72 is just above 40% occupied.
    public void DoesNotRoundOccupiedPercentageBeforeComparing(int occupied, bool expected)
    {
        var footprints = Enumerable.Range(0, occupied)
            .Select(i => (GridShape)new GridShapeBuilder(1, 1).SetDataFill(1).SetPosition(i % 18, i / 18))
            .ToArray();
        var workspace = Workspace(new GridShapeBuilder(18, 4).SetDataFill(0), footprints);
        Assert.Equal(expected, WorkspaceWarning.RequiresWarning(workspace, 40f));
    }

    [Fact]
    public void ZeroUsesVanillaBehaviorAndOneHundredDisablesWarning()
    {
        var empty = Workspace(new GridShapeBuilder(10, 1).SetDataFill(0));
        var full = Workspace(new GridShapeBuilder(10, 1).SetDataFill(0),
            new GridShapeBuilder(10, 1).SetDataFill(1));
        Assert.True(WorkspaceWarning.RequiresWarning(full, 0f));
        Assert.False(WorkspaceWarning.RequiresWarning(empty, 0f));
        Assert.False(WorkspaceWarning.RequiresWarning(full, 100f));
        var noFootprint = Workspace(new GridShapeBuilder(10, 1).SetDataFill(0),
            new GridShapeBuilder(1, 1).SetDataFill(0));
        Assert.True(WorkspaceWarning.RequiresWarning(noFootprint, 0f));
    }

    [Fact]
    public void CountsPlayableCellsAndTransformedFootprintsRatherThanBoundingBoxes()
    {
        // One blocked cell leaves five playable cells. The rotated L occupies three of those: 60% occupied.
        var workspace = Workspace(new GridShapeBuilder().SetData("000100", 3),
            new GridShapeBuilder().SetData("1110", 2).SetOrientation(1).SetPosition(1, 0));
        Assert.False(WorkspaceWarning.RequiresWarning(workspace, 60f));
        Assert.True(WorkspaceWarning.RequiresWarning(workspace, 59f));
    }

    [Fact]
    public void OverlappingFootprintsOnlyOccupyEachCellOnce()
    {
        var workspace = Workspace(new GridShapeBuilder(10, 1).SetDataFill(0),
            new GridShapeBuilder(4, 1).SetDataFill(1),
            new GridShapeBuilder(2, 1).SetDataFill(1));
        Assert.False(WorkspaceWarning.RequiresWarning(workspace, 40f));
    }
}
