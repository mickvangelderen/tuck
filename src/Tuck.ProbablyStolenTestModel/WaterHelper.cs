// Ported from dependencies/probably-stolen/decompiled/WaterHelper.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Only the pure tag-based valuation helper, unchanged.
#nullable disable
public class WaterHelper
{
    public static int GetContainerPrice(GameItem gameItem)
    {
        if (!gameItem.IsTag("LIQUID_CONTAINER_TAG"))
        {
            return 0;
        }
        return gameItem.GetTagReadonly("LIQUID_CONTAINER_BASE_PRICE_TAG").GetInt();
    }
}
