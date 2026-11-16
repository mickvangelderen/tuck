using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Tuck.Mod;

internal enum PanelAction { None, Accept, Decline }

/// <summary>Identifies existing panel buttons; input and holds remain in Hotkeys.</summary>
[HarmonyPatch]
internal static class PanelButtons
{
    internal static Options? ActiveOptions { get; private set; }

    internal static void Register(Options options)
    {
        ActiveOptions = options;
        foreach (var button in UnityEngine.Object.FindObjectsOfType<Button>(true)) Discover(button);
    }

    internal static void Unregister()
    {
        ActiveOptions = null;
        foreach (var hint in UnityEngine.Object.FindObjectsOfType<PanelActionButton>(true)) hint.Uninstall();
    }

    internal static PanelAction IdentifyAction(Type type, string method) => (type, method) switch
    {
        (_, nameof(NegociationUIManager.OnBuyAllClick) or nameof(NegociationUIManager.OnSellAllClick)
            or nameof(NegociationUIManager.OnWholesaleClick) or nameof(NegociationUIManager.OnOfferAcceptClick)
            or nameof(NegociationUIManager.OnConfirmPanelYes)) when type == typeof(NegociationUIManager) => PanelAction.Accept,
        (_, nameof(NegociationUIManager.OnConfirmPanelNo)) when type == typeof(NegociationUIManager) => PanelAction.Decline,
        (_, nameof(InspectionUIManager.OnAppraisalServiceEstimated) or nameof(InspectionUIManager.OnConfront))
            when type == typeof(InspectionUIManager) => PanelAction.Accept,
        (_, nameof(GenericConfirmationUIManager.ConfirmButtonPressed)) when type == typeof(GenericConfirmationUIManager) => PanelAction.Accept,
        (_, nameof(GenericConfirmationUIManager.CancelButtonPressed)) when type == typeof(GenericConfirmationUIManager) => PanelAction.Decline,
        (_, nameof(ConfirmPanelUIManager.OnYesClick)) when type == typeof(ConfirmPanelUIManager) => PanelAction.Accept,
        (_, nameof(ConfirmPanelUIManager.OnNoClick)) when type == typeof(ConfirmPanelUIManager) => PanelAction.Decline,
        _ => PanelAction.None
    };

    private static void Discover(Button button)
    {
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
        {
            var target = button.onClick.GetPersistentTarget(i);
            if (target != null) Bind(button, target.GetType(), button.onClick.GetPersistentMethodName(i));
        }
    }

    private static void Bind(Button button, Type type, string method)
    {
        var action = IdentifyAction(type, method);
        if (action == PanelAction.None) return;
        PanelActionButton.Install(button, action,
            buy: type == typeof(NegociationUIManager) && method == nameof(NegociationUIManager.OnBuyAllClick),
            wholesale: type == typeof(NegociationUIManager) && method == nameof(NegociationUIManager.OnWholesaleClick));
    }

    [HarmonyPostfix, HarmonyPatch(typeof(Selectable), "OnEnable")]
    private static void ButtonEnabled(Selectable __instance)
    {
        if (ActiveOptions != null && __instance is Button button) Discover(button);
    }

    // Negotiation buttons use runtime listeners, so identify their supplied callback at creation.
    [HarmonyPostfix, HarmonyPatch(typeof(TabletUI), "BuildButton")]
    private static void TabletButtonCreated(TabletButton __result, Action onClick)
    {
        if (ActiveOptions != null && onClick?.Target != null)
            Bind(__result.button, onClick.Target.GetType(), onClick.Method.Name);
    }

    internal static (Button? Accept, Button? Decline) FindTargets(StoreUIManager store)
    {
        var panel = FindPanel(store);
        return panel == null ? default : (FindButton(panel, PanelAction.Accept), FindButton(panel, PanelAction.Decline));
    }

    internal static bool CanClick(Button? button) =>
        button != null && button.isActiveAndEnabled && button.IsInteractable();

    internal static void Click(Button? button)
    {
        // Another action in this frame may have closed the panel.
        if (CanClick(button)) button!.onClick.Invoke();
    }

    internal static bool ShowHint(PanelActionButton hint)
    {
        // Keep captions independent of focus; only Buy/Wholesale need to transfer the hint.
        if (!hint.IsBuy && !hint.IsWholesale) return true;
        var negotiation = NegociationUIManager.Instance;
        return negotiation != null && negotiation.uiObject != null
            && FindButton(negotiation.uiObject, PanelAction.Accept) == hint.Button;
    }

    private static bool Visible(GameObject? panel) => panel != null && panel.activeInHierarchy;

    private static GameObject? FindPanel(StoreUIManager store)
    {
        var generic = GenericConfirmationUIManager.Instance;
        if (generic != null && Visible(generic.panel)) return generic.panel;
        var confirmation = store.confirmPanelUIManager;
        if (confirmation != null && Visible(confirmation.panel)) return confirmation.panel;
        // An unanswered prompt must not forward A/D to a window behind it, even if its buttons are unsupported.
        if (Visible(store.closeShutterConfirmationPanel)) return store.closeShutterConfirmationPanel;
        var map = MapUIManager.Instance;
        if (map != null && Visible(map.confirmationPanel)) return map.confirmationPanel;

        // Match vanilla's Escape targeting, including unrelated windows and docked paper panels.
        GameObject? focused = null;
        long focus = -1;
        if (store.paperUICanvasObject != null)
        {
            foreach (Transform child in store.paperUICanvasObject.transform) Consider(child.gameObject);
            foreach (var panel in UIDragHandler.GetActiveDockedPanels(store.paperUICanvasObject.transform)) Consider(panel);
        }
        if (WindowsHandler.current != null)
            foreach (var window in WindowsHandler.current.visibleWindows)
                if (window.canMove && window.focusStamp > focus) return null;

        // The budget confirmation belongs to the negotiation window, not to other foreground windows.
        var negotiation = NegociationUIManager.Instance;
        if (negotiation != null && focused == negotiation.uiObject && focused != null)
        {
            var frame = focused.GetComponent<NegotiationTabletFrame>();
            if (frame != null && Visible(frame.confirmRoot)) return frame.confirmRoot;
        }
        return focused;

        void Consider(GameObject panel)
        {
            if (!Visible(panel)) return;
            long stamp = PaperPanelFocusStamp.GetStamp(panel);
            if (stamp >= focus) { focus = stamp; focused = panel; }
        }
    }

    private static Button? FindButton(GameObject panel, PanelAction action) => PreferredTarget(
        panel.GetComponentsInChildren<PanelActionButton>().Where(hint => hint.Action == action)
            .Select(hint => (hint.Button, CanClick(hint.Button), hint.IsWholesale)));

    internal static Button? PreferredTarget(IEnumerable<(Button Button, bool Available, bool Wholesale)> candidates)
    {
        Button? wholesale = null;
        foreach (var candidate in candidates)
        {
            if (!candidate.Available) continue;
            if (!candidate.Wholesale) return candidate.Button;
            wholesale = candidate.Button;
        }
        return wholesale;
    }
}
