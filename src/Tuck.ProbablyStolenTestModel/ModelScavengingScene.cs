namespace Tuck.ProbablyStolenTestModel;

/// <summary>
/// Fixture counterpart of EmporiumEntry.SetupAfterhourInv: the same four storage roots,
/// panel order, ownership/backpack/waste admission rules, and ground search priority.
/// It owns no global singleton or rendered objects.
/// </summary>
public sealed class ModelScavengingScene
{
    public GameGridInventory Ground { get; }
    public GameSlotInventory LeftPocket { get; } = new();
    public GameSlotInventory Backpack { get; } = new(3, 3, stealUI: true);
    public GameSlotInventory RightPocket { get; } = new();
    public MachineryPanelRow Panels { get; }
    public PixelWindow Window { get; }
    public bool AllowsHazardousWaste { get; set; }

    public ModelScavengingScene(int groundWidth = 12, int groundHeight = 9, bool allowsHazardousWaste = true)
    {
        Ground = new GameGridInventory(groundWidth, groundHeight);
        AllowsHazardousWaste = allowsHazardousWaste;
        ContainerHelper.AllowOnlyOwnedItems(LeftPocket);
        ContainerHelper.AllowOnlyTaggedItem(Backpack, "BACKPACK_TAG", ownedOnly: true);
        ContainerHelper.AllowOnlyOwnedItems(RightPocket);
        Ground.mayInventoryAddItemFunc = (item, _) =>
            (!item.IsTag("HAZARDOUS_WASTE_TAG") || AllowsHazardousWaste) && GeneralHelper.IsItemOwned(item);
        Ground.slotPriorityFunc = (_, _, _) => 4;
        Panels = new MachineryPanelRow(Ground, LeftPocket, Backpack, RightPocket);
        Window = new PixelWindow(Panels);
    }
}
