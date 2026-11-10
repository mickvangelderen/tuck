using System.Collections.Generic;
using System.Linq;
using Tuck.Mod.Packing;

namespace Tuck.Mod.DebugRecording;

internal static class GameObservations
{
    internal static IReadOnlyList<ItemObservation> Capture(LiveBindings bindings) => bindings.Items.Select(pair =>
    {
        var item = pair.Value;
        return new ItemObservation(pair.Key, PackCapture.Shape(item.modifiedShape), PackCapture.Transform(item.modifiedShape),
            item.modifiedXOrigin, item.modifiedYOrigin, PackCapture.Facts(item.modifiedState, item), ObservedValue(item));
    }).ToArray();

    private static long? ObservedValue(GameItem item)
    {
        // GetTagReadonly inserts missing tags. Skip that observation rather than mutate the game or
        // duplicate the formula under test. All other observations remain available.
        bool Has(params string[] names) => names.All(item.modifiedState.dict.ContainsKey);
        if (item.IsTag("power_source_item") && !Has("power_source_item_energy")
            || item.IsTag("amenities") && !Has("amenities_use")
            || item.IsTag("armor_item") && !Has("current_armor_value", "current_max_armor_value", "armor_efficiency")
            || item.IsTag("LIQUID_CONTAINER_TAG") && !Has("LIQUID_CONTAINER_CURRENT")
            || item.IsTag("ORGAN_ITEM_TAG") && !Has("ORGAN_HEALTH_INT", "ORGAN_HEALTH_MAX_INT")) return null;
        return item.GetValue(singleValue: true);
    }
}
