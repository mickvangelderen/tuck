using global::Tuck.PackSolver;
using static Tuck.PackSolver.Tests.PackSolverTests;

namespace Tuck.PackSolver.Tests;

public class FannyPackPackingTests
{
    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void PacksValuableLootAroundExistingFannyPackContents(string solverName)
    {
        // The original day-two dumping-ground capture: a 3x3 fanny pack containing a
        // bandage and toothpaste, empty hands, and fabric, another bandage and three junk items.
        // Preserve the captured item order and IDs, which can affect solver tie-breaking.
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
        var state = new PackState("day-two-fanny-pack", new("inventory-0", "inventory-1", "inventory-2", "inventory-3"),
            items, inventories, items.Select(i => i.Identifier).Distinct().Select(id => new ItemDefinition(id)));

        // A reachable 165-credit loadout: hold the fabric and one junk item, and put
        // the new bandage beside the existing one. Two low-value junk items stay behind.
        var witness = new PackPlan(state.StateId,
        [
            new("item-4", new("inventory-2", new Placement.Slot())),
            new("item-5", new("inventory-3", new Placement.Slot())),
            new("item-6", new("inventory-10", new Placement.Grid(1, 0)))
        ]);
        Assert.Equal(165, PlanReplay.ReturnedValue(PlanReplay.Replay(state, witness)));

        var result = SolverUnderTest.Solve(solverName, new(state));
        Assert.Equal(81, result.InitialValue);
        Assert.Equal(165, result.FinalValue);
    }
}
