using UnityEngine;

namespace Tuck.Mod;

/// <summary>Bell-style hold and release to return from any night activity.</summary>
[DefaultExecutionOrder(-100)]
internal sealed class AfterhourReturnControls : MonoBehaviour
{
    private MapUIManager _map = null!;
    private Options _options = null!;
    private bool _holding;
    private KeyCode _heldKey;
    private float _holdStarted;
    private float _holdDuration;

    public static AfterhourReturnControls Install(MapUIManager map, Options options)
    {
        var controls = map.gameObject.AddComponent<AfterhourReturnControls>();
        controls._map = map;
        controls._options = options;
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
        if (!InputAllowed() || (_holding && _heldKey != _options.Return.Key))
        {
            CancelHold();
            return;
        }

        if (Input.GetKeyDown(_options.Return.Key))
        {
            _holdDuration = _options.ReturnHoldDuration.Value;
            if (_holdDuration <= 0f)
            {
                CancelHold();
                _map.Return();
                return;
            }
            _holding = true;
            _heldKey = _options.Return.Key;
            _holdStarted = Time.unscaledTime;
            CustomUIManager.Instance?.ShowHoldRing(0f);
        }
        else if (_holding)
        {
            if (Input.GetKey(_heldKey))
            {
                CustomUIManager.Instance?.ShowHoldRing(Mathf.Clamp01((Time.unscaledTime - _holdStarted) / _holdDuration));
                return;
            }

            bool completed = Input.GetKeyUp(_heldKey) && Time.unscaledTime - _holdStarted >= _holdDuration;
            CancelHold();
            // Preserve the game's leave warnings and the mod's scavenging value threshold.
            if (completed) _map.Return();
        }
    }

    private void CancelHold()
    {
        if (!_holding) return;
        _holding = false;
        CustomUIManager.Instance?.HideHoldRing();
    }

    private void OnDisable() => CancelHold();

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) CancelHold();
    }
}
