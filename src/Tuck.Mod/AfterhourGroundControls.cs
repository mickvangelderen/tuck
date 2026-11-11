using Tuck.Mod.Packing;
using UnityEngine;

namespace Tuck.Mod;

/// <summary>Packing and scavenge input, owned by Tuck's scene object.</summary>
// Check dialogue before StoreUIManager.Update can end it with the same key press.
[DefaultExecutionOrder(-100)]
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
    public bool InputAllowed() => isActiveAndEnabled
        && _window.handler != null && _window.handler.gameObject.activeInHierarchy
        && MapUIManager.Instance != null && MapUIManager.Instance.Current != null
        && !MapUIManager.Instance.IsInTransit && HotkeysAllowed();

    public void ClickPack()
    {
        if (InputAllowed()) Pack();
    }

    private void Update()
    {
        if (!InputAllowed())
        {
            return;
        }

        if (Input.GetKeyDown(_options.Pack.Key))
        {
            Pack();
        }
        else if (Input.GetKeyDown(_options.Scavenge.Key))
        {
            Scavenge();
        }
    }

    // The first two are what the game's InputActionManager checks for every key; the rest are what the game's own
    // hotkeys check: response windows (StoreBell), dialogue choices (ToolboxHelper), and the Options window.
    // Tuck also applies these to clicks; the EventSystem handles UI occlusion of the scene collider.
    private static bool HotkeysAllowed() =>
        !StoreUIManager.IsAnyTyping()
        && !KeyBindHelper.SuppressHotkeys
        && !Interactable.isPaused
        && !(DialogUIManager.Instance != null && DialogUIManager.Instance.IsChoice())
        && !(GameOptionUIManager.Instance != null && GameOptionUIManager.Instance.IsOpen);

    private static void Scavenge()
    {
        var map = MapUIManager.Instance;
        if (map == null || map.Current is not DumpingGroundLocation || map.IsInTransit
            || ItemPinHelper.BlocksClicks
            || (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem)
            || (DialogUIManager.Instance != null && DialogUIManager.Instance.currentDialog != null)
            || (DialoguePhoneUIManager.Instance != null && DialoguePhoneUIManager.Instance.currentDialog != null))
        {
            return;
        }

        // Use the game's button action, including its attempt/injury limits and UI refresh.
        map.ScavengeButtonClick();
    }

    // Called by Tuck and the hotkey.
    private void Pack()
    {
        // While leaving, the game moves the bag's contents itself; and moving items while one is on the cursor
        // would work from a stale picture of the ground.
        if (ItemPinHelper.BlocksClicks || (MapUIManager.Instance != null && MapUIManager.Instance.IsInTransit)
            || (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem))
        {
            return;
        }

        var entry = EmporiumEntry.Instance;
        if (entry == null || entry.afterhourInventory == null || entry.afterhourPocketSlotInvBackpack == null
            || entry.afterhourPocketSlotInvLeft == null || entry.afterhourPocketSlotInvRight == null) return;

        var result = _pack.Pack(entry.afterhourInventory, entry.afterhourPocketSlotInvBackpack,
            entry.afterhourPocketSlotInvLeft, entry.afterhourPocketSlotInvRight);
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
