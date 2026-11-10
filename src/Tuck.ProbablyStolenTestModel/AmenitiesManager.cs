// Ported from dependencies/probably-stolen/decompiled/AmenitiesManager.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Only the pure tag-based valuation helper, unchanged.
#nullable disable
public class AmenitiesManager
{
    public static int GetAmenitiesItemValue(GameItem item)
    {
        if (item.IsTag("amenities"))
        {
            return item.GetTagReadonly("amenities").valueInt * item.GetTagReadonly("amenities_use").valueInt / 2;
        }
        return 0;
    }
}
