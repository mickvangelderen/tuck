using System.Reflection;
using System.Runtime.CompilerServices;
using global::Tuck.PackSolver;
using Tuck.Mod.Packing;

namespace Tuck.Mod.Tests;

public class EquipmentBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturesEquipmentRestrictionsAndRejectsExtractionBeforeLiveProbes(bool moduleGrid)
    {
        var component = new FixtureItem(moduleGrid ? "evaporator_module_solvent_recovery" : "battery", 50);
        var machine = new FixtureItem("evaporator", 100);
        machine.EnableTag("CONTAINER_TAG"); // A machine being a container is not enough to make it packing storage.
        GameInventory equipment;
        if (moduleGrid) equipment = Grid(component);
        else
        {
            var slot = Raw<GameSlotInventory>();
            Field(slot, typeof(GameSlotInventory), "currentItem", component);
            component.parentInventory = slot;
            equipment = slot;
        }
        var window = Raw<PixelWindow>();
        Field(window, typeof(PixelWindow), "<childElement>k__BackingField", equipment);
        Field(machine, typeof(GameItem), "<contentWindow>k__BackingField", window);
        var capture = PackCapture.Capture(Grid(machine), Raw<GameSlotInventory>(), Raw<GameSlotInventory>(),
            Raw<GameSlotInventory>(), () => false);
        var inner = Assert.Single(capture.State.Inventories, i => i.OwnerItemId != null);
        Assert.Equal(AdmissionKind.Unsupported, inner.Admission.Kind);
        Assert.Contains("not packing storage", inner.UnsupportedTransfer);
        var copiedComponent = capture.State.Items.Single(i => i.Identifier == component.identifier);
        Assert.Equal(inner.Id, copiedComponent.Location.InventoryId);

        // A request to extract a component must fail replay before any live callback/probe.
        component.mayInventoryRemoveItemFunc = (_, _) => throw new Exception("Must not probe equipment extraction.");
        var extraction = new PackPlan(capture.State.StateId,
            [new(copiedComponent.Id, new(capture.State.Roots.Left, new Placement.Slot()))]);
        var error = Assert.Throws<InvalidOperationException>(() => PackExecutor.Execute(capture, extraction));
        Assert.Contains("equipment", error.Message);
        Assert.Same(equipment, component.parentInventory);
    }

    private sealed class FixtureItem : GameItem
    {
        internal FixtureItem(string id, long value) : base(id)
        {
            unitValue = value;
            state.EnableTag("IS_OWNED_TAG"); modifiedState = state.Clone();
            shape = new GridShapeBuilder(1, 1).SetDataFill(1); modifiedShape = shape.Clone();
        }
        public override void Validate() { }
        public override void Destroy() { }
    }
    private static T Raw<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Field(object obj, Type type, string name, object? value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(obj, value);
    private static GameGridInventory Grid(params GameItem[] items)
    {
        var grid = Raw<GameGridInventory>();
        Field(grid, typeof(GameGridInventory), "items", items.ToList());
        Field(grid, typeof(GameGridInventory), "<inventoryShape>k__BackingField", new GridShapeBuilder(4, 2) { outside = 1 }.SetDataFill(0));
        foreach (var item in items) item.parentInventory = grid;
        return grid;
    }
}
