using global::Tuck.PackSolver;
using static Tuck.PackSolver.Tests.PackSolverTests;

namespace Tuck.PackSolver.Tests;

public class PackingSearchDataTests
{
    [Fact]
    public void KeysDependOnPlacementRatherThanMoveHistory()
    {
        var state = State([Item("$split:1", "ground", 2, count: 3, identifier: "coins")], null,
            new ItemDefinition("coins", new(new(3))));
        var first = new TransferSimulation(state);
        var data = new PackingSearchData(first);
        Assert.True(first.TryApply(Place("$split:1", "left"), out _));
        var second = new TransferSimulation(state);
        Assert.True(second.TryApply(Place("$split:1", "right"), out _));
        Assert.NotEqual(data.Key(first), data.Key(second));
        Assert.True(second.TryApply(Place("$split:1", "left"), out _));
        Assert.Equal(data.Key(first), data.Key(second));
    }

    [Fact]
    public void HashCollisionsDoNotMergeDifferentPlacements()
    {
        var inventories = Inventories();
        inventories[0] = new("ground", new InventoryLayout.Grid(ShapeState.Rectangle(40, 40, 0, 1)), new(AdmissionKind.Ground));
        var first = new TransferSimulation(State([Item("a", "ground", 1, 0, 31)], inventories));
        var second = new TransferSimulation(State([Item("a", "ground", 1, 1, 0)], inventories));
        var data = new PackingSearchData(first);
        var a = data.Key(first); var b = data.Key(second);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, b);
        Assert.Equal(2, new HashSet<PackingStateKey> { a, b }.Count);
    }

    [Fact]
    public void KeysPreserveIdentityWithoutDependingOnDictionaryOrder()
    {
        var first = new TransferSimulation(State([Item("a;1:x", "ground", 1), Item("b", "ground", 9, 1)]));
        var reordered = new TransferSimulation(State([Item("b", "ground", 9, 1), Item("a;1:x", "ground", 1)]));
        var swapped = new TransferSimulation(State([Item("b", "ground", 9), Item("a;1:x", "ground", 1, 1)]));
        var data = new PackingSearchData(first);
        Assert.Equal(data.Key(first), data.Key(reordered));
        Assert.NotEqual(data.Key(first), data.Key(swapped));
        // Index 0 in a different capture is not necessarily the same item or inventory.
        Assert.NotEqual(data.Key(first), new PackingSearchData(first).Key(first));
    }
}
