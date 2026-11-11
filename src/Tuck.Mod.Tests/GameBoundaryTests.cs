using System.Reflection;
using System.Runtime.CompilerServices;
using global::Tuck.PackSolver;
using Tuck.Mod.Packing;

namespace Tuck.Mod.Tests;

// Use the actual game assembly. Reflection is confined to test fixture setup to bypass Unity rendering constructors.
public class GameBoundaryTests
{
    private sealed class FixtureItem : GameItem
    {
        internal FixtureItem(string id, long value = 1) : base(id)
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
    private static CapturedPack Capture(GameGridInventory ground) => PackCapture.Capture(ground,
        Raw<GameSlotInventory>(), Raw<GameSlotInventory>(), Raw<GameSlotInventory>(), () => false);

    [Fact]
    public void ShippedAssembliesPassTheGamesReflectionCapabilityCheck()
    {
        // The test host has no ModLoader game-assembly registry; supply its shipped references explicitly.
        var scan = ModScanner.Scan([typeof(ModEntry).Assembly.Location, typeof(IPackSolver).Assembly.Location],
            ["netstandard", "Assembly-CSharp", "UnityEngine.CoreModule", "0Harmony", "Newtonsoft.Json", "UnityEngine.InputLegacyModule",
                "UnityEngine.UI", "Unity.TextMeshPro", "UnityEngine.Physics2DModule", "UnityEngine.ImageConversionModule",
                "Assembly-CSharp-firstpass"]);
        Assert.Null(scan.Error);
        Assert.DoesNotContain("reflection-external", scan.Capabilities);
        Assert.DoesNotContain(scan.Pending, f => f.Kind == "reflection-external");
        Assert.False(scan.Refused, scan.FormatFindings());
#if !DEBUG
        Assert.Null(typeof(ModEntry).Assembly.GetType("Tuck.Mod.DebugRecording.PackJson"));
        Assert.DoesNotContain(typeof(ModEntry).Assembly.GetReferencedAssemblies(), a => a.Name == "Newtonsoft.Json");
#endif
    }

    [Fact]
    public void SharedWindowInventoryIsCapturedOnceWithItsPhysicalOwner()
    {
        var bag = new FixtureItem("fanny_pack", 50);
        var contents = Grid(new FixtureItem("inside", 7));
        var window = Raw<PixelWindow>();
        Field(window, typeof(PixelWindow), "<childElement>k__BackingField", contents);
        Field(bag, typeof(GameItem), "<contentWindow>k__BackingField", window);
        Field(bag, typeof(GameItem), "<examineWindow>k__BackingField", window);
        var capture = Capture(Grid(bag));
        Assert.Equal(5, capture.State.Inventories.Count);
        var inner = Assert.Single(capture.State.Inventories, i => i.OwnerItemId != null);
        Assert.Equal(capture.State.Items.Single(i => i.Identifier == "fanny_pack").Id, inner.OwnerItemId);
        Assert.Equal(AdmissionKind.Container, inner.Admission.Kind);
        Assert.Equal(2, capture.State.Items.Count);
    }

    [Fact]
    public void CapturingMissingValueTagsDoesNotInsertThemIntoTheGame()
    {
        var item = new FixtureItem("battery", 5);
        item.modifiedState.EnableTag("power_source_item");
        int count = item.modifiedState.dict.Count;
        Assert.Equal(5, Assert.Single(Capture(Grid(item)).State.Items).UnitValue);
        Assert.Equal(count, item.modifiedState.dict.Count);
    }

    [Fact]
    public void CaptureCopiesActualObjectsAndNeverRunsAdmissionCallbacks()
    {
        var item = new FixtureItem("loot", 17);
        var ground = Grid(item);
        ground.mayInventoryAddItemFunc = (_, _) => throw new Exception("Capture must not probe.");
        int tagsBefore = item.modifiedState.dict.Count;
        var capture = Capture(ground);
        var copied = Assert.Single(capture.State.Items);
        Assert.Equal(17, copied.UnitValue);
        Assert.Equal(tagsBefore, item.modifiedState.dict.Count);
        item.unitValue = 99;
        Assert.Equal(17, copied.UnitValue);
        Assert.NotNull(StateComparison.Difference(capture.State, PackCapture.Read(capture.Bindings, capture.State.StateId)));
    }
    [Fact]
    [Trait("Category", "PackingPolicy")]
    public void CaptureDoesNotInvokeOrTreatStackCompatibilityAsATransferRestriction()
    {
        var item = new FixtureItem("loot", 17);
        item.mayStackItemFunc = (_, _) => throw new Exception("Packing must not evaluate stack compatibility.");
        var capture = Capture(Grid(item));
        Assert.Null(capture.State.Definitions["loot"].Rules.UnsupportedTransfer);
        Assert.Equal(17, Assert.Single(capture.State.Items).UnitValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "PackingPolicy")]
    public void ExecutorRejectsMissingReferencesBeforeAnyLiveProbe(bool missingSource)
    {
        var item = new FixtureItem("coins", 7) { unitCount = 5 };
        var ground = Grid(item);
        var capture = Capture(ground);
        var source = Assert.Single(capture.State.Items);
        var step = new TransferStep(missingSource ? "missing" : source.Id,
            new(missingSource ? capture.State.Roots.Left : "missing", new Placement.Slot()));
        // Any attempt to probe the live game before rejecting the plan would fail this test.
        item.mayInventoryRemoveItemFunc = (_, _) => throw new Exception("Must not probe an invalid plan.");
        var error = Assert.Throws<InvalidOperationException>(() =>
            PackExecutor.Execute(capture, new(capture.State.StateId, [step])));
        Assert.Contains(missingSource ? "Missing source" : "Missing destination", error.Message);
        Assert.Same(ground, item.parentInventory);
        Assert.Equal(5, item.unitCount);
    }

    [Fact]
    public void ExecutorRejectsStaleInputBeforeCallingAnyTransfer()
    {
        var item = new FixtureItem("loot", 17);
        var ground = Grid(item);
        var capture = Capture(ground);
        var plan = new PackPlan(capture.State.StateId,
            [new(Assert.Single(capture.State.Items).Id, new(capture.State.Roots.Left, new Placement.Slot()))]);
        item.unitCount = 2;
        item.mayInventoryRemoveItemFunc = (_, _) => throw new Exception("Must not probe stale input.");
        var result = PackExecutor.Execute(capture, plan);
        Assert.Equal(ExecutionStatus.StateMismatch, result.Status);
        Assert.Equal(0, result.CompletedSteps);
        Assert.Same(ground, item.parentInventory);
    }
    [Fact]
    public void AdditionalEffectsAreCapturedAsUnsupportedTransfers()
    {
        var item = new FixtureItem("effect");
        item.onSlotAddItemFunc += (_, _, _) => { };
        var capture = Capture(Grid(item));
        Assert.Contains("effects", capture.State.Definitions["effect"].Rules.UnsupportedTransfer!);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturesVanillaRuinedModuleRemovalAndStuckState(bool stuck)
    {
        var item = new FixtureItem("system_module_ruined", 30);
        // Install the actual game helper's callback, rather than a test copy of that rule.
        ModuleHelper.InitModuleItem(item, "MODULE_TYPE_UNIVERSAL");
        if (stuck) item.EnableTag("MODULE_STUCK_TAG");
        var captured = Capture(Grid(item));
        Assert.Null(captured.State.Definitions[item.identifier].Rules.UnsupportedTransfer);
        var copied = Assert.Single(captured.State.Items);
        Assert.Equal(stuck, copied.State.IsTag("MODULE_STUCK_TAG"));
        Assert.Equal(30, copied.UnitValue);
        Assert.Equal(30, item.unitValue);
#if DEBUG
        Assert.Equal(30, Assert.Single(captured.Observations).UnitValue);
        Assert.Empty(Tuck.Mod.DebugRecording.ItemObservation.Differences(captured.State, captured.Observations));
#endif
        // A second removal condition must not be mistaken for the mapped vanilla rule.
        item.mayInventoryRemoveItemFunc += (_, _) => false;
        var restricted = Capture(item.parentInventory as GameGridInventory ?? throw new InvalidOperationException());
        Assert.NotNull(restricted.State.Definitions[item.identifier].Rules.UnsupportedTransfer);
    }
    [Fact]
    public void DetachedTransformsMatchTheActualGameInAllOrientations()
    {
        for (int flip = 0; flip < 2; flip++)
            for (int rotation = 0; rotation < 4; rotation++)
            {
                GridShape game = new GridShapeBuilder().SetData(new byte[] { 1, 0, 1, 1, 1, 0 }, 3).SetTransform(-2, 5, flip != 0, rotation);
                var copy = new PlacedShape(PackCapture.Shape(game), PackCapture.Transform(game));
                for (int y = 0; y < game.height; y++)
                    for (int x = 0; x < game.width; x++) Assert.Equal(game.ForwardTransform(x, y), copy.Forward(x, y));
            }
    }
    [Fact]
    public void NonMutatingValueCaptureMatchesGameValueAcrossRelevantFields()
    {
        var random = new Random(418);
        for (int sample = 0; sample < 100; sample++)
        {
            var item = new FixtureItem("value", random.Next(500)) { unitBaseValue = random.Next(100) };
            foreach (var tag in new[] { "power_source_item", "amenities", "armor_item", "LIQUID_CONTAINER_TAG", "ORGAN_ITEM_TAG", "not_purchased" })
                item.modifiedState.GetTag(tag).SetEnabled(random.Next(2) == 1);
            foreach (var tag in new[] { "power_source_item_energy", "POWER_SOURCE_ITEM_VALUE_PERCENT_INT", "amenities", "amenities_use",
                         "current_armor_value", "current_max_armor_value", "armor_efficiency", "LIQUID_CONTAINER_CURRENT", "ORGAN_HEALTH_INT", "ORGAN_HEALTH_MAX_INT" })
                item.modifiedState.GetTag(tag).SetInt(random.Next(1, 100));
            Assert.Equal(item.GetValue(singleValue: true), StateView.Value(item.unitValue, item.unitBaseValue, PackCapture.Facts(item.modifiedState, item)));
        }
    }
    [Fact]
    public void DerivedAttachmentsMatchActualGameHelpersAndBaseAnchors()
    {
        (string Id, Func<GameItem, GameItem, GridShapeBuilder> Shape)[] rules =
        [
            ("silencer2", GunModDirectory.SetSilencerAdvShape), ("silencer_makeshift", GunModDirectory.SetSilencerMakeshiftShape),
            ("barrel", GunModDirectory.SetBarrelShape), ("compensator", GunModDirectory.SetCompensatorShape),
            ("grip", GunModDirectory.SetGripShape), ("mag_10mm_ext", GunModDirectory.Set10MMMagExtShape),
            ("mag_10mm_smg_box", GunModDirectory.SetSMGMagShape), ("mag_22_ext", GunModDirectory.Set22MagExtShape),
            ("stock", GunModDirectory.SetStockShape), ("short_stock", GunModDirectory.SetShortStockShape)
        ];
        foreach (var rule in rules)
            foreach (var parentId in new[] { "smg", "shotgun", "printed_gun", "handmade_pistol", "heavy_handmade_pistol", "glock_receiver", "unrelated" })
                for (int flip = 0; flip < 2; flip++)
                    for (int rotation = 0; rotation < 4; rotation++)
                    {
                        var parent = new FixtureItem(parentId);
                        parent.shape = new GridShapeBuilder(7, 2).SetDataFill(1);
                        parent.modifiedShape = new GridShapeBuilder(parent.shape).SetTransform(5, 6, flip != 0, rotation);
                        var child = new FixtureItem(rule.Id);
                        child.shape = new GridShapeBuilder(2, 1).SetDataFill(1); child.modifiedShape = child.shape.Clone();
                        child.parentItemShapeModifierForItemFunc = (i, _, _, _, _, p) => rule.Shape(p, i);
                        var inner = Grid(child);
                        var window = Raw<PixelWindow>();
                        Field(window, typeof(PixelWindow), "<childElement>k__BackingField", inner);
                        Field(parent, typeof(GameItem), "<contentWindow>k__BackingField", window);
                        parent.SyncModifiedState();
                        var captured = Capture(Grid(parent));
                        var copied = captured.State.Items.Single(i => i.Identifier == parentId);
                        var effective = new StateView(captured.State).Effective(copied.Id);
                        Assert.True(StateComparison.SameShape(PackCapture.Shape(parent.modifiedShape), effective.Shape));
                        Assert.Equal(parent.modifiedXOrigin, effective.OriginX); Assert.Equal(parent.modifiedYOrigin, effective.OriginY);
                        Assert.Equal(new Placement.Grid(5, 6, rotation, flip != 0), copied.Location.Placement);
                        Assert.Equal(PackCapture.Transform(parent.modifiedShape), new StateView(captured.State).Placed(copied.Id).Transform);
                    }
    }
#if DEBUG
    [Fact]
    public void CaptureSeparatesEffectiveObservationsFromBaseFacts()
    {
        var item = new FixtureItem("battery", 5);
        item.modifiedState.EnableTag("power_source_item");
        item.modifiedState.GetTag("power_source_item_energy").SetInt(20);
        var capture = Capture(Grid(item));
        var copied = Assert.Single(capture.State.Items);
        Assert.False(copied.State.IsTag("power_source_item"));
        Assert.Equal(5, new StateView(capture.State).Effective(copied.Id).UnitValue);
        Assert.Equal(25, Assert.Single(capture.Observations).UnitValue);
        Assert.Single(Tuck.Mod.DebugRecording.ItemObservation.Differences(capture.State, capture.Observations));
    }
#endif
}
