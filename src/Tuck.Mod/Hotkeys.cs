using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Tuck.Mod;

/// <summary>Owns Tuck's action shortcuts, their contexts, and hold cancellation.</summary>
// Inspect context before the game's normal Update methods can dismiss dialogue or menus.
[DefaultExecutionOrder(-100)]
[HarmonyPatch]
internal sealed class Hotkeys : MonoBehaviour
{
    private static Options? _options;
    private static AfterhourGround? _ground;
    private static Hotkeys? _instance;
    private Hotkey _pack = null!;
    private Hotkey _scavenge = null!;
    private Hotkey _return = null!;
    private Hotkey _shutters = null!;
    private Hotkey _nightActivities = null!;
    private Hotkey _sleep = null!;
    private Hotkey _tablet = null!;
    private Hotkey _accept = null!;
    private Hotkey _decline = null!;
    private Button? _acceptTarget;
    private Button? _declineTarget;
    private Hotkey[] _all = [];

    public static void Register(Options options, AfterhourGround ground)
    {
        _options = options;
        _ground = ground;
        PanelButtons.Register(options);
        if (CustomUIManager.Instance != null) Attach(CustomUIManager.Instance);
    }

    public static void Unregister()
    {
        _options = null;
        _ground = null;
        if (_instance != null)
        {
            _instance.enabled = false;
            Destroy(_instance);
            _instance = null;
        }
        PanelButtons.Unregister();
        HoldRingPresenter.Unregister();
    }

    [HarmonyPostfix, HarmonyPatch(typeof(CustomUIManager), "Awake")]
    private static void Attach(CustomUIManager __instance)
    {
        // The UI manager persists across locations and rejects duplicate instances in Awake.
        if (_options == null || __instance != CustomUIManager.Instance || _instance != null) return;
        _instance = __instance.gameObject.AddComponent<Hotkeys>();
        _instance.Initialize(_options);
        HoldRingPresenter.Register(_instance._all);
    }

    private void Initialize(Options options)
    {
        _pack = new Hotkey(options.Pack);
        _scavenge = new Hotkey(options.Scavenge);
        _return = new Hotkey(options.Return, options.ReturnHoldDuration, options.HoldTimeoutDuration);
        _shutters = new Hotkey(options.Shutters);
        _nightActivities = new Hotkey(options.NightActivities);
        _sleep = new Hotkey(options.Sleep);
        _tablet = new Hotkey(options.Tablet);
        _accept = new Hotkey(options.Accept, options.AcceptHoldDuration, options.HoldTimeoutDuration);
        _decline = new Hotkey(options.Decline, options.DeclineHoldDuration, options.HoldTimeoutDuration);
        _all = [_pack, _scavenge, _return, _shutters, _nightActivities, _sleep, _tablet, _accept, _decline];
    }

    private void Update()
    {
        if (!CanUseHotkeys())
        {
            CancelAll();
            return;
        }

        var map = MapUIManager.Instance;
        var ui = StoreUIManager.Instance;
        var ground = _ground?.Controls;
        var drag = ItemMouseDragHandler.current;
        bool draggingItem = drag != null && drag.IsDraggingItem;
        bool nightAllowed = CanUseNightHotkeys(map);
        bool groundAllowed = nightAllowed && GroundAvailable(ground);
        // R also resets a dragged item; finish item manipulation before packing, scavenging or returning.
        bool canMoveItems = !ItemPinHelper.BlocksClicks && !draggingItem
            && !(ItemMultiSelectHandler.current != null && ItemMultiSelectHandler.current.IsGroupDragging);
        bool dialogueOpen = (DialogUIManager.Instance != null && DialogUIManager.Instance.currentDialog != null)
            || (DialoguePhoneUIManager.Instance != null && DialoguePhoneUIManager.Instance.currentDialog != null);

        // Return works even at locations without Tuck or a visible Ground window.
        bool canReturn = nightAllowed && canMoveItems && !dialogueOpen
            && !(GenericConfirmationUIManager.Instance != null
                && GenericConfirmationUIManager.Instance.panel.activeInHierarchy);
        bool canScavenge = groundAllowed && map!.Current is DumpingGroundLocation && canMoveItems && !dialogueOpen;
        // These actions previously ran from StoreUIManager.Update, so require that manager to be active.
        bool actionsAllowed = ui != null && ui.isActiveAndEnabled && CanUseActionHotkeys(ui, draggingItem);
        // End-day captures A/D, but its blocker must still prevent shop actions.
        bool shopAllowed = actionsAllowed && !ui!.backgroundBlocking.activeInHierarchy && CanUseShopHotkeys(map);
        var targets = actionsAllowed ? PanelButtons.FindTargets(ui!) : default;
        UpdatePanelTarget(_accept, targets.Accept, ref _acceptTarget);
        UpdatePanelTarget(_decline, targets.Decline, ref _declineTarget);

        // Poll every shortcut before executing any action. Poll(false) also cancels an inapplicable hold.
        bool accept = _accept.Poll(_acceptTarget != null);
        bool decline = _decline.Poll(_declineTarget != null);
        bool pack = _pack.Poll(groundAllowed && canMoveItems);
        bool scavenge = _scavenge.Poll(canScavenge);
        bool returnToShop = _return.Poll(canReturn);
        bool shutters = _shutters.Poll(shopAllowed);
        bool nightActivities = _nightActivities.Poll(shopAllowed);
        bool sleep = _sleep.Poll(shopAllowed);
        bool tablet = _tablet.Poll(shopAllowed);

        if (accept) PanelButtons.Click(_acceptTarget);
        if (decline) PanelButtons.Click(_declineTarget);

        // Preserve Pack's existing precedence when Pack and Scavenge are pressed together.
        if (pack) ground!.Pack();
        // Use the native button action for attempt/injury limits and UI refresh.
        else if (scavenge) map!.ScavengeButtonClick();
        // Keep the game's leave warnings and the mod's scavenging value threshold.
        if (returnToShop) map!.Return();
        if (shutters) TryClick(StoreShutterButton.Instance);
        if (nightActivities) TryClick(Object.FindObjectOfType<StoreDoor>());
        if (sleep) TryClick(Object.FindObjectOfType<StoreBedroll>());
        if (tablet) TryClick(Object.FindObjectOfType<StoreTablet>());
    }

    internal static void UpdatePanelTarget(Hotkey hotkey, Button? target, ref Button? previous)
    {
        // A hold must not transfer to a new panel, or from Buy to Wholesale.
        if (!ReferenceEquals(target, previous)) hotkey.Cancel();
        previous = target;
    }

    private void OnApplicationFocus(bool focused)
    {
        // Cancel on the event: the game may stop polling input until focus returns.
        if (!focused) CancelAll();
    }

    private void OnDisable() => CancelAll();

    private void CancelAll()
    {
        foreach (var hotkey in _all) hotkey.Cancel();
    }

    // Also used by Tuck's mouse interaction, which has always shared these restrictions.
    internal static bool CanUseGroundControls(AfterhourGroundControls controls) =>
        CanUseHotkeys() && CanUseNightHotkeys(MapUIManager.Instance) && GroundAvailable(controls);

    private static bool GroundAvailable(AfterhourGroundControls? controls) =>
        controls != null && controls.IsAvailable
        && !(DialogUIManager.Instance != null && DialogUIManager.Instance.IsChoice());

    // Night controls also respect input capture and the game's interaction pause.
    private static bool CanUseNightHotkeys(MapUIManager? map) =>
        map != null && map.Current != null && !map.IsInTransit
        && !KeyBindHelper.SuppressHotkeys && !Interactable.isPaused;

    private static bool CanUseHotkeys()
    {
        // Typing E or T into a text field must not also sleep or open the tablet.
        // Evidence:
        // - StoreUIManager.IsAnyTyping detects focused text fields and the console input.
        // - Our Input.GetKeyDown calls still see letters typed there; text entry does not consume them.
        if (StoreUIManager.IsAnyTyping()) return false;

        // Do not let shortcuts activate shop objects behind the Options screen's mouse blocker.
        // Evidence:
        // - GameOptionUIManager.OpenUI uses the GameOptionMenu prefab, whose root Image spans
        //   the screen and has raycastTarget enabled, preventing mouse clicks on shop objects.
        // - Opening Options does not pause Interactable; HandleClick bypasses that blocking Image.
        // - Accepting or cancelling a key binding leaves Options open, so this also covers rebinding.
        if (GameOptionUIManager.Instance != null && GameOptionUIManager.Instance.IsOpen) return false;

        return true;
    }

    private static bool CanUseActionHotkeys(StoreUIManager ui, bool draggingItem)
    {
        // These hotkeys do not apply while dragging.
        var group = ItemMultiSelectHandler.current;
        bool isDragging = draggingItem || (group != null && group.IsGroupDragging);
        if (isDragging) return false;

        // Keep paper-editor commands from also activating shop shortcuts.
        // Evidence:
        // - PaperEditor.HandleKeys handles Ctrl+C outside text fields. Our default shutter key C
        //   also sees that press, so copying could close the shop.
        // - These editor commands work without a focused text field, so IsAnyTyping does not cover them.
        if (PaperEditorWindow.IsOpen) return false;

        // Keep shop shortcuts inactive while the player uses Wild Network.
        // Evidence:
        // - WildNetworkUI.Open leaves shop objects enabled, so their click handlers do not enforce it.
        if (WildNetworkUI.IsOpen) return false;

        // Do not perform shop actions behind the full-screen run results.
        // Evidence:
        // - RunSummaryUI.Build covers the screen with an overlay Image, leaving the shop objects enabled.
        // - Calling Interactable.HandleClick skips mouse hit-testing, so the overlay alone does not block hotkeys.
        if (RunSummaryUI.IsOpen) return false;

        // Do not let shortcuts bypass the Escape menu's (Resume, Options, Main Menu, Exit) mouse blocker.
        // Evidence:
        // - EscapeUIManager.OpenEscapeMenu activates menuPanel, the scene's _EscapeMenuBG object.
        //   Its Image spans the screen and has raycastTarget enabled, blocking clicks on the shop.
        // - Opening the menu does not pause Interactable; HandleClick bypasses that blocking Image.
        if (ui.escapeUIManager != null && ui.escapeUIManager.menuPanel.activeInHierarchy) return false;

        return true;
    }

    private static bool CanUseShopHotkeys(MapUIManager? map)
    {
        // Do not let shortcuts activate shop objects behind a generic confirmation's mouse blocker.
        // Evidence:
        // - GenericConfirmationUIManager.OpenPanel and OpenPanelSingleButton enable confirmationDarkness.
        // - This is the scene's _GenericConfirmationDarkness: a screen-filling Image with raycastTarget
        //   enabled. It blocks mouse clicks on the shop without setting Interactable.isPaused.
        // - HandleClick bypasses that Image, so the native pause check does not preserve this restriction.
        if (GenericConfirmationUIManager.Instance != null && GenericConfirmationUIManager.Instance.panel.activeInHierarchy) return false;

        // Do not let shortcuts bypass the leaving-shop warning's mouse blocker.
        // Evidence:
        // - MapUIManager.OpenGoOutsideConfirm activates the scene's _GoOutConfirm panel. Its 2000x2000
        //   Darkness child has an Image with raycastTarget enabled, blocking clicks on the shop behind it.
        // - Opening the warning does not pause Interactable; HandleClick bypasses that blocking Image.
        if (map != null && map.confirmationPanel.activeInHierarchy) return false;

        return true;
    }

    private static void TryClick(MonoBehaviour target)
    {
        if (target == null) return;
        var interactable = target.GetComponent<Interactable>();
        // Let the game's enabled objects determine which actions exist in the current location.
        // Evidence:
        // - EmporiumEntry.OnLeaveStore disables the shop objects, so they cannot receive clicks outside.
        // - DrJacksonLocation.Arrive creates an enabled StoreTablet for trading there. It is a valid
        //   target for T, using the same click handler as the shop tablet.
        if (interactable != null && interactable.isActiveAndEnabled)
            interactable.HandleClick();
    }
}
