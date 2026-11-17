using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Tuck.Mod;

/// <summary>Tracks native holders so the presenter can read their actual state after input processing.</summary>
internal static class VanillaHoldRing
{
    private static readonly List<Component> Holders = new();

    internal static void Clear() => Holders.Clear();

    internal static PendingHold? ReadLatest()
    {
        PendingHold? latest = null;
        for (int i = 0; i < Holders.Count; i++)
        {
            var holder = Holders[i];
            // A scene can destroy a native holder without a final input update.
            if (holder == null)
            {
                Holders.RemoveAt(i--);
                continue;
            }
            var hold = holder switch
            {
                StoreBell bell => ReadBell(bell),
                Interactable interactable => ReadPointer(interactable),
                SelectableElementHandler handler => ReadSelection(handler),
                _ => null
            };
            latest = PendingHold.Latest(latest, hold);
        }
        return latest;
    }

    private static void Track(Component holder)
    {
        if (HoldRingPresenter.IsEnabled && !Holders.Contains(holder))
            Holders.Add(holder);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(StoreBell), "HandleHotkey")]
    private static void BellUpdated(StoreBell __instance, bool ___keyHoldActive)
    {
        if (___keyHoldActive) Track(__instance);
    }

    private static PendingHold? ReadBell(StoreBell bell) => ModReflect.Get<bool>(bell, "keyHoldActive")
        ? new PendingHold(ModReflect.Get<float>(bell, "keyHoldStartTime"), bell.HoldDuration)
        : null;

    [HarmonyPostfix, HarmonyPatch(typeof(StoreBell), "CancelKeyHold")]
    private static void BellCancelled(StoreBell __instance) => Holders.Remove(__instance);

    [HarmonyPostfix, HarmonyPatch(typeof(Interactable), "TryBeginHold")]
    private static void PointerStarted(Interactable __instance, bool __result)
    {
        if (__result) Track(__instance);
    }

    private static PendingHold? ReadPointer(Interactable interactable) => ModReflect.Get<bool>(interactable, "holdActive")
        ? new PendingHold(ModReflect.Get<float>(interactable, "holdStartTime"), ModReflect.Get<float>(interactable, "holdDuration"))
        : null;

    [HarmonyPostfix, HarmonyPatch(typeof(Interactable), "CancelHold")]
    private static void PointerCancelled(Interactable __instance) => Holders.Remove(__instance);

    [HarmonyPostfix, HarmonyPatch(typeof(SelectableElementHandler), "BeginHoldIfNeeded")]
    private static void SelectionStarted(SelectableElementHandler __instance, bool ___holdActive)
    {
        if (___holdActive) Track(__instance);
    }

    private static PendingHold? ReadSelection(SelectableElementHandler handler)
    {
        if (!ModReflect.Get<bool>(handler, "holdActive")) return null;
        var element = ModReflect.Get<SelectableElement>(handler, "currentPressElement");
        return element != null ? new PendingHold(ModReflect.Get<float>(handler, "holdStartTime"), element.holdDuration) : null;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(SelectableElementHandler), "CancelHold")]
    private static void SelectionCancelled(SelectableElementHandler __instance) => Holders.Remove(__instance);
}
