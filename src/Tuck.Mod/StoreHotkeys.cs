using HarmonyLib;
using UnityEngine;

namespace Tuck.Mod;

/// <summary>Runs the normal shutter and night-activity door actions from rebindable keys.</summary>
// Check before StoreUIManager can dismiss a dialog or options with the same key press.
[HarmonyPatch(typeof(StoreUIManager), "Update")]
internal static class StoreHotkeys
{
    private static Options? _options;

    public static void Register(Options options) => _options = options;

    public static void Unregister() => _options = null;

    private static void Prefix(StoreUIManager __instance)
    {
        if (_options == null) return;
        bool shutters = Input.GetKeyDown(_options.Shutters.Key);
        bool nightActivities = Input.GetKeyDown(_options.NightActivities.Key);
        if ((!shutters && !nightActivities)
            || StoreUIManager.IsAnyTyping() || KeyBindHelper.SuppressHotkeys || Interactable.isPaused
            || ItemPinHelper.BlocksClicks || PaperEditorWindow.IsOpen
            || (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem)
            || (GameOptionUIManager.Instance != null && GameOptionUIManager.Instance.IsOpen)
            || (DialogUIManager.Instance != null && DialogUIManager.Instance.IsChoice())
            || (DialoguePhoneUIManager.Instance != null && DialoguePhoneUIManager.Instance.currentDialog != null)
            || (GenericConfirmationUIManager.Instance != null && GenericConfirmationUIManager.Instance.panel.activeInHierarchy)
            || __instance.closeShutterConfirmationPanel.activeInHierarchy
            || TransitUI.IsOpen
            || (MapUIManager.Instance != null && (MapUIManager.Instance.IsOutside || MapUIManager.Instance.IsInTransit
                || MapUIManager.Instance.confirmationPanel.activeInHierarchy))
            || PlayerStore.Instance == null || PlayerStore.Instance.cinematicMode)
        {
            return;
        }

        if (shutters)
        {
            var button = StoreShutterButton.Instance;
            // Preserve tutorial hooks, opening/closing restrictions, animations and the close confirmation.
            if (CanClick(button)) button.OnClick();
        }
        else
        {
            // The door has no singleton. Look it up only on key press, so scene changes need no cached references.
            var door = Object.FindObjectOfType<StoreDoor>();
            // OnClick preserves night-access restrictions and the workspace warning before backpack selection.
            if (CanClick(door)) door.OnClick();
        }
    }

    private static bool CanClick(MonoBehaviour button)
    {
        if (button == null || !button.isActiveAndEnabled) return false;
        var interactable = button.GetComponent<Interactable>();
        return interactable != null && interactable.isActiveAndEnabled && interactable.isAvailable;
    }
}
