// Ported from dependencies/probably-stolen/decompiled/GameItem.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Arithmetic, tag helpers, features and descendant accounting preserved. External event/markup inputs are fixture-scoped; debug UI removed; vendor barter explicitly rejected.
#nullable disable
public abstract partial class GameItem
{
    public long GetValue(List<GameCharacterItem> party = null, GameCharacterItem vendor = null, bool singleValue = false)
    {
        return ValueModifier(this, party, vendor) * (singleValue ? 1 : unitCount);
    }

    private void AccumulateFeatureStages(ref long innateMult, ref long marketMult, ref double finalFactor, bool skipEventFeatures = false)
    {
        foreach (ItemFeature itemFeature in itemFeatures)
        {
            if (!itemFeature.isDisabled && (!skipEventFeatures || itemFeature.featureType != ItemFeature.FeatureType.Event))
            {
                switch (itemFeature.GetValueStage())
                {
                    case ItemFeature.ValueStage.Market:
                        marketMult += itemFeature.GetCurrentValueMod();
                        break;
                    case ItemFeature.ValueStage.Final:
                        finalFactor *= Math.Max(0.0, 1.0 + (double)itemFeature.GetCurrentValueMod() / 100.0);
                        break;
                    default:
                        innateMult += itemFeature.GetCurrentValueMod();
                        break;
                }
            }
        }
    }

    private static long ComposeStagedValue(long totalBaseValue, long innateMult, long marketMult, double finalFactor, int miscValue, int marketScaledValue = 0)
    {
        double num = Math.Max(0.0, (double)totalBaseValue * (1.0 + (double)innateMult / 100.0));
        double num2 = Math.Max(0.0, 1.0 + (double)marketMult / 100.0);
        return (long)Math.Round(((num + (double)marketScaledValue) * num2 + (double)miscValue) * finalFactor);
    }

    public static long CheckValueSafe(long finalValue)
    {
        if (finalValue <= 0)
        {
            finalValue = 0L;
        }
        return finalValue;
    }

    public long GetComplexeItemValue()
    {
        return 0L + (long)PowerHelper.GetPowerSourceItemValue(this);
    }

    public long GetBaseValue()
    {
        long complexeItemValue = GetComplexeItemValue();
        int itemMiscValue = GetItemMiscValue(useMarkup: false, withChild: true, includeEvents: false, forceMarkup: false, inheritClientMods: false);
        return CheckValueSafe(unitValue + complexeItemValue + itemMiscValue + WaterHelper.GetContainerPrice(this));
    }

    public int GetItemMiscValue(bool useMarkup = false, bool withChild = true, bool includeEvents = true, bool forceMarkup = false, bool inheritClientMods = true)
    {
        int num = (int)lateUnitValue;
        if (IsTag("IGNORE_CHILD_VALUE"))
        {
            withChild = false;
        }
        if (withChild)
        {
            num += GetCurrentChildValue(useMarkup, includeEvents, forceMarkup, inheritClientMods);
        }
        return num;
    }

    public int GetCurrentChildValue(bool useMarkup = false, bool includeEvents = true, bool forceMarkup = false, bool inheritClientMods = true)
    {
        long num = 0L;
        long extraMarketMod = (inheritClientMods ? GetInheritableClientMarketMod() : 0);
        foreach (GameItem item in FindAllChildItems())
        {
            if (!item.IsTag("ITEM_BEHIND_LOCK_TAG"))
            {
                num += item.GetCurrentValue(useMarkup, withChild: false, includeEvents, forceMarkup, extraMarketMod);
            }
        }
        return (int)num;
    }

    public long GetInheritableClientMarketMod()
    {
        long num = 0L;
        foreach (ItemFeature itemFeature in itemFeatures)
        {
            if (!itemFeature.isDisabled && itemFeature.GetValueStage() == ItemFeature.ValueStage.Market && !itemFeature.identifier.Equals("retailMarkUp") && (itemFeature.featureType == ItemFeature.FeatureType.Temporary || itemFeature.featureType == ItemFeature.FeatureType.TemporaryBuying || itemFeature.featureType == ItemFeature.FeatureType.TemporarySelling || itemFeature.featureType == ItemFeature.FeatureType.ClientTemporary))
            {
                num += itemFeature.GetCurrentValueMod();
            }
        }
        return num;
    }

    public List<GameItem> FindAllChildItems()
    {
        return GraphUtils.FindAllChildrenType<GameItem>(this);
    }

    public ItemFeature FindItemFeatureByID(string id)
    {
        foreach (ItemFeature itemFeature in itemFeatures)
        {
            if (id.Equals(itemFeature.identifier))
            {
                return itemFeature;
            }
        }
        return null;
    }

    public bool IsRootedInBackInv()
    {
        if (GetParentWithIdentifier("backInv") != null)
        {
            return true;
        }
        return false;
    }

    public GameInventory GetParentWithIdentifier(string targetIdentifier)
    {
        return FindParentWithIdentifier(parentInventory, targetIdentifier);
    }

    public static GameInventory FindParentWithIdentifier(GraphNodeStorage node, string targetIdentifier)
    {
        if (node == null || node.parents == null || node.parents.Count == 0)
        {
            return null;
        }
        if (node is GameInventory gameInventory && gameInventory.identifier == targetIdentifier)
        {
            return gameInventory;
        }
        foreach (GraphNodeStorage parent in node.parents)
        {
            if (parent is GameInventory gameInventory2 && gameInventory2.identifier == targetIdentifier)
            {
                return gameInventory2;
            }
            GameInventory gameInventory3 = FindParentWithIdentifier(parent, targetIdentifier);
            if (gameInventory3 != null)
            {
                return gameInventory3;
            }
        }
        return null;
    }

    public TagState GetTagReadonly(string tagName)
    {
        return modifiedState.GetTag(tagName).Clone();
    }

    public GameItem ModifyTag(string tagName, Action<TagState> action, bool skipValidate = false)
    {
        if (action != null)
        {
            action(state.GetTag(tagName));
            if (!skipValidate)
            {
                ValidateShapeState();
            }
        }
        return this;
    }

    private static long ValueModifier(GameItem item, List<GameCharacterItem> party, GameCharacterItem vendor)
    {
        int num = 0;
        num += PowerHelper.GetPowerSourceItemValue(item);
        num += AmenitiesManager.GetAmenitiesItemValue(item);
        num += ArmorHelper.GetArmorItemValue(item);
        num += LiquidContainerHelper.GetLiquidContainerValue(item);
        num += OrganHelper.GetOrganItemValue(item);
        double num2 = 1.0;
        if (item.IsTag(GameCharacterItem.TAG_NOT_PURCHASED))
        {
            num2 = 1.1;
        }
        else if (!item.IsTag(GameCharacterItem.TAG_NOT_PURCHASED) && vendor != null)
        {
            num2 = 0.9;
        }
        double num3 = 1.0;
        double num4 = 1.0;
        if (vendor != null) { throw new NotSupportedException("Vendor-specific barter is outside the scavenging model."); }
        double num5 = (double)(item.unitValue + num) * num2 * num3 * num4;
        if (!item.IsTag(GameCharacterItem.TAG_NOT_PURCHASED) && num4 < 0.5)
        {
            num5 = 1.0;
        }
        return (long)num5;
    }

    public long GetCurrentValue(bool useRetailMarkup = false, bool withChild = true, bool includeEvents = true, bool forceMarkup = false, long extraMarketMod = 0L)
    {

        long complexeItemValue = GetComplexeItemValue();
        long totalBaseValue = unitValue + complexeItemValue;
        long innateMult = 0L;
        long marketMult = 0L;
        double finalFactor = 1.0;
        if (includeEvents) { foreach (var modifier in valuationContext.EventModifiers) { if (IsTag(modifier.ItemTag)) { marketMult += modifier.Percent; } } }
        AccumulateFeatureStages(ref innateMult, ref marketMult, ref finalFactor, skipEventFeatures: true);
        if (((IsApplyMarkUp() & useRetailMarkup) | forceMarkup) && FindItemFeatureByID("retailMarkUp") == null)
        {
            marketMult += valuationContext.RetailMarkup;
        }
        marketMult += extraMarketMod;
        int itemMiscValue = GetItemMiscValue(useRetailMarkup, withChild, includeEvents, forceMarkup);
        long finalValue = ComposeStagedValue(totalBaseValue, innateMult, marketMult, finalFactor, itemMiscValue, WaterHelper.GetContainerPrice(this));

        return CheckValueSafe(finalValue);
    }

    public bool IsApplyMarkUp()
    {
        if (IsRootedInBackInv() && GeneralHelper.IsItemEffectivelyOwned(this) && valuationContext.ClientAcceptsMarkup)
        {
            return true;
        }
        return false;
    }

    public long GetNegociatedValue()
    {
        long complexeItemValue = GetComplexeItemValue();
        long totalBaseValue = unitValue + complexeItemValue;
        long innateMult = 0L;
        long marketMult = 0L;
        double finalFactor = 1.0;
        AccumulateFeatureStages(ref innateMult, ref marketMult, ref finalFactor);
        int itemMiscValue = GetItemMiscValue(useMarkup: true);
        long finalValue = ComposeStagedValue(totalBaseValue, innateMult, marketMult, finalFactor, itemMiscValue, WaterHelper.GetContainerPrice(this));

        return CheckValueSafe(finalValue);
    }
}
