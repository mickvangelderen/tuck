using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Tuck.Mod;

internal static class ItemWarnings
{
    [Flags]
    internal enum Warning { None = 0, Stolen = 1, LowBattery = 2, MissingIngredients = 4, MissingWater = 8, Spill = 16, FilterDurability = 32 }
    internal static readonly Warning[] Kinds = { Warning.Stolen, Warning.LowBattery, Warning.MissingWater, Warning.MissingIngredients, Warning.Spill, Warning.FilterDurability };
    private static Options? _options;
    private static string? _modId;
    private static WarningAnimation? _stolen;
    private static WarningAnimation? _battery;
    private static WarningAnimation? _ingredients;
    private static WarningAnimation? _water;
    private static WarningAnimation? _spill;
    private static WarningAnimation? _filter;
    private static ItemWarningPass? _pass;
    private static readonly List<GraphNodeStorage> FilterStorage = new();

    internal static void Register(Options options, string modId)
    {
        _options = options;
        _modId = modId;
        ModHook.OnModAssetsLoaded += AssetsLoaded;
    }

    internal static void Unregister()
    {
        ModHook.OnModAssetsLoaded -= AssetsLoaded;
        if (_pass != null) Object.Destroy(_pass.gameObject);
        _pass = null;
        if (_stolen != null) ClearOverlays();
        _stolen?.Dispose();
        _battery?.Dispose();
        _ingredients?.Dispose();
        _water?.Dispose();
        _spill?.Dispose();
        _filter?.Dispose();
        _stolen = _battery = _ingredients = _water = _spill = _filter = null;
        _options = null;
        _modId = null;
    }

    private static void ClearOverlays()
    {
        // A global scan is only needed on disable/teardown, including closed windows.
        foreach (var overlay in Object.FindObjectsOfType<ItemWarningOverlay>(true))
        {
            overlay.Clear();
            Object.Destroy(overlay);
        }
    }

    private static void AssetsLoaded(LoadedMod mod)
    {
        if (mod.Manifest.ID != _modId || _stolen != null) return;
        try
        {
            _stolen = new WarningAnimation(_modId!, WarningAnimation.StolenFile);
            _battery = new WarningAnimation(_modId!, WarningAnimation.BatteryFile);
            _ingredients = new WarningAnimation(_modId!, WarningAnimation.IngredientsFile);
            _water = new WarningAnimation(_modId!, WarningAnimation.WaterFile);
            _spill = new WarningAnimation(_modId!, WarningAnimation.SpillFile);
            _filter = new WarningAnimation(_modId!, WarningAnimation.FilterFile);
            _pass = new GameObject("Tuck item warnings").AddComponent<ItemWarningPass>();
            Object.DontDestroyOnLoad(_pass.gameObject);
        }
        catch
        {
            Unregister();
            throw;
        }
    }

    internal static bool IsInspection(StoreClient? client) => client?.clientIntent == StoreClient.ClientIntent.INSPECTION;
    internal static bool InspectionActive() => PlayerStore.HasInstance &&
        IsInspection(PlayerStore.Instance.currentClientInstance?.GetClientBlueprint());

    private static bool ConverterAvailable() => PlayerStore.HasInstance && PlayerStore.Instance.storeService != null &&
        CurrentConverterHelper.IsServiceActive() && !CurrentConverterHelper.IsBlackedOut();

    internal static bool HasLowBattery(GameItem item, bool? converterAvailable = null)
    {
        if (item.identifier is "bottle_printer" or "recharger" or "blender" or "heating_plate" or "chem_finisher" ||
            !item.IsTag(MachineHelper.MACHINE_RECHARGABLE_TAG) || item.IsTag("BROKEN_MACHINE_TAG") ||
            !MachineryHelper.IsMachineryToggleOn(item) ||
            (item.onCycleEndEarlySlotItemFunc == null && item.onCycleEndSlotItemFunc == null && item.onCycleEndLateSlotItemFunc == null))
            return false;
        int required = MachineryHelper.GetMachinePowerUsage(item);
        var slot = MachineHelper.GetBatterySlot(item);
        if (required <= 0 || slot == null) return false;
        var source = slot.childItem;
        // Nightly converters depend on the service, not the daytime cash check in CanSupply.
        if (CurrentConverterHelper.IsCurrentConverter(source))
            return !CurrentConverterHelper.IsOn(source) || !(converterAvailable ?? ConverterAvailable());
        return !PowerHelper.CanDrawPowerSource(source, required);
    }

    internal static bool HasOnlyDepletedFilters(GameItem host)
    {
        try
        {
            MachineryContainerUtils.Collect(host.contentWindow?.childElement, FilterStorage);
            if (host.examineWindow != host.contentWindow)
                MachineryContainerUtils.Collect(host.examineWindow?.childElement, FilterStorage);
            bool hasFilter = false;
            for (int i = 0; i < FilterStorage.Count; i++)
            {
                switch (FilterStorage[i])
                {
                    case GameSlotInventory slot:
                        if (slot.childItem is not { } filter || !filter.IsTag(AmenitiesItemDirectory.WATER_FILTER)) break;
                        // UV and other nondurable filters cannot be called depleted.
                        if (!filter.IsTag(DurabilityHelper.DURABILITY_TAG) || DurabilityHelper.GetRealDurability(filter) != 0) return false;
                        hasFilter = true;
                        break;
                    case GameInventory:
                    case GameItem:
                        break; // Never enter stored items or count loose grid contents.
                    case MachineryPanelRow row:
                        // Collect directly: row.children allocates a fresh storage list.
                        for (int panel = 0; panel < row.Panels.Count; panel++) row.Panels[panel].CollectStorage(FilterStorage);
                        break;
                    case ScrollElement scroll:
                        // ScrollElement.children only exposes GraphNodeStorage, omitting machinery wrappers.
                        MachineryContainerUtils.Collect(scroll.childElement, FilterStorage);
                        break;
                    default:
                        FilterStorage.AddRange(FilterStorage[i].children);
                        break;
                }
            }
            return hasFilter;
        }
        finally { FilterStorage.Clear(); }
    }

    internal static Warning Applicable(GameItem item, StolenWarningMode stolen, bool batteryEnabled, bool ingredientsEnabled,
        bool inspection, int outputOccupiedPercent, bool? converterAvailable = null, bool waterEnabled = false, bool spillEnabled = false,
        bool filterEnabled = false) =>
        ((stolen == StolenWarningMode.Always || (stolen == StolenWarningMode.OnInspection && inspection)) &&
            StolenHelper.IsStolenItem(item) ? Warning.Stolen : Warning.None) |
        (batteryEnabled && HasLowBattery(item, converterAvailable) ? Warning.LowBattery : Warning.None) |
        MissingIngredients.Evaluate(item, ingredientsEnabled, waterEnabled) |
        (spillEnabled && OutputCapacity.Applies(item, outputOccupiedPercent) ? Warning.Spill : Warning.None) |
        (filterEnabled && HasOnlyDepletedFilters(item) ? Warning.FilterDurability : Warning.None);

    // Fill the occupied cells bottommost, then leftmost, in displayed orientation.
    internal static int Cells(GridShape shape, int orientation, bool flipped, Span<(int X, int Y)> cells)
    {
        int count = 0;
        for (int y = 0; y < shape.height; y++)
        for (int x = 0; x < shape.width; x++)
        {
            if (shape.GetLocal(x, y) == 0) continue;
            int fx = flipped ? shape.width - 1 - x : x;
            (int X, int Y) cell = (orientation % 4) switch
            {
                1 => (y, shape.width - 1 - fx),
                2 => (shape.width - 1 - fx, shape.height - 1 - y),
                3 => (shape.height - 1 - y, fx),
                _ => (fx, y)
            };
            int index = 0;
            while (index < count && (cells[index].Y > cell.Y || (cells[index].Y == cell.Y && cells[index].X < cell.X))) index++;
            if (index >= cells.Length) continue;
            count = Math.Min(count + 1, cells.Length);
            for (int move = count - 1; move > index; move--) cells[move] = cells[move - 1];
            cells[index] = cell;
        }
        return count;
    }

    internal static Vector2 Position((int X, int Y) cell, int gridSize) =>
        new(cell.X * gridSize + (gridSize - WarningAnimation.Size) / 2f,
            -cell.Y * gridSize - (gridSize - WarningAnimation.Size) / 2f);

    internal static Warning Sequence(Warning warnings, int cells, int slot)
    {
        if (slot < 0 || slot >= cells) return Warning.None;
        int index = 0;
        var sequence = Warning.None;
        foreach (var kind in Kinds)
        {
            if ((warnings & kind) == 0) continue;
            if (index == slot || (slot == cells - 1 && index > slot)) sequence |= kind;
            index++;
        }
        return sequence;
    }

    internal static void FillFrames(Warning sequence, List<Sprite> frames)
    {
        frames.Clear();
        foreach (var kind in Kinds)
            if ((sequence & kind) != 0)
                frames.AddRange((kind switch { Warning.Stolen => _stolen, Warning.LowBattery => _battery, Warning.MissingWater => _water, Warning.Spill => _spill, Warning.FilterDurability => _filter, _ => _ingredients })!.Frames);
    }

    // Run after normal input/gameplay Update, including complete battery remove/reinsert operations.
    [DefaultExecutionOrder(10000)]
    private sealed class ItemWarningPass : MonoBehaviour
    {
        private readonly List<TreeNodeRender> _nodes = new();
        private readonly HashSet<GameItemElement> _seen = new();
        private bool _wasEnabled;
        private static readonly FieldInfo SelectedItems = typeof(ItemMultiSelectHandler).GetField("selectedItems", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private void LateUpdate()
        {
            if (_options == null || _stolen == null || !PlayerStore.HasInstance ||
                RenderHandler.current == null || UISettings.current == null || WindowsHandler.current == null) return;
            var stolen = _options.StolenWarnings.Value;
            bool battery = _options.LowBatteryWarnings.Value;
            bool ingredients = _options.MissingIngredientsWarnings.Value;
            bool water = _options.MissingWaterWarnings.Value;
            bool spill = _options.SpillWarnings.Value;
            int outputOccupiedPercent = (int)_options.OutputOccupiedPercent.Value;
            bool filter = _options.FilterDurabilityWarnings.Value;
            if (stolen == StolenWarningMode.Disabled && !battery && !ingredients && !water && !spill && !filter)
            {
                if (_wasEnabled) ClearOverlays();
                _wasEnabled = false;
                return;
            }
            _wasEnabled = true;
            double animationTime = Time.timeAsDouble;
            bool inspection = stolen == StolenWarningMode.OnInspection && InspectionActive();
            bool converter = battery && ConverterAvailable();
            try
            {
                foreach (var window in WindowsHandler.current.visibleWindows)
                {
                    var root = window.childElement?.handler;
                    if (root == null || !root.gameObject.activeInHierarchy) continue;
                    // Actual UI descendants omit closed content/examine windows and linked hidden storage.
                    root.GetComponentsInChildren(false, _nodes);
                    foreach (var node in _nodes)
                        if (node.element is GameItemElement item) Refresh(item);
                }
                // Ghosts can outlive their source window's visibility and are reparented to overlayRoot.
                var drag = ItemMouseDragHandler.current;
                if (drag != null && drag.IsDraggingItem && drag.GetCurrentItem() is GameItemElement dragged) Refresh(dragged);
                var group = ItemMultiSelectHandler.current;
                if (group != null && group.IsGroupDragging)
                    foreach (var item in (List<GameItemElement>)SelectedItems.GetValue(group)) Refresh(item);
            }
            finally
            {
                _nodes.Clear();
                _seen.Clear();
            }

            void Refresh(GameItemElement item)
            {
                if (item == null || item.IsDestroyed() || item.fakeMiddle == null || !_seen.Add(item)) return;
                var overlay = item.handler.GetComponent<ItemWarningOverlay>();
                var warnings = item.isDebugMenu ? Warning.None : Applicable(item, stolen, battery, ingredients, inspection, outputOccupiedPercent, converter, water, spill, filter);
                if (overlay == null)
                {
                    if (warnings == Warning.None) return;
                    overlay = item.handler.gameObject.AddComponent<ItemWarningOverlay>();
                }
                overlay.Refresh(item, warnings, animationTime);
            }
        }
    }
}

internal sealed class ItemWarningOverlay : MonoBehaviour
{
    private readonly Cell?[] _cells = new Cell?[ItemWarnings.Kinds.Length];

    internal void Refresh(GameItemElement item, ItemWarnings.Warning warnings, double animationTime)
    {
        Span<(int X, int Y)> real = stackalloc (int, int)[_cells.Length];
        Span<(int X, int Y)> ghost = stackalloc (int, int)[_cells.Length];
        int available = ItemWarnings.Cells(item.modifiedShape, item.modifiedShape.orientation, item.modifiedShape.flipped, real);
        // The ghost transform is authoritative even before tempShape has been assigned.
        var rotation = item.fakeRotator.rectTransform;
        ItemWarnings.Cells(item.modifiedShape, Mathf.RoundToInt(rotation.localEulerAngles.z / 90f) % 4,
            rotation.localScale.x < 0, ghost);
        for (int i = 0; i < _cells.Length; i++)
        {
            var sequence = ItemWarnings.Sequence(warnings, available, i);
            var cell = _cells[i];
            if (cell == null)
            {
                if (sequence == ItemWarnings.Warning.None) continue;
                _cells[i] = cell = new Cell(item.middle, item.fakeMiddle);
            }
            cell.Refresh(sequence, animationTime);
            Draw(cell.Real, item.background, i < available ? real[i] : null, sequence);
            Draw(cell.Ghost, item.fakeBackground, i < available ? ghost[i] : null, sequence);
        }
        int realLast = item.middle.rectTransform.childCount - 1;
        int ghostLast = item.fakeMiddle.rectTransform.childCount - 1;
        for (int i = _cells.Length - 1; i >= 0; i--)
            if (_cells[i] is { } cell)
            {
                if (cell.Real.rectTransform.GetSiblingIndex() != realLast) cell.Real.rectTransform.SetSiblingIndex(realLast);
                if (cell.Ghost.rectTransform.GetSiblingIndex() != ghostLast) cell.Ghost.rectTransform.SetSiblingIndex(ghostLast);
                realLast--;
                ghostLast--;
            }
    }

    private static void Draw(TreeNodeRender node, TreeNodeRender background, (int X, int Y)? cell, ItemWarnings.Warning sequence)
    {
        bool visible = cell.HasValue && sequence != ItemWarnings.Warning.None && background.image.isActiveAndEnabled;
        if (node.image.enabled != visible) node.image.enabled = visible;
        if (!visible) return;
        var position = ItemWarnings.Position(cell!.Value, UISettings.current.gridSize);
        if (node.rectTransform.anchoredPosition != position) node.rectTransform.anchoredPosition = position;
        var color = new Color(1f, 1f, 1f, background.image.color.a);
        if (node.image.color != color) node.image.color = color;
    }

    internal void Clear()
    {
        foreach (var cell in _cells) cell?.Destroy();
        Array.Clear(_cells, 0, _cells.Length);
    }

    private void OnDestroy() => Clear();

    private sealed class Cell
    {
        internal readonly TreeNodeRender Real;
        internal readonly TreeNodeRender Ghost;
        private readonly List<Sprite> _frames = new();
        private ItemWarnings.Warning _sequence;

        internal Cell(TreeNodeRender realParent, TreeNodeRender ghostParent)
        {
            Real = CreateNode(realParent);
            Ghost = CreateNode(ghostParent);
        }

        private static TreeNodeRender CreateNode(TreeNodeRender parent)
        {
            var node = TreeNodeRender.Instantiate().SetParent(parent);
            node.gameObject.name = "Tuck item warning";
            node.image.raycastTarget = false;
            node.rectTransform.anchorMin = node.rectTransform.anchorMax = node.rectTransform.pivot = new Vector2(0f, 1f);
            node.rectTransform.sizeDelta = new Vector2(WarningAnimation.Size, WarningAnimation.Size);
            return node;
        }

        internal void Refresh(ItemWarnings.Warning sequence, double animationTime)
        {
            if (_sequence != sequence)
            {
                _sequence = sequence;
                ItemWarnings.FillFrames(sequence, _frames);
            }
            if (_frames.Count == 0) return;
            var sprite = _frames[WarningAnimation.FrameAt(animationTime, _frames.Count)];
            if (Real.image.sprite != sprite) Real.image.sprite = sprite;
            if (Ghost.image.sprite != sprite) Ghost.image.sprite = sprite;
        }

        internal void Destroy()
        {
            DestroyNode(Real);
            DestroyNode(Ghost);
        }

        private static void DestroyNode(TreeNodeRender node)
        {
            if (node == null) return;
            node.image.enabled = false;
            Object.Destroy(node.gameObject);
        }
    }
}
