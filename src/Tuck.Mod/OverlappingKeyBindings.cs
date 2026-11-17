using HarmonyLib;

namespace Tuck.Mod;

/// <summary>Optionally allows shared keys in the game's rebinding UI.</summary>
internal static class OverlappingKeyBindings
{
    private static Options? _options;

    public static void Register(Options options) => _options = options;

    public static void Unregister() => _options = null;

    [HarmonyPostfix, HarmonyPatch(typeof(GameOptionUIManager), "FindPendingKeyOwner")]
    internal static void Postfix(ref string? __result)
    {
        if (_options?.AllowOverlappingKeyBindings.Value == true) __result = null;
    }
}
