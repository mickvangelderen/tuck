// Ported from dependencies/probably-stolen/decompiled/OrganHelper.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Only the pure tag-based valuation helper, unchanged.
#nullable disable
public class OrganHelper
{
    public static int GetOrganItemValue(GameItem item)
    {
        if (item.IsTag("ORGAN_ITEM_TAG"))
        {
            double num = item.GetTagReadonly("ORGAN_HEALTH_INT").GetInt();
            double num2 = item.GetTagReadonly("ORGAN_HEALTH_MAX_INT").GetInt();
            return (int)(num / num2 * 2.5 * num2);
        }
        return 0;
    }
}
