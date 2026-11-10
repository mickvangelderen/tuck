using global::Tuck.PackSolver;

namespace Tuck.PackSolver.Tests;

public class PackSolverTests
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
    private static InventoryState BagGrid(int width = 1) => new("bag-grid", new InventoryLayout.Grid(ShapeState.Rectangle(width, 1, 0, 1)), new(AdmissionKind.Container), "bag");
    private static ItemDefinition Coins(int maximum = 10) => new("coins", new(new(maximum)));

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void ReplacesAnOccupiedHandUsingAnExplicitGroundTransfer(string solverName)
    {
        var state = State([Item("cheap", "left", 1), Item("keep", "right", 20), Item("loot", "ground", 10)]);
        var result = SolverUnderTest.Solve(solverName, new(state));
        var final = PlanReplay.Replay(state, result.Plan);
        Assert.Equal(30, result.FinalValue);
        Assert.Equal("ground", final.Items.Single(i => i.Id == "cheap").Location.InventoryId);
        Assert.True(PlanReplay.IsReturned(final, "loot"));
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void PinnedContentsKeepTheEnclosingBackpackInPlace(string solverName)
    {
        var state = State([Item("bag", "back", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("pin", "bag-grid", 1, tags: ["PINNED_TAG"]), Item("loot", "ground", 50)], Inventories().Append(BagGrid(2)).ToArray());
        Assert.Throws<InvalidOperationException>(() => PlanReplay.Replay(state, new("test", [Place("bag", "ground", placement: new Placement.Grid(1))])));
        var final = PlanReplay.Replay(state, SolverUnderTest.Solve(solverName, new(state)).Plan);
        Assert.Equal("back", final.Items.Single(i => i.Id == "bag").Location.InventoryId);
        Assert.True(PlanReplay.IsReturned(final, "loot"));
    }
    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void UnsupportedTransfersKeepOccupantsAndAllowTheirContainerToTravel(string solverName)
    {
        var state = State([Item("bag", "ground", 1, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]), Item("fixed", "bag-grid", 90)],
            Inventories().Append(BagGrid()).ToArray(), new ItemDefinition("fixed", rules: new(unsupportedTransfer: "attachment admission")));
        var result = SolverUnderTest.Solve(solverName, new(state));
        Assert.Equal(91, result.FinalValue); Assert.Single(result.UnsupportedRules);
        var final = PlanReplay.Replay(state, result.Plan);
        Assert.Equal("bag-grid", final.Items.Single(i => i.Id == "fixed").Location.InventoryId);
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    [Trait("Category", "PackingPolicy")]
    public void ReplacesAStackWithoutMergingCompatibleItems(string solverName)
    {
        var state = State([Item("source", "ground", 2, count: 4, identifier: "coins"), Item("target", "left", 2, count: 3, identifier: "coins"),
            Item("fixed", "right", 1, tags: ["PINNED_TAG"])], null, Coins());
        var result = SolverUnderTest.Solve(solverName, new(state)); var final = PlanReplay.Replay(state, result.Plan);
        Assert.Equal(9, result.FinalValue);
        Assert.Equal("left", final.Items.Single(i => i.Id == "source").Location.InventoryId);
        Assert.Equal("ground", final.Items.Single(i => i.Id == "target").Location.InventoryId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "PackingPolicy")]
    public void ReplayRejectsMissingReferencesWithoutMutation(bool missingSource)
    {
        var state = State([Item("source", "ground", 2, count: 5, identifier: "coins"), Item("target", "left", 2, count: 3, identifier: "coins")],
            null, Coins());
        var step = new TransferStep(missingSource ? "missing" : "source",
            new(missingSource ? "right" : "missing", new Placement.Slot()));
        var simulation = new TransferSimulation(state);
        Assert.False(simulation.TryApply(step, out var reason));
        Assert.Contains(missingSource ? "Missing source" : "Missing destination", reason);
        Assert.Null(StateComparison.Difference(state, simulation.Snapshot()));
        Assert.Throws<InvalidOperationException>(() => PlanReplay.Replay(state, new("test", [step])));
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void ToolboxHasSeparateRestrictedSlotsWithoutExteriorSizeLimits(string solverName)
    {
        var inventories = Inventories().Concat(new[] {
            new InventoryState("socket0", new InventoryLayout.Slot(), new(AdmissionKind.Toolbox, slotIndex: 0), "box"),
            new InventoryState("socket3", new InventoryLayout.Slot(), new(AdmissionKind.Toolbox, slotIndex: 3), "box") }).ToArray();
        var state = State([Item("box", "left", 1, tags: ["CONTAINER_TAG", "NESTABLE_DEVICE_TAG"], unlocked: 3),
            Item("tool", "ground", 50, shape: ShapeState.Rectangle(3, 1), types: ["TOOL"]), Item("fixed", "right", 1, tags: ["PINNED_TAG"])], inventories);
        var result = SolverUnderTest.Solve(solverName, new(state)); Assert.Equal(52, result.FinalValue);
        Assert.Equal("socket0", PlanReplay.Replay(state, result.Plan).Items.Single(i => i.Id == "tool").Location.InventoryId);
        Assert.Throws<InvalidOperationException>(() => PlanReplay.Replay(state, new("test", [Place("tool", "socket3", placement: new Placement.Slot())])));
    }
    [Fact]
    public void RejectsContainmentCyclesDanglingDefinitionsAndPlansForAnotherCapture()
    {
        var state = State([Item("item", "ground", 1)]);
        Assert.Throws<ArgumentException>(() => PlanReplay.Replay(state, new("different", [])));
        var inventories = Inventories().Append(new InventoryState("inside", new InventoryLayout.Grid(ShapeState.Rectangle(1, 1, 0)), new(AdmissionKind.Owned), "item")).ToArray();
        Assert.Throws<ArgumentException>(() => State([Item("item", "inside", 1)], inventories));
        Assert.Throws<ArgumentException>(() => new PackState("test", Roots, state.Items, state.Inventories, []));
    }
}
