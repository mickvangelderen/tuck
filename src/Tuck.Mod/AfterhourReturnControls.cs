using UnityEngine;

namespace Tuck.Mod;

/// <summary>Bell-style hold and release to return from any night activity.</summary>
[DefaultExecutionOrder(-100)]
internal sealed class AfterhourReturnControls : MonoBehaviour
{
    private MapUIManager _map = null!;
    private Hotkey _returnHotkey = null!;

    public static AfterhourReturnControls Install(MapUIManager map, Hotkey returnHotkey)
    {
        var controls = map.gameObject.AddComponent<AfterhourReturnControls>();
        controls._map = map;
        controls._returnHotkey = returnHotkey;
        return controls;
    }

    public void Uninstall()
    {
        enabled = false;
        Destroy(this);
    }

    private bool InputAllowed() => _map.Current != null && !_map.IsInTransit
        && !StoreUIManager.IsAnyTyping()
        && !KeyBindHelper.SuppressHotkeys
        && !Interactable.isPaused
        && !ItemPinHelper.BlocksClicks
        // R also resets item orientation; never start or finish a return while dragging.
        && !(ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem)
        && !(DialogUIManager.Instance != null && DialogUIManager.Instance.currentDialog != null)
        && !(DialoguePhoneUIManager.Instance != null && DialoguePhoneUIManager.Instance.currentDialog != null)
        && !(GameOptionUIManager.Instance != null && GameOptionUIManager.Instance.IsOpen)
        && !(GenericConfirmationUIManager.Instance != null
            && GenericConfirmationUIManager.Instance.panel.activeInHierarchy);

    private void Update()
    {
        // Preserve the game's leave warnings and the mod's scavenging value threshold.
        if (_returnHotkey.Poll(canExecute: InputAllowed())) _map.Return();
    }

    private void OnDisable() => _returnHotkey?.Cancel();
}
