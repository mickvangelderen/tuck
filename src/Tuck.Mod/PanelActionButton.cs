using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tuck.Mod;

/// <summary>Decorates an existing caption without owning input or changing the button's action.</summary>
internal sealed class PanelActionButton : MonoBehaviour
{
    internal PanelAction Action { get; private set; }
    internal Button Button { get; private set; } = null!;
    internal bool IsBuy { get; private set; }
    internal bool IsWholesale { get; private set; }
    private TMP_Text? _label;
    private Text? _legacyLabel;
    private string? _original;
    private string? _shown;

    internal static void Install(Button button, PanelAction action, bool buy, bool wholesale)
    {
        if (button.GetComponent<PanelActionButton>() != null) return;
        var hint = button.gameObject.AddComponent<PanelActionButton>();
        hint.Button = button;
        hint.Action = action;
        hint.IsBuy = buy;
        hint.IsWholesale = wholesale;
        hint._label = button.GetComponentInChildren<TMP_Text>(true);
        if (hint._label == null) hint._legacyLabel = button.GetComponentInChildren<Text>(true);
    }

    private string Caption
    {
        get => _label != null ? _label.text : _legacyLabel != null ? _legacyLabel.text : "";
        set { if (_label != null) _label.text = value; else if (_legacyLabel != null) _legacyLabel.text = value; }
    }

    private void LateUpdate()
    {
        var options = PanelButtons.ActiveOptions;
        if (options == null) return;
        // The game and localization can replace the caption while the button is visible.
        if (_shown == null || Caption != _shown) _original = Caption;
        var key = Action == PanelAction.Decline ? options.Decline.Key : options.Accept.Key;
        if (!PanelButtons.ShowHint(this)) key = KeyCode.None;
        var glyph = _label != null && _label.richText ? HotkeyGlyphs.GetTag(key) : null;
        _shown = FormatCaption(options, _original!, key, glyph);
        if (Caption != _shown) Caption = _shown;
    }

    internal static string FormatCaption(Options options, string caption, KeyCode key, string? glyph)
    {
        if (key == KeyCode.None) return caption;
        // Tooltip-sized glyphs protrude below short button captions; shrink and raise them.
        string hint = glyph == null ? $"({KeyBindHelper.KeyName(key)})"
            : $"<voffset=0.2em><size=75%>{glyph}</size></voffset>";
        return options.Text("text_with_key_hint", caption, hint);
    }

    private void OnDisable()
    {
        if (_shown != null && Caption == _shown) Caption = _original!;
        _shown = null;
    }

    internal void Uninstall()
    {
        enabled = false;
        OnDisable();
        Destroy(this);
    }
}
