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
        var slot = (GameSlotInventory)RuntimeHelpers.GetUninitializedObject(typeof(GameSlotInventory));
        Field(slot, typeof(GameSlotInventory), "currentItem", target);
        target.parentInventory = slot;
        slot.mayInventoryAddItemFunc = (item, _) => GeneralHelper.IsItemOwned(item) && item.IsTag("WATER_FILTER");
        return (source, target);
    }

    private static GameGridInventory Grid(int width, Item item)
    {
        var grid = (GameGridInventory)RuntimeHelpers.GetUninitializedObject(typeof(GameGridInventory));
        Field(grid, typeof(GameGridInventory), "items", new List<GameItem> { item });
        Field(grid, typeof(GameGridInventory), "<inventoryShape>k__BackingField",
            new GridShapeBuilder(width, 1) { outside = 1 }.SetDataFill(0));
        item.parentInventory = grid;
        return grid;
    }

    private static void Field(object obj, Type type, string name, object value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);

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
