using HarmonyLib;
using UnityEngine;

namespace Tuck.Mod;

/// <summary>Runs the normal click actions of the available shop objects from rebindable keys.</summary>
// Check before StoreUIManager can dismiss a dialog or options with the same key press.
[HarmonyPatch(typeof(StoreUIManager), "Update")]
internal sealed class StoreHotkeys
{
    private static StoreHotkeys? _current;
    private readonly Hotkey _shutters;
    private readonly Hotkey _nightActivities;
    private readonly Hotkey _sleep;
    private readonly Hotkey _tablet;

    public StoreHotkeys(Hotkey shutters, Hotkey nightActivities, Hotkey sleep, Hotkey tablet)
    {
        _shutters = shutters;
        _nightActivities = nightActivities;
        _sleep = sleep;
        _tablet = tablet;
    }

    public void Register() => _current = this;

    public void Unregister() => _current = null;

    private static void Prefix(StoreUIManager __instance)
    {
        var hotkeys = _current;
        if (hotkeys == null) return;
        bool shutters = hotkeys._shutters.Poll();
        bool nightActivities = hotkeys._nightActivities.Poll();
        bool sleep = hotkeys._sleep.Poll();
        bool tablet = hotkeys._tablet.Poll();
        if (!shutters && !nightActivities && !sleep && !tablet) return;

        // Evaluate context before any action can open a dialog or change location.
        if (!CanUseShopHotkeys(__instance)) return;
        if (shutters) TryClick(StoreShutterButton.Instance);
        if (nightActivities) TryClick(Object.FindObjectOfType<StoreDoor>());
        if (sleep) TryClick(Object.FindObjectOfType<StoreBedroll>());
        if (tablet) TryClick(Object.FindObjectOfType<StoreTablet>());
    }

    private static bool CanUseShopHotkeys(StoreUIManager ui)
    {
        var map = MapUIManager.Instance;

        // These hotkeys do not apply while dragging. 
        var drag = ItemMouseDragHandler.current;
        var group = ItemMultiSelectHandler.current;
        bool isDragging = (drag != null && drag.IsDraggingItem)
            || (group != null && group.IsGroupDragging);
        if (isDragging) return false;

        // Typing E or T into a text field must not also sleep or open the tablet.
        // Evidence:
        // - StoreUIManager.IsAnyTyping detects focused text fields and the console input.
        // - Our Input.GetKeyDown calls still see letters typed there; text entry does not consume them.
        if (StoreUIManager.IsAnyTyping()) return false;

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

        // Finish or cancel the end-of-day screen before starting another shop action.
        // Evidence:
        // - EndOfDayUIManager.OpenUI enables backgroundBlocking to prevent clicks on the shop behind it.
        // - Our direct click calls bypass that blocker.
        if (ui.backgroundBlocking.activeInHierarchy) return false;

        // Do not let shortcuts activate shop objects behind the Options screen's mouse blocker.
        // Evidence:
        // - GameOptionUIManager.OpenUI uses the GameOptionMenu prefab, whose root Image spans
        //   the screen and has raycastTarget enabled, preventing mouse clicks on shop objects.
        // - Opening Options does not pause Interactable; HandleClick bypasses that blocking Image.
        // - Accepting or cancelling a key binding leaves Options open, so this also covers rebinding.
        if (GameOptionUIManager.Instance != null && GameOptionUIManager.Instance.IsOpen) return false;

        // Do not let shortcuts bypass the Escape menu's (Resume, Options, Main Menu, Exit) mouse blocker.
        // Evidence:
        // - EscapeUIManager.OpenEscapeMenu activates menuPanel, the scene's _EscapeMenuBG object.
        //   Its Image spans the screen and has raycastTarget enabled, blocking clicks on the shop.
        // - Opening the menu does not pause Interactable; HandleClick bypasses that blocking Image.
        if (ui.escapeUIManager != null && ui.escapeUIManager.menuPanel.activeInHierarchy) return false;

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
