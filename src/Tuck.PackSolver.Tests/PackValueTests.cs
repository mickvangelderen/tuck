using global::Tuck.PackSolver;
using static Tuck.PackSolver.Tests.PackSolverTests;

namespace Tuck.PackSolver.Tests;

public class PackValueTests
{
    [Theory]
    [SolverData(1, 10)]
    [SolverData(10, 10)]
    [Trait("Category", "SolverContract")]
    public void KeepsMoreValuableInstancesWithTheSameIdentifier(string solverName, int left, int right)
    {
        var state = State([
            Item("$split:0", "left", left, identifier: "coins"),
            Item("held", "right", right, identifier: "coins"), Item("loose", "ground", 1, identifier: "coins")
        ], null, new ItemDefinition("coins", new(new(10))));
        var result = SolverUnderTest.Solve(solverName, new(state));

        Assert.Equal(left + right, result.FinalValue);
        Assert.Equal(PlanningStatus.Unchanged, result.Status);
        Assert.Empty(result.Plan.Steps);
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void ChoosesCargoByWholeStackValueRatherThanUnitPrice(string solverName)
    {
        var state = State([
            Item("stack", "ground", 2, count: 10), Item("single", "ground", 10, 1),
            Item("fixed", "right", 1, tags: ["PINNED_TAG"])
        ], null, new ItemDefinition("stack", new(new(10))));
        var result = SolverUnderTest.Solve(solverName, new(state));
        var final = PlanReplay.Replay(state, result.Plan);

        Assert.Equal(21, result.FinalValue);
        Assert.True(PlanReplay.IsReturned(final, "stack"));
        Assert.False(PlanReplay.IsReturned(final, "single"));
    }

    [Theory]
    [SolverData(false, false)]
    [SolverData(true, false)]
    [SolverData(false, true)]
    [Trait("Category", "SolverContract")]
    public void RuinedModuleRecoveryStillRequiresALegalRemoval(string solverName, bool stuck, bool unsupportedRemoval)
    {
        var state = State([Item("module", "ground", 30, identifier: "system_module_ruined",
            tags: stuck ? ["MODULE_STUCK_TAG"] : [])], null,
            new ItemDefinition("system_module_ruined", new(unsupportedTransfer: unsupportedRemoval ? "additional removal callback" : null)));
        var result = SolverUnderTest.Solve(solverName, new(state));
        if (stuck || unsupportedRemoval)
        {
            Assert.Empty(result.Plan.Steps);
            Assert.Equal(0, result.FinalValue);
        }
        else
        {
            Assert.Equal(48, result.FinalValue);
            Assert.True(PlanReplay.IsReturned(PlanReplay.Replay(state, result.Plan), "module"));
        }
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void RuinedModuleReplacesMoreExpensiveLootBecauseItsPartsAreWorthMore(string solverName)
    {
        var state = State([Item("held", "left", 40), Item("keep", "right", 100),
            Item("module", "ground", 30, identifier: "system_module_ruined")]);
        var result = SolverUnderTest.Solve(solverName, new(state));
        var final = PlanReplay.Replay(state, result.Plan);
        Assert.Equal(140, result.InitialValue);
        Assert.Equal(148, result.FinalValue);
        Assert.True(PlanReplay.IsReturned(final, "module"));
        Assert.False(PlanReplay.IsReturned(final, "held"));
        Assert.Equal(30, new StateView(final).Effective("module").UnitValue);
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void RecoveryValueDoesNotReduceAnAlreadyMoreValuableModule(string solverName)
    {
        var state = State([Item("module", "left", 80, identifier: "system_module_ruined"), Item("keep", "right", 100), Item("loot", "ground", 70)]);
        var result = SolverUnderTest.Solve(solverName, new(state));
        Assert.Empty(result.Plan.Steps);
        Assert.Equal(180, result.FinalValue);
    }

    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void AModuleInsideAContainerContributesRecoveryValueExactlyOnce(string solverName)
    {
        var inventories = Inventories().Append(new InventoryState("inside", new InventoryLayout.Grid(ShapeState.Rectangle(1, 1, 0, 1)),
            new(AdmissionKind.Owned), "box")).ToArray();
        var state = State([Item("held", "left", 40), Item("keep", "right", 100), Item("box", "ground", 2),
            Item("module", "inside", 30, identifier: "system_module_ruined")], inventories,
            new ItemDefinition("system_module_ruined", rules: new(unsupportedTransfer: "fixed attachment")));
        var result = SolverUnderTest.Solve(solverName, new(state));
        var final = PlanReplay.Replay(state, result.Plan);
        Assert.Equal(150, result.FinalValue);
        Assert.True(PlanReplay.IsReturned(final, "box"));
        Assert.Equal("inside", final.Items.Single(i => i.Id == "module").Location.InventoryId);
    }
}
