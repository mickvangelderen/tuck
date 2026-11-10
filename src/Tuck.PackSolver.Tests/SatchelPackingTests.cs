using global::Tuck.PackSolver;
using static Tuck.PackSolver.Tests.PackSolverTests;

namespace Tuck.PackSolver.Tests;

public class SatchelPackingTests
{
    [Theory]
    [SolverData(5000, 200000)]
    [SolverData(32, 2000)]
    [Trait("Category", "SolverContract")]
    public void RearrangesSatchelContentsToPackTheRemainingJunk(string solverName, int maxSequenceNodes, int maxPlacementChecks)
    {
        var state = SatchelState();

        // The 6x3 satchel starts with horizontal 3x2 junk at (0,0) and 2x3 milk at (3,0).
        // Rotating the junk and sliding the milk right makes room for the ground junk.
        // Replay a witness so this regression also proves that all six items can be carried.
        var witness = new PackPlan(state.StateId,
        [
            Place("item-4", "inventory-4", placement: new Placement.Grid(0, 0, orientation: 1)),
            Place("item-5", "inventory-4", placement: new Placement.Grid(4, 0)),
            Place("item-0", "inventory-4", placement: new Placement.Grid(2, 0, orientation: 1))
        ]);
        AssertAllItemsReturned(state, PlanReplay.Replay(state, witness));

        // Cover both the recorded defaults and a small deterministic budget, without a wall-clock timeout.
        var result = SolverUnderTest.Solve(solverName, new(state), new(maxSequenceNodes, maxPlacementChecks));
        Assert.Equal(PlanningStatus.Improved, result.Status);
        Assert.Equal(272, result.InitialValue);
        Assert.Equal(275, result.FinalValue);
        AssertAllItemsReturned(state, PlanReplay.Replay(state, result.Plan));
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void PacksEverythingAfterTheSatchelJunkIsManuallyDropped(string solverName)
    {
        var state = SatchelState(junkDropped: true);
        var result = SolverUnderTest.Solve(solverName, new(state));

        Assert.Equal(269, result.InitialValue);
        Assert.Equal(275, result.FinalValue);
        AssertAllItemsReturned(state, PlanReplay.Replay(state, result.Plan));
    }

    private static PackState SatchelState(bool junkDropped = false)
    {
        // From the November 8 scavenge failure and its successful retry. Preserve capture order
        // and IDs because the solver uses IDs to break ties. The retry recaptures ground items first.
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

    private static void AssertAllItemsReturned(PackState initial, PackState final)
    {
        Assert.Equal(initial.Items.Select(i => (i.Id, i.Identifier, i.UnitCount)).OrderBy(i => i.Id),
            final.Items.Select(i => (i.Id, i.Identifier, i.UnitCount)).OrderBy(i => i.Id));
        Assert.All(final.Items, item => Assert.True(PlanReplay.IsReturned(final, item.Id), item.Id));
        Assert.Equal(275, PlanReplay.ReturnedValue(final));
    }
}
