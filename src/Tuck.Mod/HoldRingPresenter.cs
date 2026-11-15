using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Tuck.Mod;

/// <summary>Applies the final ring display after native and mod input handlers have updated their holds.</summary>
[HarmonyPatch]
internal sealed class HoldRingPresenter : MonoBehaviour
{
    private static IReadOnlyList<Hotkey>? _hotkeys;
    private static HoldRingPresenter? _instance;
    internal static bool IsEnabled => _hotkeys != null;

    public static void Register(IReadOnlyList<Hotkey> hotkeys)
    {
        _hotkeys = hotkeys;
        if (CustomUIManager.Instance != null) Attach(CustomUIManager.Instance);
    }

    public static void Unregister()
    {
        _hotkeys = null;
        VanillaHoldRing.Clear();
        if (_instance != null)
        {
            _instance.enabled = false;
            Destroy(_instance);
            _instance = null;
        }
        CustomUIManager.Instance?.HideHoldRing();
    }

    [HarmonyPostfix, HarmonyPatch(typeof(CustomUIManager), "Awake")]
    private static void Attach(CustomUIManager __instance)
    {
        // The native UI manager survives scene changes and rejects duplicate instances in Awake.
        if (IsEnabled && __instance == CustomUIManager.Instance && _instance == null)
            _instance = __instance.gameObject.AddComponent<HoldRingPresenter>();
    }

    internal static float? GetProgress(IReadOnlyList<Hotkey> hotkeys, PendingHold? vanillaHold, float now)
    {
        var latest = vanillaHold;
        foreach (var hotkey in hotkeys)
            latest = PendingHold.Latest(latest, hotkey.ReadHold());

        if (!latest.HasValue) return null;
        var selected = latest.Value;
        return selected.RequiredSeconds > 0f ? Mathf.Clamp01((now - selected.Started) / selected.RequiredSeconds) : 1f;
    }

    private void LateUpdate()
    {
        var ui = CustomUIManager.Instance;
        if (ui == null || _hotkeys == null) return;
        var progress = GetProgress(_hotkeys, VanillaHoldRing.ReadLatest(), Time.unscaledTime);
        if (progress.HasValue) ui.ShowHoldRing(progress.Value);
        else ui.HideHoldRing();
    }
}
