using Tuck.PackSolver;

namespace Tuck.Mod.Tests;

/// <summary>Detached snapshots and explicit transfers for recording codec tests.</summary>
internal static class RecordingFixtures
{
    internal static readonly PackRoots Roots = new("ground", "back", "left", "right");
    internal static InventoryState[] Inventories(int groundWidth = 4) =>
    [
        new("ground", new InventoryLayout.Grid(ShapeState.Rectangle(groundWidth, 2, 0, 1)), new(AdmissionKind.Ground)),
        new("back", new InventoryLayout.Slot(), new(AdmissionKind.Backpack)),
        new("left", new InventoryLayout.Slot(), new(AdmissionKind.Owned)),
        new("right", new InventoryLayout.Slot(), new(AdmissionKind.Owned))
    ];
    internal static ItemFacts Facts(string[]? tags = null, string[]? types = null, int unlocked = 0) => new(
        new[] { "IS_OWNED_TAG" }.Concat(tags ?? []).Select(t => new KeyValuePair<string, TagValue>(t, new(true)))
            .Concat(unlocked == 0 ? [] : new[] { new KeyValuePair<string, TagValue>("TOOLBOX_UNLOCKED_SLOTS_INT", new(false, unlocked)) }), types);
    internal static ItemState Item(string id, string inventory, long value, int x = 0, int y = 0,
        string[]? tags = null, ShapeState? shape = null, int count = 1, string? identifier = null, string[]? types = null, int unlocked = 0) =>
        new(id, identifier ?? id, new(inventory, inventory is "back" or "left" or "right" ? new Placement.Slot() : new Placement.Grid(x, y)),
            count, value, shape ?? ShapeState.Rectangle(1, 1), Facts(tags, types, unlocked));
    internal static PackState State(ItemState[] items, InventoryState[]? inventories = null, params ItemDefinition[] definitions)
    {
        var known = definitions.ToDictionary(d => d.Identifier);
        foreach (var item in items) known.TryAdd(item.Identifier, new(item.Identifier));
        return new("test", Roots, items, inventories ?? Inventories(), known.Values);
    }
    internal static TransferStep Place(string item, string inventory, Placement? placement = null) =>
        new(item, new(inventory, placement ?? (inventory is "back" or "left" or "right" ? new Placement.Slot() : new Placement.Grid())));
}
