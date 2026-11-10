// Ported from dependencies/probably-stolen/decompiled/LiquidContainerHelper.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Only the pure tag-based valuation helper, unchanged.
#nullable disable
public class LiquidContainerHelper
{
    public static int GetLiquidContainerValue(GameItem item)
    {
        if (item.IsTag("LIQUID_CONTAINER_TAG"))
        {
            return item.GetTagReadonly("LIQUID_CONTAINER_CURRENT").GetInt() / 100;
        }
        return 0;
    }
}
