using Tuck.Mod.Packing;
using Tuck.Mod.DebugRecording;
using Tuck.ProbablyStolenTestModel;

namespace Tuck.PackSolver.Tests;

[Trait("Category", "SolverContract")]
public class MachineBayIntegrationTests
{
    [Theory]
    [InlineData("machine_bay", 6, 8)]
    [InlineData("machine_bay_ext", 11, 14)]
    public void CapturesAndRoundTripsBayAdmissionAndStorageTag(string identifier, int width, int height)
    {
        var scene = new ModelScavengingScene();
        var grid = new GameGridInventory(width, height);
        var bay = new ModelItem(identifier, new GridShapeBuilder().SetData("1", 1), contents: grid);
        bay.EnableTag("IS_OWNED_TAG");
        ModelStorageRules.InitMachineBay(grid, bay);
        Assert.True(scene.LeftPocket.UncheckedAccept(bay));
        var captured = PackCapture.Capture(scene.Ground, scene.Backpack, scene.LeftPocket, scene.RightPocket, () => true);
        var state = PackJson.Deserialize(PackJson.Serialize(captured.State, new())).State;
        Assert.Null(StateComparison.Difference(captured.State, state));
        Assert.Equal(AdmissionKind.MachineBay, Assert.Single(state.Inventories, i => i.OwnerItemId != null).Admission.Kind);
        Assert.True(Assert.Single(state.Items).State.IsTag("STORAGE"));
    }

    [Theory]
    [SolverData]
    public void PacksRecordedMachinesUsingTheBayAndExecutesInGameModel(string solverName)
    {
        // Failed recording 1caed7dd84e541d6aff84fed64c84d45 (04:51:36Z) and manually packed
        // 55b5de684e1842cfbd2cf5827453d679 (04:52:58Z), 2026-11-16. Snapshot IDs renumber;
        // these six identifiers are unique, with unchanged quantities, values and masks.
        // Omit the unchanged backpack subtree and empty equipment inventories.
        var witness = new RecordedMachines();
        witness.PackManually();
        witness.AssertAllReturned();

        var fixture = new RecordedMachines();
        var captured = PackCapture.Capture(fixture.Scene.Ground, fixture.Scene.Backpack,
            fixture.Scene.LeftPocket, fixture.Scene.RightPocket, () => false);
        var result = SolverUnderTest.Solve(solverName, new(captured.State));
        Assert.Equal(1100, result.FinalValue);
        var executed = PackExecutor.Execute(captured, result.Plan);
        Assert.True(executed.Status == ExecutionStatus.Completed, executed.Detail);
        fixture.AssertAllReturned();
    }

    private sealed class RecordedMachines
    {
        internal ModelScavengingScene Scene { get; } = new(allowsHazardousWaste: false);
        private readonly GameGridInventory bay = new(6, 8);
        private readonly Dictionary<string, ModelItem> items = new();

        internal RecordedMachines()
        {
            var owner = Item("machine_bay", 300, 4, 4, bay);
            ModelStorageRules.InitMachineBay(bay, owner);
            Place(owner, Scene.LeftPocket);
            Place(Item("fridge", 100, 3, 3), Scene.Ground, 2, 0);
            Place(Item("furnace", 200, 3, 3), Scene.Ground, 4, 3);
            Place(Item("security_alarm", 150, 3, 3), Scene.Ground, 4, 6);
            Place(Item("recharger", 150, 4, 2), Scene.Ground, 0, 7);
            Place(Item("powerblock", 200, 2, 2), Scene.RightPocket);
        }

        internal void PackManually()
        {
            Place(items["security_alarm"], bay, 0, 0);
            Place(items["furnace"], bay, 3, 0);
            Place(items["fridge"], bay, 0, 3);
            Place(items["powerblock"], bay, 3, 3);
            Place(items["recharger"], Scene.RightPocket);
        }

        internal void AssertAllReturned()
        {
            Assert.Empty(Scene.Ground.childItems);
            Assert.Equal(1100, items.Values.Sum(i => i.unitValue));
            Assert.All(items.Values, item =>
            {
                Assert.Equal(1, item.unitCount);
                var root = item;
                while (root.parentInventory.GetParentItem() is ModelItem owner) root = owner;
                Assert.Contains(root.parentInventory, new GameInventory[] { Scene.LeftPocket, Scene.RightPocket, Scene.Backpack });
            });
        }

        private ModelItem Item(string id, long value, int width, int height, GameInventory? contents = null)
        {
            var item = new ModelItem(id, new GridShapeBuilder().SetData(new string('1', width * height), width), contents: contents)
            { unitValue = value };
            item.EnableTag("IS_OWNED_TAG");
            if (id != "machine_bay") item.SetGameItemType("MACHINE");
            if (id != "powerblock") item.EnableTag("CONTAINER_TAG");
            items.Add(id, item);
            return item;
        }

        private static void Place(GameItem item, GameInventory inventory, int x = 0, int y = 0)
        {
            var slot = inventory.TryInventorySlot(item, item.unitCount,
                new GridShapeBuilder(item.modifiedShape).SetTransform(x, y, false, 0));
            Assert.NotNull(slot);
            Assert.Equal(1, slot.TryAcceptOnce());
        }
    }
}
