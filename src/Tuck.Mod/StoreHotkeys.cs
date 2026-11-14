using HarmonyLib;
using UnityEngine;

namespace Tuck.Mod;

/// <summary>Runs the normal shop click actions from rebindable keys.</summary>
// Check before StoreUIManager can dismiss a dialog or options with the same key press.
[HarmonyPatch(typeof(StoreUIManager), "Update")]
internal static class StoreHotkeys
{
    private static Options? _options;

    public static void Register(Options options) => _options = options;

    public static void Unregister() => _options = null;

    private static void Prefix(StoreUIManager __instance)
    {
        var options = _options;
        if (options == null) return;
        bool shutters = Pressed(options.Shutters.Key);
        bool nightActivities = Pressed(options.NightActivities.Key);
        bool sleep = Pressed(options.Sleep.Key);
        bool tablet = Pressed(options.Tablet.Key);
        if (!shutters && !nightActivities && !sleep && !tablet) return;

        // Evaluate context before any action can open a dialog or change location.
        if (!CanUseShopHotkeys(__instance)) return;
        if (shutters) TryClick(StoreShutterButton.Instance);
        if (nightActivities) TryClick(Object.FindObjectOfType<StoreDoor>());
        if (sleep) TryClick(Object.FindObjectOfType<StoreBedroll>());
        if (tablet) TryClick(Object.FindObjectOfType<StoreTablet>());
    }

    private static bool Pressed(KeyCode key) => key != KeyCode.None && Input.GetKeyDown(key);

    private static bool CanUseShopHotkeys(StoreUIManager ui)
    {
        var store = PlayerStore.Instance;
        var map = MapUIManager.Instance;

        bool shopNotLoaded = store == null; // The shop state must exist; otherwise reading its location or invoking shop actions can fail.
        if (shopNotLoaded) return false;

        // Null checks below treat absent optional managers and handlers as inactive.
        bool playerAwayFromShop = store.playerLocation != PlayerStore.PlayerLocation.IN_STORE; // Shop objects can remain loaded elsewhere; without this, their shortcuts could act on the shop remotely.
        bool cinematicInProgress = store.cinematicMode; // A scripted sequence controls the scene; shop actions could otherwise open panels or change the day during it.
        bool travelScreenOpen = TransitUI.IsOpen; // The travel screen can be open before travel starts; otherwise shop shortcuts remain usable behind destination selection.
        bool nightLocationActive = map != null && map.IsOutside; // Also honours the map's active-location state; without this, excluding night activities relies solely on playerLocation matching it.
        bool travelInProgress = map != null && map.IsInTransit; // Travel changes the active location and inventories; otherwise a shop action could run during that transition.
        if (playerAwayFromShop || cinematicInProgress || travelScreenOpen || nightLocationActive || travelInProgress) return false;

        bool typingInTextField = StoreUIManager.IsAnyTyping(); // Letters belong to the focused field or console; without this, typing E/T could also sleep or toggle the tablet.
        bool capturingKeyBinding = KeyBindHelper.SuppressHotkeys; // Covers key capture and its completion frame; otherwise the key chosen for a binding could immediately execute an action.
        bool pinModeBlocksClicks = ItemPinHelper.BlocksClicks; // Reserves pin mode and its exit frame for pinning; otherwise shop actions can run during that interaction.
        bool paperEditorOpen = PaperEditorWindow.IsOpen; // Keeps shop actions out of paper editing even when no text field is focused; otherwise shortcuts could operate the shop behind the editor.
        bool draggingItem = ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem; // Leaving or changing the shop can move inventories mid-drag; this shared guard also blocks the tablet while dragging.
        bool dialogueChoicePending = DialogUIManager.Instance != null && DialogUIManager.Instance.IsChoice(); // A dialogue decision owns the interaction; otherwise a shop shortcut could advance or leave the shop before it is answered.
        bool phoneConversationActive = DialoguePhoneUIManager.Instance != null && DialoguePhoneUIManager.Instance.currentDialog != null; // Keeps shop actions out of a phone conversation; otherwise they can open panels or change the scene behind the dialogue.
        if (typingInTextField || capturingKeyBinding || pinModeBlocksClicks || paperEditorOpen || draggingItem || dialogueChoicePending || phoneConversationActive) return false;

        bool wildNetworkOpen = WildNetworkUI.IsOpen; // Explicitly requested policy: block shop shortcuts in Wild Network; its rear-layer window does not prevent those actions itself.
        bool runSummaryOpen = RunSummaryUI.IsOpen; // The run has ended; otherwise surviving shop objects could still be activated behind the results screen.
        bool shopMouseBlockerActive = ui.backgroundBlocking.activeInHierarchy; // Mirrors the mouse blocker used by the end-of-day screen; direct method calls would otherwise activate the covered shop.
        bool optionsMenuOpen = GameOptionUIManager.Instance != null && GameOptionUIManager.Instance.IsOpen; // Settings own the interaction even outside key capture; otherwise shop actions could run while the player is configuring the game.
        bool escapeMenuOpen = ui.escapeUIManager != null && ui.escapeUIManager.menuPanel.activeInHierarchy; // The menu covers gameplay; direct method calls would otherwise operate shop objects underneath it.
        bool genericConfirmationPending = GenericConfirmationUIManager.Instance != null && GenericConfirmationUIManager.Instance.panel.activeInHierarchy; // Waits for the current confirm/cancel decision; otherwise a shop action could change its context before it is answered.
        bool shutterConfirmationPending = ui.closeShutterConfirmationPanel.activeInHierarchy; // The decision to end the day is pending; otherwise shortcuts could reopen that prompt or start another shop action behind it.
        bool departureConfirmationPending = map != null && map.confirmationPanel.activeInHierarchy; // The leaving-shop warning is awaiting a decision; otherwise shop actions could run behind that unresolved prompt.
        return !(wildNetworkOpen || runSummaryOpen || shopMouseBlockerActive || optionsMenuOpen || escapeMenuOpen
            || genericConfirmationPending || shutterConfirmationPending || departureConfirmationPending);
    }

    private static void TryClick(MonoBehaviour target)
    {
        if (target == null) return;
        var interactable = target.GetComponent<Interactable>();
        if (interactable != null && interactable.isActiveAndEnabled)
            interactable.HandleClick();
    }
}
