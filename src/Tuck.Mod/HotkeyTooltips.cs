using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Tuck.Mod;

/// <summary>Decorates freshly generated tooltips, so bindings and translations stay live.</summary>
[HarmonyPatch]
internal static class HotkeyTooltips
{
    private static Options? _options;
    internal static Options? ActiveOptions => _options?.ShowHotkeysInTooltips.Value == true ? _options : null;

    public static void Register(Options options)
    {
        _options = options;
        options.ShowHotkeysInTooltips.OnChanged += RefreshButtons;
        RefreshButtons(options.ShowHotkeysInTooltips.Value);
    }

    public static void Unregister()
    {
        if (_options != null) _options.ShowHotkeysInTooltips.OnChanged -= RefreshButtons;
        _options = null;
        RefreshButtons(false);
    }

    internal static string AppendHint(Options options, string text, KeyCode key, bool hold = false)
    {
        if (!options.ShowHotkeysInTooltips.Value || key == KeyCode.None) return text;
        string hint = hold ? options.Text("hold_and_release_key", KeyBindHelper.KeyName(key))
            : $"({KeyBindHelper.KeyName(key)})";
        return options.Text("text_with_key_hint", text, hint);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(StoreShutterButton), nameof(StoreShutterButton.GetTooltip))]
    internal static void ShutterHint(ref string __result)
    {
        if (ActiveOptions is { } options) __result = AppendHint(options, __result, options.Shutters.Key);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(StoreDoor), nameof(StoreDoor.GetTooltip))]
    internal static void DoorHint(ref string __result)
    {
        if (ActiveOptions is { } options) __result = AppendHint(options, __result, options.NightActivities.Key);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(StoreBell), nameof(StoreBell.GetTooltip))]
    private static void BellHint(StoreBell __instance, ref string __result)
    {
        if (ActiveOptions is not { } options) return;
        __result = AppendHint(options, __result, KeyBindHelper.Get(KeyBindHelper.CALL_NEXT_CUSTOMER),
            __instance.ShouldRequireHold() && options.BellHoldDuration.Value > 0f);
    }

    internal enum ButtonAction { None, Scavenge, Return, Options, ControlHints }

    // Match callbacks, never localized captions or scene object names.
    internal static ButtonAction IdentifyAction(Type type, string method) => (type, method) switch
    {
        (_, nameof(MapUIManager.ScavengeButtonClick)) when type == typeof(MapUIManager) => ButtonAction.Scavenge,
        (_, "OnScavenge") when type == typeof(DumpingGroundLocation) => ButtonAction.Scavenge,
        (_, nameof(MapUIManager.Return) or nameof(MapUIManager.LeaveScavenging)
            or nameof(MapUIManager.LeaveCommissary) or nameof(MapUIManager.LeaveUpgradeMerchant))
            when type == typeof(MapUIManager) => ButtonAction.Return,
        (_, nameof(GameOptionUIManager.OpenUI)) when type == typeof(GameOptionUIManager) => ButtonAction.Options,
        (_, nameof(StoreUIManager.ToggleControlHintOverlay)) when type == typeof(StoreUIManager) => ButtonAction.ControlHints,
        _ => ButtonAction.None
    };

    internal static string ButtonHint(Options options, ButtonAction action) => action switch
    {
        ButtonAction.Scavenge => options.Scavenge.Key == KeyCode.None ? "" :
            AppendHint(options, options.Text("scavenge"), options.Scavenge.Key),
        ButtonAction.Return => options.Return.Key == KeyCode.None ? "" :
            AppendHint(options, options.Text("return_from_night_activity"), options.Return.Key,
                options.ReturnHoldDuration.Value > 0f),
        ButtonAction.Options => VanillaButtonHint(options, KeyBindHelper.OPEN_OPTIONS),
        ButtonAction.ControlHints => VanillaButtonHint(options, KeyBindHelper.CONTROL_HINTS),
        _ => ""
    };

    internal static string VanillaButtonHint(Options options, string id)
    {
        KeyCode key = KeyBindHelper.Get(id);
        return key == KeyCode.None ? "" : AppendHint(options, KeyBindHelper.Find(id).label(), key);
    }

    [HarmonyPrefix, HarmonyPatch(typeof(TooltipManager), nameof(TooltipManager.SetAndShowTooltip),
        new[] { typeof(string), typeof(Component) })]
    private static void ExistingButtonTooltip(ref string message, Component owner)
    {
        if (ActiveOptions is not { } options || owner is not TooltipContainer container) return;
        var hint = container.GetComponent<HotkeyButtonTooltip>();
        if (hint == null || !hint.isActiveAndEnabled || !hint.UsesLegacyTooltip) return;
        string shortcut = ButtonHint(options, hint.Action);
        if (shortcut.Length > 0) message += "\n" + shortcut;
    }

    private static void RefreshButtons(bool enabled)
    {
        if (!enabled)
        {
            foreach (var hint in UnityEngine.Object.FindObjectsOfType<HotkeyButtonTooltip>(true)) hint.Uninstall();
            return;
        }
        foreach (var button in UnityEngine.Object.FindObjectsOfType<Button>(true)) DiscoverButton(button);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(Selectable), "OnEnable")]
    private static void ButtonEnabled(Selectable __instance)
    {
        if (ActiveOptions != null && __instance is Button button) DiscoverButton(button);
    }

    private static void DiscoverButton(Button button)
    {
        if (ActiveOptions == null) return;
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
        {
            var target = button.onClick.GetPersistentTarget(i);
            if (target == null) continue;
            var action = IdentifyAction(target.GetType(), button.onClick.GetPersistentMethodName(i));
            if (action != ButtonAction.None) HotkeyButtonTooltip.Install(button.gameObject, action);
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(CustomUIBuilder), nameof(CustomUIBuilder.AddButton))]
    private static void CustomButtonAdded(CustomUIBuilder __instance, Action onClick)
    {
        if (ActiveOptions == null || onClick?.Target == null || __instance.Last?.root == null) return;
        var action = IdentifyAction(onClick.Target.GetType(), onClick.Method.Name);
        if (action != ButtonAction.None) HotkeyButtonTooltip.Install(__instance.Last.root, action);
    }
}
