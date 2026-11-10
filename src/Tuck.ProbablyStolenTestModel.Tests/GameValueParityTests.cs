#if HAS_GAME_GEOMETRY
extern alias original;
#endif
using System.Reflection;

namespace Tuck.ProbablyStolenTestModel.Tests;

public class GameValueParityTests
{
#if HAS_GAME_GEOMETRY
    [Fact]
    public void LeafValuesMatchOriginalAssemblyAcrossQuantitiesAndValueTags()
    {
        var random = new Random(121);
        for (int sample = 0; sample < 100; sample++)
        {
            var game = new OriginalItem();
            var model = new ModelItem("item", new GridShapeBuilder(1, 1).SetDataFill(1), stackCapacity: 8);
            game.unitValue = model.unitValue = random.Next(-10, 500);
            game.unitBaseValue = model.unitBaseValue = random.Next(0, 100);
            game.lateUnitValue = model.lateUnitValue = random.Next(-10, 100);
            game.unitCount = model.unitCount = random.Next(1, 9);
            foreach (var tag in new[] { "power_source_item", "amenities", "armor_item",
                "LIQUID_CONTAINER_TAG", "ORGAN_ITEM_TAG", "not_purchased" })
            {
                bool enabled = random.Next(2) == 1;
                game.state.GetTag(tag).SetEnabled(enabled);
                model.state.GetTag(tag).SetEnabled(enabled);
            }
            foreach (var tag in new[] { "power_source_item_energy", "POWER_SOURCE_ITEM_VALUE_PERCENT_INT",
                "amenities", "amenities_use", "current_armor_value", "current_max_armor_value",
                "armor_efficiency", "LIQUID_CONTAINER_CURRENT", "LIQUID_CONTAINER_BASE_PRICE_TAG",
                "ORGAN_HEALTH_INT", "ORGAN_HEALTH_MAX_INT" })
            {
                int value = random.Next(1, 101);
                game.state.GetTag(tag).SetInt(value);
                model.state.GetTag(tag).SetInt(value);
            }
            game.ValidateShapeState();
            model.ValidateShapeState();
            Assert.Equal(game.GetBaseValue(), model.GetBaseValue());
            Assert.Equal(game.GetValue(), model.GetValue());
            Assert.Equal(game.GetValue(singleValue: true), model.GetValue(singleValue: true));
        }
    }

    [Fact]
    public void StagedValueArithmeticMatchesOriginalAssembly()
    {
        var originalFormula = typeof(original::GameItem).GetMethod("ComposeStagedValue",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var modelFormula = typeof(GameItem).GetMethod("ComposeStagedValue",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var random = new Random(345);
        for (int i = 0; i < 500; i++)
        {
            object[] inputs = { (long)random.Next(-100, 10000), (long)random.Next(-200, 200),
                (long)random.Next(-200, 200), random.Next(0, 10) / 4.0, random.Next(-50, 1000), random.Next(0, 100) };
            Assert.Equal(originalFormula.Invoke(null, inputs), modelFormula.Invoke(null, inputs));
        }
    }

    [Fact]
    public void FeatureStagesAndExposedModifiersMatchOriginalAssembly()
    {
        foreach (var id in new[] { "ordinary", "bargainDiscount", "retailMarkUp" })
            for (int stage = 0; stage < 3; stage++)
                for (int type = 0; type < 7; type++)
                {
                    var game = new original::ItemFeature((original::ItemFeature.FeatureType)type)
                    { identifier = id, valueStage = (original::ItemFeature.ValueStage)stage };
                    var model = new ItemFeature((ItemFeature.FeatureType)type)
                    { identifier = id, valueStage = (ItemFeature.ValueStage)stage };
                    game.SetValueModifier(-20).SetPreExposedValueMod(10);
                    model.SetValueModifier(-20).SetPreExposedValueMod(10);
                    Assert.Equal((int)game.GetValueStage(), (int)model.GetValueStage());
                    Assert.Equal(game.GetCurrentValueMod(), model.GetCurrentValueMod());
                    game.SetExposed(true);
                    model.SetExposed(true);
                    Assert.Equal(game.GetCurrentValueMod(), model.GetCurrentValueMod());
                }
    }

    private sealed class OriginalItem : original::GameItem
    {
        internal OriginalItem() : base("item") { }
        public override void Validate() { }
        public override void Destroy() { }
    }
#else
    [Fact(Skip = "Fetch the game assemblies to compare model valuation with the original implementation.")]
    public void LeafValuesMatchOriginalAssemblyAcrossQuantitiesAndValueTags() { }
    [Fact(Skip = "Fetch the game assemblies to compare model valuation with the original implementation.")]
    public void StagedValueArithmeticMatchesOriginalAssembly() { }
    [Fact(Skip = "Fetch the game assemblies to compare model valuation with the original implementation.")]
    public void FeatureStagesAndExposedModifiersMatchOriginalAssembly() { }
#endif
}
