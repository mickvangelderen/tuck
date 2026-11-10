// Ported from dependencies/probably-stolen/decompiled/GeneralHelper.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Ownership subset; service-gun predicate inlined from GunHelper.IsServiceGun.
#nullable disable
public class GeneralHelper
{
    public static bool IsItemOwned(GameItem item)
    {
        return item.IsTag("IS_OWNED_TAG");
    }
    public static bool IsInsideNonOwnedContainer(GameItem item)
    {
        if (item?.parentInventory == null)
        {
            return false;
        }
        GameItem gameItem = GraphUtils.FindParentType<GameItem>(item.parentInventory);
        if (gameItem == null)
        {
            return false;
        }
        if (gameItem.IsTag("SERVICE_GUN_TAG"))
        {
            return !IsItemOwned(item);
        }
        return !IsItemOwned(gameItem);
    }
    public static bool MayOwnershipAcceptItem(GameItem item, GameInventory inventory)
    {
        if (!IsItemOwned(item))
        {
            return true;
        }
        GameItem gameItem = GraphUtils.FindParentType<GameItem>(inventory);
        if (gameItem == null)
        {
            return true;
        }
        if (!IsItemOwned(gameItem))
        {
            return gameItem.IsTag("SERVICE_GUN_TAG");
        }
        return true;
    }

    public static bool IsItemEffectivelyOwned(GameItem item)
    {
        if (!IsItemOwned(item))
        {
            return false;
        }
        for (GameItem gameItem = ((item.parentInventory == null) ? null : GraphUtils.FindParentType<GameItem>(item.parentInventory)); gameItem != null; gameItem = ((gameItem.parentInventory == null) ? null : GraphUtils.FindParentType<GameItem>(gameItem.parentInventory)))
        {
            if (!IsItemOwned(gameItem))
            {
                return false;
            }
        }
        return true;
    }
}
