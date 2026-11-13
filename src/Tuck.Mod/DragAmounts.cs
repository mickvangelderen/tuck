using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Tuck.Mod;

/// <summary>Temporarily expands a drag using the game's rectangular-selection handler.</summary>
[HarmonyPatch]
internal static class DragAmounts
{
    private static Options? _options;
    private static Session? _session;
    private static List<GameItemElement>? _pendingSelection;
    private static bool _changingHandlers;

    public static void Register(Options options) => _options = options;

    public static void Unregister()
    {
        _options = null;
        if (_session?.Group.IsGroupDragging == true) _session.Shrink();
        _session = null;
        _pendingSelection = null;
    }

    private static bool Held(KeyBinding binding) => KeyHeld(binding.Key);
    private static bool KeyHeld(KeyCode key) => key != KeyCode.None && Input.GetKey(key);
    private enum Expansion { Original, Matching, Inventory }
    private static Expansion CurrentExpansion => _options == null ? Expansion.Original
        : Held(_options.DragEntireInventory) ? Expansion.Inventory
        : Held(_options.DragAllMatching) ? Expansion.Matching : Expansion.Original;
    private static List<GameItemElement> Items(ItemMultiSelectHandler group) =>
        ModReflect.Get<List<GameItemElement>>(group, "selectedItems");
    private static Dictionary<GameItemElement, GameInventory> Homes(ItemMultiSelectHandler group) =>
        ModReflect.Get<Dictionary<GameItemElement, GameInventory>>(group, "selectedHomes");
    private static Dictionary<GameItemElement, Vector2> Offsets(ItemMultiSelectHandler group) =>
        ModReflect.Get<Dictionary<GameItemElement, Vector2>>(group, "dragOffsets");
    private static Vector2 MousePosition => RenderHandler.current.overlayRoot.rectTransform.InverseTransformPoint(Input.mousePosition);
    private static Vector2 HomePosition(GameItemElement item) =>
        RenderHandler.current.overlayRoot.rectTransform.InverseTransformPoint(item.GetRotatorCenteredPosition());

    [HarmonyPostfix, HarmonyPatch(typeof(ItemMultiSelectHandler), "IsPreciseModifierHeld")]
    private static void PreciseModifier(ref bool __result)
    {
        if (_options == null) return;
        // The default keeps vanilla's handling of both Shift keys.
        if (_options.DragPrecisePlacement.Key == KeyCode.LeftShift) return;
        // Leave vanilla and other mods' result alone unless a binding needs to override it.
        if (Held(_options.DragPrecisePlacement)) __result = true;
        else if (_options.DragAllMatching.Key != KeyCode.None) __result = false;
    }

    // Run before release dispatch as well as mouse movement: a stationary key change,
    // including one on the drop frame, must change what will be dropped.
    [HarmonyPrefix, HarmonyPatch(typeof(InputActionManager), "Update")]
    private static void UpdateModifiers()
    {
        if (_options == null) return;
        if (_session != null)
        {
            var session = _session;
            if (session.Group != ItemMultiSelectHandler.current || !session.Group.IsGroupDragging)
                _session = null;
            else
            {
                ModReflect.Call(session.Group, "PruneSelection");
                if (_session == session) session.SetExpansion(CurrentExpansion);
            }
        }
        else if (CurrentExpansion != Expansion.Original && ItemMouseDragHandler.current != null
                 && ItemMouseDragHandler.current.IsDraggingItem)
            PromoteSingle(ItemMouseDragHandler.current, null);
        else if (ItemMultiSelectHandler.current != null && ItemMultiSelectHandler.current.enabled)
            StartFromEmptySpace(ItemMultiSelectHandler.current, Input.mousePosition);
    }

    // Vanilla clears a selection on a modified click to allow quick transfers. Keep
    // its membership until a drag actually starts, without consuming those clicks.
    [HarmonyPrefix, HarmonyPatch(typeof(ItemMultiSelectHandler), "OnEventPress")]
    private static void RememberSelection(ItemMultiSelectHandler __instance, ISet<KeyCode> keysPressed)
    {
        if (_options == null || !keysPressed.Contains(__instance.key)) return;
        _pendingSelection = null;
        if (ItemPinHelper.BlocksClicks || ModReflect.Get(__instance, "state").ToString() != "Selected"
            || !(CurrentExpansion != Expansion.Original || Held(_options.DragPrecisePlacement))) return;
        ModReflect.Call(__instance, "PruneSelection");
        var hit = RenderHandler.RaycastElement<GameItemElement>(Input.mousePosition);
        var items = Items(__instance);
        if (items.Count > 1 && hit != null && items.Contains(hit))
            _pendingSelection = new List<GameItemElement>(items);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(ItemMultiSelectHandler), "OnEventRelease")]
    private static void ForgetPendingSelection(ItemMultiSelectHandler __instance, ISet<KeyCode> keysReleased)
    {
        if (keysReleased.Contains(__instance.key)) _pendingSelection = null;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(ItemMouseDragHandler), "StartDrag")]
    private static void SingleStarted(ItemMouseDragHandler __instance)
    {
        if (_options == null || _changingHandlers || !__instance.IsDraggingItem) return;
        var selection = _pendingSelection;
        _pendingSelection = null;
        if (selection != null || CurrentExpansion != Expansion.Original) PromoteSingle(__instance, selection);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(ItemMouseDragHandler), "OnEventUpdate")]
    private static void ConsumePromotedDrag(ref bool __result)
    {
        // Promotion can occur after the higher-priority group handler's turn.
        // Still consume this frame so quick-click handlers cannot re-arm below us.
        if (_session?.Group.IsGroupDragging == true) __result = true;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(ItemMultiSelectHandler), "StartGroupDrag")]
    private static void GroupStarted(ItemMultiSelectHandler __instance)
    {
        if (_options == null || _changingHandlers || !__instance.IsGroupDragging) return;
        _session = new Session(__instance);
        _session.SetExpansion(CurrentExpansion);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(ItemMultiSelectHandler), "OnEventReset")]
    private static void GroupReset(ItemMultiSelectHandler __instance) => ForgetSession(__instance);

    [HarmonyPostfix, HarmonyPatch(typeof(ItemMultiSelectHandler), "EndGroupDrag")]
    private static void GroupEnded(ItemMultiSelectHandler __instance) => ForgetSession(__instance);

    private static void ForgetSession(ItemMultiSelectHandler group)
    {
        if (!_changingHandlers && _session?.Group == group) _session = null;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(ItemMultiSelectHandler), "OnEventMouseMove")]
    private static bool StartFromEmptySpace(ItemMultiSelectHandler __instance, Vector3 mousePosition)
    {
        if (_options == null || _changingHandlers || CurrentExpansion == Expansion.Original
            || StoreUIManager.Instance.hoveringStoreUI || UIDragHandler.isDraggingAnyUI) return true;
        string state = ModReflect.Get(__instance, "state").ToString();
        if (state != "PendingBand" && state != "Banding") return true;
        var press = ModReflect.Get<Vector3>(__instance, "pressPosition");
        if ((mousePosition - press).magnitude < __instance.dragThreshold) return true;
        var inventory = ModReflect.Get<GameInventory>(__instance, "bandInventory");
        if (inventory == null) return true;
        var items = inventory.childItems.OfType<GameItemElement>().Where(item => Selectable(item, inventory)).ToList();
        if (items.Count == 0) return true;
        var band = new BandStart(__instance);
        _changingHandlers = true;
        try
        {
            ModReflect.Call(__instance, "HideBand");
            FillSelection(__instance, items);
            ModReflect.Call(__instance, "StartGroupDrag");
            // Anchor at the mouse-down position, just like an ordinary group drag.
            Vector2 origin = RenderHandler.current.overlayRoot.rectTransform.InverseTransformPoint(press);
            foreach (var item in Items(__instance)) Offsets(__instance)[item] = HomePosition(item) - origin;
            _session = new Session(__instance) { Band = band, Mode = Expansion.Inventory };
        }
        finally { _changingHandlers = false; }
        return false;
    }

    private static bool Selectable(GameItemElement item, GameInventory home) =>
        Eligible(item, home) && item.handler != null && item.handler.gameObject.activeInHierarchy;

    internal static bool Eligible(GameItem item, GameInventory home) =>
        item.parentInventory == home
        && !(item is GameItemElement element && (element.IsDestroyed() || element.isDebugMenu))
        && !ItemPinHelper.IsPinned(item) && item.MayRemove() && item.MaxNumRemove() >= 1;

    private static void FillSelection(ItemMultiSelectHandler group, IEnumerable<GameItemElement> items)
    {
        Items(group).Clear();
        Homes(group).Clear();
        foreach (var item in items)
        {
            Items(group).Add(item);
            Homes(group)[item] = item.parentInventory;
        }
    }

    private static void PromoteSingle(ItemMouseDragHandler single, List<GameItemElement>? selection)
    {
        var item = single.GetCurrentItem() as GameItemElement;
        var group = ItemMultiSelectHandler.current;
        if (group == null || !group.enabled || item?.parentInventory == null || !Eligible(item, item.parentInventory)) return;
        var originals = selection?.Where(i => i.parentInventory != null && Selectable(i, i.parentInventory)).ToList()
                        ?? new List<GameItemElement> { item };
        if (!originals.Contains(item)) return;
        var saved = new SingleDrag(single, item);
        var center = item.GetCenteredPosition();
        _changingHandlers = true;
        try
        {
            group.Cancel();
            ModReflect.Set(single, "isDragging", false);
            ModReflect.Call(single, "ClearMayHighlights");
            single.UpdateReset();
            OverlayHandler.current.HideTooltip();
            // Native formation placement uses the source orientations. Restore the
            // single item's temporary rotation only when returning to a single drag.
            SetGhostOrientation(item, item.modifiedShape);
            FillSelection(group, originals);
            ModReflect.Call(group, "StartGroupDrag");
            if (selection == null)
            {
                item.SetCenteredPosition(center.Item1, center.Item2);
                Offsets(group)[item] = new Vector2(center.Item1, center.Item2) - MousePosition;
            }
            _session = new Session(group) { Single = selection == null ? saved : null };
            _session.SetExpansion(CurrentExpansion);
        }
        finally { _changingHandlers = false; }
    }

    private static void SetGhostOrientation(GameItemElement item, GridShape shape)
    {
        if (item.tempShape.flipped != shape.flipped) item.GridFlipX();
        while (item.tempShape.orientation != shape.orientation) item.GridRotCW();
    }

    // Snapshot identifiers and homes once; newly included items never broaden the kinds.
    internal sealed class SelectionKinds
    {
        internal readonly HashSet<GameItem> Originals;
        private readonly Dictionary<GameInventory, HashSet<string>> _kinds = new();

        internal SelectionKinds(IEnumerable<GameItem> originals)
        {
            Originals = new HashSet<GameItem>(originals);
            foreach (var item in Originals)
            {
                if (item.parentInventory == null) continue;
                if (!_kinds.TryGetValue(item.parentInventory, out var kinds))
                    _kinds.Add(item.parentInventory, kinds = new HashSet<string>());
                kinds.Add(item.identifier);
            }
        }

        internal IEnumerable<GameItem> MatchingItems(bool entireInventory = false)
        {
            foreach (var pair in _kinds)
                foreach (var item in pair.Key.childItems)
                    if ((entireInventory || pair.Value.Contains(item.identifier)) && Eligible(item, pair.Key)) yield return item;
        }
    }

    private sealed class Session
    {
        internal readonly ItemMultiSelectHandler Group;
        internal SingleDrag? Single;
        internal BandStart? Band;
        internal Expansion Mode;
        private readonly SelectionKinds _kinds;
        private readonly Dictionary<GameItemElement, Vector2> _homePositions;

        internal Session(ItemMultiSelectHandler group)
        {
            Group = group;
            _kinds = new SelectionKinds(Items(group));
            _homePositions = Items(group).ToDictionary(item => item, HomePosition);
        }

        internal void SetExpansion(Expansion mode)
        {
            // Both modifiers mean the whole inventory when the drag began on empty space.
            if (Band != null && mode != Expansion.Original) mode = Expansion.Inventory;
            if (mode == Mode) return;
            if (mode == Expansion.Original) { Shrink(); return; }
            var candidates = _kinds.MatchingItems(mode == Expansion.Inventory).OfType<GameItemElement>()
                .Where(item => Selectable(item, item.parentInventory)).ToList();
            var keep = new HashSet<GameItem>(_kinds.Originals);
            keep.UnionWith(candidates);
            RetainItems(keep);
            var items = Items(Group);
            var homes = Homes(Group);
            var offsets = Offsets(Group);
            foreach (var item in candidates)
            {
                if (items.Contains(item)) continue;
                var anchor = items.FirstOrDefault(i => _kinds.Originals.Contains(i) && homes[i] == item.parentInventory);
                if (anchor == null) continue;
                var offset = offsets[anchor] + HomePosition(item) - _homePositions[anchor];
                item.contentWindow?.Hide();
                item.examineWindow?.Hide();
                item.UpdateFakeReal(item.MaxNumRemove());
                item.ToggleFakeBackground(true, UISettings.current.dragOpacity);
                item.tempShape.Set(item.modifiedShape);
                item.fakeBackground.image.raycastTarget = false;
                items.Add(item);
                homes[item] = item.parentInventory;
                offsets[item] = offset;
            }
            Mode = mode;
            if (items.Count == 0) Group.Cancel();
            else ModReflect.Call(Group, "ClearDropTarget");
        }

        internal void Shrink()
        {
            Mode = Expansion.Original;
            if (Band != null)
            {
                Band.Resume(Group);
                return;
            }
            RetainItems(_kinds.Originals);
            ModReflect.Call(Group, "ClearDropTarget");
            if (Items(Group).Count == 0)
            {
                Group.Cancel();
                return;
            }
            if (Single != null) Single.Resume(Group);
        }

        private void RetainItems(HashSet<GameItem> keep)
        {
            foreach (var item in Items(Group).Where(i => !keep.Contains(i)).ToList())
            {
                ModReflect.Call(Group, "RestoreLiveGhost", item);
                Items(Group).Remove(item);
                Homes(Group).Remove(item);
                Offsets(Group).Remove(item);
            }
        }
    }

    private sealed class SingleDrag
    {
        private readonly ItemMouseDragHandler _handler;
        private readonly GameItemElement _item;
        private readonly int _amount;
        private readonly KeyCode _key;
        private readonly GridShape _shape;

        internal SingleDrag(ItemMouseDragHandler handler, GameItemElement item)
        {
            _handler = handler;
            _item = item;
            _amount = ModReflect.Get<int>(handler, "currentRemove");
            _key = ModReflect.Get<KeyCode>(handler, "lastMouseClickedKey");
            _shape = item.tempShape.Clone();
        }

        internal void Resume(ItemMultiSelectHandler group)
        {
            if (!Items(group).Contains(_item) || _item.parentInventory == null || !Eligible(_item, _item.parentInventory))
            {
                group.Cancel();
                return;
            }
            // Detach ownership before cancelling so the original ghost keeps its
            // position. StartDrag restores native single-item hints.
            Items(group).Remove(_item);
            group.Cancel();
            _changingHandlers = true;
            try
            {
                var center = _item.GetCenteredPosition();
                ModReflect.Set(_handler, "currentItem", _item);
                ModReflect.Set(_handler, "lastMouseClickedKey", _key);
                ModReflect.Call(_handler, "StartDrag");
                ModReflect.Set(_handler, "isMult10", KeyHeld(_handler.scrollMult10Key));
                ModReflect.Set(_handler, "isMult100", KeyHeld(_handler.scrollMult100Key));
                ModReflect.Set(_handler, "isScrollIgnore", KeyHeld(_handler.scrollIgnoreKey));
                _item.fakeBackground.image.raycastTarget = true;
                SetGhostOrientation(_item, _shape);
                _item.SetCenteredPosition(center.Item1, center.Item2);
                int amount = System.Math.Min(_amount, _item.MaxNumRemove());
                ModReflect.Set(_handler, "currentRemove", amount);
                _item.UpdateFakeReal(amount);
                ModReflect.Call(_handler, "Drag", Vector3.zero);
            }
            finally { _changingHandlers = false; }
        }
    }

    private sealed class BandStart
    {
        private readonly object _state;
        private readonly Vector3 _press;
        private readonly GameInventory _inventory;
        private readonly PixelWindow _window;

        internal BandStart(ItemMultiSelectHandler group)
        {
            _state = ModReflect.Get(group, "state");
            _press = ModReflect.Get<Vector3>(group, "pressPosition");
            _inventory = ModReflect.Get<GameInventory>(group, "bandInventory");
            _window = ModReflect.Get<PixelWindow>(group, "bandWindow");
        }

        internal void Resume(ItemMultiSelectHandler group)
        {
            group.Cancel();
            ModReflect.Set(group, "state", _state);
            ModReflect.Set(group, "pressPosition", _press);
            ModReflect.Set(group, "bandInventory", _inventory);
            ModReflect.Set(group, "bandWindow", _window);
            ModReflect.Call(group, "OnEventMouseMove", Input.mousePosition, Vector3.zero);
        }
    }
}
