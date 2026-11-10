using global::Tuck.PackSolver;
using global::Tuck.PackSolver.Solvers;
using static Tuck.PackSolver.Tests.PackSolverTests;

namespace Tuck.PackSolver.Tests;

[Trait("Category", "SolverContract")]
public class CompactPackingTests
{
    public enum ReturnedLoot { LooseItems, NestedPouch, InstalledEquipment }

    [Theory]
    [SolverData(Except = typeof(BaselinePackSolver))]
    [SolverData(Only = typeof(BaselinePackSolver), Skip = "Baseline returns the loot but leaves both hands occupied.")]
    public void ReturnsBothKottonFabricsWithTheSatchelEquippedAndBothHandsFree(string solverName)
    {
        // Constructed from the November 9 situation: one fabric is already held, while
        // the other fabric, satchel and shiv are on the ground. No recording is loaded.
        var state = State([
            Item("ground-fabric", "ground", 65, shape: ShapeState.Rectangle(3, 1), identifier: "kotton_fabric"),
            Item("satchel", "ground", 150, 6, 2, tags: ["BACKPACK_TAG", "CONTAINER_TAG"],
                shape: ShapeState.Rectangle(3, 3)),
            Item("shiv", "ground", 8, 3, shape: ShapeState.Rectangle(3, 1),
                identifier: "glass_shard_shiv", types: ["MELEE_WEAPON"]),
            Item("held-fabric", "right", 65, shape: ShapeState.Rectangle(3, 1), identifier: "kotton_fabric")
        ], Storage(Grid("satchel-interior", "satchel", 6, 3)));

        // A legal witness proves the requested outcome is feasible; the solver need not
        // use this transfer order, these coordinates, or these orientations.
        var witness = new PackPlan(state.StateId,
        [
            Place("satchel", "back"),
            Place("ground-fabric", "satchel-interior"),
            Place("held-fabric", "satchel-interior", new Placement.Grid(3, 0)),
            Place("shiv", "satchel-interior", new Placement.Grid(0, 1))
        ]);
        AssertPreferredResult(PlanReplay.Replay(state, witness), "satchel", occupiedHands: 0);

        var result = SolverUnderTest.Solve(solverName, new(state));

        AssertPreferredResult(PlanReplay.Replay(state, result.Plan), "satchel", occupiedHands: 0);
    }

    [Theory]
    [SolverData(Except = typeof(BaselinePackSolver))]
    [SolverData(Only = typeof(BaselinePackSolver), Skip = "Baseline keeps the smaller satchel equipped.")]
    public void ReturnsBothBackpacksWithTheLargerEquippedAndOneHandFree(string solverName)
    {
        // The equipped satchel has 18 storage cells; the ground backpack has 25.
        // These two bags cannot nest, but loose items may finish in either bag.
        var state = State([
            Item("flux-a", "ground", 4, identifier: "flux_agent"),
            Item("flux-b", "ground", 4, 1, identifier: "flux_agent"),
            Item("wire", "ground", 16, 4, 3, shape: ShapeState.Rectangle(2, 2)),
            Item("kids-backpack", "ground", 200, 5, 5, tags: ["BACKPACK_TAG", "CONTAINER_TAG"],
                shape: ShapeState.Rectangle(3, 3), identifier: "kids_backpack"),
            Item("bandage", "ground", 16, 2, tags: ["MEDICAL"], shape: ShapeState.Rectangle(1, 2),
                identifier: "hemostatic_bandage_item"),
            Item("module", "ground", 30, 8, 1, shape: new(2, 2, [1, 1, 0, 1]),
                identifier: "system_module_ruined"),
            Item("satchel", "back", 150, tags: ["BACKPACK_TAG", "CONTAINER_TAG"],
                shape: ShapeState.Rectangle(3, 3))
        ], Storage(Grid("kids-interior", "kids-backpack", 5, 5), Grid("satchel-interior", "satchel", 6, 3)));

        var witness = new PackPlan(state.StateId,
        [
            Place("satchel", "left"),
            Place("kids-backpack", "back"),
            Place("module", "satchel-interior"),
            Place("wire", "kids-interior"),
            Place("bandage", "kids-interior", new Placement.Grid(2, 0)),
            Place("flux-a", "kids-interior", new Placement.Grid(3, 0)),
            Place("flux-b", "kids-interior", new Placement.Grid(4, 0))
        ]);
        AssertPreferredResult(PlanReplay.Replay(state, witness), "kids-backpack", occupiedHands: 1);

        var result = SolverUnderTest.Solve(solverName, new(state));

        AssertPreferredResult(PlanReplay.Replay(state, result.Plan), "kids-backpack", occupiedHands: 1);
    }

    [Theory]
    [SolverData(ReturnedLoot.LooseItems, Except = typeof(BaselinePackSolver))]
    [SolverData(ReturnedLoot.LooseItems, Only = typeof(BaselinePackSolver), Skip = "Baseline leaves already-returned loot in both hands.")]
    [SolverData(ReturnedLoot.NestedPouch, Except = typeof(BaselinePackSolver))]
    [SolverData(ReturnedLoot.NestedPouch, Only = typeof(BaselinePackSolver), Skip = "Baseline leaves already-returned loot in both hands.")]
    [SolverData(ReturnedLoot.InstalledEquipment, Except = typeof(BaselinePackSolver))]
    [SolverData(ReturnedLoot.InstalledEquipment, Only = typeof(BaselinePackSolver), Skip = "Baseline leaves already-returned loot in both hands.")]
    public void FreesBothHandsWithoutLosingAlreadyReturnedLoot(string solverName, ReturnedLoot scenario)
    {
        var (state, witness) = ReturnedLootScenario(scenario);
        AssertPreferredResult(PlanReplay.Replay(state, witness), "bag", occupiedHands: 0);

        var result = SolverUnderTest.Solve(solverName, new(state));
        var final = PlanReplay.Replay(state, result.Plan);
        AssertPreferredResult(final, "bag", occupiedHands: 0);
        Assert.Equal(result.InitialValue, result.FinalValue);
        Assert.Equal(PlanningStatus.Improved, result.Status);
        if (scenario == ReturnedLoot.InstalledEquipment)
            Assert.Equal("socket", final.Items.Single(i => i.Id == "part").Location.InventoryId);
    }

    [Theory]
    [SolverData(ReturnedLoot.LooseItems)]
    [SolverData(ReturnedLoot.NestedPouch)]
    [SolverData(ReturnedLoot.InstalledEquipment)]
    public void LeavesAlreadyCompactLootAlone(string solverName, ReturnedLoot scenario)
    {
        var (state, witness) = ReturnedLootScenario(scenario);
        var compact = PlanReplay.Replay(state, witness);
        AssertPreferredResult(compact, "bag", occupiedHands: 0);

        var result = SolverUnderTest.Solve(solverName, new(compact));
        Assert.Equal(PlanningStatus.Unchanged, result.Status);
        Assert.Empty(result.Plan.Steps);
    }

    [Theory]
    [SolverData(Except = typeof(BaselinePackSolver))]
    [SolverData(Only = typeof(BaselinePackSolver), Skip = "Baseline keeps the smaller bag equipped when the other hand is pinned.")]
    public void EquipsTheLargerBagEvenWhenTheOtherHandIsPinned(string solverName)
    {
        var state = State([
            Item("small", "back", 5, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("large", "left", 10, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("fixed", "right", 1, tags: ["PINNED_TAG"])
        ], Inventories().Concat([Grid("small-grid", "small", 1, 1), Grid("large-grid", "large", 2, 1)]).ToArray());
        var witness = new PackPlan(state.StateId,
            [Place("small", "ground"), Place("large", "back"), Place("small", "left")]);
        AssertPreferredResult(PlanReplay.Replay(state, witness), "large", occupiedHands: 2);

        var result = SolverUnderTest.Solve(solverName, new(state));
        var final = PlanReplay.Replay(state, result.Plan);
        AssertPreferredResult(final, "large", occupiedHands: 2);
        Assert.Equal("right", final.Items.Single(i => i.Id == "fixed").Location.InventoryId);
        Assert.Equal(result.InitialValue, result.FinalValue);
        Assert.Equal(PlanningStatus.Improved, result.Status);
    }

    [Theory]
    [SolverData]
    public void PrefersFreeHandsWhenTheLargerBagWouldNeedAHand(string solverName)
    {
        // The large-capacity bag fits in the smaller-capacity bag, but not vice versa.
        var state = State([
            Item("small", "back", 5, tags: ["BACKPACK_TAG", "CONTAINER_TAG", "NESTABLE_DEVICE_TAG"],
                shape: ShapeState.Rectangle(3, 3)),
            Item("large", "small-grid", 10, tags: ["BACKPACK_TAG", "CONTAINER_TAG", "NESTABLE_DEVICE_TAG"])
        ], Inventories().Concat([Grid("small-grid", "small", 1, 1), Grid("large-grid", "large", 2, 1)]).ToArray());
        var result = SolverUnderTest.Solve(solverName, new(state));

        Assert.Equal(PlanningStatus.Unchanged, result.Status);
        Assert.Equal(15, result.FinalValue);
        Assert.Empty(result.Plan.Steps);
        AssertPreferredResult(PlanReplay.Replay(state, result.Plan), "small", occupiedHands: 0);
    }

    [Theory]
    [SolverData]
    public void KeepsAZeroValuePinnedItemInItsHand(string solverName)
    {
        var state = State([
            Item("bag", "back", 5, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("fixed", "right", 0, tags: ["PINNED_TAG"])
        ], Inventories().Append(Grid("bag-grid", "bag", 1, 1)).ToArray());
        var result = SolverUnderTest.Solve(solverName, new(state));

        Assert.Equal(PlanningStatus.Unchanged, result.Status);
        Assert.Empty(result.Plan.Steps);
        AssertPreferredResult(PlanReplay.Replay(state, result.Plan), "bag", occupiedHands: 1);
    }

    [Theory]
    [SolverData]
    public void DoesNotDiscardValuableCargoJustToFreeAHand(string solverName)
    {
        var state = State([
            Item("bag", "back", 5, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]),
            Item("packed", "bag-grid", 100), Item("held", "left", 10)
        ], Inventories().Append(Grid("bag-grid", "bag", 1, 1)).ToArray());
        var result = SolverUnderTest.Solve(solverName, new(state));

        Assert.Equal(115, result.FinalValue);
        Assert.Empty(result.Plan.Steps);
        AssertPreferredResult(PlanReplay.Replay(state, result.Plan), "bag", occupiedHands: 1);
    }

    private static (PackState State, PackPlan Witness) ReturnedLootScenario(ReturnedLoot scenario)
    {
        bool nested = scenario == ReturnedLoot.NestedPouch;
        var items = new List<ItemState>
        {
            Item("bag", "back", 5, tags: ["BACKPACK_TAG", "CONTAINER_TAG"]), Item("loot", "right", 2)
        };
        var inventories = Inventories().Append(Grid("bag-grid", "bag", nested ? 1 : 2, 1)).ToList();
        TransferStep[] steps;
        if (nested)
        {
            items.Add(Item("pouch", "left", 10, tags: ["CONTAINER_TAG", "NESTABLE_DEVICE_TAG"]));
            inventories.Add(Grid("pouch-grid", "pouch", 1, 1));
            steps = [Place("loot", "pouch-grid"), Place("pouch", "bag-grid")];
        }
        else
        {
            items.Add(Item("gun", "left", 10));
            if (scenario == ReturnedLoot.InstalledEquipment)
            {
                items.Add(new("part", "part", new("socket", new Placement.Slot()), 1, 1, ShapeState.Rectangle(1, 1), Facts()));
                inventories.Add(new("socket", new InventoryLayout.Slot(), new(AdmissionKind.Owned), "gun"));
            }
            steps = [Place("gun", "bag-grid"), Place("loot", "bag-grid", new Placement.Grid(1))];
        }
        var state = State(items.ToArray(), inventories.ToArray());
        return (state, new(state.StateId, steps));
    }

    private static InventoryState Grid(string id, string owner, int width, int height) =>
        new(id, new InventoryLayout.Grid(ShapeState.Rectangle(width, height, 0, 1)),
            new(AdmissionKind.Container), owner);

    private static InventoryState[] Storage(params InventoryState[] interiors)
    {
        var roots = Inventories();
        roots[0] = new("ground", new InventoryLayout.Grid(ShapeState.Rectangle(12, 9, 0, 1)),
            new(AdmissionKind.Ground));
        return roots.Concat(interiors).ToArray();
    }

    private static void AssertPreferredResult(PackState final, string equippedBackpack, int occupiedHands)
    {
        Assert.All(final.Items, item => Assert.True(PlanReplay.IsReturned(final, item.Id),
            $"{item.Id} must be returned."));
        Assert.Equal(equippedBackpack,
            Assert.Single(final.Items, item => item.Location.InventoryId == final.Roots.Backpack).Id);
        // No requirement on which hand is free, which bag holds each loose item,
        // grid coordinates, orientations, or the sequence used to reach the result.
        Assert.Equal(occupiedHands, final.Items.Count(item =>
            item.Location.InventoryId == final.Roots.Left || item.Location.InventoryId == final.Roots.Right));
    }
}
