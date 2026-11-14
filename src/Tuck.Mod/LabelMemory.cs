using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Tuck.Mod;

/// <summary>Remembers deliberate labeler edits for the current game session.</summary>
[HarmonyPatch]
internal static class LabelMemory
{
    private static Options? _options;
    private static readonly Dictionary<string, string> Labels = new();
    private static readonly Dictionary<string, string> Names = new();
    private static GameItem? _usedDragItem;
    private static int _initializing;
    private static bool Enabled => _options?.RememberLabels.Value == true;

    public static void Register(Options options)
    {
        _options = options;
        options.RememberLabels.OnChanged += OnSettingChanged;
        ModHook.OnGameLoadedInit += ClearMemory;
    }

    public static void Unregister()
    {
        if (_options != null) _options.RememberLabels.OnChanged -= OnSettingChanged;
        ModHook.OnGameLoadedInit -= ClearMemory;
        _options = null;
        ClearMemory();
    }

    private static void OnSettingChanged(bool enabled)
    {
        if (!enabled) _usedDragItem = null;
    }

    private static void ClearMemory()
    {
        Labels.Clear();
        Names.Clear();
        _usedDragItem = null;
    }

    // Setting the initial dropdown selection also fires OnDropdownValueChanged.
    [HarmonyPrefix, HarmonyPatch(typeof(LabelerElement), nameof(LabelerElement.InitElement))]
    internal static void BeginInit(out int __state)
    {
        __state = _initializing;
        _initializing++;
    }

    [HarmonyFinalizer, HarmonyPatch(typeof(LabelerElement), nameof(LabelerElement.InitElement))]
    internal static void EndInit(int __state) => _initializing = __state;

    [HarmonyPostfix, HarmonyPatch(typeof(LabelerElement), nameof(LabelerElement.OnDropdownValueChanged))]
    internal static void RememberLabel(ItemFeature ___thisFeature)
    {
        if (Enabled && _initializing == 0 && IsEditable(___thisFeature)
            && ___thisFeature.fakeCondition != null)
            Labels[___thisFeature.category] = ___thisFeature.fakeCondition.identifier;
    }

    private static bool IsEditable(ItemFeature feature) => feature.category != null
        && feature.featureType == ItemFeature.FeatureType.Normale && LabelerHelper.CanEditLabel(feature);

    private static ItemCondition? RememberedCondition(ItemFeature feature)
    {
        if (feature.category == null || !Labels.TryGetValue(feature.category, out var id) || !LabelerHelper.IsEditableLabelFeature(feature))
            return null;
        return ItemConditionList.conditionLookup[feature.category].Find(condition => condition.identifier == id);
    }

    internal static bool ApplyLabels(GameItem item)
    {
        if (!Enabled || Labels.Count == 0) return false;
        bool applied = false;
        foreach (var feature in item.itemFeatures)
        {
            if (feature.category != null && Labels.ContainsKey(feature.category)
                && ItemConditionList.conditionLookup.TryGetValue(feature.category, out var conditions))
                applied |= ApplyLabel(feature, conditions);
        }
        return applied;
    }

    internal static bool ApplyLabel(ItemFeature feature, List<ItemCondition> conditions)
    {
        if (!Enabled || !IsEditable(feature) || !Labels.TryGetValue(feature.category, out var id)) return false;
        int index = conditions.FindIndex(condition => condition.identifier == id);
        if (index < 0) return false;
        feature.fakeCondition = conditions[index];
        feature.SetExposed(false);
        WaterFeatureHelper.OnWaterLabelUpdated(feature, index);
        return true;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(LabelerUIManager), nameof(LabelerUIManager.CanRepeat))]
    internal static bool CanRepeat(GameItem? ___curItem, ref bool __result)
    {
        if (!Enabled) return true;
        __result = ___curItem != null && ___curItem.itemFeatures.Any(feature => RememberedCondition(feature) != null);
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(LabelerUIManager), nameof(LabelerUIManager.Repeat))]
    private static bool Repeat(LabelerUIManager __instance, GameItem? ___curItem)
    {
        if (!Enabled) return true;
        if (___curItem != null && ApplyLabels(___curItem))
        {
            LabelApplied();
            __instance.OpenUI(___curItem);
        }
        return false;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(LabelerBlueUIManager), nameof(LabelerBlueUIManager.OnEditEnd))]
    internal static void RememberName(GameItem? ___curItem)
    {
        // Storage-tab editing uses the same UI with no current item.
        if (!Enabled || ___curItem == null || ___curItem.identifier == "dossier") return;
        var name = GeneralHelper.GetCustomName(___curItem);
        if (string.IsNullOrEmpty(name)) Names.Remove(___curItem.identifier);
        else Names[___curItem.identifier] = name;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(LabelerBlueUIManager), nameof(LabelerBlueUIManager.OnClearButtonClick))]
    internal static void NameCleared(GameItem? ___curItem) => RememberName(___curItem);

    [HarmonyPostfix, HarmonyPatch(typeof(LabelerBlueUIManager), nameof(LabelerBlueUIManager.OpenUI), new[] { typeof(GameItem) })]
    private static void PrefillName(LabelerBlueUIManager __instance, GameItem gameItem)
    {
        var name = NameToPrefill(gameItem);
        if (name != null) __instance.inputField.SetTextWithoutNotify(name);
    }

    internal static string? NameToPrefill(GameItem item) => Enabled && item.identifier != "dossier"
        && !GeneralHelper.HasCustomName(item) && Names.TryGetValue(item.identifier, out var name) ? name : null;

    internal static bool HasMemory(GameItem source, GameItem target) => Enabled && source != target &&
        (source.identifier == "labeler" && target.itemFeatures.Any(feature => RememberedCondition(feature) != null)
         || source.identifier == "labeler_blue" && target.identifier != "dossier" && Names.ContainsKey(target.identifier));

    internal static bool TryReapply(GameItem source, GameItem target)
    {
        if (!HasMemory(source, target) || !source.MayTarget(target) || !source.CanTarget(target)) return false;
        if (source.identifier == "labeler") return ApplyLabels(target);
        GeneralHelper.SetCustomName(target, Names[target.identifier]);
        if (target.contentWindow != null && target.contentWindow.IsVisible())
            target.contentWindow.SetTitle(target.GetDisplayName(preferCustomName: true));
        return true;
    }

    internal static bool HandleRightClick(GameItem source, GameItem target, ISet<KeyCode> keysPressed,
        KeyCode primary, KeyCode secondary)
    {
        if (!Enabled || (source.identifier != "labeler" && source.identifier != "labeler_blue")
            || source == target || !keysPressed.Contains(secondary)
            || (source.identifier == "labeler_blue" && target.identifier == "dossier")) return true;
        if (TryReapply(source, target)) return false;
        // Let vanilla dispatch the ordinary tool action, including its permission checks and selection lifecycle.
        keysPressed.Remove(secondary);
        keysPressed.Add(primary);
        return true;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(ItemSelectHandler), "OnEventPress")]
    private static bool RightClick(ItemSelectHandler __instance, ISet<KeyCode> keysPressed, GameItem? ___currentItem,
        ref GameItem? ___lastItem)
    {
        if (!Enabled || ___currentItem == null || !keysPressed.Contains(__instance.secondaryKey)
            || StoreUIManager.IsAnyTyping() || (StoreUIManager.Instance != null && StoreUIManager.Instance.hoveringStoreUI))
            return true;
        var target = RenderHandler.RaycastElement<GameItem>(Input.mousePosition);
        if (target == null || HandleRightClick(___currentItem, target, keysPressed, __instance.key, __instance.secondaryKey)) return true;
        LabelApplied();
        ___lastItem = null;
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(ItemMouseDragHandler), "OnEventPress")]
    private static void DragRightClick(ItemMouseDragHandler __instance, ISet<KeyCode> keysPressed,
        GameItemElement? ___currentItem, bool ___isDragging)
    {
        if (!Enabled || !___isDragging || ___currentItem == null || ___currentItem.IsDestroyed()
            || !keysPressed.Contains(__instance.secondaryKey) || StoreUIManager.IsAnyTyping()
            || (StoreUIManager.Instance != null && StoreUIManager.Instance.hoveringStoreUI)) return;
        var target = RenderHandler.RaycastElement<GameItem>(Input.mousePosition, ___currentItem.fakeBackground);
        if (!TryUseDraggedLabeler(___currentItem, target, keysPressed, __instance.secondaryKey, out bool repeated)) return;
        if (repeated) LabelApplied();
        else
        {
            ItemBehaviourManager.current.Target(___currentItem, target);
            if (___currentItem.MayTarget(target) && ___currentItem.CanTarget(target)) ___currentItem.Target(target);
        }
    }

    internal static bool TryUseDraggedLabeler(GameItem source, GameItem? target, ISet<KeyCode> keysPressed,
        KeyCode secondary, out bool repeated)
    {
        repeated = false;
        if (!Enabled || !keysPressed.Contains(secondary) || target == null || source == target || (source.identifier != "labeler" && source.identifier != "labeler_blue")
            || (source.identifier == "labeler_blue" && target.identifier == "dossier")
            || !source.MayTarget(target) || !source.CanTarget(target)) return false;
        _usedDragItem = source;
        repeated = TryReapply(source, target);
        // The same secondary key drives vanilla flip/rotation. Leave other pressed keys alone.
        keysPressed.Remove(secondary);
        return true;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(ItemMouseDragHandler), "EndDrag")]
    internal static void FinishLabelerDrag(GameItem? ___currentItem, ref GameItem? ___lastItem,
        ref GameInventory? ___lastInventory, ref bool cancelled)
    {
        if (!Enabled || _usedDragItem == null || _usedDragItem != ___currentItem) return;
        _usedDragItem = null;
        // Keep vanilla drag cleanup, but do not use or drop the tool again on mouse release.
        ___lastItem = null;
        ___lastInventory = null;
        cancelled = true;
    }

    private static void LabelApplied()
    {
        EmporiumAudioPlayer.Instance.audioLabeler.Play();
        AchievementTracker.Instance.CheckLowerLevelsSpecial();
    }

    internal static IEnumerable<string> RememberedText(GameItem source, GameItem? target = null)
    {
        if (target == null || source == target) yield break;
        if (Enabled && source.identifier == "labeler_blue" && HasMemory(source, target))
            yield return Names[target.identifier];
        if (!Enabled || source.identifier != "labeler") yield break;
        foreach (var feature in target.itemFeatures)
        {
            var condition = RememberedCondition(feature);
            if (condition == null) continue;
            var category = ItemFeatureList.CategoryNames.TryGetValue(feature.category, out var title) ? title : feature.category;
            yield return _options!.Text("label_category_and_condition", category, condition.display);
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(AbilityItemHelper), nameof(AbilityItemHelper.CreateHoverTargetTooltip))]
    internal static void TargetTooltip(RichTextBuilder builder, GameItem source, GameItem target)
    {
        if (!Enabled || source == target || (source.identifier != "labeler" && source.identifier != "labeler_blue")) return;
        bool blue = source.identifier == "labeler_blue";
        if ((blue && target.identifier == "dossier") || !source.MayTarget(target) || !source.CanTarget(target)) return;
        var remembered = string.Join(", ", RememberedText(source, target));
        string hint = blue
            ? remembered.Length == 0 ? "secondary_click_to_choose_name" : "secondary_click_to_apply_name"
            : remembered.Length == 0 ? "secondary_click_to_choose_label" : "secondary_click_to_apply_label";
        var key = KeyBindHelper.Get(KeyBindHelper.SECONDARY_CLICK);
        var keyName = key == KeyCode.Mouse1 ? _options!.Text("right_click") : KeyBindHelper.KeyName(key);
        builder.AddLine("");
        builder.AddLine(_options!.Text(hint, keyName, remembered));
    }

    [HarmonyPostfix, HarmonyPatch(typeof(RichTextGenerator), nameof(RichTextGenerator.UseItemOnOtherItem))]
    private static void DragTooltip(GameItem sourceItem, GameItem targetItem, bool canInsert,
        RichTextBuilder? customBuilder, RichTextBuilder ____builder, ref RichText __result)
    {
        if (!Enabled || !canInsert || (sourceItem.identifier != "labeler" && sourceItem.identifier != "labeler_blue")) return;
        var builder = customBuilder ?? ____builder;
        TargetTooltip(builder, sourceItem, targetItem);
        __result = builder.Build();
    }
}
