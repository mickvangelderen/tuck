namespace Tuck.ProbablyStolenTestModel.Tests;

public class ValuationTests
{
    private static GridShape Mask() => new GridShapeBuilder(1, 1).SetDataFill(1);
    private static ModelItem Item(long value, int count = 1)
        => new("item", Mask(), count, stackCapacity: Math.Max(1, count)) { unitValue = value };

    private static ItemFeature Feature(int percent, ItemFeature.ValueStage stage = ItemFeature.ValueStage.Innate,
        ItemFeature.FeatureType type = ItemFeature.FeatureType.Normale, string id = "feature")
    {
        var feature = new ItemFeature(type) { identifier = id };
        feature.SetValueModifier(percent).SetValueStage(stage);
        feature.SetExposed(true);
        return feature;
    }

    [Fact]
    public void StackQuantityOnlyMultipliesGetValue()
    {
        var item = Item(10, 5);
        item.unitBaseValue = 999;
        item.lateUnitValue = 7;
        Assert.Equal(50, item.GetValue());
        Assert.Equal(10, item.GetValue(singleValue: true));
        Assert.Equal(17, item.GetBaseValue());
        Assert.Equal(17, item.GetCurrentValue());
    }

    [Fact]
    public void NestedContentsAreCountedOnceAndValuesFollowTransfers()
    {
        var outerContents = new GameGridInventory(1, 1);
        var innerContents = new GameGridInventory(1, 1);
        var outer = new ModelItem("outer", Mask(), contents: outerContents) { unitValue = 100 };
        var inner = new ModelItem("inner", Mask(), contents: innerContents) { unitValue = 20 };
        var loot = Item(3, 5);
        outerContents.UncheckedAccept(inner);
        innerContents.UncheckedAccept(loot);
        var ground = new GameGridInventory(1, 1);
        ground.UncheckedAccept(outer);
        Assert.Equal(123, outer.GetBaseValue()); // The game's child current values do not multiply quantities.
        Assert.Equal(123, outer.GetCurrentValue());
        Assert.Equal(100, outer.GetValue()); // This API excludes contents.
        Assert.Equal(135, ground.GetTotalValue()); // Flattened inventory valuation uses stack quantities.

        var pocket = new GameSlotInventory();
        Assert.Equal(0, GraphUtils.TryAcceptAll(pocket, loot));
        Assert.Equal(120, outer.GetBaseValue());
        Assert.Equal(20, inner.GetBaseValue());
        Assert.Equal(135, ground.GetTotalValue() + pocket.GetTotalValue());
    }

    [Fact]
    public void ValueChangesAreProportionalToActualPartialMerge()
    {
        var source = new ModelItem("stack", Mask(), count: 5, stackCapacity: 10) { unitValue = 7 };
        var target = new ModelItem("stack", Mask(), count: 8, stackCapacity: 10) { unitValue = 7 };
        source.mayStackItemFunc = target.mayStackItemFunc = (a, b) => a.identifier == b.identifier;
        var ground = new GameGridInventory(1, 1);
        var bag = new GameGridInventory(1, 1);
        ground.UncheckedAccept(source);
        bag.UncheckedAccept(target);
        long before = ground.GetTotalValue() + bag.GetTotalValue();
        Assert.Equal(3, GraphUtils.TryAcceptAll(bag, source));
        Assert.Equal(21, ground.GetTotalValue());
        Assert.Equal(70, bag.GetTotalValue());
        Assert.Equal(before, ground.GetTotalValue() + bag.GetTotalValue());
    }

    [Fact]
    public void ChildValueHonorsIgnoreAndLockedTags()
    {
        var contents = new GameGridInventory(2, 1);
        var bag = new ModelItem("bag", Mask(), contents: contents) { unitValue = 20 };
        var a = Item(5);
        var b = Item(7);
        contents.UncheckedAccept(a);
        contents.UncheckedAccept(b);
        Assert.Equal(32, bag.GetBaseValue());
        a.EnableTag("ITEM_BEHIND_LOCK_TAG");
        Assert.Equal(27, bag.GetBaseValue());
        bag.EnableTag("IGNORE_CHILD_VALUE");
        Assert.Equal(20, bag.GetBaseValue());
        Assert.Equal(12, contents.GetTotalValue()); // GetValue uses a different policy.
    }

    [Fact]
    public void StagedFeaturesWaterAndLateValueFollowOriginalArithmetic()
    {
        var item = Item(100);
        item.lateUnitValue = 7;
        item.EnableTag("LIQUID_CONTAINER_TAG");
        item.ModifyTag("LIQUID_CONTAINER_BASE_PRICE_TAG", tag => tag.SetInt(10));
        item.itemFeatures.Add(Feature(20));
        item.itemFeatures.Add(Feature(50, ItemFeature.ValueStage.Market));
        item.itemFeatures.Add(Feature(-50, ItemFeature.ValueStage.Final));
        Assert.Equal(117, item.GetBaseValue()); // Own features do not affect this API.
        Assert.Equal(101, item.GetCurrentValue()); // ((100 * 1.2 + 10) * 1.5 + 7) * .5
        item.itemFeatures[2].isDisabled = true;
        Assert.Equal(202, item.GetCurrentValue());
    }

    [Theory]
    [InlineData(5, 2)]
    [InlineData(7, 4)]
    public void FinalValueUsesMidpointToEvenRounding(int value, long expected)
    {
        var item = Item(value);
        item.itemFeatures.Add(Feature(-50, ItemFeature.ValueStage.Final));
        Assert.Equal(expected, item.GetCurrentValue());
    }

    [Fact]
    public void NegativeMultipliersClampAtTheOriginalStages()
    {
        var item = Item(100);
        item.lateUnitValue = 7;
        item.itemFeatures.Add(Feature(-200));
        Assert.Equal(7, item.GetCurrentValue());
        item.itemFeatures.Add(Feature(-200, ItemFeature.ValueStage.Final));
        Assert.Equal(0, item.GetCurrentValue());
        var negative = Item(-5, 2);
        Assert.Equal(0, negative.GetBaseValue());
        Assert.Equal(-10, negative.GetValue()); // GetValue itself does not clamp.
    }

    [Fact]
    public void CurrentValueUsesContextEventsButSkipsEventFeatures()
    {
        var item = Item(100);
        item.EnableTag("material");
        item.ValuationContext.EventModifiers.Add(new("material", 30));
        item.ValuationContext.EventModifiers.Add(new("unrelated", 500));
        item.itemFeatures.Add(Feature(20, type: ItemFeature.FeatureType.Event));
        Assert.Equal(130, item.GetCurrentValue());
        Assert.Equal(100, item.GetCurrentValue(includeEvents: false));
        Assert.Equal(120, item.GetNegociatedValue());
        Assert.Equal(100, item.GetBaseValue());
    }

    [Fact]
    public void TemporaryClientMarketModifierIsInheritedByContents()
    {
        var contents = new GameGridInventory(1, 1);
        var bag = new ModelItem("bag", Mask(), contents: contents) { unitValue = 100 };
        var loot = Item(100);
        contents.UncheckedAccept(loot);
        bag.itemFeatures.Add(Feature(50, ItemFeature.ValueStage.Market, ItemFeature.FeatureType.ClientTemporary));
        Assert.Equal(200, bag.GetBaseValue()); // No client-modifier inheritance in GetBaseValue.
        Assert.Equal(300, bag.GetCurrentValue());
        bag.itemFeatures[0].identifier = "retailMarkUp";
        Assert.Equal(250, bag.GetCurrentValue()); // Retail markup is not inherited.
    }

    [Fact]
    public void ForcedMarkupIsNotAppliedTwiceWhenFeatureAlreadyExists()
    {
        var item = Item(100);
        item.ValuationContext.RetailMarkup = 25;
        Assert.Equal(125, item.GetCurrentValue(forceMarkup: true));
        item.itemFeatures.Add(Feature(25, ItemFeature.ValueStage.Market, id: "retailMarkUp"));
        Assert.Equal(125, item.GetCurrentValue(forceMarkup: true));
    }

    [Fact]
    public void OrdinaryRetailMarkupRequiresOwnedBackInventoryAndClientConsent()
    {
        var storage = new GameGridInventory(1, 1) { identifier = "backInv" };
        var owner = new ModelItem("owner", Mask(), contents: storage);
        owner.EnableTag("IS_OWNED_TAG");
        var item = Item(100);
        item.EnableTag("IS_OWNED_TAG");
        item.ValuationContext.RetailMarkup = 25;
        storage.UncheckedAccept(item);
        Assert.Equal(100, item.GetCurrentValue(useRetailMarkup: true));
        item.ValuationContext.ClientAcceptsMarkup = true;
        Assert.Equal(125, item.GetCurrentValue(useRetailMarkup: true));
        owner.DisableTag("IS_OWNED_TAG");
        Assert.Equal(100, item.GetCurrentValue(useRetailMarkup: true));
    }

    [Fact]
    public void TagBasedValueHelpersAreCalculatedRatherThanFrozenTotals()
    {
        var item = Item(5, 2);
        item.unitBaseValue = 20;
        item.EnableTag("power_source_item");
        item.ModifyTag("power_source_item_energy", tag => tag.SetInt(100));
        item.ModifyTag("POWER_SOURCE_ITEM_VALUE_PERCENT_INT", tag => tag.Enable().SetInt(50));
        item.ModifyTag("amenities", tag => tag.Enable().SetInt(2));
        item.ModifyTag("amenities_use", tag => tag.SetInt(4));
        item.EnableTag("armor_item");
        item.ModifyTag("current_armor_value", tag => tag.SetInt(20));
        item.ModifyTag("current_max_armor_value", tag => tag.SetInt(30));
        item.ModifyTag("armor_efficiency", tag => tag.SetInt(50));
        item.EnableTag("LIQUID_CONTAINER_TAG");
        item.ModifyTag("LIQUID_CONTAINER_CURRENT", tag => tag.SetInt(450));
        item.ModifyTag("LIQUID_CONTAINER_BASE_PRICE_TAG", tag => tag.SetInt(10));
        item.EnableTag("ORGAN_ITEM_TAG");
        item.ModifyTag("ORGAN_HEALTH_INT", tag => tag.SetInt(12));
        item.ModifyTag("ORGAN_HEALTH_MAX_INT", tag => tag.SetInt(20));
        Assert.Equal(70, item.GetComplexeItemValue());
        Assert.Equal(85, item.GetBaseValue());
        Assert.Equal(85, item.GetCurrentValue());
        Assert.Equal(276, item.GetValue());
        item.ModifyTag("power_source_item_energy", tag => tag.SetInt(50));
        Assert.Equal(60, item.GetBaseValue());
        Assert.Equal(226, item.GetValue());
    }

    [Fact]
    public void NotPurchasedMultiplierUsesTruncationBeforeStackQuantity()
    {
        var item = Item(15, 3);
        item.EnableTag(GameCharacterItem.TAG_NOT_PURCHASED);
        Assert.Equal(48, item.GetValue()); // (long)(15 * 1.1) * 3, not (long)(15 * 3 * 1.1)
    }

    [Fact]
    public void FeatureExposureAndConditionValuesAreRespected()
    {
        var item = Item(100);
        var feature = new ItemFeature { identifier = "condition" };
        feature.SetValueModifier(-20).SetPreExposedValueMod(30);
        item.itemFeatures.Add(feature);
        Assert.Equal(130, item.GetCurrentValue());
        feature.SetExposed(true);
        Assert.Equal(80, item.GetCurrentValue());
        feature.useCondition = true;
        feature.fakeCondition = new ItemCondition { modValue = 10 };
        feature.realCondition = new ItemCondition { modValue = -50 };
        Assert.Equal(50, item.GetCurrentValue());
        feature.SetExposed(false);
        Assert.Equal(110, item.GetCurrentValue());
        feature.realCondition.onActivateActionId = "live-action";
        Assert.Throws<NotSupportedException>(() => feature.SetExposed(true));
        Assert.False(feature.IsExposed());
    }

    [Fact]
    public void VendorSpecificBarterIsExplicitlyUnsupported()
    {
        Assert.Throws<NotSupportedException>(() => Item(100).GetValue(vendor: new Vendor()));
    }

    private sealed class Vendor : GameCharacterItem
    {
        internal Vendor() : base("vendor") { }
        public override void Validate() { }
        public override void Destroy() { }
    }
}
