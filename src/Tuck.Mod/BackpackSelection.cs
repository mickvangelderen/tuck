using HarmonyLib;

namespace Tuck.Mod;

/// <summary>Optionally centers the backpack chooser opened by either the door or the hotkey.</summary>
[HarmonyPatch(typeof(EmporiumEntry), nameof(EmporiumEntry.ShowResponseWindow))]
internal static class BackpackSelection
{
    private static Options? _options;

    public static void Register(Options options) => _options = options;

    public static void Unregister() => _options = null;

    private static void Postfix(EmporiumEntry __instance, StoreResponse response)
    {
        if (_options?.CenterBackpackSelection.Value != true || response.identifier != "afterhours_response") return;

        // ShowResponseWindow has already shown and sized the window. Its next Show resets the position,
        // so centering this chooser does not move the other menus that reuse the same response window.
        var window = response.closable ? __instance.responseWindowClosable : __instance.responseWindow;
        window.Center();
    }
}
