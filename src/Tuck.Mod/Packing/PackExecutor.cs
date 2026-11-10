using System;
using System.Linq;
using global::Tuck.PackSolver;

namespace Tuck.Mod.Packing;

internal enum ExecutionStatus { Completed, StateMismatch, TransferRefused }

internal sealed class ExecutionResult
{
    internal ExecutionStatus Status { get; }
    internal int CompletedSteps { get; }
    internal string? Detail { get; }
    internal ExecutionResult(ExecutionStatus status, int completedSteps, string? detail = null)
    { Status = status; CompletedSteps = completedSteps; Detail = detail; }
}

/// <summary>Executes specified operations only. No layout search, temporary-move selection or rollback planning.</summary>
internal static class PackExecutor
{
    internal static ExecutionResult Execute(CapturedPack captured, PackPlan plan)
    {
        // Reject an invalid plan in full before touching the game.
        PlanReplay.Replay(captured.State, plan);
        var expected = captured.State;
        var bindings = captured.Bindings;
        for (int index = 0; index < plan.Steps.Count; index++)
        {
            string? difference = Difference(bindings, expected);
            if (difference != null) return new(ExecutionStatus.StateMismatch, index, difference);
            var step = plan.Steps[index];
            var item = bindings.Items[step.SourceItemId];
            var location = step.Destination;
            int quantity = expected.Items.Single(i => i.Id == step.SourceItemId).UnitCount;
            var destination = bindings.Inventories[location.InventoryId];
            var next = PlanReplay.Replay(expected, new PackPlan(plan.InitialStateId, new[] { step }));
            var intended = new StateView(next).Placed(step.SourceItemId);
            if (!item.MayRemove() || destination.IsInsertLocked())
                return new(ExecutionStatus.TransferRefused, index, "Game refused the specified transfer.");
            var position = intended.Transform;
            var shape = new GridShapeBuilder(item.modifiedShape).SetTransform(position.X, position.Y, position.Flipped, position.Orientation);
            var slot = destination.TryInventorySlot(item, quantity, shape);
            if (slot == null || slot.numTransfer < quantity || slot.inventory != destination || slot.item != item
                || slot.targetItem != null || !PackCapture.Transform(slot.itemGridShape).Equals(intended.Transform))
                return new(ExecutionStatus.TransferRefused, index, "Game did not validate the exact destination/quantity.");
            // A live probe can invoke callbacks. Check again before mutation.
            difference = Difference(bindings, expected);
            if (difference != null) return new(ExecutionStatus.StateMismatch, index, difference);
            int transferred = slot.TryAcceptOnce(quantity);
            if (transferred != quantity)
                return new(ExecutionStatus.StateMismatch, index, $"Step {index} transferred {transferred}/{quantity} units; it may have partially applied.");
            difference = Difference(bindings, next);
            if (difference != null) return new(ExecutionStatus.StateMismatch, index, $"Step {index} applied but its result differs: {difference}");
            expected = next;
        }
        var finalDifference = Difference(bindings, expected);
        return finalDifference == null ? new(ExecutionStatus.Completed, plan.Steps.Count)
            : new(ExecutionStatus.StateMismatch, plan.Steps.Count, finalDifference);
    }
    private static string? Difference(LiveBindings bindings, PackState expected)
    {
        // Known capture discrepancies become an execution result; arbitrary game/input exceptions propagate.
        try { return StateComparison.Difference(expected, PackCapture.Read(bindings, expected.StateId)); }
        catch (InvalidOperationException error) { return error.Message; }
        catch (NotSupportedException error) { return error.Message; }
    }
}
