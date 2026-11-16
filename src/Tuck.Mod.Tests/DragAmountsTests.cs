using System.Reflection;
using System.Runtime.CompilerServices;

namespace Tuck.Mod.Tests;

[CollectionDefinition("Drag amounts", DisableParallelization = true)]
public class DragAmountsCollection { }

[Collection("Drag amounts")]
public class DragAmountsTests : IDisposable
{
    private readonly UISettings _previousSettings = UISettings.current;

    public DragAmountsTests()
    {
        UISettings.current = (UISettings)RuntimeHelpers.GetUninitializedObject(typeof(UISettings));
        UISettings.current.gridSize = 16;
    }

    public void Dispose() => UISettings.current = _previousSettings;

    [Fact]
    public void ResumingSingleDragOnlyWritesWritableGameMembers()
    {
        // Check the actual reflection targets without starting Unity's rendering/input loop.
        using var module = DseCecil.ModuleDefinition.ReadModule(typeof(DragAmounts).Assembly.Location);
        var resume = module.GetType(typeof(DragAmounts).FullName).NestedTypes.Single(t => t.Name == "SingleDrag")
            .Methods.Single(m => m.Name == "Resume");
        string? member = null;
        var writes = new List<string>();
        foreach (var instruction in resume.Body.Instructions)
        {
            if (instruction.OpCode == DseCecil.Cil.OpCodes.Ldstr) member = (string)instruction.Operand;
            if (instruction.Operand is DseCecil.MethodReference { Name: "Set", DeclaringType.Name: "ModReflect" })
                writes.Add(Assert.IsType<string>(member));
        }
        Assert.NotEmpty(writes);
        var handler = (ItemMouseDragHandler)RuntimeHelpers.GetUninitializedObject(typeof(ItemMouseDragHandler));
        foreach (var name in writes)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var field = typeof(ItemMouseDragHandler).GetField(name, flags);
            var property = typeof(ItemMouseDragHandler).GetProperty(name, flags);
            var type = field?.FieldType ?? property?.PropertyType;
            Assert.NotNull(type);
            var value = type.IsValueType ? Activator.CreateInstance(type) : null;
            var error = Record.Exception(() => ModReflect.Set(handler, name, value));
            Assert.True(error == null, $"Resuming a drag cannot write ItemMouseDragHandler.{name}: {error}");
        }
    }

    [Fact]
    public void ExpandsEveryOriginalKindWithoutChangingTheOriginalSelection()
    {
        var bolt = new Item("bolt");
        var nut = new Item("nut");
        var otherBolt = new Item("bolt");
        var otherNut = new Item("nut");
        var wire = new Item("wire");
        Grid(bolt, nut, otherBolt, otherNut, wire);
        var selection = new DragAmounts.SelectionKinds([bolt, nut]);

        Assert.Equal([bolt, nut, otherBolt, otherNut], selection.MatchingItems());
        Assert.Equal(new HashSet<GameItem> { bolt, nut }, selection.Originals);
        Assert.Equal([bolt, nut, otherBolt, otherNut], selection.MatchingItems());
    }

    [Fact]
    public void EachSourceExpandsOnlyItsOriginalKinds()
    {
        var bolt = new Item("bolt");
        var otherBolt = new Item("bolt");
        var unrelatedNut = new Item("nut");
        Grid(bolt, otherBolt, unrelatedNut);
        var nut = new Item("nut");
        var otherNut = new Item("nut");
        var unrelatedBolt = new Item("bolt");
        Grid(nut, otherNut, unrelatedBolt);
        var elsewhere = new Item("bolt");
        Grid(elsewhere);

        var matches = new DragAmounts.SelectionKinds([bolt, nut]).MatchingItems().ToHashSet();
        Assert.Equal(new HashSet<GameItem> { bolt, otherBolt, nut, otherNut }, matches);

        var inventoryItems = new DragAmounts.SelectionKinds([bolt, nut]).MatchingItems(entireInventory: true).ToHashSet();
        Assert.Equal(new HashSet<GameItem> { bolt, otherBolt, unrelatedNut, nut, otherNut, unrelatedBolt }, inventoryItems);
    }

    [Fact]
    public void WholeInventoryExpansionDoesNotRedefineMatchingKindsOrOriginalSelection()
    {
        var original = new Item("bolt");
        var otherBolt = new Item("bolt");
        var nut = new Item("nut");
        Grid(original, otherBolt, nut);
        var selection = new DragAmounts.SelectionKinds([original]);

        Assert.Equal([original, otherBolt], selection.MatchingItems());
        Assert.Equal([original, otherBolt, nut], selection.MatchingItems(entireInventory: true));
        Assert.Equal([original, otherBolt], selection.MatchingItems());
        Assert.Equal(new HashSet<GameItem> { original }, selection.Originals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExcludesPinnedLockedAndRetainedItems(bool entireInventory)
    {
        var original = new Item("bolt");
        var same = new Item("bolt");
        var pinned = new Item("bolt");
        pinned.EnableTag(ItemPinHelper.PINNED_TAG);
        var locked = new Item("bolt") { mayInventoryRemoveItemFunc = (_, _) => false };
        var retained = new Item("bolt") { minSlotAmountItemFunc = (_, _, _) => 1 };
        Grid(original, same, pinned, locked, retained);

        Assert.Equal([original, same], new DragAmounts.SelectionKinds([original]).MatchingItems(entireInventory));
    }

    [Fact]
    public void MatchingKindsAndSourcesStayFixedAcrossRepeatedExpansion()
    {
        var original = new Item("bolt");
        var otherBolt = new Item("bolt");
        var wire = new Item("wire");
        Grid(original, otherBolt, wire);
        var selection = new DragAmounts.SelectionKinds([original]);
        Assert.Equal([original, otherBolt], selection.MatchingItems());

        // An item changing kind or moving cannot redefine what the drag expands.
        original.identifier = "wire";
        Grid(original, new Item("wire"), new Item("bolt"));
        Assert.Equal([otherBolt], selection.MatchingItems());
        Assert.Equal([otherBolt, wire], selection.MatchingItems(entireInventory: true));
        Assert.Equal(new HashSet<GameItem> { original }, selection.Originals);
    }

    private sealed class Item : GameItem
    {
        internal Item(string id) : base(id)
        {
            state.EnableTag("IS_OWNED_TAG");
            modifiedState = state.Clone();
            shape = new GridShapeBuilder(1, 1).SetDataFill(1);
            modifiedShape = shape.Clone();
        }
        public override void Validate() { }
        public override void Destroy() { }
    }

    private static GameGridInventory Grid(params Item[] items)
    {
        var grid = (GameGridInventory)RuntimeHelpers.GetUninitializedObject(typeof(GameGridInventory));
        typeof(GameGridInventory).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(grid, items.Cast<GameItem>().ToList());
        typeof(GameGridInventory).GetField("<inventoryShape>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(grid, new GridShapeBuilder(items.Length, 1) { outside = 1 }.SetDataFill(0));
        for (int i = 0; i < items.Length; i++)
        {
            items[i].parentInventory = grid;
            items[i].modifiedShape = new GridShapeBuilder(items[i].shape).SetPosition(i, 0);
        }
        return grid;
    }
}
