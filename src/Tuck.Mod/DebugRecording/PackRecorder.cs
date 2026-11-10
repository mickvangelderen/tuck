using System;
using global::Tuck.PackSolver;
using Tuck.Mod.Packing;

namespace Tuck.Mod.DebugRecording;

internal static class PackRecorder
{
    private static string? _modId;
    internal static void Register(string modId)
    { _modId = modId; Log.Info("Debug packing cases: ModData/" + modId + "/PackCases."); }
    internal static void Unregister() => _modId = null;
    internal static void Record(CapturedPack captured, PlanningOptions options, PlanningResult? result = null,
        string? executionStatus = null, int completedSteps = 0, string? detail = null)
    {
        if (_modId == null) return;
        try
        {
            ModHelper.WriteModDataFileText(_modId, "PackCases/" + captured.State.StateId + ".json",
                PackJson.Serialize(captured.State, options, result?.Plan, result?.Status.ToString(), executionStatus,
                    completedSteps, detail, captured.Observations));
        }
        catch (Exception error) { Log.Error("Packing recording failed: " + error); }
    }
}
