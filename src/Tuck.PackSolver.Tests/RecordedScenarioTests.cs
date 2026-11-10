using Newtonsoft.Json.Linq;
using Tuck.PackSolver;

namespace Tuck.PackSolver.Tests;

public class RecordedScenarioTests
{
    [Theory]
    [SolverData]
    [Trait("Category", "SolverContract")]
    public void PlansAndSequencesTheOriginalDayTwoScavengingScene(string solverName)
    {
        // Import the observed facts of this one audited vanilla fixture, not arbitrary callback names.
        var document = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "day-2-dumping-ground.json")));
        var nodes = document["nodes"]!.ToDictionary(n => (int)n["id"]!);
        var items = nodes.Values.Where(n => n["item"]?.Type == JTokenType.Object).ToArray();
        var owners = new Dictionary<int, int>();
        foreach (var item in items)
        {
            int content = (int)item["item"]!["contentNodeId"]!;
            if (content >= 0) Visit(content, (int)item["id"]!);
        }
        var inventories = nodes.Values.Where(n => n["inventory"]?.Type == JTokenType.Object).Select(n =>
        {
            int id = (int)n["id"]!;
            var inv = n["inventory"]!;
            bool grid = (string)inv["kind"]! == "grid";
            return new InventoryState("inventory-" + id, grid ? new InventoryLayout.Grid(ReadOldShape(inv["shape"]!)) : new InventoryLayout.Slot((int)inv["defaultOrientation"]!),
                new(id == 0 ? AdmissionKind.Ground : id == 1 ? AdmissionKind.Backpack : owners.ContainsKey(id) ? AdmissionKind.Container : AdmissionKind.Owned),
                owners.TryGetValue(id, out int owner) ? "item-" + owner : null,
                (bool)inv["insertLocked"]!, (bool)inv["removeLocked"]!);
        }).ToArray();
        var capturedItems = items.Select(n =>
        {
            var item = n["item"]!;
            int quantity = (int)item["quantity"]!;
            long Scalar(string name) => long.Parse((string)item["scalars"]!.Single(s => (string)s["name"]! == name)["value"]!);
            int parent = (int)item["parentInventoryId"]!;
            Placement placement = (string)nodes[parent]["inventory"]!["kind"]! == "slot"
                ? new Placement.Slot() : ReadOldTransform(item["modifiedShape"]!);
            return new ItemState("item-" + (int)n["id"]!, (string)item["identifier"]!, new("inventory-" + parent, placement),
                quantity, Scalar("unitValue"), ReadOldShape(item["shape"]!), ReadOldFacts(item["state"]!, item["itemTypes"]!), Scalar("unitBaseValue"));
        }).ToArray();
        var state = new PackState("import-day-two", new("inventory-0", "inventory-1", "inventory-2", "inventory-3"), capturedItems, inventories, capturedItems.Select(i => i.Identifier).Distinct().Select(id => new ItemDefinition(id)));
        var view = new StateView(state);
        foreach (var node in items)
        {
            var item = node["item"]!;
            string id = "item-" + (int)node["id"]!;
            var effective = view.Effective(id);
            Assert.True(StateComparison.SameShape(ReadOldShape(item["modifiedShape"]!), effective.Shape));
            Assert.True(StateComparison.SameFacts(ReadOldFacts(item["modifiedState"]!, item["itemTypes"]!), effective.State));
            long observedValue = long.Parse((string)item["observations"]!.Single(o => (string)o["name"]! == "getValue")["value"]!)
                / (int)item["quantity"]!;
            Assert.Equal(observedValue, effective.UnitValue);
            Assert.Equal(0, effective.OriginX);
            Assert.Equal(0, effective.OriginY);
            var observed = ReadOldTransform(item["modifiedShape"]!);
            var placed = view.Placed(id).Transform;
            if (state.Items.Single(i => i.Id == id).Location.Placement is Placement.Grid)
                Assert.Equal(observed, placed);
            else
            {
                Assert.Equal(observed.Orientation, placed.Orientation);
                Assert.Equal(observed.Flipped, placed.Flipped);
            }
        }
        Assert.Equal(8, state.Items.Count);
        var result = SolverUnderTest.Solve(solverName, new(state));
        Assert.True(result.FinalValue > result.InitialValue);

        void Visit(int id, int owner)
        {
            var node = nodes[id];
            if (node["inventory"]?.Type == JTokenType.Object) { owners.Add(id, owner); return; }
            if (node["item"]?.Type == JTokenType.Object) return;
            foreach (var child in node["children"]!) Visit((int)child, owner);
        }
        static ShapeState ReadOldShape(JToken shape) => new((int)shape["width"]!, (int)shape["height"]!,
            shape["cells"]!.Select(c => (byte)(int)c), (byte)(int)shape["outside"]!);
        static Placement.Grid ReadOldTransform(JToken s) => new((int)s["minX"]!, (int)s["minY"]!, (int)s["orientation"]!, (bool)s["flipped"]!);
        static ItemFacts ReadOldFacts(JToken tags, JToken types) => new(tags.Where(t => (string)t["key"]! is
                "IS_OWNED_TAG" or "BACKPACK_TAG" or "CONTAINER_TAG" or "not_purchased" or "amenities" or "amenities_use")
            .Select(t => new KeyValuePair<string, TagValue>((string)t["key"]!, new((bool)t["enabled"]!, (int)t["intValue"]!)))
            .Where(p => p.Value.Enabled || p.Value.Int != 0), types.Select(t => (string)t!).Where(t => t is "STORAGE" or "MACHINE" or "TOOL" or "MELEE_WEAPON"));
    }
}
