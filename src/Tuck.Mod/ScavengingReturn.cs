using HarmonyLib;

namespace Tuck.Mod;

/// <summary>Skips the scavenging leave warning when the ground's total value is below the configured minimum.</summary>
[HarmonyPatch(typeof(MapUIManager), nameof(MapUIManager.Return))]
internal static class ScavengingReturn
{
    // The game loader installs attributed patches before Init and owns their removal. Keep this one inert until
    // OnEnable completes registration, and make it inert again on OnDisable.
    private static Options? _options;

    public static void Register(Options options) => _options = options;

    public static void Unregister() => _options = null;

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
