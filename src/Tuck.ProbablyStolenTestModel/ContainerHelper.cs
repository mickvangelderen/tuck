// Ported from dependencies/probably-stolen/decompiled/ContainerHelper.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Scavenging admission helper subset, unchanged.
#nullable disable
public class ContainerHelper
{
    public const string BACKPACK_TAG = "BACKPACK_TAG";
    public const string CONTAINER_TAG = "CONTAINER_TAG";
    public const string POUCH_TAG = "POUCH_TAG";
    public static void AllowOnlyTaggedItem(GameInventory itemInventory, string tag, bool ownedOnly = false, bool excludeContainer = false)
    {
        itemInventory.mayInventoryAddItemFunc = (GameItem item, GameInventory _) =>
        {
            if (ownedOnly && !GeneralHelper.IsItemOwned(item))
            {
                return false;
            }
            if (excludeContainer && item.IsTag("CONTAINER_TAG"))
            {
                return false;
            }
            return item.IsTag(tag) ? true : false;
        };
    }
    public static void AllowOnlyOwnedItems(GameInventory itemInventory)
    {
        itemInventory.mayInventoryAddItemFunc = (GameItem item, GameInventory _) => GeneralHelper.IsItemOwned(item) ? true : false;
    }
    public static void AllowOnlyOwnedItemsExcludeContainers(GameInventory itemInventory)
    {
        itemInventory.mayInventoryAddItemFunc = (GameItem item, GameInventory _) => (GeneralHelper.IsItemOwned(item) && !item.IsTag("CONTAINER_TAG")) ? true : false;
    }
}
