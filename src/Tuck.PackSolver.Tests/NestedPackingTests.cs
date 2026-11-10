using global::Tuck.PackSolver.Algorithms;

namespace Tuck.PackSolver.Tests;

using PackPlan = global::Tuck.PackSolver.Algorithms.PackPlan;
using PackProblem = global::Tuck.PackSolver.Algorithms.PackProblem;

// Layout-engine tests. Public solver behavior is covered in NestedStorageSolverTests.
public class NestedPackingTests
{
    [Fact]
    public void PacksUpgradePackIntoToolboxInsideBackpackAndUsesThePacksInterior()
    {
        var problem = NestedProblem();
        var result = PackPlanner.Plan(problem);
        var plan = Assert.IsType<PackPlan>(result.Plan);
        Assert.Equal(89, plan.TotalValue);
        Assert.Empty(plan.LeftItems);
        Assert.Empty(plan.HandItems);
        Assert.True(result.ReachedCellUpperBound);
        AssertDestination(plan, "backpack", "backpack-slot");
        AssertDestination(plan, "toolbox", "backpack-grid");
        AssertDestination(plan, "upgrade-pack", "toolbox-slot");
        AssertDestination(plan, "mod", "upgrade-pack-grid");
        CheckLayout(problem, plan);
    }

    [Fact]
    public void UsesToolboxCapacityWhileHoldingTheToolbox()
    {
        var problem = new PackProblem([
            Item("box", 5, 1, 1, ["root"], hand: true),
            Item("pack", 50, 3, 3, ["socket"]),
            Item("loot", 100, 1, 1, ["root"], hand: true)
        ], [new("root", CellMask.Rectangle(1, 1)), PackingContainer.Slot("socket", "box")], 1);
        var plan = Assert.IsType<PackPlan>(PackPlanner.Plan(problem).Plan);
        Assert.Equal(155, plan.TotalValue);
        Assert.Equal("box", Assert.Single(plan.HandItems).Id);
        AssertDestination(plan, "pack", "socket");
        AssertDestination(plan, "loot", "root");
        CheckLayout(problem, plan);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MutuallyNestedContainersCannotProvideUnrootedCapacity(bool required)
    {
        var problem = new PackProblem([
            Item("a", 10, 1, 1, ["b-grid"], required),
            Item("b", 10, 1, 1, ["a-grid"], required),
            Item("loot", 100, 1, 1, ["a-grid", "b-grid"])
        ], [new("a-grid", CellMask.Rectangle(2, 2), ownerItemId: "a"),
            new("b-grid", CellMask.Rectangle(2, 2), ownerItemId: "b")], 0);
        var result = PackPlanner.Plan(problem);
        Assert.NotNull(result.CellAllocation); // The relaxed capacity fits; rooted geometry does not.
        if (required) Assert.Null(result.Plan);
        else
        {
            var plan = Assert.IsType<PackPlan>(result.Plan);
            Assert.Equal(0, plan.TotalValue);
            Assert.Equal(3, plan.LeftItems.Count);
            CheckLayout(problem, plan);
        }
    }

    [Fact]
    public void LeavingAnOversizedContainerAlsoRemovesItsAvailableCapacity()
    {
        var problem = new PackProblem([
            Item("box", 5, 3, 3, ["root"]), Item("loot", 100, 1, 1, ["inside"])
        ], [new("root", CellMask.Rectangle(1, 1)), new("inside", CellMask.Rectangle(3, 3), ownerItemId: "box")], 0);
        var plan = Assert.IsType<PackPlan>(PackPlanner.Plan(problem).Plan);
        Assert.Equal(0, plan.TotalValue);
        Assert.Equal(new[] { "box", "loot" }, plan.LeftItems.Select(i => i.Id));
        CheckLayout(problem, plan);
    }

    [Fact]
    public void ReassignsFlexibleSlotsWithoutUsingLockedOrInadmissibleSlots()
    {
        var problem = new PackProblem([
            Item("flexible", 1, 4, 4, ["any", "restricted"], required: true),
            Item("limited", 50, 3, 3, ["any"], required: true),
            Item("locked-only", 1000, 1, 1, ["locked"])
        ], [PackingContainer.Slot("any"), PackingContainer.Slot("restricted"),
            PackingContainer.Slot("locked", occupied: true)], 0);
        var result = PackPlanner.Plan(problem);
        var plan = Assert.IsType<PackPlan>(result.Plan);
        Assert.Equal(51, plan.TotalValue);
        Assert.Equal(2, result.CellAllocation!.UsedCells); // Two slots, despite the items occupying 25 cells.
        AssertDestination(plan, "flexible", "restricted");
        AssertDestination(plan, "limited", "any");
        Assert.Equal("locked-only", Assert.Single(plan.LeftItems).Id);
        CheckLayout(problem, plan);
    }

    [Fact]
    public void PublicGeometricEntryPointAlsoEnforcesContainerActivation()
    {
        var problem = NestedProblem();
        var allocation = Assert.IsType<CellAllocation>(CellKnapsackSolver.Solve(problem));
        var plan = Assert.IsType<PackPlan>(GeometricPacker.Pack(allocation).Plan);
        Assert.Equal(89, plan.TotalValue);
        CheckLayout(problem, plan);
    }

    [Fact]
    public void ReportsContainerSearchLimitAndKeepsAValidBaseline()
    {
        var problem = NestedProblem();
        var result = PackPlanner.Plan(problem, new PackingOptions(maxContainerArrangements: 1));
        Assert.True(result.SearchBudgetExhausted);
        Assert.Equal(1, result.ContainerArrangements);
        Assert.False(result.ReachedCellUpperBound);
        var plan = Assert.IsType<PackPlan>(result.Plan);
        Assert.Equal(14, plan.TotalValue);
        CheckLayout(problem, plan);
        var zero = PackPlanner.Plan(problem, new PackingOptions(0, 0));
        Assert.Null(zero.Plan);
        Assert.True(zero.SearchBudgetExhausted);
        Assert.Equal(0, zero.PlacementChecks);
        Assert.Equal(0, zero.ShuffleNodes);
    }

    [Fact]
    public void SharesGeometryBudgetAcrossContainerCombinationsAtTwoHundredFortyItems()
    {
        string[] slots = Enumerable.Range(0, 9).Select(i => "slot-" + i).ToArray();
        var items = Enumerable.Range(0, 240).Select(i => Item("loot-" + i, 1 + i % 37,
            1 + i % 3, 1 + i % 3, i % 2 == 0 ? slots.Append("grid").ToArray() : ["grid"], hand: true)).ToList();
        items.Add(Item("box", 15, 3, 3, ["grid"], hand: true));
        var problem = new PackProblem(items, new[] { new PackingContainer("grid", CellMask.Rectangle(10, 10)) }
            .Concat(slots.Select(s => PackingContainer.Slot(s, "box"))), 2);
        var options = new PackingOptions();
        var result = PackPlanner.Plan(problem, options);
        var plan = Assert.IsType<PackPlan>(result.Plan);
        Assert.DoesNotContain(plan.LeftItems, i => i.Id == "box");
        Assert.Contains(plan.Placements, p => p.Container.IsSlot);
        Assert.InRange(result.PlacementChecks, 0, options.MaxPlacementChecks);
        Assert.InRange(result.ShuffleNodes, 0, options.MaxShuffleNodes);
        Assert.InRange(result.ContainerArrangements, 1, options.MaxContainerArrangements);
        CheckLayout(problem, plan);
    }

    private static PackProblem NestedProblem() => new([
        Item("backpack", 5, 4, 4, ["backpack-slot"], required: true),
        Item("toolbox", 15, 2, 3, ["backpack-grid"]),
        Item("upgrade-pack", 20, 3, 3, ["toolbox-slot"]),
        Item("mod", 40, 2, 2, ["upgrade-pack-grid"]),
        Item("unrelated", 9, 1, 1, ["backpack-grid"])
    ], [PackingContainer.Slot("backpack-slot"), new("backpack-grid", CellMask.Rectangle(3, 3), ownerItemId: "backpack"),
        PackingContainer.Slot("toolbox-slot", "toolbox"), new("upgrade-pack-grid", CellMask.Rectangle(2, 2), ownerItemId: "upgrade-pack")], 0);

    private static PackingItem Item(string id, long value, int width, int height, string[] allowed,
        bool required = false, bool hand = false) => new(id, value,
            PackingOrientation.FromShape(CellMask.Rectangle(width, height)), hand, required, allowed);
    private static void AssertDestination(PackPlan plan, string item, string container) =>
        Assert.Equal(container, Assert.Single(plan.Placements, p => p.Item.Id == item).Container.Id);

    // Independent final-plan checks: scalar cell sets and parent walks, without PlacementCatalog/PackingLayout.
    private static void CheckLayout(PackProblem problem, PackPlan plan)
    {
        var retained = plan.Placements.Select(p => p.Item).Concat(plan.HandItems).ToArray();
        Assert.Equal(retained.Length, retained.Select(i => i.Id).Distinct().Count());
        Assert.Equal(problem.Items.Count, retained.Length + plan.LeftItems.Count);
        Assert.Equal(problem.FixedValue + retained.Sum(i => i.Value), plan.TotalValue);
        Assert.All(problem.Items.Where(i => i.Required), i => Assert.Contains(i, retained));
        Assert.InRange(plan.HandItems.Count, 0, problem.AvailableHands);
        Assert.All(plan.HandItems, i => Assert.True(i.CanUseHand));
        var byItem = plan.Placements.ToDictionary(p => p.Item.Id);
        foreach (var group in plan.Placements.GroupBy(p => p.Container.Id))
        {
            var container = problem.Containers.Single(c => c.Id == group.Key);
            var occupied = container.OccupiedCells.Cells.Select(c => (c.X, c.Y)).ToHashSet();
            foreach (var p in group)
            {
                Assert.True(p.Item.CanEnter(container.Id));
                var cells = container.IsSlot ? new[] { new GridCell(0, 0) } : p.Orientation.Shape.Cells.ToArray();
                foreach (var cell in cells)
                {
                    var position = (p.X + cell.X, p.Y + cell.Y);
                    Assert.True(container.UsableCells.Contains(position.Item1, position.Item2));
                    Assert.True(occupied.Add(position), "Overlapping placements.");
                }
                var ancestors = new HashSet<string> { p.Item.Id };
                string? parent = container.OwnerItemId;
                while (parent != null)
                {
                    Assert.Contains(retained, i => i.Id == parent);
                    Assert.True(ancestors.Add(parent), "Cyclic container nesting.");
                    parent = byItem.TryGetValue(parent, out var placement) ? placement.Container.OwnerItemId : null;
                }
            }
        }
    }
}
