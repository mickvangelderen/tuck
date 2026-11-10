// Ported from dependencies/probably-stolen/decompiled/PowerHelper.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Only the pure tag-based valuation helper, unchanged.
#nullable disable
public class PowerHelper
{
    public static int GetPowerSourceItemValue(GameItem gameItem)
    {
        if (gameItem.IsTag("power_source_item"))
        {
            int num = gameItem.GetTagReadonly("power_source_item_energy").GetInt();
            if (gameItem.IsTag("POWER_SOURCE_ITEM_VALUE_PERCENT_INT"))
            {
                num = num * gameItem.GetTagReadonly("POWER_SOURCE_ITEM_VALUE_PERCENT_INT").GetInt() / 100;
            }
            return (int)(num + gameItem.unitBaseValue);
        }
        return 0;
    }
}
