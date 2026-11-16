using System.Collections.Generic;
using Tuck.Mod.Packing;
using UnityEngine;

namespace Tuck.Mod;

/// <summary>Packing and mouse interaction, owned by Tuck's scene object.</summary>
internal sealed partial class AfterhourGroundControls : MonoBehaviour
{
    private PixelWindow _window = null!;
    private Options _options = null!;
    private PackEngine _pack = null!;

    // The host stays inactive until all of Tuck's components have been initialized.
    public static AfterhourGroundControls Install(GameObject host, PixelWindow window, Options options)
    {
        var controls = host.AddComponent<AfterhourGroundControls>();
        controls._window = window;
        controls._options = options;
        controls._pack = new PackEngine(options);
        return controls;
    }

    public string PackTooltip()
    {
        var key = MapUIManager.Instance?.Current?.Id switch
        {
            "dumping_ground" => "youre_leaving_that_theres_room_watch",
            "commissary" => "you_bought_all_this_without_checking_the_bag",
            "dr_jackson" => "heavy_things_at_the_bottom_weve_been_over_this",
            _ => "let_me_see_what_fits"
        };
        var line = _options.Text(key);
        return HotkeyTooltips.AppendHint(_options, line, _options.Pack.Key);
    }

    // The old column inherited the Ground window's visibility. The scene object must check it explicitly.
    internal bool IsAvailable => isActiveAndEnabled
        && _window.handler != null && _window.handler.gameObject.activeInHierarchy;

    public bool InputAllowed() => Hotkeys.CanUseGroundControls(this);

    public void ClickPack()
    {
        if (InputAllowed()) Pack();
    }

    // Called by Tuck and the hotkey.
    internal void Pack()
    {
        // While leaving, the game moves the bag's contents itself; and moving items while one is on the cursor
        // would work from a stale picture of the ground.
        if (ItemPinHelper.BlocksClicks || (MapUIManager.Instance != null && MapUIManager.Instance.IsInTransit)
            || (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem)
            || (ItemMultiSelectHandler.current != null && ItemMultiSelectHandler.current.IsGroupDragging))
        {
            return;
        }

        var entry = EmporiumEntry.Instance;
        if (entry == null || entry.afterhourInventory == null || entry.afterhourPocketSlotInvBackpack == null
            || entry.afterhourPocketSlotInvLeft == null || entry.afterhourPocketSlotInvRight == null) return;

        var sources = new Dictionary<string, GameInventory>();
        if (MapUIManager.Instance?.Current?.Id == "dr_jackson")
        {
            if (entry.frontInvinvElement != null) sources.Add("counter-front", entry.frontInvinvElement);
            if (entry.backInvinvElement != null) sources.Add("counter-back", entry.backInvinvElement);
        }
        var result = _pack.Pack(entry.afterhourInventory, entry.afterhourPocketSlotInvBackpack,
            entry.afterhourPocketSlotInvLeft, entry.afterhourPocketSlotInvRight, sources);
        if (result.Planning == null) return;
        if (result.Execution == null)
        {
            Log.Warning("Packing could not derive the scene: " + string.Join("; ", result.Planning.UnsupportedRules));
            return;
        }
        Log.Info($"Packing {result.Execution.Status}: {result.Execution.CompletedSteps}/{result.Planning.Plan.Steps.Count} transfers; "
            + $"planning {result.Planning.Status}.");
        if (result.Execution.Status == ExecutionStatus.Completed)
            Log.Info($"Packing value {result.Planning.InitialValue} -> {result.Planning.FinalValue}.");
        else Log.Warning(result.Execution.Detail ?? "Packing stopped after a state discrepancy.");
        if (result.Planning.UnsupportedRules.Count > 0)
            Log.Warning("Packing restricted unsupported transfers: " + string.Join("; ", result.Planning.UnsupportedRules));

    }
}
