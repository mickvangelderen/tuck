using global::Tuck.PackSolver;
using Newtonsoft.Json.Linq;
using Tuck.Mod.DebugRecording;

namespace Tuck.Mod.Tests;

public class PackRecordingTests
{
    [Fact]
    public void RoundTripsObservedGameValueAlongsideAnExplicitPlan()
    {
        var state = RecordingFixtures.State([RecordingFixtures.Item("module", "ground", 30, identifier: "system_module_ruined")]);
        var observation = new ItemObservation("module", ShapeState.Rectangle(1, 1), new(), 0, 0, RecordingFixtures.Facts(), 30);
        var plan = new PackPlan(state.StateId, [RecordingFixtures.Place("module", "left")]);
        var recorded = PackJson.Deserialize(PackJson.Serialize(state, new(), plan, observations: [observation]));
        Assert.Equal(30, Assert.Single(recorded.State.Items).UnitValue);
        Assert.Empty(ItemObservation.Differences(recorded.State, recorded.Observations));
        Assert.Null(StateComparison.Difference(PlanReplay.Replay(state, plan), PlanReplay.Replay(recorded.State, recorded.Plan!)));
    }
    [Fact]
    public void ObservationsAreRoundTrippedSeparatelyFromPlanningFacts()
    {
        var state = RecordingFixtures.State([RecordingFixtures.Item("loot", "ground", 30)]);
        var observation = new ItemObservation("loot", ShapeState.Rectangle(4, 4), new(), 2, 1, new(), 999999);
        var root = JObject.Parse(PackJson.Serialize(state, new(), observations: [observation]));
        var item = root["state"]!["items"]![0]!;
        Assert.Null(item["modifiedShape"]); Assert.Null(item["modifiedState"]); Assert.Null(item["resolvedUnitValue"]);
        Assert.Null(item["shape"]!["transform"]); Assert.Null(item["parentInventoryId"]);
        Assert.NotNull(item["location"]); Assert.NotNull(root["state"]!["definitions"]!["loot"]);
        var recorded = PackJson.Deserialize(root.ToString());
        Assert.Single(ItemObservation.Differences(recorded.State, recorded.Observations));
        Assert.Null(StateComparison.Difference(state, recorded.State));
        Assert.Equal(30, Assert.Single(recorded.State.Items).UnitValue);
    }
    [Fact]
    [Trait("Category", "PackingPolicy")]
    public void RecordsOnlyExistingItemFactsAndWholeItemMoves()
    {
        var definition = new ItemDefinition("coins", new(new(5)));
        var state = RecordingFixtures.State([RecordingFixtures.Item("source", "ground", 99, count: 5, identifier: "coins")], null, definition);
        var plan = new PackPlan("test", [RecordingFixtures.Place("source", "left"), RecordingFixtures.Place("source", "right")]);
        var json = PackJson.Serialize(state, new(), plan);
        var document = JObject.Parse(json);
        Assert.Equal(4, (int)document["schemaVersion"]!);
        var recordedDefinition = document["state"]!["definitions"]!["coins"]!;
        Assert.Null(recordedDefinition["defaults"]);
        Assert.Null(recordedDefinition["rules"]!["stackByIdentifier"]);
        foreach (var step in document["plan"]!["steps"]!)
        {
            Assert.Null(step["quantity"]);
            Assert.Null(step["destination"]!["kind"]);
            Assert.NotNull(step["destination"]!["inventoryId"]);
        }
        var recorded = PackJson.Deserialize(json);
        Assert.Null(StateComparison.Difference(state, recorded.State));
        var final = PlanReplay.Replay(recorded.State, recorded.Plan!);
        Assert.Equal("right", Assert.Single(final.Items).Location.InventoryId);
        Assert.Equal(5, Assert.Single(final.Items).UnitCount);
        Assert.Equal(495, PlanReplay.ReturnedValue(final));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [Trait("Category", "PackingPolicy")]
    public void ImportsLegacyWholeStackMovesWithoutChangingTheirFacts(int rules)
    {
        var state = RecordingFixtures.State([RecordingFixtures.Item("source", "ground", 99, count: 5, identifier: "coins")],
            null, new ItemDefinition("coins", new(new(5))));
        var legacy = LegacyRecording(state, rules);
        var recorded = PackJson.Deserialize(legacy.ToString());
        Assert.Null(StateComparison.Difference(state, recorded.State));
        Assert.Equal(495, PlanReplay.ReturnedValue(PlanReplay.Replay(recorded.State, recorded.Plan!)));
        var rewritten = JObject.Parse(PackJson.Serialize(recorded.State, recorded.Options, recorded.Plan));
        Assert.Null(rewritten["state"]!["definitions"]!["coins"]!["defaults"]);
        Assert.Null(rewritten["plan"]!["steps"]![0]!["quantity"]);
    }

    [Theory]
    [InlineData(3, false, 2)]
    [InlineData(4, false, 2)]
    [InlineData(3, false, 6)]
    [InlineData(4, false, 6)]
    [InlineData(3, true, 5)]
    [InlineData(4, true, 5)]
    [Trait("Category", "PackingPolicy")]
    public void RejectsLegacyStackManipulationPlans(int rules, bool merge, int quantity)
    {
        var state = RecordingFixtures.State([RecordingFixtures.Item("source", "ground", 99, count: 5, identifier: "coins")],
            null, new ItemDefinition("coins", new(new(5))));
        var legacy = LegacyRecording(state, rules);
        var step = legacy["plan"]!["steps"]![0]!;
        step["quantity"] = quantity;
        if (merge) step["destination"] = new JObject { ["kind"] = "Merge", ["targetItemId"] = "source" };
        var error = Assert.Throws<InvalidOperationException>(() => PackJson.Deserialize(legacy.ToString()));
        Assert.Contains(merge ? "stacking" : "entire item count", error.Message);
        legacy["plan"] = JValue.CreateNull();
        Assert.Null(StateComparison.Difference(state, PackJson.Deserialize(legacy.ToString()).State));
    }

    [Fact]
    [Trait("Category", "PackingPolicy")]
    public void NewSchemaDoesNotSilentlyDiscardLegacyQuantityInstructions()
    {
        var state = RecordingFixtures.State([RecordingFixtures.Item("source", "ground", 99, count: 5, identifier: "coins")],
            null, new ItemDefinition("coins", new(new(5))));
        var root = JObject.Parse(PackJson.Serialize(state, new(), new("test", [RecordingFixtures.Place("source", "left")])));
        root["plan"]!["steps"]![0]!["quantity"] = 2;
        Assert.Throws<InvalidOperationException>(() => PackJson.Deserialize(root.ToString()));
    }

    private static JObject LegacyRecording(PackState state, int rules)
    {
        var root = JObject.Parse(PackJson.Serialize(state, new(), new(state.StateId, [RecordingFixtures.Place("source", "left")])));
        root["schemaVersion"] = 3; root["rulesVersion"] = rules;
        var step = root["plan"]!["steps"]![0]!;
        step["quantity"] = 5;
        step["destination"] = new JObject { ["kind"] = "Place", ["location"] = step["destination"]!.DeepClone() };
        var definition = root["state"]!["definitions"]!["coins"]!;
        definition["rules"]!["stackByIdentifier"] = true;
        definition["defaults"] = new JObject
        {
            ["shape"] = root["state"]!["items"]![0]!["shape"]!.DeepClone(),
            ["state"] = root["state"]!["items"]![0]!["state"]!.DeepClone(),
            ["unitValue"] = 999,
            ["unitBaseValue"] = 0
        };
        return root;
    }
    [Fact]
    public void RoundTripsThePackedGraphAndExplicitPlan()
    {
        var state = RecordingFixtures.State([RecordingFixtures.Item("loot", "ground", 30),
            RecordingFixtures.Item("fixed", "right", 2, tags: ["PINNED_TAG"])]);
        var plan = new PackPlan(state.StateId, [RecordingFixtures.Place("loot", "left")]);
        string json = PackJson.Serialize(state, new(), plan);
        var recording = PackJson.Deserialize(json);
        Assert.Null(StateComparison.Difference(state, recording.State));
        Assert.NotNull(recording.Plan);
        Assert.Null(StateComparison.Difference(PlanReplay.Replay(state, plan), PlanReplay.Replay(recording.State, recording.Plan)));
        Assert.DoesNotContain("Orientations", json);
        Assert.DoesNotContain("Unity", json);
    }
    [Fact]
    public void KeepsUnsupportedOccupantsAndDistinguishesNullFromEmptyRuleLists()
    {
        var inventories = RecordingFixtures.Inventories();
        inventories[2] = new("left", new InventoryLayout.Slot(), new(AdmissionKind.Container, allowedTags: []));
        var state = RecordingFixtures.State([RecordingFixtures.Item("blocked", "ground", 5)], inventories, new ItemDefinition("blocked", rules: new(unsupportedTransfer: "unknown effect")));
        var recording = PackJson.Deserialize(PackJson.Serialize(state, new()));
        Assert.Null(recording.Plan);
        Assert.Null(StateComparison.Difference(state, recording.State));
        Assert.Equal("unknown effect", recording.State.Definitions["blocked"].Rules.UnsupportedTransfer);
        Assert.Empty(recording.State.Inventories.Single(i => i.Id == "left").Admission.AllowedTags!);
        Assert.Null(recording.State.Inventories.Single(i => i.Id == "right").Admission.AllowedTags);
    }
    [Fact]
    public void RejectsAnUnknownSchemaAndAnIllegalRecordedPlan()
    {
        var state = RecordingFixtures.State([RecordingFixtures.Item("pin", "ground", 1, tags: ["PINNED_TAG"])]);
        var unknown = JObject.Parse(PackJson.Serialize(state, new()));
        unknown["schemaVersion"] = 9;
        Assert.Throws<NotSupportedException>(() => PackJson.Deserialize(unknown.ToString()));
        string illegal = PackJson.Serialize(state, new(), new("test", [RecordingFixtures.Place("pin", "left")]));
        Assert.Throws<InvalidOperationException>(() => PackJson.Deserialize(illegal));
    }
}
