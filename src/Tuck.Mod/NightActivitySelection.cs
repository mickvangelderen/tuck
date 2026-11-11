using HarmonyLib;
using UnityEngine;

namespace Tuck.Mod;

/// <summary>Optionally travels as soon as a night activity is selected.</summary>
[HarmonyPatch(typeof(TransitUI))]
internal sealed class NightActivitySelection
{
    // Harmony callbacks are static; forward them only while this feature is registered.
    private static NightActivitySelection? _active;
    private readonly Options _options;
    private GameObject? _hiddenTravelButton;

    public NightActivitySelection(Options options) => _options = options;

    public void Register()
    {
        _active = this;
        _options.OneClickNightActivities.OnChanged += OnChanged;
        OnChanged(_options.OneClickNightActivities.Value);
    }

    public void Unregister()
    {
        _options.OneClickNightActivities.OnChanged -= OnChanged;
        _active = null;
        RestoreTravelButton();
    }

    private void OnChanged(bool enabled)
    {
        if (!enabled) RestoreTravelButton();
        else if (TransitUI.Root != null) HideTravelButton(TransitUI.Root.GetComponent<TransitFrame>());
    }

    [HarmonyPostfix, HarmonyPatch(nameof(TransitUI.Open))]
    internal static void OnOpened(TransitFrame ___frame)
    {
        if (_active?._options.OneClickNightActivities.Value != true || ___frame == null) return;
        _active.HideTravelButton(___frame);
    }

    [HarmonyPostfix, HarmonyPatch(nameof(TransitUI.Select))]
    internal static void OnSelected(AfterhourLocation location, AfterhourLocation? ___selected, TransitFrame ___frame)
    {
        if (_active?._options.OneClickNightActivities.Value != true || location == null || ___selected != location) return;

        // Let the game select the destination, then run the button's existing handler and other listeners.
        ___frame.travelButton.onClick.Invoke();
    }

    private void HideTravelButton(TransitFrame frame)
    {
        var button = frame.travelButton.gameObject;
        if (_hiddenTravelButton == button) return;
        RestoreTravelButton();
        if (!button.activeSelf) return;
        _hiddenTravelButton = button;
        button.SetActive(false);
    }

    private void RestoreTravelButton()
    {
        if (_hiddenTravelButton != null) _hiddenTravelButton.SetActive(true);
        _hiddenTravelButton = null;
    }
}
