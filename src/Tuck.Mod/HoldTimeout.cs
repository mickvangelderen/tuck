namespace Tuck.Mod;

internal static class HoldTimeout
{
    internal const float DisabledAt = 3f;

    internal static float? GetSeconds(float? settingValue) => settingValue >= DisabledAt ? null : settingValue;

    // The timeout starts when the hold becomes ready; null leaves it armed until release.
    internal static bool HasExpired(float started, float holdDuration, float? timeout, float now) =>
        timeout.HasValue && now >= started + holdDuration + timeout.Value;
}
