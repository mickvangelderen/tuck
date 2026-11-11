using System;
using System.Threading;
using global::Tuck.PackSolver;

namespace Tuck.Mod.Packing;

internal sealed partial class PackEngine
{
    private readonly Options _options;
    private bool _running;
    internal PackEngine(Options options) => _options = options;
    internal IPackSolver Solver => _options.PackingSolver.Value;
    internal PackResult Pack(GameInventory ground, GameSlotInventory backpack, GameSlotInventory left, GameSlotInventory right)
    {
        if (_running) return new PackResult(null, null);
        _running = true;
        try
        {
            var captured = PackCapture.Capture(ground, backpack, left, right,
                () => MapUIManager.Instance != null && MapUIManager.Instance.Current != null && MapUIManager.Instance.Current.AllowsHazardousWaste);
            var options = new PlanningOptions();
            RecordState(captured, options);
            PlanningResult planned;
            using (var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(250)))
                planned = Solver.Solve(new PackProblem(captured.State), options, budget.Token);
            RecordPlan(captured, planned, options);
            if (planned.Status == PlanningStatus.Unsupported) return new PackResult(planned, null);
            var executed = PackExecutor.Execute(captured, planned.Plan);
            RecordExecution(captured, planned, options, executed);
            return new PackResult(planned, executed);
        }
        finally { _running = false; }
    }
    partial void RecordState(CapturedPack captured, PlanningOptions options);
    partial void RecordPlan(CapturedPack captured, PlanningResult result, PlanningOptions options);
    partial void RecordExecution(CapturedPack captured, PlanningResult result, PlanningOptions options, ExecutionResult execution);
}

internal sealed class PackResult
{
    internal PlanningResult? Planning { get; }
    internal ExecutionResult? Execution { get; }
    internal PackResult(PlanningResult? planning, ExecutionResult? execution) { Planning = planning; Execution = execution; }
}
