using System;
using HarmonyLib;

namespace Tuck.Mod;

internal static class GamePatches
{
    // Keep Harmony attributes on methods only: the loader's PatchAll runs before language selection.
    internal static readonly Type[] Types =
    [
        typeof(AppraisalGuides), typeof(AppraisalSteps), typeof(BackpackReturn), typeof(BackpackSelection),
        typeof(BellHold), typeof(ChemFinisherBatch), typeof(DocumentDiscard), typeof(DragAmounts),
        typeof(FilterReplacement), typeof(HotkeyTooltips), typeof(ItemWarningPreset), typeof(LabelMemory),
        typeof(MagnifierPosition), typeof(NightActivitySelection), typeof(PanelButtons), typeof(ScavengingReturn),
        typeof(VanillaHoldRing), typeof(VanillaHoldTimeouts), typeof(WorkspaceWarning), typeof(YeastBioreactor)
    ];

    internal static void Install(Harmony harmony)
    {
        foreach (var type in Types) harmony.CreateClassProcessor(type).Patch();
    }
}
