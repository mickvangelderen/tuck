// Ported from dependencies/probably-stolen/decompiled/HopperHelper.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Only the tag-based admission exception; hopper automation is not modeled.
#nullable disable
public class HopperHelper
{
    public static bool IsIntake(GameItem item) => item?.IsTag("HOPPER_INTAKE_TAG") ?? false;
    public static bool IsOutlet(GameItem item) => item?.IsTag("HOPPER_OUTLET_TAG") ?? false;
    public static bool IsHopper(GameItem item)
    {
        if (!IsIntake(item))
        {
            return IsOutlet(item);
        }
        return true;
    }
    public static bool MayBypassGridRules(GameItem item, GameInventory inv)
    {
        if (IsHopper(item) && GeneralHelper.IsItemOwned(item))
        {
            if (inv != null)
            {
                return !inv.IsTag("NO_HOPPER_INVENTORY_TAG");
            }
            return true;
        }
        return false;
    }
}
