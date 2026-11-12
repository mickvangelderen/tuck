using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace Tuck.Mod;

/// <summary>Reads owned reference documents when an item is inspected.</summary>
[HarmonyPatch]
internal static class AppraisalGuides
{
    private static Options? _options;
    private static readonly HashSet<string> OpenedGuides = new();

    public static void Register(Options options)
    {
        _options = options;
        options.AutoOpenAppraisalGuides.OnChanged += OnSettingChanged;
    }

    public static void Unregister()
    {
        if (_options != null) _options.AutoOpenAppraisalGuides.OnChanged -= OnSettingChanged;
        _options = null;
        CloseGuides(CloseGuide);
    }

    private static void OnSettingChanged(bool enabled)
    {
        if (!enabled) CloseGuides(CloseGuide);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InspectionUIManager), nameof(InspectionUIManager.OnInspectItem))]
    internal static void Postfix(InspectionUIManager __instance, GameItem gameItem)
    {
        if (_options?.AutoOpenAppraisalGuides.Value != true) return;

        bool openedGuide = false;
        foreach (string guideId in GetGuideIds(gameItem))
        {
            var guide = FindReadableGuide(EmporiumEntry.Instance.GetAllItems(), guideId);
            if (guide == null) continue;
            guide.ActivateSlotItem();
            OpenedGuides.Add(guideId);
            openedGuide = true;
        }
        // Update both draw order and Escape focus, including when inspection was already open.
        if (openedGuide) PaperPanelFocusStamp.BumpPanel(__instance.panel);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(StoreUI), nameof(StoreUI.OnDisable))]
    internal static void OnPanelDisabled(StoreUI __instance)
    {
        if (_options?.AutoOpenAppraisalGuides.Value != true || OpenedGuides.Count == 0) return;
        // Both CloseUI and Escape disable the panel, so this covers every dismissal path.
        var inspection = InspectionUIManager.Instance;
        if (inspection != null && __instance.gameObject == inspection.panel) CloseGuides(CloseGuide);
    }

    internal static void CloseGuides(Action<string> closeGuide)
    {
        var guides = OpenedGuides.ToArray();
        OpenedGuides.Clear();
        foreach (string guideId in guides) closeGuide(guideId);
    }

    private static void CloseGuide(string guideId)
    {
        // Mod paper books expose their item IDs and their own close operation.
        foreach (var book in PaperBookRegistry.All.ToArray())
            if (book.registeredItemId == guideId) PaperBookRegistry.Close(book.fullId);

        var paper = PaperUIManager.Instance;
        switch (guideId)
        {
            case "hand_guide":
            case "ribwich_guide":
            case "fuel_guide":
                var multiPage = MultiPageUIManager.Instance;
                if (multiPage != null && multiPage.panel.activeSelf) multiPage.CloseUI();
                break;
            case "cigarette_guide":
                if (paper != null && paper.cigaretteGuidePanel.activeSelf) paper.CloseCigaretteGuide();
                break;
            case "cigarette_color":
                if (paper != null && paper.cigaretteColorPanel.activeSelf) paper.CloseCigaretteColor();
                break;
            case "injector_guide":
                if (paper != null && paper.injectorGuidePanel.activeSelf) paper.CloseInjectorGuide();
                break;
            case "stamp_guide":
                if (paper != null && paper.foodStampGuidePanel.activeSelf) paper.CloseStampGuide();
                break;
            case "logo_checker":
                var logo = RepublicLogoUIManager.Instance;
                if (logo != null && logo.panel.activeSelf) logo.ClosePanel();
                break;
        }
    }

    internal static IEnumerable<string> GetGuideIds(GameItem item)
    {
        // The registry is authoritative, including replacements and guides added by other mods.
        var type = AppraisalItemRegistry.Find(item);
        if (type != null)
        {
            if (!string.IsNullOrEmpty(type.guideItemId)) yield return type.guideItemId;
            yield break;
        }

        // Counterfeit inspections are not part of the appraisal-service registry.
        if (item.IsTag("CIGARETTE_TAG"))
        {
            yield return "cigarette_guide";
            yield return "cigarette_color";
        }
        else if (item.IsTag("INS_INJECTOR_TAG")) yield return "injector_guide";
        else if (item.IsTag("FOOD_STAMP_TAG"))
        {
            yield return "stamp_guide";
            yield return "logo_checker";
        }
    }

    internal static GameItem? FindReadableGuide(IEnumerable<GameItem> items, string guideId)
    {
        foreach (var item in items)
            if (item.identifier == guideId && GeneralHelper.IsItemEffectivelyOwned(item) &&
                item.MayActivateSlotItem() && item.CanActivateSlotItem())
                return item;
        return null;
    }
}
