// Ported from dependencies/probably-stolen/decompiled/ArmorHelper.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Only the pure tag-based valuation helper, unchanged.
#nullable disable
public class ArmorHelper
{
    public static int GetArmorItemValue(GameItem item)
    {
        if (item.IsTag("armor_item"))
        {
            int num = item.GetTagReadonly("current_armor_value").GetInt();
            int num2 = item.GetTagReadonly("current_max_armor_value").GetInt();
            int num3 = item.GetTagReadonly("armor_efficiency").GetInt();
            return (num + num2) * num3 / 100;
        }
        return 0;
    }
}
