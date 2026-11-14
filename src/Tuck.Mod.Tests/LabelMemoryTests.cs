using System.Reflection;
using HarmonyLib;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class LabelMemoryTests : IDisposable
{
    private readonly string _id = "tuck-label-test-" + Guid.NewGuid().ToString("N");
    // The game's condition catalog initializes Unity localization; supply conditions at that boundary.
    private static readonly Dictionary<string, List<ItemCondition>> Conditions =
        new[] { "CATEGORY_RUSTED", "CATEGORY_WATER_PURITY", "CATEGORY_DENTED" }
            .ToDictionary(category => category, category => Enumerable.Range(0, 6)
                .Select(index => new ItemCondition { category = category, identifier = category + index, display = "Label " + index }).ToList());
    private static Dictionary<string, ModConfig> Configs =>
        (Dictionary<string, ModConfig>)AccessTools.Field(typeof(ModConfig), "configs").GetValue(null)!;

    public LabelMemoryTests()
    {
        var config = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: [_id], culture: null)!;
        Configs.Add(_id, config);
        LabelMemory.Register(new Options(new ModManifest { ID = _id, Name = "Tuck" }));
    }

    [Fact]
    public void RemembersLatestEditIndependentlyPerCategoryAndLeavesUnrememberedFeaturesAlone()
    {
        var rust = Feature("CATEGORY_RUSTED", 1);
        var water = Feature("CATEGORY_WATER_PURITY", 4);
        LabelMemory.RememberLabel(rust);
        LabelMemory.RememberLabel(water);
        var targetRust = Feature(rust.category, 0);
        var targetWater = Feature(water.category, 0);
        var untouched = Feature("CATEGORY_DENTED", 0);
        var original = untouched.fakeCondition;
        targetRust.SetExposed(true);
        targetWater.SetExposed(true);
        Assert.True(Apply(targetRust));
        Assert.True(Apply(targetWater));
        Assert.False(Apply(untouched));
        Assert.Same(rust.fakeCondition, targetRust.fakeCondition);
        Assert.Same(water.fakeCondition, targetWater.fakeCondition);
        Assert.Same(original, untouched.fakeCondition);
        Assert.False(targetRust.IsExposed());
        Assert.False(targetWater.IsExposed());
        Assert.Equal(9000, targetWater.customIntValue1);

        LabelMemory.RememberLabel(Feature(rust.category, 0));
        Assert.True(Apply(targetRust));
        Assert.True(Apply(targetWater));
        Assert.Same(Conditions[rust.category][0], targetRust.fakeCondition);
        Assert.Same(water.fakeCondition, targetWater.fakeCondition);
    }

    [Fact]
    public void BlueNamesAreRememberedPerDefinitionIdAndPrefillOnlyUnnamedItems()
    {
        var blue = Item("labeler_blue");
        var bottle = Item("bottle");
        var box = Item("box");
        Rename(bottle, "Clean water");
        Rename(box, "Spare parts");
        var otherBottle = Item("bottle");
        Assert.Equal("Clean water", LabelMemory.NameToPrefill(otherBottle));
        Assert.False(GeneralHelper.HasCustomName(otherBottle));
        Assert.Equal("Spare parts", LabelMemory.NameToPrefill(Item("box")));
        Assert.Null(LabelMemory.NameToPrefill(Item("different_bottle")));
        Assert.Null(LabelMemory.NameToPrefill(bottle));
        Assert.True(LabelMemory.HasMemory(blue, bottle));
        // Tooltips contain only the hovered item's applicable name, never the whole dictionary.
        Assert.Empty(LabelMemory.RememberedText(blue));
        Assert.Equal("Clean water", Assert.Single(LabelMemory.RememberedText(blue, otherBottle)));
        Assert.Empty(LabelMemory.RememberedText(blue, Item("different_bottle")));
        Rename(otherBottle, "Drinking water");
        Assert.Equal("Drinking water", LabelMemory.NameToPrefill(Item("bottle")));
        Assert.Equal("Spare parts", LabelMemory.NameToPrefill(Item("box")));
        Assert.Equal("Clean water", GeneralHelper.GetCustomName(bottle));
    }

    private static void Rename(GameItem item, string name)
    {
        GeneralHelper.SetCustomName(item, name);
        LabelMemory.RememberName(item);
    }

    private static ItemFeature Feature(string category, int index) => new()
    {
        category = category,
        fakeCondition = Conditions[category][index],
        realCondition = Conditions[category][0]
    };

    private static bool Apply(ItemFeature feature) => LabelMemory.ApplyLabel(feature, Conditions[feature.category]);

    private static GameItem Item(string id)
    {
        var item = new FixtureItem(id);
        item.state.EnableTag("IS_OWNED_TAG");
        item.modifiedState = item.state.Clone();
        item.shape = new GridShapeBuilder(1, 1).SetDataFill(1);
        item.modifiedShape = item.shape.Clone();
        return item;
    }

    private sealed class FixtureItem(string id) : GameItem(id)
    {
        public override void Validate() { }
        public override void Destroy() => throw new InvalidOperationException("Labeling must not destroy items.");
    }

    public void Dispose()
    {
        LabelMemory.Unregister();
        Configs.Remove(_id);
    }
}
