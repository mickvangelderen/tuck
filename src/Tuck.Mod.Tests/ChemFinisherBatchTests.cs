using System.Reflection;
using System.Runtime.CompilerServices;

namespace Tuck.Mod.Tests;

public class ChemFinisherBatchTests : IDisposable
{
    private readonly UISettings _previousSettings = UISettings.current;

    public ChemFinisherBatchTests()
    {
        // Placement uses gridSize but no renderer. Bypass the Unity component constructor in this headless fixture.
        UISettings.current = Raw<UISettings>();
        UISettings.current.gridSize = 16;
    }

    public void Dispose() => UISettings.current = _previousSettings;

    [Fact]
    public void FullTrayDoesNotCallOutputOrConsumeMaterials()
    {
        var tray = Grid(1, 1, new FixtureItem(Shape(1, 1)));
        var probe = new ChemPlacementProbe("dream_dust", Shape(1, 1));
        int materials = 20;
        Assert.False(ChemFinisherBatch.TryPrintOne(probe, tray, () => materials > 0, () =>
        {
            materials--;
            throw new Exception("Full output must be rejected before calling vanilla.");
        }));
        Assert.Equal(20, materials);
        Assert.Null(probe.parentInventory);
        Assert.Single(tray.childItems);
    }

    [Theory]
    [InlineData(1, 30, 30, 1)]
    [InlineData(5, 30, 30, 5)]
    [InlineData(20, 30, 30, 20)]
    [InlineData(20, 3, 30, 3)]
    [InlineData(20, 30, 3, 3)]
    [InlineData(5, 0, 5, 0)]
    public void RechecksMaterialsAndSpaceForEveryFreshOutput(int requested, int materials, int capacity, int expected)
    {
        var tray = Grid(capacity, 1);
        var probe = new ChemPlacementProbe("dream_dust", Shape(1, 1));
        int initialMaterials = materials;
        int calls = 0;
        double now = 0;
        var queue = new ChemPrintQueue(() => now, () => ChemFinisherBatch.TryPrintOne(probe, tray, () => materials > 0, () =>
        {
            // Simulate only the renderer-dependent mutation. Search with the game's actual implementation
            // and a NEW output object, as the real button does. Never insert the placement probe.
            var product = new FixtureItem(Shape(1, 1));
            var slot = tray.TryFindOneValidInventorySlot(product);
            Assert.NotNull(slot);
            Assert.True(slot.numTransfer > 0);
            product.modifiedShape = slot.itemGridShape.Clone();
            product.parentInventory = tray;
            Items(tray).Add(product);
            materials--;
            calls++;
            return true;
        }));
        queue.Enqueue(requested);
        for (int frame = 0; frame <= requested; frame++)
        {
            queue.Tick();
            now += 0.02;
        }
        Assert.Equal(ChemPrintQueue.PrintState.Ready, queue.State);
        Assert.Equal(expected, queue.Completed);
        Assert.Equal(expected, calls);
        Assert.Equal(initialMaterials - expected, materials);
        Assert.Equal(expected, tray.childItems.Count);
        Assert.DoesNotContain(probe, tray.childItems);
        Assert.Null(probe.parentInventory);
        Assert.Equal(0, probe.uniqueId);
        Assert.Equal(1, probe.unitCount);
        Assert.All(tray.childItems, product => Assert.NotSame(probe, product));
    }

    [Fact]
    public void PlacementChecksShapeAndRotationRatherThanFreeCellCount()
    {
        var probe = new ChemPlacementProbe("pink_alt_injector", Shape(1, 3));
        Assert.True(probe.HasSpace(Grid(3, 1))); // Only fits after rotation.
        var middle = new FixtureItem(Shape(1, 2).SetPosition(1, 0));
        Assert.False(probe.HasSpace(Grid(3, 2, middle))); // Four free cells, but no length-three opening.
    }

    [Fact]
    public void NativeLocksAdmissionAndQuantityLimitsAreHonored()
    {
        var tray = Grid(2, 2);
        var probe = new ChemPlacementProbe("dream_dust", Shape(1, 1));
        Assert.True(probe.HasSpace(tray));
        tray.LockInv();
        Assert.False(probe.HasSpace(tray));
        tray.UnlockInv();
        tray.mayInventoryAddItemFunc = (_, _) => false;
        Assert.False(probe.HasSpace(tray));
        tray.mayInventoryAddItemFunc = (item, inv) => ContainerHelper.CanContainerStoredItem(item, inv);
        Assert.True(probe.HasSpace(tray));
        tray.maxSlotAddItemFunc = (_, _, _) => 0;
        Assert.False(probe.HasSpace(tray));
    }

    [Fact]
    public void StopsIfOriginalActionDoesNotCompleteAndPropagatesExceptions()
    {
        var tray = Grid(2, 2);
        var probe = new ChemPlacementProbe("dream_dust", Shape(1, 1));
        int calls = 0;
        Assert.False(ChemFinisherBatch.TryPrintOne(probe, tray, () => true, () => { calls++; return false; }));
        Assert.Equal(1, calls);
        var failure = new InvalidOperationException("Input callback failed");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            ChemFinisherBatch.TryPrintOne(probe, tray, () => true, () => throw failure)));
    }

    [Fact]
    public void UnsupportedRecipesHaveNoPlacementPrototype() =>
        Assert.Null(ChemPlacementProbe.SpriteFor("unknown_modded_product"));

    [Theory]
    [InlineData("dream_dust", "DreamDust")]
    [InlineData("nightmare_dust", "NightmareDust")]
    [InlineData("caffeine_pill", "CaffeinePill")]
    [InlineData("zyanide_pill", "ZyanidePill")]
    [InlineData("birth_control_pill", "BirthControlPill")]
    [InlineData("fanta_pill", "FentanylPill")]
    [InlineData("pink_alt_injector", "Injector")]
    public void ProbeSpriteExistsInTheFetchedGamesProductFactory(string recipe, string factory)
    {
        // Inspect the shipped IL without invoking Unity factories or consuming random purity rolls.
        using var module = DseCecil.ModuleDefinition.ReadModule(typeof(GameItem).Assembly.Location);
        var method = module.GetType("MedsItemDirectory").Methods.Single(m => m.Name == factory);
        var strings = method.Body.Instructions.Select(i => i.Operand).OfType<string>().ToArray();
        Assert.Contains("Items/items_medical", strings);
        Assert.Contains(ChemPlacementProbe.SpriteFor(recipe), strings);
    }

    [Fact]
    public void EveryFetchedVanillaRecipeHasAProbeMapping()
    {
        using var module = DseCecil.ModuleDefinition.ReadModule(typeof(GameItem).Assembly.Location);
        var method = module.GetType("ChemFinisherRecipe").Methods.Single(m => m.Name == "InitRecipes");
        var recipes = method.Body.Instructions
            .Where(i => i.Operand is DseCecil.FieldReference f && f.Name == "identifier")
            .Select(i => Assert.IsType<string>(i.Previous.Operand)).ToArray();
        Assert.NotEmpty(recipes);
        Assert.All(recipes, recipe => Assert.NotNull(ChemPlacementProbe.SpriteFor(recipe)));
    }

    private static GridShapeBuilder Shape(int width, int height) => new GridShapeBuilder(width, height).SetDataFill(1);
    private static T Raw<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static List<GameItem> Items(GameGridInventory grid) =>
        (List<GameItem>)typeof(GameGridInventory).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(grid)!;

    private static GameGridInventory Grid(int width, int height, params GameItem[] items)
    {
        var grid = Raw<GameGridInventory>();
        typeof(GameGridInventory).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(grid, items.ToList());
        typeof(GameGridInventory).GetField("<inventoryShape>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(grid, new GridShapeBuilder(width, height) { outside = 1 }.SetDataFill(0));
        foreach (var item in items) item.parentInventory = grid;
        return grid;
    }

    private sealed class FixtureItem : GameItem
    {
        internal FixtureItem(GridShape itemShape) : base("actual_product")
        {
            shape = itemShape.Clone();
            modifiedShape = shape.Clone();
            state.EnableTag("IS_OWNED_TAG");
            modifiedState = state.Clone();
        }
        public override void Validate() { }
        public override void Destroy() => throw new Exception("Test products must not be destroyed.");
    }
}
