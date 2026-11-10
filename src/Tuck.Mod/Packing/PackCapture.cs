using System;
using System.Collections.Generic;
using System.Linq;
using global::Tuck.PackSolver;

namespace Tuck.Mod.Packing;

internal sealed class CapturedPack
{
    internal PackState State { get; }
    internal LiveBindings Bindings { get; }
#if DEBUG
    internal System.Collections.Generic.IReadOnlyList<DebugRecording.ItemObservation> Observations { get; }
#endif
    internal CapturedPack(PackState state, LiveBindings bindings)
    {
        State = state; Bindings = bindings;
#if DEBUG
        Observations = DebugRecording.GameObservations.Capture(bindings);
#endif
    }
}

/// <summary>Invocation-local identity binding, separate from the immutable snapshot.</summary>
internal sealed class LiveBindings
{
    internal Dictionary<string, GameItem> Items { get; } = new();
    internal Dictionary<string, GameInventory> Inventories { get; } = new();
    internal PackRoots Roots { get; } = new("ground", "backpack", "left", "right");
    internal Func<bool> AllowsHazardousWaste { get; }
    internal LiveBindings(Func<bool> allowsHazardousWaste) => AllowsHazardousWaste = allowsHazardousWaste;
}

internal static class PackCapture
{
    internal static CapturedPack Capture(GameInventory ground, GameSlotInventory backpack, GameSlotInventory left,
        GameSlotInventory right, Func<bool> allowsHazardousWaste)
    {
        var bindings = new LiveBindings(allowsHazardousWaste);
        bindings.Inventories.Add(bindings.Roots.Ground, ground);
        bindings.Inventories.Add(bindings.Roots.Backpack, backpack);
        bindings.Inventories.Add(bindings.Roots.Left, left);
        bindings.Inventories.Add(bindings.Roots.Right, right);
        var graph = Walk(bindings);
        foreach (var item in graph.Items) bindings.Items.Add("item-" + bindings.Items.Count, item);
        foreach (var inventory in graph.Owners.Keys)
            if (!bindings.Inventories.ContainsValue(inventory)) bindings.Inventories.Add("inventory-" + bindings.Inventories.Count, inventory);
        return new CapturedPack(Read(bindings, Guid.NewGuid().ToString("N"), graph), bindings);
    }
    internal static PackState Read(LiveBindings bindings, string stateId) => Read(bindings, stateId, Walk(bindings));
    private static PackState Read(LiveBindings bindings, string stateId, Graph graph)
    {
        var itemIds = bindings.Items.ToDictionary(p => p.Value, p => p.Key);
        var inventoryIds = bindings.Inventories.ToDictionary(p => p.Value, p => p.Key);
        if (graph.Items.Count != itemIds.Count || graph.Owners.Count != inventoryIds.Count
            || graph.Items.Any(i => !itemIds.ContainsKey(i)) || graph.Owners.Keys.Any(i => !inventoryIds.ContainsKey(i)))
            throw new InvalidOperationException("Live item/inventory membership changed.");
        bool allowsHazardousWaste = bindings.AllowsHazardousWaste();
        var inventories = graph.Owners.Select(pair =>
        {
            var live = pair.Key;
            var rule = GameRuleCapture.InventoryRule(live, pair.Value, inventoryIds[live], bindings.Roots, allowsHazardousWaste);
            if (live is not GameGridInventory && live is not GameSlotInventory)
                throw new NotSupportedException("Packing contains an unsupported inventory kind.");
            if (live is GameGridInventory g && (g.inventoryShape.minX != 0 || g.inventoryShape.minY != 0
                || g.inventoryShape.orientation != 0 || g.inventoryShape.flipped))
                throw new NotSupportedException("Transformed inventory coordinate frames are not represented.");
            return new InventoryState(inventoryIds[live], live is GameSlotInventory slot
                    ? new InventoryLayout.Slot(slot.defaultOrientation) : new InventoryLayout.Grid(Shape(((GameGridInventory)live).inventoryShape)),
                rule.Rule, pair.Value == null ? null : itemIds[pair.Value], live.overrideLockInsert, live.overrideLockRemove,
                unsupportedTransfer: rule.Unsupported, unsupportedParentEffects:
                    live.parentItemShapeModifierForItemFunc != null || live.parentItemStateModifierForItemFunc != null);
        }).ToArray();
        var items = graph.Items.Select(live =>
        {
            if (live.parentInventory == null || !inventoryIds.TryGetValue(live.parentInventory, out var parentId))
                throw new InvalidOperationException("Item has no captured parent inventory.");
            var a = live.modifiedShape.ForwardTransform(live.modifiedXOrigin, live.modifiedYOrigin);
            var b = live.modifiedShape.ForwardTransform(live.modifiedXOrigin + live.shape.width - 1, live.modifiedYOrigin + live.shape.height - 1);
            Placement placement = live.parentInventory is GameSlotInventory ? new Placement.Slot()
                : new Placement.Grid(Math.Min(a.Item1, b.Item1), Math.Min(a.Item2, b.Item2), live.modifiedShape.orientation, live.modifiedShape.flipped);
            return new ItemState(itemIds[live], live.identifier, new(parentId, placement), live.unitCount,
                live.unitValue, Shape(live.shape), Facts(live.state, live), live.unitBaseValue);
        }).ToArray();
        var definitions = new Dictionary<string, ItemDefinition>();
        foreach (var live in graph.Items)
        {
            var definition = GameRuleCapture.Definition(live);
            if (definitions.TryGetValue(live.identifier, out var previous) && !StateComparison.SameDefinition(previous, definition))
                throw new NotSupportedException("Instances disagree about factory rules: " + live.identifier);
            definitions[live.identifier] = definition;
        }
        return new PackState(stateId, bindings.Roots, items, inventories, definitions.Values);
    }
    internal static ItemFacts Facts(TagSystem state, GameItem item) => new(
        GameRuleCapture.Tags.Concat(GameRuleCapture.IntTags).Where(state.dict.ContainsKey).Select(name =>
        {
            var tag = state.dict[name];
            return new KeyValuePair<string, TagValue>(name, new(tag.valueEnabled, GameRuleCapture.IntTags.Contains(name) ? tag.valueInt : 0));
        }).Where(p => p.Value.Enabled || p.Value.Int != 0), GameRuleCapture.Types.Where(item.IsGameItemType));
    internal static ShapeState Shape(GridShape shape) => new(shape.width, shape.height,
        Enumerable.Range(0, checked(shape.width * shape.height)).Select(n => shape.GetLocal(n % shape.width, n / shape.width)),
        shape.GetLocal(-1, -1));
    internal static Placement.Grid Transform(GridShape shape) => new(shape.minX, shape.minY, shape.orientation, shape.flipped);

    private sealed class Graph
    {
        internal List<GameItem> Items { get; } = new();
        internal Dictionary<GameInventory, GameItem?> Owners { get; } = new();
    }
    private static Graph Walk(LiveBindings bindings)
    {
        var graph = new Graph();
        var seen = new HashSet<(GraphNodeStorage, GameItem?)>();
        var itemSet = new HashSet<GameItem>();
        var pending = new Queue<(GraphNodeStorage Node, GameItem? Owner)>();
        foreach (var id in new[] { bindings.Roots.Ground, bindings.Roots.Backpack, bindings.Roots.Left, bindings.Roots.Right })
            pending.Enqueue((bindings.Inventories[id], null));
        while (pending.Count > 0)
        {
            var (node, owner) = pending.Dequeue();
            if (!seen.Add((node, owner))) continue;
            if (seen.Count > 4096) throw new NotSupportedException("Packing graph exceeds the capture limit.");
            if (node is GameInventory inventory)
            {
                if (graph.Owners.TryGetValue(inventory, out var previous) && !ReferenceEquals(previous, owner))
                    throw new NotSupportedException("Inventory has ambiguous ownership.");
                graph.Owners[inventory] = owner;
            }
            if (node is GameItem item)
            {
                if (itemSet.Add(item)) graph.Items.Add(item);
                owner = item;
            }
            // Slot.children also includes borrowed UI; the physical contents are childItems.
            IEnumerable<GraphNodeStorage> children = node is GameInventory inv ? inv.childItems : node.children;
            foreach (var child in children) if (child != null) pending.Enqueue((child, owner));
        }
        return graph;
    }
}
