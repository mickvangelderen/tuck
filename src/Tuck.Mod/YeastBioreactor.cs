using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace Tuck.Mod;

internal static class YeastBioreactor
{
    private static Options? _options;
    private static readonly List<WeakReference<GameItem>> Reactors = new();

    public static void Register(Options options)
    {
        _options = options;
        options.YeastBioreactorTool.OnChanged += OnChanged;
        OnChanged(options.YeastBioreactorTool.Value);
    }

    public static void Unregister()
    {
        if (_options != null) _options.YeastBioreactorTool.OnChanged -= OnChanged;
        _options = null;
        OnChanged(false);
    }

    private static bool IsTool(GameItem item) =>
        _options?.YeastBioreactorTool.Value == true && item != null && item.IsID("wine_yeast_infinite");

    private static void OnChanged(bool enabled)
    {
        if (enabled)
        {
            if (EmporiumEntry.Instance == null) return;
            foreach (var item in EmporiumEntry.Instance.GetAllItems())
                if (IsTool(item)) CreatedPostfix(item);
            return;
        }
        foreach (var reference in Reactors)
            if (reference.TryGetTarget(out var item))
            {
                item.maySelectSlotItemFunc -= Selectable;
                item.canSelectSlotItemFunc -= Selectable;
                item.onThisTargetItemFunc -= OnTarget;
            }
        Reactors.Clear();
    }

    // The factory assigns the identifier after returning; this hook is specific to the reactor factory.
    [HarmonyPostfix, HarmonyPatch(typeof(WineDirectory), "YeastBioreactor")]
    internal static void CreatedPostfix(GameItem __result)
    {
        if (_options?.YeastBioreactorTool.Value != true) return;
        Reactors.RemoveAll(reference => !reference.TryGetTarget(out _));
        if (Reactors.Any(reference => reference.TryGetTarget(out var item) && item == __result)) return;
        Reactors.Add(new WeakReference<GameItem>(__result));
        // Do not replace another mod's selection restrictions or OR an unconditional true into them.
        __result.maySelectSlotItemFunc ??= Selectable;
        __result.canSelectSlotItemFunc ??= Selectable;
        __result.onThisTargetItemFunc += OnTarget;
    }

    private static bool Selectable(GameItem item, GameInventory inventory, SlotMarker slot) => IsTool(item);

    // Extend only the bottle's liquid-source predicate; vanilla still evaluates every other targeting rule.
    [HarmonyPostfix, HarmonyPatch(typeof(WineHelper), "CanFillBottle")]
    internal static void CanFillBottlePostfix(GameItem source, GameItem bottle, ref bool __result)
    {
        if (!__result && IsTool(source))
            __result = CanFerment(source, bottle, WaterHelper.GetCurrentCapacityML(bottle));
    }

    private static void OnTarget(GameItem source, GameItem target)
    {
        if (IsTool(source) && target != null && target.IsTag(WineHelper.WINE_CONTAINER_TAG))
            TryFerment(source, target, WaterHelper.GetCurrentCapacityML(target), WineHelper.StartFermentation);
    }

    internal static bool CanFerment(GameItem source, GameItem target, int waterMl)
    {
        if (!IsTool(source) || !source.MaySelectSlotItem() || !source.CanSelectSlotItem() ||
            target == null || source == target ||
            !target.IsTag(WineHelper.WINE_CONTAINER_TAG) || !GeneralHelper.IsItemEffectivelyOwned(target) ||
            !WineHelper.CanModifyLiquid(target) || waterMl < WineHelper.FERMENTATION_MIN_ML)
            return false;

        var contents = GraphUtils.FindAllChildrenType<GameItem>(target);
        // A reactor inside this bottle would be expelled by vanilla fermentation instead of staying equipped.
        return !contents.Contains(source) && contents.Any(item => item.IsTag(WineHelper.WINE_INGREDIENT_TAG)) &&
            source.MayRemove() && GeneralHelper.MayPlayerInsertInto(target) &&
            target.TryFindOneValidInventorySlot(source)?.IsValid() == true;
    }

    internal static bool TryFerment(GameItem source, GameItem target, int waterMl, Action<GameItem> startFermentation)
    {
        if (!CanFerment(source, target, waterMl) || !source.MayTarget(target) || !source.CanTarget(target)) return false;
        bool hasYeast = GraphUtils.FindAllChildrenType<GameItem>(target).Any(item => item.IsTag(WineHelper.WINE_YEAST_TAG));
        startFermentation(target);
        if (target.GetTagReadonly(WineHelper.WINE_CONTAINER_STATE).GetInt() != 1) return false;

        // Vanilla consumes the ingredients and awards any installed yeast's quality bonus; add it only once.
        if (!hasYeast)
            target.ModifyTag(WineHelper.WINE_SCORE_INT, state => state.Enable().SetInt(state.GetInt() + 1));
        int power = Math.Max(target.GetTagReadonly(WineHelper.WINE_YEAST_POWER_INT).GetInt(),
            source.GetTagReadonly(WineHelper.WINE_YEAST_POWER_INT).GetInt());
        target.ModifyTag(WineHelper.WINE_YEAST_POWER_INT, state => state.Enable().SetInt(power));
        return true;
    }
}
