using global::Tuck.PackSolver;
using static Tuck.PackSolver.Tests.PackSolverTests;

namespace Tuck.PackSolver.Tests;

public class DerivedStateTests
{
    [Fact]
    public void MovingAnAssembledGunPreservesItsAttachmentsInEveryOrientation()
    {
        for (int flip = 0; flip < 2; flip++)
            for (int rotation = 0; rotation < 4; rotation++)
            {
                var location = new ItemLocation("ground", new Placement.Grid(5, 5, rotation, flip != 0));
                var gun = new ItemState("gun", "smg", location, 1, 50, ShapeState.Rectangle(7, 2), Facts());
                var stock = new ItemState("attachment", "stock", new("mods", new Placement.Slot()), 1, 10, ShapeState.Rectangle(2, 1), Facts());
                var invs = Inventories().Append(new InventoryState("mods", new InventoryLayout.Slot(), new(AdmissionKind.Owned), "gun")).ToArray();
                invs[0] = new("ground", new InventoryLayout.Grid(ShapeState.Rectangle(20, 20, 0, 1)), new(AdmissionKind.Ground));
                var state = State([gun, stock], invs, new ItemDefinition("stock", rules: new(parentShape: ParentShapeRule.GunAttachment)));
                var initial = new StateView(state);
                Assert.Equal(9, initial.Effective("gun").Shape.Width); Assert.Equal(2, initial.Effective("gun").OriginX);
                var moved = new Placement.Grid(8, 8, rotation, flip != 0);
                var final = PlanReplay.Replay(state, new("test", [Place("gun", "ground", placement: moved)]));
                Assert.Equal(new ItemLocation("ground", moved), final.Items.Single(i => i.Id == "gun").Location);
                Assert.Equal(stock.Location, final.Items.Single(i => i.Id == "attachment").Location);
                var derived = new StateView(final);
                Assert.Equal(9, derived.Effective("gun").Shape.Width); Assert.Equal(2, derived.Effective("gun").OriginX);
                Assert.Equal(initial.Placed("gun").Transform.X + 3, derived.Placed("gun").Transform.X);
                Assert.Equal(initial.Placed("gun").Transform.Y + 3, derived.Placed("gun").Transform.Y);
                Assert.Equal(0, derived.ReturnedValue);
                Assert.Equal(9, initial.Effective("gun").Shape.Width);
            }
    }
    [Fact]
    public void InstallingAttachmentsIsNotAPackingAction()
    {
        var gun = Item("gun", "ground", 50, x: 2, shape: ShapeState.Rectangle(7, 2), identifier: "smg");
        var stock = Item("stock", "left", 10, shape: ShapeState.Rectangle(2, 1));
        var invs = Inventories(12).Append(new InventoryState("mods", new InventoryLayout.Slot(), new(AdmissionKind.Owned), "gun")).ToArray();
        var state = State([gun, stock, Item("blocker", "ground", 1)], invs, new ItemDefinition("stock", rules: new(parentShape: ParentShapeRule.GunAttachment)));
        var simulation = new TransferSimulation(state);
        Assert.False(simulation.TryApply(Place("stock", "mods", placement: new Placement.Slot()), out var reason));
        Assert.Contains("equipment", reason);
        Assert.Null(StateComparison.Difference(state, simulation.Snapshot()));
    }
    [Fact]
    public void OwnershipAndReturnMembershipComeFromTheEntireAncestorChain()
    {
        var notOwned = new ItemState("outer", "outer", new("left", new Placement.Slot()), 1, 5, ShapeState.Rectangle(1, 1), new());
        var invs = Inventories().Concat(new[] {
            new InventoryState("middle-grid", new InventoryLayout.Grid(ShapeState.Rectangle(2, 1, 0)), new(AdmissionKind.Owned), "outer"),
            new InventoryState("inner-grid", new InventoryLayout.Grid(ShapeState.Rectangle(2, 1, 0)), new(AdmissionKind.Owned), "middle") }).ToArray();
        var state = State([notOwned, Item("middle", "middle-grid", 3), Item("loot", "inner-grid", 10)], invs);
        var view = new StateView(state);
        Assert.True(view.IsReturned("loot")); Assert.False(view.IsEffectivelyOwned("loot"));
        Assert.Equal(0, new TransferSimulation(state).Removable(state.Items.Single(i => i.Id == "loot")));
    }
    [Fact]
    [Trait("Category", "PackingPolicy")]
    public void WholeStackMovesRetainEachInstancesFacts()
    {
        var definition = new ItemDefinition("coins", rules: new(new(10)));
        var state = State([Item("source", "ground", 99, count: 2, identifier: "coins"), Item("target", "left", 4, identifier: "coins")], null, definition);
        var final = PlanReplay.Replay(state, new("test", [Place("source", "right")]));
        Assert.Equal(202, PlanReplay.ReturnedValue(final));
        Assert.Equal(99, final.Items.Single(i => i.Id == "source").UnitValue);
        Assert.Equal(4, final.Items.Single(i => i.Id == "target").UnitValue);
    }
}
