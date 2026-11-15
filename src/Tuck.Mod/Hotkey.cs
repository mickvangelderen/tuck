using UnityEngine;

namespace Tuck.Mod;

/// <summary>A configurable shortcut that fires on press, or on release after a timed hold.</summary>
internal sealed class Hotkey
{
    private readonly KeyBinding _binding;
    private readonly ScalarSetting? _requiredHoldDuration;
    private readonly ScalarSetting? _timeoutAfterReady;
    private float _holdStarted;

    /// <summary>
    /// A started hold awaiting release, including time before readiness. Cancellation clears this
    /// even while the key stays down, so its eventual release cannot execute the action.
    /// Keyboard state alone cannot remember that cancellation.
    /// </summary>
    private bool _holdPending;

    public Hotkey(KeyBinding binding, ScalarSetting? requiredHoldDuration = null, ScalarSetting? timeoutAfterReady = null)
    {
        _binding = binding;
        _requiredHoldDuration = requiredHoldDuration;
        _timeoutAfterReady = timeoutAfterReady;
    }

    /// <summary>Call once per frame. Returns true when the action should execute; unavailability cancels a pending hold.</summary>
    public bool Poll(bool canExecute = true)
    {
        var key = _binding.Key;
        return Poll(canExecute, Time.unscaledTime, Input.GetKeyDown(key), Input.GetKey(key), Input.GetKeyUp(key));
    }

    // Separate input sampling so press/hold/release sequences can be tested without a Unity runtime.
    internal bool Poll(bool canExecute, float now, bool pressed, bool held, bool released)
    {
        if (!canExecute || _binding.Key == KeyCode.None)
        {
            Cancel();
            return false;
        }
        if (!pressed && !_holdPending) return false;

        float requiredHoldSeconds = _requiredHoldDuration?.Value ?? 0f;
        if (pressed)
        {
            if (requiredHoldSeconds <= 0f)
            {
                Cancel();
                return true;
            }
            _holdStarted = now;
            _holdPending = true;
        }

        // Check before release as well as while held: expiry must win in either event order.
        float? timeout = HoldTimeout.GetSeconds(_timeoutAfterReady?.Value);
        if (HoldTimeout.HasExpired(_holdStarted, requiredHoldSeconds, timeout, now))
        {
            Cancel();
            return false;
        }
        if (released || !held)
        {
            bool completed = released && now - _holdStarted >= requiredHoldSeconds;
            Cancel();
            return completed;
        }

        return false;
    }

    internal PendingHold? ReadHold() => _holdPending
        ? new PendingHold(_holdStarted, _requiredHoldDuration?.Value ?? 0f)
        : null;

    public void Cancel() => _holdPending = false;
}
