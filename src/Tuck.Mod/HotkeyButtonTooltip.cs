using UnityEngine;
using UnityEngine.EventSystems;

namespace Tuck.Mod;

/// <summary>A live tooltip for action buttons, including buttons with no original tooltip.</summary>
internal sealed class HotkeyButtonTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    internal HotkeyTooltips.ButtonAction Action { get; private set; }
    internal bool UsesExistingTooltip { get; private set; }
    private bool _hovered;
    private string? _shownText;

    internal static void Install(GameObject host, HotkeyTooltips.ButtonAction action)
    {
        var hint = host.GetComponent<HotkeyButtonTooltip>();
        if (hint != null) return;
        hint = host.AddComponent<HotkeyButtonTooltip>();
        hint.Action = action;
        var existing = host.GetComponent<TooltipContainer>();
        // Enrich existing scene tooltips through their normal display path, without a second popup.
        hint.UsesExistingTooltip = existing != null &&
            (!string.IsNullOrWhiteSpace(existing.content) || host.GetComponent<TooltipInterface>() != null);
    }

    public void OnPointerEnter(PointerEventData eventData) => _hovered = true;

    public void OnPointerExit(PointerEventData eventData) => Hide();

    private void Update()
    {
        if (UsesExistingTooltip || !_hovered || TooltipManager._instance == null
            || HotkeyTooltips.ActiveOptions is not { } options) return;
        string text = HotkeyTooltips.ButtonHint(options, Action);
        if (text == _shownText) return;
        if (string.IsNullOrEmpty(text)) TooltipManager._instance.HideTooltip(this);
        else TooltipManager._instance.SetAndShowTooltip(text, this);
        _shownText = text;
    }

    private void Hide()
    {
        if (_hovered && TooltipManager._instance != null)
            TooltipManager._instance.HideTooltip(this);
        _hovered = false;
        _shownText = null;
    }

    private void OnDisable() => Hide();

    internal void Uninstall()
    {
        enabled = false;
        Hide();
        Destroy(this);
    }
}
