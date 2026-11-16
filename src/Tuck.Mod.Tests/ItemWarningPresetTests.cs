using System.Collections;
using System.Reflection;
using HarmonyLib;
using static Tuck.Mod.ItemWarningPreset;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class ItemWarningPresetTests
{
    [Fact]
    public void PresetContainsWarningMachinesChargeStepsAndTheRequestedSuppliesOnce()
    {
        var contents = Contents().ToArray();
        Assert.Equal(41, contents.Length);
        Assert.Equal(new[] { "hydroponic", "printer", "furnace", "plant_dna_manipulator", "desequencer", "moisture_farm",
            "water_purifier", "evaporator", "wine_rack", "ras", "recharger", "bottle_printer" }, MachineIds);
        foreach (var id in MachineIds)
        {
            Assert.Equal(id is not ("bottle_printer" or "recharger" or "wine_rack"), MissingIngredients.Supports(new Item(id)));
            Assert.Single(contents, entry => entry.Id == id);
        }
        Assert.Equal(Enumerable.Range(0, 11).Select(i => (int?)(i * 10)),
            contents.Where(e => e.Id == "energy_credit").Select(e => e.Charge));
        Assert.Equal(3, contents.Count(e => e.Id == "metal_ingot"));
        Assert.Equal(3, contents.Count(e => e.Id == "scrap_metal"));
        Assert.Equal("gamblers_coin", Assert.Single(contents, e => e.Sample == Sample.Stolen).Id);
        var seeds = PlantRegistry.All.Select(p => p.id).ToHashSet();
        Assert.Equal("nutrifruit_seed", Assert.Single(contents, e => seeds.Contains(e.Id)).Id);
        Assert.Equal(new[] { Sample.Normal, Sample.WrittenDna }, contents.Where(e => e.Id == "plant_dna_disk").Select(e => e.Sample));
        Assert.Equal("fast_grower", DnaTrait);
        Assert.Single(contents, e => e.Id == "printer_chip_internal_springs_improved");
        Assert.Equal(new[] { Sample.EmptyBottle, Sample.BaseWater }, contents.Where(e => e.Id == "large_bottled_water").Select(e => e.Sample));
        Assert.Equal(FishSpecies.Tilapia, Fish);
        Assert.Equal(Fish.fishIdentifier, Assert.Single(contents, e => e.Sample == Sample.AdultFish).Id);
        Assert.Equal(new[] { Sample.Normal, Sample.FertilizedRoe }, contents.Where(e => e.Id == Fish.roeIdentifier).Select(e => e.Sample));
        Assert.Single(contents, e => e.Id == "crypto_module_sec");
        Assert.Single(contents, e => e.Id == "blank_keycard");
        Assert.DoesNotContain(contents, e => e.Id is "blender" or "heating_plate" or "chem_finisher");
    }

    [Fact]
    public void PresetTurnsMachinesOnWithoutConfiguringManualRecipes()
    {
        var machine = new Item("hydroponic");
        machine.EnableTag(MachineryHelper.MACHINERY_TAG);
        machine.ModifyTag(MachineryHelper.IS_MACHINERY_POWERED, t => t.Enable().SetBool(false));
        ConfigureMachine(machine);
        Assert.True(MachineryHelper.IsMachineryToggleOn(machine));
        var finisher = new Item("chem_finisher");
        ConfigureMachine(finisher);
        Assert.False(finisher.IsTag(MachineChemFinisher.SELECTED_CHEM_ID_TAG));
    }

    [Theory]
    [InlineData("AddLegacyButtons", typeof(DebugPanel), nameof(DebugPanel.AddProceduralDebugFunction))]
    [InlineData("AddModernButtons", typeof(CustomUIDebugPanel), "RegisterFunctions")]
    public void ButtonsAppendAfterTheActualNativePopulationMethods(string hook, Type target, string methodName)
    {
        var method = AccessTools.DeclaredMethod(typeof(ItemWarningPreset), hook);
        Assert.NotNull(method.GetCustomAttribute<HarmonyPostfix>());
        var patch = method.GetCustomAttribute<HarmonyPatch>()!;
        Assert.Equal(target, patch.info.declaringType);
        Assert.Equal(methodName, patch.info.methodName);
        Assert.NotNull(AccessTools.DeclaredMethod(target, methodName));
    }

    [Fact]
    public void ModernActionsWaitForNativeEntriesThenAppendWithBluePrefixWithoutDuplicates()
    {
        var entries = (IList)AccessTools.Field(typeof(CustomUIDebugPanel), "entries").GetValue(null)!;
        var registered = AccessTools.Field(typeof(CustomUIDebugPanel), "registered");
        var previous = registered.GetValue(null);
        string nativeSource = "tuck-test-native";
        var manifest = new ModManifest { ID = "tuck-preset-test", Name = "Tuck" };
        int before = entries.Count;
        try
        {
            registered.SetValue(null, false);
            Register(manifest);
            Assert.Equal(before, entries.Count);
            CustomUIDebugPanel.Add(CustomUIDebugPanel.CATEGORY_SPAWN, "Native preset", () => { }, source: nativeSource);
            CustomUIDebugPanel.Add(CustomUIDebugPanel.CATEGORY_MARKETING_CUSTOMERS, "Native customer", () => { }, source: nativeSource);
            registered.SetValue(null, true);
            var hook = AccessTools.DeclaredMethod(typeof(ItemWarningPreset), "AddModernButtons");
            hook.Invoke(null, null);
            hook.Invoke(null, null);
            Register(manifest);
            Assert.Equal(before + 5, entries.Count);
            var ours = entries.Cast<object>().Skip(before + 2).ToArray();
            Assert.Equal(new[] { ButtonName, AppraisalButtonName, InspectionButtonName }.Select(n => Options.FormatMenuLabel(manifest, n)),
                ours.Select(e => Entry<string>(e, "name")));
            Assert.Equal(new[] { "Spawn", "Customers", "Customers" }, ours.Select(e => Entry<string>(e, "category")));
            Assert.Equal(new[] { "Spawn", "CallAppraisalCustomer", "CallSecurityInspection" }, ours.Select(e => Entry<Action>(e, "action").Method.Name));
            Assert.All(ours, e => Assert.Contains("[Tuck]</color> ", Entry<string>(e, "name")));
            Unregister();
            hook.Invoke(null, null);
            Assert.Equal(before + 2, entries.Count);
            Register(manifest); // Re-enable after the native menu already exists.
            Assert.Equal(before + 5, entries.Count);
        }
        finally
        {
            Unregister();
            CustomUIDebugPanel.Remove(nativeSource);
            registered.SetValue(null, previous);
        }
    }

    [Fact]
    public void CustomerActionsUseNativeFactoriesAndArrivalWithoutDismissingVisitors()
    {
        using var module = DseCecil.ModuleDefinition.ReadModule(typeof(ItemWarningPreset).Assembly.Location);
        var type = module.Types.Single(t => t.FullName == typeof(ItemWarningPreset).FullName);
        string[] Calls(string method) => type.Methods.Single(m => m.Name == method).Body.Instructions
            .Select(i => i.Operand).OfType<DseCecil.MethodReference>().Select(m => m.DeclaringType.Name + "." + m.Name).ToArray();
        Assert.Contains("StoreClientListAppraisal.CreateAppraisalRandom", Calls("CallAppraisalCustomer"));
        Assert.Contains("StoreClientList.CreateTrailerInspectionClient", Calls("CallSecurityInspection"));
        var calls = Calls("CallCustomer");
        Assert.Contains("StoreClientManager.AddNextClient", calls);
        Assert.Contains("PlayerStore.TryCallNextClient", calls);
        Assert.DoesNotContain(calls, c => c.Contains("Dismiss") || c.Contains("RingBell"));
    }

    private static T Entry<T>(object entry, string field) => (T)AccessTools.Field(entry.GetType(), field).GetValue(entry)!;

    private sealed class Item(string id) : GameItem(id)
    {
        public override void Validate() { }
        public override void Destroy() => throw new InvalidOperationException("Preset configuration must not destroy items.");
    }
}
