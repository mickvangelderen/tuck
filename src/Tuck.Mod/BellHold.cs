using HarmonyLib;

namespace Tuck.Mod;

/// <summary>Configures the bell's existing hold behavior for both pointer and keyboard input.</summary>
[HarmonyPatch(typeof(StoreBell))]
internal static class BellHold
{
    private static Options? _options;

    public static void Register(Options options) => _options = options;

    public static void Unregister() => _options = null;

    [HarmonyPatch(nameof(StoreBell.HoldDuration), MethodType.Getter)]
    [HarmonyPostfix]
    internal static void DurationPostfix(ref float __result)
    {
        if (_options != null) __result = _options.BellHoldDuration.Value;
    }

    [HarmonyPatch(nameof(StoreBell.ShouldRequireHold))]
    [HarmonyPostfix]
    internal static void RequireHoldPostfix(ref bool __result)
    {
        // Preserve vanilla's instant mode and no-customer behavior. For zero duration, skip the hold entirely:
        // Interactable substitutes a 1.25 s hold when ShouldRequireHold is true but HoldDuration is zero.
        if (_options != null && _options.BellHoldDuration.Value <= 0f) __result = false;
    }
}
