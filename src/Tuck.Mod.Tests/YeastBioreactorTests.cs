using System.Reflection;
using System.Runtime.CompilerServices;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class YeastBioreactorTests : IDisposable
{
    private readonly string _id = "tuck-yeast-test-" + Guid.NewGuid().ToString("N");
    private readonly UISettings _previousSettings = UISettings.current;
    private readonly GameMaster _previousMaster = GameMaster.current;
    private static Dictionary<string, ModConfig> Configs =>
        (Dictionary<string, ModConfig>)typeof(ModConfig)
            .GetField("configs", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    public YeastBioreactorTests()
    {
        UISettings.current = Raw<UISettings>();
        UISettings.current.gridSize = 16;
        typeof(GameMaster).GetProperty(nameof(GameMaster.current))!.SetValue(null, Raw<GameMaster>());
        var config = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [_id], null)!;
        Configs.Add(_id, config);
        YeastBioreactor.Register(new Options(new ModManifest { ID = _id, Name = "Tuck" }));
    }

    [Fact]
    public void StartsFermentationWithYeastBonusWithoutMovingTheUsedReactor()
    {
        var usedReactor = new Item("wine_yeast_infinite", 2, 2);
        usedReactor.EnableTag(WineHelper.WINE_YEAST_TAG);
        usedReactor.Set(WineHelper.WINE_YEAST_POWER_INT, 2);
        var originalSlot = Raw<GameSlotInventory>();
        typeof(GameSlotInventory).GetField("currentItem", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(originalSlot, usedReactor);
        usedReactor.parentInventory = originalSlot;
        YeastBioreactor.CreatedPostfix(usedReactor);

        var berry = new Item("wine_berry", 2, 2);
        berry.EnableTag(WineHelper.WINE_INGREDIENT_TAG);
        var ingredients = new List<GameItem> { berry };
        var bottle = new Item("wine_bottle");
        bottle.EnableTag(WineHelper.WINE_CONTAINER_TAG);
        bottle.Set(WineHelper.WINE_CONTAINER_STATE, 0);
        bottle.Ingredients = Raw<GameGridInventory>();
        typeof(GameGridInventory).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(bottle.Ingredients, ingredients);
        typeof(GameGridInventory).GetProperty(nameof(GameGridInventory.inventoryShape))!
            .SetValue(bottle.Ingredients, new GridShapeBuilder(6, 2) { outside = 1 }.SetDataFill(0));
        ContainerHelper.AllowOnlyTaggedItemsOr(bottle.Ingredients,
            [WineHelper.WINE_INGREDIENT_TAG, WineHelper.WINE_YEAST_TAG], ownedOnly: true);
        // Supply volume without Unity's liquid catalog; retain native selection, targeting and placement checks.
        bottle.mayItemTargetThisFunc = bottle.canItemTargetThisFunc = (source, target) =>
            YeastBioreactor.CanFerment(source, target, 750);

        int starts = 0;
        Assert.True(YeastBioreactor.TryFerment(usedReactor, bottle, 750, target =>
        {
            Assert.Same(bottle, target);
            Assert.DoesNotContain(usedReactor, ingredients);
            starts++;
            // Stand in for vanilla fermentation, which also accesses Unity sprites and windows.
            ingredients.Clear();
            bottle.Set(WineHelper.WINE_CONTAINER_STATE, 1);
            bottle.Set(WineHelper.WINE_SCORE_INT, 3);
            bottle.Set(WineHelper.WINE_YEAST_POWER_INT, 1);
        }));

        Assert.Equal(1, starts);
        Assert.Equal(1, bottle.GetTagReadonly(WineHelper.WINE_CONTAINER_STATE).GetInt());
        Assert.Equal(4, bottle.GetTagReadonly(WineHelper.WINE_SCORE_INT).GetInt());
        Assert.Equal(2, bottle.GetTagReadonly(WineHelper.WINE_YEAST_POWER_INT).GetInt());
        Assert.Same(originalSlot, usedReactor.parentInventory);
        Assert.Same(usedReactor, originalSlot.childItem);
        Assert.True(usedReactor.MaySelectSlotItem());
        Assert.True(usedReactor.CanSelectSlotItem());
    }

    private static T Raw<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private sealed class Item : GameItem
    {
        internal GameGridInventory? Ingredients;
        public override List<GraphNodeStorage> children => Ingredients == null ? [] : [Ingredients];

        internal Item(string id, int width = 1, int height = 1) : base(id)
        {
            state.EnableTag("IS_OWNED_TAG");
            modifiedState = state.Clone();
            shape = new GridShapeBuilder(width, height).SetDataFill(1);
            modifiedShape = shape.Clone();
        }

        internal void Set(string tag, int value) => ModifyTag(tag, state => state.Enable().SetInt(value));
        public override void Validate() { }
        public override void Destroy() => throw new InvalidOperationException("The used reactor must not be consumed.");
    }

    public void Dispose()
    {
        YeastBioreactor.Unregister();
        Configs.Remove(_id);
        UISettings.current = _previousSettings;
        typeof(GameMaster).GetProperty(nameof(GameMaster.current))!.SetValue(null, _previousMaster);
    }
}
