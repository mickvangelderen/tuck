using System.Reflection;
using System.Runtime.CompilerServices;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class BackpackReturnTests : IDisposable
{
    private readonly string _id = "tuck-backpack-test-" + Guid.NewGuid().ToString("N");
    private readonly UISettings _previousSettings = UISettings.current;
    private readonly EmporiumEntry _previousEntry = EmporiumEntry.Instance;
    private readonly EmporiumEntry _entry = Raw<EmporiumEntry>();
    private readonly ModConfig _config;
    private static Dictionary<string, ModConfig> Configs =>
        (Dictionary<string, ModConfig>)typeof(ModConfig).GetField("configs", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;

    public BackpackReturnTests()
    {
        UISettings.current = Raw<UISettings>();
        UISettings.current.gridSize = 16;
        typeof(EmporiumEntry).GetProperty(nameof(EmporiumEntry.Instance))!.SetValue(null, _entry);
        _entry.invElement = Grid();
        Field(_entry, typeof(EmporiumEntry), "<backInvinvElement>k__BackingField", Grid());
        Field(_entry, typeof(EmporiumEntry), "<backInvinvElementCounter>k__BackingField", Grid());
        Field(_entry, typeof(EmporiumEntry), "<showcaseElement>k__BackingField", Grid());
        _entry.afterhourInventory = Grid();
        _entry.afterhourPocketSlotInvBackpack = Raw<Slot>();
        _entry.afterhourPocketSlotInvLeft = Raw<Slot>();
        _entry.afterhourPocketSlotInvRight = Raw<Slot>();
        _config = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [_id], null)!;
        Configs.Add(_id, _config);
        BackpackReturn.Register(new Options(new ModManifest { ID = _id, Name = "Tuck" }));
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
    public void RestoresSelectedInstancePositionAndOrientationBeforeHandLoot(int rotation, bool flipped)
    {
        var bag = Bag(3, 2, rotation, flipped);
        var identical = Bag();
        Put(bag, _entry.invElement);
        Put(identical, _entry.invElement);
        LeaveWith(bag);
        Put(new Item("loot"), _entry.afterhourPocketSlotInvLeft);

        Return();

        Assert.Same(_entry.invElement, bag.parentInventory);
        AssertPlacement(bag, 3, 2, rotation, flipped);
        Assert.Same(_entry.invElement, identical.parentInventory);
        AssertPlacement(identical, 0, 0, 0, false);
        Assert.Equal("loot", Assert.Single(_entry.backInvinvElement.childItems).identifier);
        Assert.Null(_entry.afterhourPocketSlotInvBackpack.childItem);
    }

    [Fact]
    public void BlockedSpotFallsBackWithoutMovingTheOccupant()
    {
        var bag = Bag(3, 2, 1, true);
        Put(bag, _entry.invElement);
        LeaveWith(bag);
        var occupant = new Item("loot") { modifiedShape = new GridShapeBuilder(1, 1).SetDataFill(1).SetPosition(3, 2) };
        Put(occupant, _entry.invElement);

        Return();

        Assert.Same(_entry.backInvinvElement, bag.parentInventory);
        Assert.Same(occupant, Assert.Single(_entry.invElement.childItems));
        AssertPlacement(occupant, 3, 2, 0, false);
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("remove")]
    [InlineData("rule")]
    public void RefusedRestorationLeavesTheBagInItsCarriedSlot(string restriction)
    {
        var bag = Bag();
        Put(bag, _entry.invElement);
        LeaveWith(bag);
        if (restriction == "insert") _entry.invElement.overrideLockInsert = true;
        if (restriction == "remove") _entry.afterhourPocketSlotInvBackpack.overrideLockRemove = true;
        if (restriction == "rule") _entry.invElement.mayInventoryAddItemFunc = (_, _) => false;

        BackpackReturn.BeforeReturn(_entry);

        Assert.Same(bag, _entry.afterhourPocketSlotInvBackpack.childItem);
        Assert.Empty(_entry.invElement.childItems);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DoesNotSubstituteAnIdenticalBagOrRetrieveOneLeftBehind(bool bringOriginalInHand)
    {
        var original = Bag(3, 2, 1, true);
        var replacement = Bag();
        Put(original, _entry.invElement);
        LeaveWith(original);
        Put(original, bringOriginalInHand ? _entry.afterhourPocketSlotInvLeft : _entry.afterhourInventory);
        Put(replacement, _entry.afterhourPocketSlotInvBackpack);

        Return();

        Assert.Same(_entry.backInvinvElement, replacement.parentInventory);
        if (bringOriginalInHand)
        {
            Assert.Same(_entry.invElement, original.parentInventory);
            AssertPlacement(original, 3, 2, 1, true);
        }
        else
        {
            Assert.Null(original.parentInventory); // Vanilla discards the ground contents.
            Assert.Empty(_entry.invElement.childItems);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoresIntoOriginalContainerOnlyWhileItRemainsInTheShop(bool containerRemoved)
    {
        var bag = Bag(3, 2, 1, true);
        var container = new Item("container");
        var contents = Grid();
        contents.AddParent(container);
        Put(container, _entry.invElement);
        Put(bag, contents);
        LeaveWith(bag);
        if (containerRemoved) _entry.invElement.Expel(container);

        Return();

        Assert.Same(containerRemoved ? _entry.backInvinvElement : contents, bag.parentInventory);
        if (!containerRemoved) AssertPlacement(bag, 3, 2, 1, true);
    }

    [Fact]
    public void JacksonRestoresBagAfterShopContentsAndBeforePurchases()
    {
        var bag = Bag(0, 0, 1, true);
        Put(bag, _entry.backInvinvElement);
        var existing = new Item("existing") { modifiedShape = new GridShapeBuilder(1, 1).SetDataFill(1).SetPosition(5, 4) };
        Put(existing, _entry.backInvinvElement);
        LeaveWith(bag);
        var map = Raw<MapUIManager>();
        map.TransferBackInvToCounter();

        map.TransferCounterToBackInv();
        BackpackReturn.AfterCounterRestored();
        var purchase = new Item("purchase");
        GraphUtils.TryAcceptAll(_entry.backInvinvElement, purchase);
        Return();

        Assert.Same(_entry.backInvinvElement, bag.parentInventory);
        AssertPlacement(bag, 0, 0, 1, true);
        AssertPlacement(existing, 5, 4, 0, false);
        Assert.Same(_entry.backInvinvElement, purchase.parentInventory);
        Assert.False(bag.modifiedShape.Intersects(purchase.modifiedShape));
        Assert.Equal(3, _entry.backInvinvElement.childItems.Count);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("unregistered")]
    [InlineData("loaded")]
    [InlineData("no bag")]
    public void ClearsMemoryWhenTheTripIsNoLongerApplicable(string reason)
    {
        var bag = Bag(3, 2, 1, true);
        Put(bag, _entry.invElement);
        LeaveWith(bag);
        switch (reason)
        {
            case "disabled": SetEnabled(false); SetEnabled(true); break;
            case "unregistered": BackpackReturn.Unregister(); break;
            case "loaded":
                typeof(ModHook).GetMethod("FireOnGameLoadedInit", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
                break;
            case "no bag": BackpackReturn.Remember(_entry); break;
        }

        Return();

        Assert.Same(_entry.backInvinvElement, bag.parentInventory);
    }

    private void LeaveWith(GameItem bag)
    {
        _entry.selectedBackpack = bag;
        BackpackReturn.Remember(_entry);
        _entry.TransferBackpackToInv();
        Assert.Same(bag, _entry.afterhourPocketSlotInvBackpack.childItem);
    }

    private void Return()
    {
        BackpackReturn.BeforeReturn(_entry);
        _entry.TransferAfterhourToInv();
    }

    private void SetEnabled(bool enabled)
    {
        var setting = (ConfigEntry<bool>)_config.Get("return_backpack");
        var property = typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!;
        property.SetValue(setting, null);
        try { setting.Set(enabled); }
        finally { property.SetValue(setting, _config); }
    }

    private static Item Bag(int x = 0, int y = 0, int rotation = 0, bool flipped = false) =>
        new("large_backpack") { modifiedShape = new GridShapeBuilder(2, 1).SetDataFill(1).SetTransform(x, y, flipped, rotation) };

    private static void AssertPlacement(GameItem item, int x, int y, int rotation, bool flipped) =>
        Assert.Equal((x, y, rotation, flipped),
            (item.modifiedShape.minX, item.modifiedShape.minY, item.modifiedShape.orientation, item.modifiedShape.flipped));

    private static void Put(GameItem item, GameInventory inventory)
    {
        item.parentInventory?.Expel(item);
        Assert.True(inventory.UncheckedAccept(item));
    }

    private static T Raw<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Field(object obj, Type type, string name, object? value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);

    private static GridInventory Grid()
    {
        var grid = Raw<GridInventory>();
        Field(grid, typeof(GameGridInventory), "items", new List<GameItem>());
        Field(grid, typeof(GameGridInventory), "<inventoryShape>k__BackingField",
            new GridShapeBuilder(8, 6) { outside = 1 }.SetDataFill(0));
        return grid;
    }

    // Keep the game's placement validation and SlotMarker transfers; omit rendering and UI notifications.
    private sealed class GridInventory : GameGridInventory
    {
        private GridInventory() : base(1, 1) { }
        private List<GameItem> Items => (List<GameItem>)typeof(GameGridInventory)
            .GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(this)!;
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
        public override bool UncheckedAccept(GameItem item)
        {
            if (item.parentInventory != null || childItem != null) return false;
            Field(this, typeof(GameSlotInventory), "currentItem", item);
            item.parentInventory = this;
            return true;
        }
        public override bool Expel(GameItem item)
        {
            if (item.parentInventory != this || childItem != item) return false;
            Field(this, typeof(GameSlotInventory), "currentItem", null);
            item.parentInventory = null;
            return true;
        }
    }

    private sealed class Item : GameItem
    {
        internal Item(string id) : base(id)
        {
            state.EnableTag("IS_OWNED_TAG");
            modifiedState = state.Clone();
            shape = new GridShapeBuilder(1, 1).SetDataFill(1);
            modifiedShape = shape.Clone();
        }
        public override void Validate() { }
        public override void Destroy() => throw new InvalidOperationException("Returning must not destroy items.");
    }

    public void Dispose()
    {
        BackpackReturn.Unregister();
        Configs.Remove(_id);
        UISettings.current = _previousSettings;
        typeof(EmporiumEntry).GetProperty(nameof(EmporiumEntry.Instance))!.SetValue(null, _previousEntry);
    }
}
