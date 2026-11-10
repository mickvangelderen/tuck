using UnityEngine;
using UnityEngine.EventSystems;

namespace Tuck.Mod;

/// <summary>A live tooltip for action buttons, including buttons with no original tooltip.</summary>
internal sealed class HotkeyButtonTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    internal HotkeyTooltips.ButtonAction Action { get; private set; }
    internal bool UsesLegacyTooltip { get; private set; }
    private CustomUITooltipTrigger _trigger = null!;
    private bool _ownsTrigger;
    private bool _hovered;
    private string? _shownText;

    internal static void Install(GameObject host, HotkeyTooltips.ButtonAction action)
    {
        var hint = host.GetComponent<HotkeyButtonTooltip>();
        if (hint != null) return;
        hint = host.AddComponent<HotkeyButtonTooltip>();
        hint.Action = action;
        var legacy = host.GetComponent<TooltipContainer>();
        // Enrich existing scene tooltips through their normal display path, without a second popup.
        hint.UsesLegacyTooltip = legacy != null &&
            (!string.IsNullOrWhiteSpace(legacy.content) || host.GetComponent<TooltipInterface>() != null);
        if (hint.UsesLegacyTooltip) return;
        hint._trigger = host.GetComponent<CustomUITooltipTrigger>();
        hint._ownsTrigger = hint._trigger == null;
        if (hint._ownsTrigger) hint._trigger = host.AddComponent<CustomUITooltipTrigger>();
    }

    public void OnPointerEnter(PointerEventData eventData) => _hovered = true;

    public void OnPointerExit(PointerEventData eventData) => Hide();

    private void Update()
    {
        if (UsesLegacyTooltip || !_hovered || CustomUIManager.Instance == null || _trigger == null
            || HotkeyTooltips.ActiveOptions is not { } options) return;
        string hint = HotkeyTooltips.ButtonHint(options, Action);
        // Read existing text each time; leave other mods' tooltip content intact.
        string original = _trigger.tooltipText;
        string text = string.IsNullOrEmpty(original) ? hint
            : string.IsNullOrEmpty(hint) ? original : original + "\n" + hint;
        if (text == _shownText) return;
        if (string.IsNullOrEmpty(text)) CustomUIManager.Instance.HideTooltip(_trigger);
        else CustomUIManager.Instance.ShowTooltip(text, _trigger);
        _shownText = text;
    }

    private void Hide()
    {
        if (_hovered && CustomUIManager.Instance != null && _trigger != null)
            CustomUIManager.Instance.HideTooltip(_trigger);
        _hovered = false;
        _shownText = null;
    }

    private void OnDisable() => Hide();

    internal void Uninstall()
    {
        enabled = false;
        Hide();
        if (_ownsTrigger && _trigger != null) Destroy(_trigger);
        Destroy(this);
    }
}
