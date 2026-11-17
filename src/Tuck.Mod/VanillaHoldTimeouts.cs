using HarmonyLib;
using UnityEngine;

namespace Tuck.Mod;

// Use each vanilla hold implementation's own cancellation to clear its state and presentation.
internal static class VanillaHoldTimeouts
{
    private static Options? _options;

    public static void Register(Options options) => _options = options;

    public static void Unregister() => _options = null;

    // The bell tracks keyboard holds separately from Interactable's pointer holds.
    // HandleHotkey both updates and releases the hold, so this also guards activation.
    [HarmonyPrefix, HarmonyPatch(typeof(StoreBell), "HandleHotkey")]
    private static void KeyboardHoldUpdated(StoreBell __instance, bool ___keyHoldActive, float ___keyHoldStartTime)
    {
        if (_options == null || !___keyHoldActive) return;
        float? timeout = HoldTimeout.GetSeconds(_options.HoldTimeoutDuration.Value);
        if (HoldTimeout.HasExpired(___keyHoldStartTime, __instance.HoldDuration, timeout, Time.unscaledTime))
            ModReflect.Call(__instance, "CancelKeyHold");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(Interactable), "UpdateHold")]
    private static void PointerHoldUpdated(Interactable __instance, bool ___holdActive, float ___holdStartTime, float ___holdDuration)
    {
        if (_options == null || !___holdActive) return;
        float? timeout = HoldTimeout.GetSeconds(_options.HoldTimeoutDuration.Value);
        if (HoldTimeout.HasExpired(___holdStartTime, ___holdDuration, timeout, Time.unscaledTime))
            ModReflect.Call(__instance, "CancelHold");
    }

    // Pointer-up can arrive before UpdateHold in the expiry frame.
    [HarmonyPrefix, HarmonyPatch(typeof(Interactable), nameof(Interactable.OnPointerUp))]
    private static void PointerReleased(Interactable __instance, bool ___holdActive, float ___holdStartTime, float ___holdDuration) =>
        PointerHoldUpdated(__instance, ___holdActive, ___holdStartTime, ___holdDuration);
}
