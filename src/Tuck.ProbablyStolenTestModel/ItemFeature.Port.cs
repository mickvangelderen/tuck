// Ported from dependencies/probably-stolen/decompiled/ItemFeature.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Value-stage, exposure and modifier calculations unchanged; display/gameplay behavior omitted.
#nullable disable
public partial class ItemFeature
{
    public ValueStage GetValueStage()
    {
        if (valueStage == ValueStage.Innate)
        {
            if (featureType == FeatureType.Event)
            {
                return ValueStage.Market;
            }
            if (identifier != null && legacyFinalStageIds.Contains(identifier))
            {
                return ValueStage.Final;
            }
        }
        return valueStage;
    }

    public ItemFeature SetValueStage(ValueStage stage)
    {
        valueStage = stage;
        return this;
    }

    public bool IsExposed()
    {
        return isFeatureExposed;
    }

    public int GetActualValueModifier()
    {
        if (useCondition)
        {
            return realCondition.modValue;
        }
        return valueModifier;
    }

    public int GetFakeValueModifier()
    {
        if (useCondition)
        {
            return fakeCondition.modValue;
        }
        return preExposeValueModifier;
    }

    public ItemFeature SetValueModifier(int value)
    {
        valueModifier = value;
        return this;
    }

    public ItemFeature SetExposedValueMod(int value)
    {
        valueModifier = value;
        return this;
    }

    public ItemFeature SetPreExposedValueMod(int value, bool flag = true)
    {
        preExposeValueModifier = value;
        usePreExposeValue = flag;
        return this;
    }

    public int GetCurrentValueMod()
    {
        if (!useCondition)
        {
            if (IsExposed())
            {
                return valueModifier;
            }
            if (usePreExposeValue)
            {
                return preExposeValueModifier;
            }
            return 0;
        }
        if (IsExposed())
        {
            return realCondition.modValue;
        }
        return fakeCondition.modValue;
    }
}
