using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Tuck.Mod;

/// <summary>Remembers the inspection glass and brings its center on-screen when opening.</summary>
[HarmonyPatch]
internal static class MagnifierPosition
{
    private static Options? _options;
    private static GameObject? _lastGlass;
    private static Image? _glassImage;
    private static Image? _trayImage;
    private static Graphic? _raycastTarget;
    private static readonly Dictionary<CounterMoveChild, Vector3> PendingMoves = new();

    public static void Register(Options options)
    {
        _options = options;
        options.RememberMagnifierPosition.OnChanged += OnSettingChanged;
    }

    public static void Unregister()
    {
        if (_options != null) _options.RememberMagnifierPosition.OnChanged -= OnSettingChanged;
        _options = null;
        OnSettingChanged(false);
    }

    private static void OnSettingChanged(bool enabled)
    {
        _lastGlass = null;
        _glassImage = null;
        _trayImage = null;
        PendingMoves.Clear();
    }

    [HarmonyPrefix, HarmonyPatch(typeof(InspectionUIManager), nameof(InspectionUIManager.OpenUI))]
    internal static void BeforeOpen(InspectionUIManager __instance, out Vector3? __state)
    {
        __state = null;
        if (_options?.RememberMagnifierPosition.Value != true) return;
        // OpenUI resets all enlarged images, including those that were inactive.
        PendingMoves.Clear();
        if (_lastGlass == __instance.magnifyingGlass)
            __state = __instance.magnifyingGlass.transform.localPosition;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(InspectionUIManager), nameof(InspectionUIManager.OpenUI))]
    internal static void AfterOpen(InspectionUIManager __instance, Vector3? __state)
    {
        if (_options?.RememberMagnifierPosition.Value != true) return;
        _lastGlass = __instance.magnifyingGlass;
        _glassImage = _lastGlass.GetComponent<Image>();
        _trayImage = __instance.panel.GetComponent<Image>();
        var glass = (RectTransform)_lastGlass.transform;
        if (__state.HasValue) MoveGlass(glass, __state.Value);
        ClampOnOpen(glass);
    }

    [HarmonyPrefix, HarmonyPatch(typeof(Graphic), nameof(Graphic.Raycast))]
    internal static void BeforeRaycast(Graphic __instance, out Graphic? __state)
    {
        __state = _raycastTarget;
        if (_options?.RememberMagnifierPosition.Value == true) _raycastTarget = __instance;
    }

    [HarmonyFinalizer, HarmonyPatch(typeof(Graphic), nameof(Graphic.Raycast))]
    internal static void AfterRaycast(Graphic? __state) => _raycastTarget = __state;

    [HarmonyPrefix, HarmonyPatch(typeof(Image), nameof(Image.IsRaycastLocationValid))]
    internal static bool CheckImageRaycast(Image __instance, ref bool __result)
    {
        if (_options?.RememberMagnifierPosition.Value != true || _glassImage is null || _trayImage is null
            || !ReferenceEquals(_raycastTarget, _glassImage) || !ReferenceEquals(__instance, _trayImage)) return true;
        // Graphic.Raycast checks ancestor Images too; the tray's transparent border must not reject the glass.
        __result = true;
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(CounterMoveChild), "LateUpdate")]
    internal static void BeforeCounterMove(CounterMoveChild __instance, ref Vector3 ___lastParentPosition,
        float ___movementMultiplier)
    {
        if (_options?.RememberMagnifierPosition.Value != true || !PendingMoves.TryGetValue(__instance, out var move)) return;
        PendingMoves.Remove(__instance);
        var corrected = CorrectImage(__instance.transform.position,
            __instance.transform.parent.position, move, ___movementMultiplier);
        __instance.transform.position = corrected.Position;
        ___lastParentPosition = corrected.LastParentPosition;
    }

    // OpenUI resets the enlarged image. Do not let vanilla counter-move it a second time.
    internal static (Vector3 Position, Vector3 LastParentPosition) CorrectImage(Vector3 position,
        Vector3 parentPosition, Vector3 delta, float multiplier) => (position - delta * multiplier, parentPosition);

    private static void ClampOnOpen(RectTransform glass)
    {
        if (glass.parent is not RectTransform parent) return;
        var canvas = glass.GetComponentInParent<Canvas>()?.rootCanvas;
        if (canvas == null) return;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var worldCenter = glass.TransformPoint(glass.rect.center);
        var screenCenter = RectTransformUtility.WorldToScreenPoint(camera, worldCenter);
        var clamped = ClampCenter(screenCenter, Screen.width, Screen.height);
        if (clamped.x == screenCenter.x && clamped.y == screenCenter.y) return;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, clamped, camera, out var localCenter))
            MoveGlass(glass, glass.localPosition + (Vector3)localCenter - parent.InverseTransformPoint(worldCenter));
    }

    internal static Vector2 ClampCenter(Vector2 center, float screenWidth, float screenHeight) => new(
        Math.Clamp(center.x, 0, screenWidth), Math.Clamp(center.y, 0, screenHeight));

    private static void MoveGlass(RectTransform glass, Vector3 position)
    {
        var previous = glass.position;
        glass.localPosition = position;
        var delta = glass.position - previous;
        // Vanilla only counter-moves the enlarged image during dragging, not when restoring a position.
        foreach (var child in glass.GetComponentsInChildren<CounterMoveChild>(includeInactive: true))
        {
            PendingMoves.TryGetValue(child, out var pending);
            PendingMoves[child] = pending + delta;
        }
    }
}
