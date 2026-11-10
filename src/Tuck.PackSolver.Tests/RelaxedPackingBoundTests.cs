using global::Tuck.PackSolver;
using global::Tuck.PackSolver.Solvers;
using static Tuck.PackSolver.Tests.PackSolverTests;

namespace Tuck.PackSolver.Tests;

public class RelaxedPackingBoundTests
{
    [Theory]
    [InlineData(1, 10)]
    [InlineData(10, 10)]
    public void UsesEachInstancesValueRegardlessOfIdentifierOrLegacyId(int left, int right)
    {
        var state = State([
            Item("$split:0", "left", left, identifier: "coins"),
            Item("held", "right", right, identifier: "coins"), Item("loose", "ground", 1, identifier: "coins")
        ], null, new ItemDefinition("coins", new(new(10))));

        Assert.True(RelaxedPackingBound.TryCreate(new(state), state.Items.Select(i => i.Id).ToArray(), default,
            out long bound, out _, out int hands));
        Assert.Equal(left + right, bound);
        Assert.Equal(2, hands);
    }

    [Fact]
    public void IncludesExistingContentsEvenWhenInsertionIsLocked()
    {
        var inventories = Inventories();
        inventories[2] = new("left", new InventoryLayout.Slot(), new(AdmissionKind.Owned), overrideLockInsert: true);
        var state = State([Item("kept", "left", 50), Item("loot", "ground", 10)], inventories);
        Assert.True(RelaxedPackingBound.TryCreate(new(state), ["kept", "loot"], default, out long bound, out _));
        Assert.Equal(60, bound);
    }

    [Fact]
    public void DeclinesEquipmentContentsInsteadOfTreatingThemAsIndependentCargo()
    {
        var state = State([
            Item("gun", "left", 10),
            new ItemState("part", "part", new("socket", new Placement.Slot()), 1, 1, ShapeState.Rectangle(1, 1), Facts())
        ], Inventories().Append(new InventoryState("socket", new InventoryLayout.Slot(), new(AdmissionKind.Owned), "gun")).ToArray());

        Assert.False(RelaxedPackingBound.TryCreate(new(state), ["gun", "part"], default, out _, out _));
    }

    [Fact]
    public void CountsAPinnedHandEvenWhenItsItemIsWorthNothing()
    {
        var state = State([Item("fixed", "right", 0, tags: ["PINNED_TAG"])]);

        Assert.True(RelaxedPackingBound.TryCreate(new(state), ["fixed"], default, out long bound, out _, out int hands));
        Assert.Equal(0, bound);
        Assert.Equal(1, hands);
    }
}
