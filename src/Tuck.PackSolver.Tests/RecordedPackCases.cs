using global::Tuck.PackSolver;

namespace Tuck.PackSolver.Tests;

internal sealed record RecordedPackCase(PackState State, long MinimumReturnedValue, TransferStep[] Witness);

// Editable C# cases recovered from game interactions. No external files are needed.
// Add an entry here to run it against every solver, including reduced reproductions.
internal static class RecordedPackCases
{
    internal static IReadOnlyDictionary<string, RecordedPackCase> All { get; } = CreateCases();

    private static IReadOnlyDictionary<string, RecordedPackCase> CreateCases()
    {
        var fanny = new RecordedPackCase(FannyPackState(), 165,
        [
            Place("item-4", "inventory-2", new Placement.Slot()),
            Place("item-5", "inventory-3", new Placement.Slot()),
            Place("item-6", "inventory-10", new Placement.Grid(1, 0))
        ]);
        var satchel = new RecordedPackCase(SatchelState(), 275,
        [
            Place("item-4", "inventory-4", new Placement.Grid(0, 0, 1)),
            Place("item-5", "inventory-4", new Placement.Grid(4, 0)),
            Place("item-0", "inventory-4", new Placement.Grid(2, 0, 1))
        ]);
        var satchelRetry = new RecordedPackCase(SatchelState(junkDropped: true), 275,
        [
            Place("item-5", "inventory-4", new Placement.Grid(4, 0)),
            Place("item-0", "inventory-4", new Placement.Grid(0, 0, 1)),
            Place("item-1", "inventory-4", new Placement.Grid(2, 0, 1))
        ]);
        var toolbox = new RecordedPackCase(ToolboxState(), 1590,
        [
            Place("item-13", "inventory-10", new Placement.Slot()),
            Place("item-2", "inventory-4", new Placement.Grid(0, 0))
        ]);
        var shard = new RecordedPackCase(ShardState(), 1585,
        [
            Place("item-13", "ground", new Placement.Grid(0, 0)),
            Place("item-8", "inventory-4", new Placement.Grid(0, 0, 1)),
            Place("item-9", "inventory-4", new Placement.Grid(2, 0)),
            Place("item-10", "inventory-4", new Placement.Grid(2, 1)),
            Place("item-6", "inventory-4", new Placement.Grid(3, 0, 1)),
            Place("item-2", "inventory-4", new Placement.Grid(4, 0, 1))
        ]);
        return new Dictionary<string, RecordedPackCase>
        {
            ["fanny-pack"] = fanny,
            ["satchel-rearrangement"] = satchel,
            ["satchel-manual-retry"] = satchelRetry,
            ["electronics-toolbox"] = toolbox,
            ["electronics-toolbox-reduced"] = toolbox with
            {
                State = ReducedToolboxState(), MinimumReturnedValue = 598
            },
            ["electronics-glass-shard"] = shard
        };
    }

    private static PackState FannyPackState()
    {
        ItemState[] items =
        [
            new("item-4", "kotton_fabric", new("inventory-0", new Placement.Grid()),
                1, 65, ShapeState.Rectangle(3, 1), Facts()),
            new("item-5", "junk", new("inventory-0", new Placement.Grid(3, 0)),
                1, 3, ShapeState.Rectangle(3, 2), Facts()),
            new("item-6", "hemostatic_bandage_item", new("inventory-0", new Placement.Grid(6, 0)),
                1, 16, ShapeState.Rectangle(1, 2), Facts()),
            new("item-7", "junk", new("inventory-0", new Placement.Grid(7, 0)),
                1, 3, ShapeState.Rectangle(3, 2), Facts()),
            new("item-8", "junk", new("inventory-0", new Placement.Grid(0, 1)),
                1, 3, ShapeState.Rectangle(3, 2), Facts()),
            new("item-9", "fanny_pack", new("inventory-1", new Placement.Slot()),
                1, 50, ShapeState.Rectangle(3, 2), Facts(["BACKPACK_TAG", "CONTAINER_TAG"])),
            new("item-12", "topical_bandage_item", new("inventory-10", new Placement.Grid()),
                1, 16, ShapeState.Rectangle(1, 2), Facts()),
            new("item-13", "toothpaste", new("inventory-10", new Placement.Grid(0, 2)),
                1, 15, ShapeState.Rectangle(3, 1), Facts())
        ];
        InventoryState[] inventories =
        [
            new("inventory-0", new InventoryLayout.Grid(ShapeState.Rectangle(12, 9, 0, 1)), new(AdmissionKind.Ground)),
            new("inventory-1", new InventoryLayout.Slot(), new(AdmissionKind.Backpack)),
            new("inventory-2", new InventoryLayout.Slot(), new(AdmissionKind.Owned)),
            new("inventory-3", new InventoryLayout.Slot(), new(AdmissionKind.Owned)),
            new("inventory-10", new InventoryLayout.Grid(ShapeState.Rectangle(3, 3, 0, 1)), new(AdmissionKind.Container), "item-9")
        ];
        return new PackState("day-two-fanny-pack", new("inventory-0", "inventory-1", "inventory-2", "inventory-3"),
            items, inventories, items.Select(i => i.Identifier).Distinct().Select(id => new ItemDefinition(id)));
    }

    private static PackState SatchelState(bool junkDropped = false)
    {
        string satchel = junkDropped ? "item-2" : "item-1";
        var items = new List<ItemState>
        {
            Item("item-0", "ground", 3, shape: ShapeState.Rectangle(3, 2), identifier: "junk")
        };
        if (junkDropped)
            items.Add(Item("item-1", "ground", 3, 8, 2, shape: ShapeState.Rectangle(3, 2), identifier: "junk"));
        items.Add(new(satchel, "satchel", new("backpack", new Placement.Slot()), 1, 150,
            ShapeState.Rectangle(3, 3), Facts(["BACKPACK_TAG", "CONTAINER_TAG"])));
        items.Add(Item(junkDropped ? "item-3" : "item-2", "left", 100,
            shape: ShapeState.Rectangle(2, 3), identifier: "module_extractor", types: ["TOOL"]));
        items.Add(Item(junkDropped ? "item-4" : "item-3", "right", 3,
            shape: ShapeState.Rectangle(3, 2), identifier: "junk"));
        if (!junkDropped)
            items.Add(Item("item-4", "inventory-4", 3, shape: ShapeState.Rectangle(3, 2), identifier: "junk"));
        items.Add(Item("item-5", "inventory-4", 16, 3, 0,
            shape: ShapeState.Rectangle(2, 3), identifier: "processed_milk"));

        return new("satchel-rearrangement", new("ground", "backpack", "left", "right"), items,
        [
            new("ground", new InventoryLayout.Grid(ShapeState.Rectangle(12, 9, 0, 1)),
                new(AdmissionKind.Ground, allowsHazardousWaste: true)),
            new("backpack", new InventoryLayout.Slot(), new(AdmissionKind.Backpack)),
            new("left", new InventoryLayout.Slot(), new(AdmissionKind.Owned)),
            new("right", new InventoryLayout.Slot(), new(AdmissionKind.Owned)),
            new("inventory-4", new InventoryLayout.Grid(ShapeState.Rectangle(6, 3, 0, 1)),
                new(AdmissionKind.Container), satchel)
        ],
        [new("junk"), new("satchel"), new("module_extractor"), new("processed_milk")]);
    }

    // Put the gun parts in the toolbox to free a 3x2 space for electronics.
    private static PackState ToolboxState() => ElectronicsState("electronics-toolbox", "item-4",
    [
        Loot("item-0", "junk", "ground", 3, 3, 2, new Placement.Grid(0, 2)),
        Loot("item-1", "junk", "ground", 3, 3, 2, new Placement.Grid(3, 2)),
        Loot("item-2", "common_electronic", "ground", 33, 3, 2, new Placement.Grid(7, 5, 2)),
        Loot("item-3", "junk", "ground", 3, 3, 2, new Placement.Grid(7, 0)),
        Loot("item-4", "backpack_small", "backpack", 300, 3, 4, new Placement.Slot(), Facts(["BACKPACK_TAG", "CONTAINER_TAG"])),
        Loot("item-5", "li_eat_snackbar", "left", 17, 3, 1, new Placement.Slot(), SnackFacts()),
        Loot("item-6", "junk", "right", 3, 3, 2, new Placement.Slot()),
        Loot("item-7", "flux_agent", "inventory-4", 4, 1, 1, new Placement.Grid(3, 0)),
        Loot("item-8", "topical_bandage_item", "inventory-4", 16, 1, 2, new Placement.Grid(4, 0, 1), Facts(["MEDICAL"])),
        Loot("item-9", "junk", "inventory-4", 3, 3, 2, new Placement.Grid(3, 1)),
        Loot("item-10", "glass_shard", "inventory-4", 2, 3, 1, new Placement.Grid(0, 2)),
        Loot("item-11", "junk", "inventory-4", 3, 3, 2, new Placement.Grid(0, 3, 3)),
        Toolbox(),
        Loot("item-13", "gun_part", "inventory-4", 25, 3, 2, new Placement.Grid(), Facts(["CAN_USE_ON_NON_OWNED_TAG"])),
        .. Tools()
    ]);

    // Discarding the shard leaves six cells, which can be rearranged to fit electronics.
    // The left pocket is also free; solvers may choose a better solution using that space.
    private static PackState ShardState() => ElectronicsState("electronics-glass-shard", "item-5",
    [
        Loot("item-0", "junk", "ground", 3, 3, 2, new Placement.Grid(0, 2)),
        Loot("item-1", "junk", "ground", 3, 3, 2, new Placement.Grid(3, 2)),
        Loot("item-2", "common_electronic", "ground", 33, 3, 2, new Placement.Grid(7, 5, 2)),
        Loot("item-3", "junk", "ground", 3, 3, 2, new Placement.Grid(7, 0)),
        Loot("item-4", "junk", "ground", 3, 3, 2, new Placement.Grid(2, 6)),
        Loot("item-5", "backpack_small", "backpack", 300, 3, 4, new Placement.Slot(), Facts(["BACKPACK_TAG", "CONTAINER_TAG"])),
        Loot("item-6", "li_eat_snackbar", "inventory-4", 17, 3, 1, new Placement.Grid(3, 1), SnackFacts()),
        Loot("item-7", "junk", "right", 3, 3, 2, new Placement.Slot()),
        Loot("item-8", "gun_part", "inventory-4", 25, 3, 2, new Placement.Grid(), Facts(["CAN_USE_ON_NON_OWNED_TAG"])),
        Loot("item-9", "flux_agent", "inventory-4", 4, 1, 1, new Placement.Grid(3, 0)),
        Loot("item-10", "topical_bandage_item", "inventory-4", 16, 1, 2, new Placement.Grid(4, 0, 1), Facts(["MEDICAL"])),
        Loot("item-11", "junk", "inventory-4", 3, 3, 2, new Placement.Grid(0, 3, 3)),
        Toolbox(),
        Loot("item-13", "glass_shard", "inventory-4", 2, 3, 1, new Placement.Grid(1, 2)),
        .. Tools()
    ]);

    // Reduced toolbox reproduction. Item IDs and order affect search tie-breaking.
    private static PackState ReducedToolboxState()
    {
        ItemState[] items =
        [
            Loot("item-0", "junk", "ground", 3, 3, 2, new Placement.Grid(0, 2)),
            Loot("item-1", "junk", "ground", 3, 3, 2, new Placement.Grid(3, 2)),
            Loot("item-2", "common_electronic", "ground", 33, 3, 2, new Placement.Grid(7, 5, 2)),
            Loot("item-3", "junk", "ground", 3, 3, 2, new Placement.Grid(7, 0)),
            Loot("item-4", "backpack_small", "backpack", 300, 3, 4, new Placement.Slot(),
                Facts(["BACKPACK_TAG", "CONTAINER_TAG"])),
            Loot("item-5", "li_eat_snackbar", "left", 17, 3, 1, new Placement.Slot(),
                new(new Dictionary<string, TagValue>
                {
                    ["IS_OWNED_TAG"] = new(true), ["amenities"] = new(true, 4), ["amenities_use"] = new(true, 2)
                })),
            Loot("item-6", "junk", "right", 3, 3, 2, new Placement.Slot()),
            Loot("item-9", "junk", "inventory-4", 3, 3, 2, new Placement.Grid(3, 1)),
            Loot("item-11", "junk", "inventory-4", 3, 3, 2, new Placement.Grid(0, 3, 3)),
            Loot("item-12", "toolbox", "inventory-4", 210, 4, 3, new Placement.Grid(2, 3),
                new(new Dictionary<string, TagValue>
                {
                    ["IS_OWNED_TAG"] = new(true), ["CONTAINER_TAG"] = new(true),
                    ["NESTABLE_DEVICE_TAG"] = new(true), ["TOOLBOX_UNLOCKED_SLOTS_INT"] = new(true, 9)
                }, ["STORAGE"])),
            Loot("item-13", "gun_part", "inventory-4", 25, 3, 2, new Placement.Grid(), Facts(["CAN_USE_ON_NON_OWNED_TAG"]))
        ];
        return new("reduced-toolbox", new("ground", "backpack", "left", "right"), items,
        [
            new("ground", new InventoryLayout.Grid(ShapeState.Rectangle(12, 9, 0, 1)), new(AdmissionKind.Ground, allowsHazardousWaste: true)),
            new("backpack", new InventoryLayout.Slot(), new(AdmissionKind.Backpack)),
            new("left", new InventoryLayout.Slot(), new(AdmissionKind.Owned)),
            new("right", new InventoryLayout.Slot(), new(AdmissionKind.Owned)),
            new("inventory-4", new InventoryLayout.Grid(ShapeState.Rectangle(6, 6, 0, 1)), new(AdmissionKind.Container), "item-4"),
            new("inventory-10", new InventoryLayout.Slot(), new(AdmissionKind.Toolbox, slotIndex: 5), "item-12")
        ], items.Select(i => i.Identifier).Distinct().Select(id => new ItemDefinition(id)));
    }

    private static ItemState Toolbox() => Loot("item-12", "toolbox", "inventory-4", 210, 4, 3,
        new Placement.Grid(2, 3), new(new Dictionary<string, TagValue>
        {
            ["IS_OWNED_TAG"] = new(true), ["CONTAINER_TAG"] = new(true),
            ["NESTABLE_DEVICE_TAG"] = new(true), ["TOOLBOX_UNLOCKED_SLOTS_INT"] = new(true, 9)
        }, ["STORAGE"]));

    private static ItemState[] Tools() =>
    [
        Loot("item-14", "screwdriver", "inventory-5", 25, 1, 3, new Placement.Slot(), Facts(types: ["TOOL"])),
        Loot("item-15", "wire_cutter", "inventory-6", 40, 1, 3, new Placement.Slot(), Facts(types: ["TOOL"])),
        Loot("item-16", "chem_scanner", "inventory-8", 400, 2, 3, new Placement.Slot(), Facts(["CAN_USE_ON_NON_OWNED_TAG"])),
        Loot("item-17", "metal_scanner", "inventory-13", 505, 4, 3, new Placement.Slot(), Facts(types: ["TOOL"]))
    ];

    private static ItemFacts SnackFacts() => new(new Dictionary<string, TagValue>
    {
        ["IS_OWNED_TAG"] = new(true), ["amenities"] = new(true, 4), ["amenities_use"] = new(true, 2)
    });

    private static ItemState Loot(string id, string identifier, string inventory, long value,
        int width, int height, Placement placement, ItemFacts? facts = null) =>
        new(id, identifier, new(inventory, placement), 1, value, ShapeState.Rectangle(width, height), facts ?? Facts());

    private static PackState ElectronicsState(string id, string backpack, ItemState[] items) => new(id,
        new("ground", "backpack", "left", "right"), items,
        new InventoryState[]
        {
            new("ground", new InventoryLayout.Grid(ShapeState.Rectangle(12, 9, 0, 1)), new(AdmissionKind.Ground, allowsHazardousWaste: true)),
            new("backpack", new InventoryLayout.Slot(), new(AdmissionKind.Backpack)),
            new("left", new InventoryLayout.Slot(), new(AdmissionKind.Owned)),
            new("right", new InventoryLayout.Slot(), new(AdmissionKind.Owned)),
            new("inventory-4", new InventoryLayout.Grid(ShapeState.Rectangle(6, 6, 0, 1)), new(AdmissionKind.Container), backpack)
        }.Concat(Enumerable.Range(0, 9).Select(slot => new InventoryState($"inventory-{slot + 5}",
            new InventoryLayout.Slot(), new(AdmissionKind.Toolbox, slotIndex: slot), "item-12"))),
        items.Select(i => i.Identifier).Distinct().Select(id => new ItemDefinition(id)));

    private static ItemFacts Facts(string[]? tags = null, string[]? types = null) => new(
        new[] { "IS_OWNED_TAG" }.Concat(tags ?? []).Select(tag =>
            new KeyValuePair<string, TagValue>(tag, new(true))), types);

    private static ItemState Item(string id, string inventory, long value, int x = 0, int y = 0,
        ShapeState? shape = null, string? identifier = null, string[]? types = null) =>
        new(id, identifier ?? id, new(inventory, inventory is "left" or "right"
            ? new Placement.Slot() : new Placement.Grid(x, y)), 1, value, shape ?? ShapeState.Rectangle(1, 1), Facts(types: types));

    private static TransferStep Place(string item, string inventory, Placement placement) =>
        new(item, new(inventory, placement));
}
