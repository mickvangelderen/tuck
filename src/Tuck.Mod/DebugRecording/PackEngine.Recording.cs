using global::Tuck.PackSolver;
using Tuck.Mod.DebugRecording;

namespace Tuck.Mod.Packing;

internal sealed partial class PackEngine
{
    partial void RecordState(CapturedPack captured, PlanningOptions options) => PackRecorder.Record(captured, options);
    partial void RecordPlan(CapturedPack captured, PlanningResult result, PlanningOptions options) => PackRecorder.Record(captured, options, result);
    partial void RecordExecution(CapturedPack captured, PlanningResult result, PlanningOptions options, ExecutionResult execution) =>
        PackRecorder.Record(captured, options, result, execution.Status.ToString(), execution.CompletedSteps, execution.Detail);
}
