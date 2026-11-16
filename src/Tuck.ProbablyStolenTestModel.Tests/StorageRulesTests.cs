using Tuck.ProbablyStolenTestModel;

namespace Tuck.ProbablyStolenTestModel.Tests;

public class StorageRulesTests
{
    [Theory]
    [InlineData("machine_bay", 6, 8)]
    [InlineData("machine_bay_ext", 11, 14)]
    public void BayOverridesOrdinaryContainerMachineRestriction(string id, int width, int height)
    {
        var grid = new GameGridInventory(width, height);
        var bay = Owned(id, grid);
        ModelStorageRules.InitContainerItem(grid, bay);
        var machine = Owned("fridge");
        machine.SetGameItemType("MACHINE");
        machine.EnableTag("CONTAINER_TAG");
        Assert.Equal(1, TransferScenario.Move(machine, grid));
        ModelStorageRules.InitMachineBay(grid, bay);
        Assert.Equal(0, TransferScenario.Move(machine, grid));
        Assert.Same(machine, Assert.Single(grid.childItems));
    }

    [Theory]
    [InlineData(false, true, false, false, false, false, false)]
    [InlineData(true, false, false, false, false, false, false)]
    [InlineData(true, true, true, false, true, false, false)]
    [InlineData(true, true, false, true, false, false, false)]
    [InlineData(true, true, false, true, true, false, true)]
    [InlineData(true, true, false, true, false, true, true)]
    [InlineData(true, true, false, false, false, false, true)]
    public void BayChecksOwnershipBackpacksAndStorageTag(bool ownedItem, bool ownedBay,
        bool backpack, bool storageTag, bool nestable, bool package, bool accepted)
    {
        var grid = new GameGridInventory(6, 8);
        var bay = Owned("machine_bay", grid);
        ModelStorageRules.InitMachineBay(grid, bay);
        if (!ownedBay) bay.DisableTag("IS_OWNED_TAG");
        var item = Owned("incoming");
        item.SetGameItemType("STORAGE");
        if (!ownedItem) item.DisableTag("IS_OWNED_TAG");
        if (!storageTag) item.DisableTag("STORAGE");
        if (backpack) item.EnableTag("BACKPACK_TAG");
        if (nestable) item.EnableTag("NESTABLE_DEVICE_TAG");
        if (package) item.EnableTag("STORABLE_PACKAGE_TAG");
        Assert.Equal(accepted ? 0 : 1, TransferScenario.Move(item, grid));
    }

    private static ModelItem Owned(string id, GameInventory? contents = null)
    {
        var item = new ModelItem(id, new GridShapeBuilder().SetData("1", 1), contents: contents);
        item.EnableTag("IS_OWNED_TAG");
        return item;
    }
}
