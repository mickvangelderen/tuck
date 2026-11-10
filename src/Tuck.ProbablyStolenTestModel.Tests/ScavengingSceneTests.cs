namespace Tuck.ProbablyStolenTestModel.Tests;

public class ScavengingSceneTests
{
    private static ModelItem OwnedItem()
    {
        var item = new ModelItem("loot", new GridShapeBuilder(1, 1).SetDataFill(1));
        item.EnableTag("IS_OWNED_TAG");
        return item;
    }

    [Fact]
    public void WindowTraversesAllFourInventoriesInGameOrder()
    {
        var scene = new ModelScavengingScene();
        Assert.Same(scene.Panels, scene.Window.child);
        Assert.Equal(new GameInventory[] { scene.Ground, scene.LeftPocket, scene.Backpack, scene.RightPocket },
            GraphUtils.FindChildrenType<GameInventory>(scene.Window));
        Assert.All(scene.Panels.children, storage => Assert.Same(scene.Panels, Assert.Single(storage.parents)));
        Assert.Same(scene.Window, scene.Panels.parent);
    }

    [Fact]
    public void WindowSearchPrefersGroundPriorityThenLastEqualPrioritySlot()
    {
        var scene = new ModelScavengingScene(1, 1);
        var groundItem = OwnedItem();
        Assert.Equal(0, GraphUtils.TryAcceptAll(scene.Window, groundItem));
        Assert.Same(scene.Ground, groundItem.parentInventory);
        var pocketItem = OwnedItem();
        Assert.Equal(0, GraphUtils.TryAcceptAll(scene.Window, pocketItem));
        Assert.Same(scene.RightPocket, pocketItem.parentInventory);
        var last = OwnedItem();
        Assert.Equal(0, GraphUtils.TryAcceptAll(scene.Window, last));
        Assert.Same(scene.LeftPocket, last.parentInventory);
        Assert.Equal(1, GraphUtils.TryAcceptAll(scene.Window, OwnedItem()));
    }

    [Fact]
    public void NonPrioritySearchUsesReversePanelOrder()
    {
        var scene = new ModelScavengingScene();
        scene.Panels.searchUsingPriority = false;
        var item = OwnedItem();
        Assert.Equal(0, GraphUtils.TryAcceptAll(scene.Window, item));
        Assert.Same(scene.RightPocket, item.parentInventory);
    }

    [Fact]
    public void WindowTraversalIncludesContentsOfCarriedContainer()
    {
        var scene = new ModelScavengingScene();
        var contents = new GameGridInventory(1, 1);
        var bag = new ModelItem("bag", new GridShapeBuilder(1, 1).SetDataFill(1), contents: contents);
        bag.EnableTag("BACKPACK_TAG");
        bag.EnableTag("IS_OWNED_TAG");
        var loot = OwnedItem();
        GraphUtils.TryAcceptAll(scene.Backpack, bag);
        GraphUtils.TryAcceptAll(contents, loot);
        Assert.Contains(bag, GraphUtils.FindAllChildrenType<GameItem>(scene.Window));
        Assert.Contains(loot, GraphUtils.FindAllChildrenType<GameItem>(scene.Window));
        Assert.Same(scene.Window, GraphUtils.FindParentType<PixelWindow>(scene.Backpack));
    }

    [Fact]
    public void SceneAppliesOwnedBackpackAndWasteRules()
    {
        var scene = new ModelScavengingScene(allowsHazardousWaste: false);
        var item = OwnedItem();
        item.EnableTag("HAZARDOUS_WASTE_TAG");
        Assert.False(scene.Ground.MayHaveValidInventorySlot(item));
        scene.AllowsHazardousWaste = true;
        Assert.True(scene.Ground.MayHaveValidInventorySlot(item));
        Assert.False(scene.Backpack.MayHaveValidInventorySlot(item));
        item.EnableTag("BACKPACK_TAG");
        Assert.True(scene.Backpack.MayHaveValidInventorySlot(item));
        item.DisableTag("IS_OWNED_TAG");
        Assert.All(scene.Panels.children, inventory => Assert.False(inventory.MayHaveValidInventorySlot(item)));
    }
}
