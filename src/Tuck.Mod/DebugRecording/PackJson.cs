using System;
using System.Collections.Generic;
using System.Linq;
using global::Tuck.PackSolver;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Tuck.Mod.DebugRecording;

/// <summary>Explicit Debug-only codec: no reflective DTO or game-object serialization.</summary>
internal static class PackJson
{
    internal static string Serialize(PackState state, PlanningOptions options, PackPlan? plan = null,
        string? planningStatus = null, string? executionStatus = null, int completedSteps = 0, string? detail = null,
        IEnumerable<ItemObservation>? observations = null) => new JObject
        {
            ["schemaVersion"] = 4,
            ["rulesVersion"] = 4,
            ["recordedUtc"] = DateTime.UtcNow.ToString("O"),
            ["state"] = new JObject
            {
                ["stateId"] = state.StateId,
                ["roots"] = new JObject
                {
                    ["ground"] = state.Roots.Ground,
                    ["backpack"] = state.Roots.Backpack,
                    ["left"] = state.Roots.Left,
                    ["right"] = state.Roots.Right
                },
                ["items"] = new JArray(state.Items.Select(WriteItem)),
                ["inventories"] = new JArray(state.Inventories.Select(WriteInventory)),
                ["definitions"] = new JObject(state.Definitions.Select(p => new JProperty(p.Key, WriteDefinition(p.Value))))
            },
            ["observations"] = new JArray((observations ?? Array.Empty<ItemObservation>()).Select(o => new JObject
            {
                ["itemId"] = o.ItemId,
                ["shape"] = WriteShape(o.Shape),
                ["transform"] = WritePlacement(o.Transform),
                ["originX"] = o.OriginX,
                ["originY"] = o.OriginY,
                ["state"] = WriteFacts(o.State),
                ["unitValue"] = o.UnitValue
            })),
            ["options"] = new JObject
            {
                ["maxSequenceNodes"] = options.MaxSequenceNodes,
                ["maxPlacementChecks"] = options.MaxPlacementChecks,
                ["maxContainerArrangements"] = options.MaxContainerArrangements
            },
            ["plan"] = plan == null ? JValue.CreateNull() : WritePlan(plan),
            ["planningStatus"] = planningStatus,
            ["executionStatus"] = executionStatus,
            ["completedSteps"] = completedSteps,
            ["detail"] = detail
        }.ToString(Formatting.Indented);

    internal static (PackState State, PlanningOptions Options, PackPlan? Plan, IReadOnlyList<ItemObservation> Observations) Deserialize(string json)
    {
        var root = JObject.Parse(json);
        int schema = (int)root["schemaVersion"]!;
        int rules = (int)root["rulesVersion"]!;
        if (!(schema == 4 && rules == 4 || schema == 3 && rules is 3 or 4))
            throw new NotSupportedException("Unknown packing recording schema or rules version.");
        var state = root["state"]!; var roots = state["roots"]!;
        var captured = new PackState((string)state["stateId"]!,
            new((string)roots["ground"]!, (string)roots["backpack"]!, (string)roots["left"]!, (string)roots["right"]!),
            state["items"]!.Select(ReadItem), state["inventories"]!.Select(ReadInventory),
            ((JObject)state["definitions"]!).Properties().Select(p => ReadDefinition(p.Name, p.Value)));
        var options = root["options"]!; var plan = root["plan"];
        var parsed = plan == null || plan.Type == JTokenType.Null ? null : new PackPlan((string)plan["initialStateId"]!,
            plan["steps"]!.Select(t => ReadStep(t, captured, schema)));
        // Older captures remain readable, but every plan must satisfy today's whole-item policy.
        if (parsed != null) PlanReplay.Replay(captured, parsed);
        var observations = root["observations"]!.Select(o => new ItemObservation((string)o["itemId"]!, ReadShape(o["shape"]!),
            (Placement.Grid)ReadPlacement(o["transform"]!), (int)o["originX"]!, (int)o["originY"]!, ReadFacts(o["state"]!), (long?)o["unitValue"])).ToArray();
        return (captured, new((int)options["maxSequenceNodes"]!, (int)options["maxPlacementChecks"]!,
            (int)options["maxContainerArrangements"]!), parsed, observations);
    }
    private static JObject WriteItem(ItemState item) => new()
    {
        ["id"] = item.Id,
        ["identifier"] = item.Identifier,
        ["location"] = WriteLocation(item.Location),
        ["unitCount"] = item.UnitCount,
        ["unitValue"] = item.UnitValue,
        ["unitBaseValue"] = item.UnitBaseValue,
        ["shape"] = WriteShape(item.Shape),
        ["state"] = WriteFacts(item.State)
    };
    private static ItemState ReadItem(JToken i) => new((string)i["id"]!, (string)i["identifier"]!, ReadLocation(i["location"]!),
        (int)i["unitCount"]!, (long)i["unitValue"]!, ReadShape(i["shape"]!), ReadFacts(i["state"]!), (long)i["unitBaseValue"]!);

    private static JObject WriteDefinition(ItemDefinition d) => new()
    {
        ["rules"] = new JObject
        {
            ["quantity"] = WriteQuantity(d.Rules.Quantity),
            ["parentShape"] = d.Rules.ParentShape.ToString(),
            ["unsupportedParentState"] = d.Rules.UnsupportedParentState,
            ["unsupportedTransfer"] = d.Rules.UnsupportedTransfer
        }
    };
    private static ItemDefinition ReadDefinition(string identifier, JToken token)
    {
        // Legacy factory defaults and stack compatibility have no meaning for whole-item packing.
        var r = token["rules"]!;
        return new(identifier, new(ReadQuantity(r["quantity"]!),
            (string)r["parentShape"]! switch
            {
                "None" => ParentShapeRule.None,
                "GunAttachment" => ParentShapeRule.GunAttachment,
                "Unsupported" => ParentShapeRule.Unsupported,
                _ => throw new NotSupportedException("Unknown parent shape rule.")
            },
            (bool)r["unsupportedParentState"]!, (string?)r["unsupportedTransfer"]));
    }
    private static JObject WriteInventory(InventoryState i) => new()
    {
        ["id"] = i.Id,
        ["ownerItemId"] = i.OwnerItemId,
        ["layout"] = i.Layout is InventoryLayout.Grid g ? new JObject { ["kind"] = "Grid", ["cells"] = WriteShape(g.Cells) }
            : new JObject { ["kind"] = "Slot", ["orientation"] = ((InventoryLayout.Slot)i.Layout).Orientation },
        ["overrideLockInsert"] = i.OverrideLockInsert,
        ["overrideLockRemove"] = i.OverrideLockRemove,
        ["rules"] = new JObject
        {
            ["admission"] = WriteAdmission(i.Admission),
            ["quantity"] = WriteQuantity(i.Quantity),
            ["unsupportedTransfer"] = i.UnsupportedTransfer,
            ["unsupportedParentEffects"] = i.UnsupportedParentEffects
        }
    };
    private static InventoryState ReadInventory(JToken i)
    {
        var l = i["layout"]!; var r = i["rules"]!;
        InventoryLayout layout = (string)l["kind"]! switch
        {
            "Grid" => new InventoryLayout.Grid(ReadShape(l["cells"]!)),
            "Slot" => new InventoryLayout.Slot((int)l["orientation"]!),
            _ => throw new NotSupportedException("Unknown inventory layout.")
        };
        return new((string)i["id"]!, layout, ReadAdmission(r["admission"]!), (string?)i["ownerItemId"],
            (bool)i["overrideLockInsert"]!, (bool)i["overrideLockRemove"]!, ReadQuantity(r["quantity"]!),
            (string?)r["unsupportedTransfer"], (bool)r["unsupportedParentEffects"]!);
    }
    private static JObject WriteAdmission(AdmissionRule r)
    {
        var result = new JObject { ["kind"] = r.Kind.ToString() };
        if (r.Kind == AdmissionKind.Ground) result["allowsHazardousWaste"] = r.AllowsHazardousWaste;
        if (r.Kind == AdmissionKind.Toolbox) result["slotIndex"] = r.SlotIndex;
        if (r.AllowedTags != null) result["allowedTags"] = new JArray(r.AllowedTags);
        if (r.AllowedIdentifiers != null) result["allowedIdentifiers"] = new JArray(r.AllowedIdentifiers);
        return result;
    }
    private static AdmissionRule ReadAdmission(JToken r) => new((string)r["kind"]! switch
    {
        "Owned" => AdmissionKind.Owned,
        "Ground" => AdmissionKind.Ground,
        "Backpack" => AdmissionKind.Backpack,
        "Container" => AdmissionKind.Container,
        "Toolbox" => AdmissionKind.Toolbox,
        "Tagged" => AdmissionKind.Tagged,
        "Unsupported" => AdmissionKind.Unsupported,
        _ => throw new NotSupportedException("Unknown admission rule.")
    },
        r["allowedTags"]?.Select(t => (string)t!), r["allowedIdentifiers"]?.Select(t => (string)t!),
        (int?)r["slotIndex"] ?? 0, (bool?)r["allowsHazardousWaste"] ?? false);
    private static JObject WriteQuantity(QuantityRules r)
    {
        var result = new JObject();
        if (r.Maximum.HasValue) result["maximum"] = r.Maximum;
        if (r.Minimum != 0) result["minimum"] = r.Minimum;
        if (r.MaxAdd != int.MaxValue) result["maxAdd"] = r.MaxAdd;
        if (r.MaxRemove != int.MaxValue) result["maxRemove"] = r.MaxRemove;
        return result;
    }
    private static QuantityRules ReadQuantity(JToken r) => new((int?)r["maximum"], (int?)r["minimum"] ?? 0,
        (int?)r["maxAdd"] ?? int.MaxValue, (int?)r["maxRemove"] ?? int.MaxValue);
    private static JObject WriteFacts(ItemFacts f) => new()
    {
        ["tags"] = new JObject(f.Tags.Select(p => new JProperty(p.Key, new JObject { ["enabled"] = p.Value.Enabled, ["int"] = p.Value.Int }))),
        ["types"] = new JArray(f.Types)
    };
    private static ItemFacts ReadFacts(JToken f) => new(((JObject)f["tags"]!).Properties().Select(p =>
        new KeyValuePair<string, TagValue>(p.Name, new((bool)p.Value["enabled"]!, (int)p.Value["int"]!))), f["types"]!.Select(t => (string)t!));
    private static JObject WriteShape(ShapeState s) => new()
    { ["width"] = s.Width, ["height"] = s.Height, ["cells"] = new JArray(s.Cells.Select(c => (int)c)), ["outsideBounds"] = s.OutsideBounds };
    private static ShapeState ReadShape(JToken s) => new((int)s["width"]!, (int)s["height"]!, s["cells"]!.Select(c => checked((byte)(int)c)), checked((byte)(int)s["outsideBounds"]!));
    private static JObject WriteLocation(ItemLocation l) => new() { ["inventoryId"] = l.InventoryId, ["placement"] = WritePlacement(l.Placement) };
    private static ItemLocation ReadLocation(JToken l) => new((string)l["inventoryId"]!, ReadPlacement(l["placement"]!));
    private static JObject WritePlacement(Placement p) => p is Placement.Grid g
        ? new JObject { ["kind"] = "Grid", ["x"] = g.X, ["y"] = g.Y, ["orientation"] = g.Orientation, ["flipped"] = g.Flipped }
        : new JObject { ["kind"] = "Slot" };
    private static Placement ReadPlacement(JToken p) => (string)p["kind"]! switch
    {
        "Slot" => new Placement.Slot(),
        "Grid" => new Placement.Grid((int)p["x"]!, (int)p["y"]!, (int)p["orientation"]!, (bool)p["flipped"]!),
        _ => throw new NotSupportedException("Unknown placement.")
    };
    private static JObject WritePlan(PackPlan plan) => new()
    {
        ["initialStateId"] = plan.InitialStateId,
        ["steps"] = new JArray(plan.Steps.Select(s => new JObject
        { ["sourceItemId"] = s.SourceItemId, ["destination"] = WriteLocation(s.Destination) }))
    };

    private static TransferStep ReadStep(JToken step, PackState state, int schema)
    {
        string sourceId = (string)step["sourceItemId"]!;
        var destination = step["destination"]!;
        if (schema == 4)
        {
            // Do not silently reinterpret a stale quantity-bearing plan as a whole-item move.
            if (step["quantity"] != null || destination["kind"] != null)
                throw new InvalidOperationException("Schema 4 only represents whole-item placements.");
            return new(sourceId, ReadLocation(destination));
        }
        // Migration lives at the recording boundary, not in the solver's input/output models.
        if ((string)destination["kind"]! != "Place")
            throw new InvalidOperationException("Packing only places whole items; stacking is not allowed.");
        var item = state.Items.SingleOrDefault(i => i.Id == sourceId)
            ?? throw new InvalidOperationException("Missing source item in legacy plan.");
        if ((int)step["quantity"]! != item.UnitCount)
            throw new InvalidOperationException("Packing must transfer the entire item count; splitting is not allowed.");
        return new(sourceId, ReadLocation(destination["location"]!));
    }
}
