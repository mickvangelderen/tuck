using global::Tuck.PackSolver;

namespace Tuck.PackSolver.Benchmarks;

internal static class Scenarios
{
    internal static InventoryState[] Inventories(int width = 4, int height = 1) =>
    [
        new("ground", new InventoryLayout.Grid(ShapeState.Rectangle(width, height, 0, 1)), new(AdmissionKind.Ground)),
        new("back", new InventoryLayout.Slot(), new(AdmissionKind.Backpack)),
        new("left", new InventoryLayout.Slot(), new(AdmissionKind.Owned)),
        new("right", new InventoryLayout.Slot(), new(AdmissionKind.Owned))
    ];

    internal static ItemState Item(string id, string inventory, long value, int x = 0, int y = 0,
        int width = 1, int height = 1, int count = 1, params string[] tags) =>
        new(id, id, new(inventory, inventory is "back" or "left" or "right" ? new Placement.Slot() : new Placement.Grid(x, y)),
            count, value, ShapeState.Rectangle(width, height),
            new(new[] { "IS_OWNED_TAG" }.Concat(tags).Select(t => new KeyValuePair<string, TagValue>(t, new(true)))));

    internal static PackState State(string name, IEnumerable<ItemState> items, InventoryState[]? inventories = null)
    {
        var all = items.ToArray();
        return new(name, new("ground", "back", "left", "right"), all, inventories ?? Inventories(),
            all.Select(i => new ItemDefinition(i.Identifier, new(new(i.UnitCount)))));
    }

    internal static InventoryState Bag(int width, int height = 1) =>
        new("inside", new InventoryLayout.Grid(ShapeState.Rectangle(width, height, 0, 1)), new(AdmissionKind.Container), "bag");

    internal static PackState Satchel(int seed = -1)
    {
        var random = new Random(seed);
        long Value(long original) => seed < 0 ? original : random.Next(1, 101);
        var items = new[]
        {
            Item("loot", "ground", Value(3), width: 3, height: 2),
            Item("bag", "back", 150, width: 3, height: 3, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("tool", "left", Value(100), width: 2, height: 3),
            Item("held", "right", Value(3), width: 3, height: 2),
            Item("junk", "inside", Value(3), width: 3, height: 2),
            Item("milk", "inside", Value(16), x: 3, width: 2, height: 3)
        };
        // Vary capture order as well as value; geometry retains a known all-items-returned witness.
        if (seed >= 0) random.Shuffle(items);
        return State(seed < 0 ? "satchel-rearrangement" : $"satchel-{seed}", items,
            [.. Inventories(12, 9), Bag(6, 3)]);
    }

    internal static IEnumerable<PackState> Benchmarks()
    {
        yield return State("direct-packing", [Item("loot", "ground", 10)]);
        yield return State("replace-hand", [Item("cheap", "left", 1), Item("keep", "right", 20), Item("loot", "ground", 10)]);
        yield return State("full-ground", [Item("cheap", "left", 1), Item("keep", "right", 20), Item("loot", "ground", 10)], Inventories(1));
        yield return State("ground-buffer", [Item("loot", "ground", 20, width: 2), Item("clutter", "ground", 1, x: 3),
            Item("cheap", "left", 2, width: 2), Item("fixed", "right", 1, tags: ["PINNED_TAG"])], Inventories(5));
        yield return Satchel();
        yield return State("pack-container", [Item("bag", "ground", 2, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("loot", "ground", 50, x: 1), Item("left", "left", 1, tags: ["PINNED_TAG"]),
            Item("right", "right", 1, tags: ["PINNED_TAG"])], [.. Inventories(), Bag(1)]);
        yield return State("intact-stack", [Item("coins", "ground", 10, count: 5), Item("cheap", "left", 5),
            Item("fixed", "right", 1, tags: ["PINNED_TAG"])]);
        yield return State("equipment", [Item("machine", "ground", 100), Item("component", "equipment", 50)],
            [.. Inventories(), new("equipment", new InventoryLayout.Grid(ShapeState.Rectangle(2, 1, 0, 1)), new(AdmissionKind.Owned), "machine")]);
        yield return State("crowded-selection", Enumerable.Range(0, 7).Select(i => Item($"loot-{i}", "ground", (i * 17 % 23) + 1, x: i))
            .Concat([Item("bag", "back", 2, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]), Item("left", "left", 1), Item("right", "right", 2)]),
            [.. Inventories(9), Bag(3)]);
        yield return State("already-best", [Item("left", "left", 10), Item("right", "right", 10), Item("ground", "ground", 10)]);
    }

    // Tiny finite graphs suitable for an independent BFS oracle. Unit squares make all rotations
    // equivalent, so the oracle can enumerate one orientation without dropping legal arrangements.
    internal static PackState Tiny(int seed)
    {
        var random = new Random(seed);
        bool bag = seed % 3 == 0;
        var inventories = Inventories(random.Next(1, 4));
        var items = new List<ItemState>
        {
            Item("a", "left", random.Next(1, 11), count: random.Next(1, 3), tags: seed % 7 == 0 ? ["PINNED_TAG"] : []),
            Item("b", "right", random.Next(1, 11), tags: seed % 5 == 0 ? ["HAZARDOUS_WASTE_TAG"] : []),
            Item("c", "ground", random.Next(1, 11), count: random.Next(1, 3))
        };
        if (bag)
        {
            inventories = [.. inventories, Bag(random.Next(1, 3))];
            items.Add(Item("bag", "back", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG", "PINNED_TAG"]));
            items.Add(Item("d", "inside", random.Next(1, 11)));
        }
        var shuffled = items.ToArray();
        random.Shuffle(shuffled);
        return State($"tiny-{seed}", shuffled, inventories);
    }

    internal static PackState Buffer(int seed)
    {
        var random = new Random(seed);
        var items = new[]
        {
            Item("loot", "ground", random.Next(10, 51), width: 2),
            Item("clutter", "ground", random.Next(1, 5), x: random.Next(2, 5)),
            Item("cheap", "left", random.Next(1, 10), width: 2),
            Item("fixed", "right", 1, tags: ["PINNED_TAG"])
        };
        random.Shuffle(items);
        return State($"buffer-{seed}", items, Inventories(5));
    }

    internal static PackState Crowded(int seed)
    {
        var random = new Random(seed);
        var items = Enumerable.Range(0, 7).Select(i => Item($"loot-{i}", "ground", random.Next(3, 51), x: i))
            .Concat([Item("bag", "back", 2, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
                Item("left", "left", 1), Item("right", "right", 2)]).ToArray();
        random.Shuffle(items);
        return State($"crowded-{seed}", items, [.. Inventories(9), Bag(3)]);
    }
}
