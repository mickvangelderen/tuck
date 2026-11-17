using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tuck.Mod;

/// <summary>Arranges appraisal increments above the estimate field and send button.</summary>
internal static class AppraisalSteps
{
    private static Sprite? _positiveSprite;
    private static Sprite? _negativeSprite;
    private static Options? _options;
    private static RectTransform? _panel;
    private static RectTransform? _row;
    private static RectTransform? _input;
    private static RectTransform? _send;
    private static RectTransform? _positiveRow;
    private static RectTransform? _negativeRow;
    private static Vector2 _rowOffset;
    private static Vector2 _sendOffset;
    private static Vector2 _inputSizeOffset;
    private static float _inputResizeFactor = 1f;
    private static readonly List<GameObject> HiddenButtons = new();
    private static readonly List<Button> AddedButtons = new();

    public static void Register(Options options)
    {
        _options = options;
        options.AppraisalFiftyButtons.OnChanged += OnSettingChanged;
    }

    public static void Unregister()
    {
        if (_options != null) _options.AppraisalFiftyButtons.OnChanged -= OnSettingChanged;
        _options = null;
        RemoveLayout();
        DestroySprite(ref _positiveSprite);
        DestroySprite(ref _negativeSprite);
    }

    private static void OnSettingChanged(bool enabled)
    {
        // Clean up immediately, even when the appraisal window is closed. Enabling waits for its next update.
        if (!enabled) RemoveLayout();
    }

    [HarmonyPostfix, HarmonyPatch(typeof(InspectionUIManager), nameof(InspectionUIManager.UpdateAppraisalPanel))]
    internal static void Postfix(InspectionUIManager __instance)
    {
        if (_options?.AppraisalFiftyButtons.Value != true) return;
        if (!__instance.appraisalPanel.activeSelf) return;

        if (_panel != __instance.appraisalPanel.transform)
        {
            RemoveLayout();
            Install(__instance);
        }
    }

    private static void Install(InspectionUIManager inspection)
    {
        var row = (RectTransform)inspection.apprisalServiceInputField.transform.parent;
        var minusTen = row.Find("-10")?.GetComponent<Button>();
        var plusTen = row.Find("+10")?.GetComponent<Button>();
        var send = inspection.appraisalPanel.transform.Find("buttonSend") as RectTransform;
        // Leave an unfamiliar/replaced appraisal layout alone.
        if (minusTen == null || plusTen == null || send == null ||
            row.GetComponent<HorizontalLayoutGroup>() == null) return;

        if (_positiveSprite == null)
            _positiveSprite = LoadButtonSprite("positive");
        if (_negativeSprite == null)
            _negativeSprite = LoadButtonSprite("negative");

        _panel = (RectTransform)inspection.appraisalPanel.transform;
        _row = row;
        _send = send;
        _positiveRow = CreateRow("Tuck positive appraisal steps", 32f);
        _negativeRow = CreateRow("Tuck negative appraisal steps", -8f);
        foreach (int amount in new[] { 5, 10, 50, 100 })
        {
            CreateButton(plusTen, inspection, amount, _positiveRow);
            CreateButton(minusTen, inspection, -amount, _negativeRow);
        }

        // The +/-100 buttons retain their old +/-20 object names in the shipped scene.
        // Hide only the vanilla increments; the input and any other mod's controls remain intact.
        foreach (string name in new[] { "-20", "-10", "-5", "-1", "+1", "+5", "+10", "+20" })
        {
            var button = row.Find(name);
            if (button == null || !button.gameObject.activeSelf) continue;
            HiddenButtons.Add(button.gameObject);
            button.gameObject.SetActive(false);
        }

        // The native row now centers just the input. Position it alongside Send without reparenting
        // either control, preserving input focus, native callbacks and the original panel dimensions.
        const float gap = 12f;
        var input = (RectTransform)inspection.apprisalServiceInputField.transform;
        _input = input;
        // Resize the sliced image's rectangle to keep its border at the native thickness.
        _inputResizeFactor = send.rect.height * send.localScale.y /
            (input.rect.height * input.localScale.y * row.localScale.y);
        _inputSizeOffset = input.rect.size * (_inputResizeFactor - 1f);
        input.sizeDelta += _inputSizeOffset;
        foreach (var text in input.GetComponentsInChildren<TextMeshProUGUI>(true))
            text.fontSize *= _inputResizeFactor;
        float inputWidth = input.rect.width * input.localScale.x * row.localScale.x;
        _rowOffset = new Vector2(-(send.rect.width + gap) / 2f, -75f) - row.anchoredPosition;
        _sendOffset = new Vector2((inputWidth + gap) / 2f, -75f) - send.anchoredPosition;
        row.anchoredPosition += _rowOffset;
        send.anchoredPosition += _sendOffset;
    }

    private static RectTransform CreateRow(string name, float y)
    {
        var row = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
        var rect = (RectTransform)row.transform;
        rect.SetParent(_panel, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, y);
        rect.sizeDelta = new Vector2(304f, 32f);
        var layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = layout.childControlHeight = false;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        return rect;
    }

    private static void CreateButton(Button template, InspectionUIManager inspection, int amount, Transform parent)
    {
        var button = Object.Instantiate(template, parent, false);
        AddedButtons.Add(button);
        string label = amount > 0 ? $"+{amount}" : amount.ToString();
        button.name = "Tuck " + label;
        var text = button.GetComponentInChildren<TextMeshProUGUI>(true);
        text.text = label;
        button.image.sprite = amount > 0 ? _positiveSprite : _negativeSprite;
        ((RectTransform)button.transform).sizeDelta = new Vector2(70f, 32f);
        // Replacing the event also removes the cloned persistent +/-10 callback.
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() => inspection.ChangeAppraisalServicePrice(amount));
    }

    private static Sprite LoadButtonSprite(string name)
    {
        using var stream = typeof(AppraisalSteps).Assembly.GetManifestResourceStream($"Tuck.AppraisalButton.{name}.png")
            ?? throw new System.InvalidOperationException("Missing appraisal button sprite.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        try
        {
            if (!ImageConversion.LoadImage(texture, bytes.ToArray(), true))
                throw new System.InvalidOperationException("Could not decode appraisal button sprite.");
            // Preserve button_confront_2x's native size, pixel density, and four-pixel slicing border.
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(4f, 4f, 4f, 4f));
        }
        catch
        {
            Object.Destroy(texture);
            throw;
        }
    }

    private static void DestroySprite(ref Sprite? sprite)
    {
        if (sprite == null) return;
        Object.Destroy(sprite.texture);
        Object.Destroy(sprite);
        sprite = null;
    }

    private static void RemoveLayout()
    {
        // Undo only our translations and visibility changes, rather than resetting the whole panel.
        if (_row != null) _row.anchoredPosition -= _rowOffset;
        if (_input != null)
        {
            _input.sizeDelta -= _inputSizeOffset;
            foreach (var text in _input.GetComponentsInChildren<TextMeshProUGUI>(true))
                text.fontSize /= _inputResizeFactor;
        }
        if (_send != null) _send.anchoredPosition -= _sendOffset;
        _rowOffset = _sendOffset = Vector2.zero;
        _inputSizeOffset = Vector2.zero;
        _inputResizeFactor = 1f;
        foreach (var button in HiddenButtons)
            if (button != null) button.SetActive(true);
        HiddenButtons.Clear();
        foreach (var button in AddedButtons)
        {
            if (button == null) continue;
            button.onClick.RemoveAllListeners();
        }
        AddedButtons.Clear();
        DestroyRow(_positiveRow);
        DestroyRow(_negativeRow);
        _positiveRow = null;
        _negativeRow = null;
        _row = null;
        _input = null;
        _send = null;
        _panel = null;
    }

    private static void DestroyRow(RectTransform? row)
    {
        if (row == null) return;
        row.gameObject.SetActive(false);
        Object.Destroy(row.gameObject);
    }

}
