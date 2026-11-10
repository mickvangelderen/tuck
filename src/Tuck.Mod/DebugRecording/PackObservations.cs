using System;
using System.Collections.Generic;
using System.Linq;
using global::Tuck.PackSolver;

namespace Tuck.Mod.DebugRecording;

/// <summary>Debug evidence only. Neither PackProblem nor replay accepts observations.</summary>
internal sealed class ItemObservation
{
    internal string ItemId { get; }
    internal ShapeState Shape { get; }
    internal Placement.Grid Transform { get; }
    internal int OriginX { get; }
    internal int OriginY { get; }
    internal ItemFacts State { get; }
    internal long? UnitValue { get; }
    internal ItemObservation(string itemId, ShapeState shape, Placement.Grid transform, int originX, int originY, ItemFacts state, long? unitValue)
    { ItemId = itemId; Shape = shape; Transform = transform; OriginX = originX; OriginY = originY; State = state; UnitValue = unitValue; }
    internal static IEnumerable<string> Differences(PackState state, IEnumerable<ItemObservation> observations)
    {
        var view = new StateView(state);
        foreach (var observation in observations)
        {
            var effective = view.Effective(observation.ItemId);
            // Slot translations are presentation coordinates; only a grid stores a spatial anchor.
            bool grid = state.Items.Single(i => i.Id == observation.ItemId).Location.Placement is Placement.Grid;
            var transform = view.Placed(observation.ItemId).Transform;
            bool sameTransform = grid ? transform.Equals(observation.Transform)
                : transform.Orientation == observation.Transform.Orientation && transform.Flipped == observation.Transform.Flipped;
            if (!StateComparison.SameShape(effective.Shape, observation.Shape) || !sameTransform
                || effective.OriginX != observation.OriginX || effective.OriginY != observation.OriginY
                || !StateComparison.SameFacts(effective.State, observation.State)
                || observation.UnitValue.HasValue && effective.UnitValue != observation.UnitValue)
                yield return observation.ItemId + ": derived attributes differ from game observations.";
        }
    }
}
