namespace Tuck.Mod;

/// <summary>Current timing read from a pending hold; the source still owns its lifecycle.</summary>
internal readonly struct PendingHold
{
    internal readonly float Started;
    internal readonly float RequiredSeconds;

    internal PendingHold(float started, float requiredSeconds)
    {
        Started = started;
        RequiredSeconds = requiredSeconds;
    }

    // Equal start times keep the first source, avoiding changes between tied holds.
    internal static PendingHold? Latest(PendingHold? first, PendingHold? second) =>
        second.HasValue && (!first.HasValue || second.Value.Started > first.Value.Started) ? second : first;
}
