using HarmonyLib;

namespace Tuck.Mod;

/// <summary>Skips the scavenging leave warning when the ground's total value is below the configured minimum.</summary>
internal static class ScavengingReturn
{
    private static Options? _options;

    public static void Register(Options options) => _options = options;

    public static void Unregister() => _options = null;

    [HarmonyPrefix, HarmonyPatch(typeof(MapUIManager), nameof(MapUIManager.Return))]
    private static bool Prefix(MapUIManager __instance)
    {
        if (_options == null || __instance.Current is not DumpingGroundLocation
            || RequiresConfirmation(EmporiumEntry.Instance.afterhourInventory, _options.ScavengingLeaveWarningMinimumValue.Value))
        {
            return true;
        }

        // This is the warning's confirm action, including the game's transit guard, leave hooks and item handling.
        __instance.ReturnNow();
        return false;
    }

    // The game sums whole stacks and nested contents; equipped backpack/hand slots are separate inventories.
    internal static bool RequiresConfirmation(GameInventory ground, float minimumValue) =>
        minimumValue < 1000f && (minimumValue <= 0f || ground.GetTotalValue() >= minimumValue);
}
