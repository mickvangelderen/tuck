using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Tuck.Mod;

internal static class ItemWarningPreset
{
    internal const string ButtonName = "Item warning test";
    internal const string AppraisalButtonName = "Spawn appraisal customer (or queue)";
    internal const string InspectionButtonName = "Spawn security check (or queue)";
    internal const string DnaTrait = PlantTraitList.FAST_GROWER;
    internal static readonly string[] MachineIds =
    {
        "hydroponic", "printer", "furnace", "plant_dna_manipulator", "desequencer", "moisture_farm",
        "water_purifier", "evaporator", "wine_rack", "ras", "recharger", "bottle_printer"
    };
    internal enum Sample { Normal, Stolen, EmptyBottle, BaseWater, AdultFish, FertilizedRoe, WrittenDna }
    internal static readonly FishSpecies Fish = FishSpecies.Tilapia;
    private static readonly FieldInfo ModernRegistered = typeof(CustomUIDebugPanel).GetField("registered", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly GameObject?[] LegacyButtons = new GameObject?[3];
    private static ModManifest? _manifest;
    private static string? _source;
    private static bool _modernAdded;
    private static PixelWindow? _overflowWindow;
    private static GameGridScrollableInventory? _overflow;

    internal static void Register(ModManifest manifest)
    {
        if (_source != null) return;
        _manifest = manifest;
        _source = manifest.ID + ".item-warning-test";
        if ((bool)ModernRegistered.GetValue(null)) AddModernButtons();
        // Awake can precede the native Start population; its postfix will append our buttons then.
        if (DebugPanel.Instance != null && DebugPanel.Instance.kitGroup?.childCount > 0) AddLegacyButtons();
    }

    internal static void Unregister()
    {
        var source = _source;
        _source = null;
        _manifest = null;
        _modernAdded = false;
        if (source != null) CustomUIDebugPanel.Remove(source);
        for (int i = 0; i < LegacyButtons.Length; i++)
        {
            if (LegacyButtons[i] != null)
            {
                LegacyButtons[i]!.SetActive(false);
                Object.Destroy(LegacyButtons[i]);
            }
            LegacyButtons[i] = null;
        }
        // Leave the temporary inventory usable so disabling the mod never deletes its contents.
    }

    [HarmonyPostfix, HarmonyPatch(typeof(CustomUIDebugPanel), "RegisterFunctions")]
    private static void AddModernButtons()
    {
        if (_source == null || _modernAdded) return;
        // Add can rebuild an open panel, which calls RegisterFunctions again.
        _modernAdded = true;
        CustomUIDebugPanel.Add(CustomUIDebugPanel.CATEGORY_SPAWN, Options.FormatMenuLabel(_manifest!, ButtonName), Spawn, source: _source,
            info: "Test machines, batteries 0-100, materials, bottles, a seed, blank/written DNA disks, a printing chip, fish, roe and access-card supplies. Overflow is temporary: move items into the store before leaving. Stolen warnings require an inspection.");
        const string customerInfo = "Arrives immediately if the shop is open and ready for a customer; otherwise queued next. Existing customers are never dismissed.";
        CustomUIDebugPanel.Add(CustomUIDebugPanel.CATEGORY_MARKETING_CUSTOMERS, Options.FormatMenuLabel(_manifest!, AppraisalButtonName), CallAppraisalCustomer,
            source: _source, info: customerInfo);
        CustomUIDebugPanel.Add(CustomUIDebugPanel.CATEGORY_MARKETING_CUSTOMERS, Options.FormatMenuLabel(_manifest!, InspectionButtonName), CallSecurityInspection,
            source: _source, info: customerInfo);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(DebugPanel), nameof(DebugPanel.AddProceduralDebugFunction))]
    private static void AddLegacyButtons()
    {
        if (_source == null || DebugPanel.Instance == null) return;
        AppendLegacyButton(0, ButtonName, Spawn, DebugPanel.Instance.kitGroup);
        AppendLegacyButton(1, AppraisalButtonName, CallAppraisalCustomer, DebugPanel.Instance.marketingGroup);
        AppendLegacyButton(2, InspectionButtonName, CallSecurityInspection, DebugPanel.Instance.marketingGroup);
    }

    private static void AppendLegacyButton(int index, string name, Action action, Transform group)
    {
        if (group == null) return;
        if (LegacyButtons[index] == null)
        {
            DebugPanel.AddNewDebugAction(Options.FormatMenuLabel(_manifest!, name), action, group);
            LegacyButtons[index] = group.GetChild(group.childCount - 1).gameObject;
        }
        LegacyButtons[index]!.transform.SetAsLastSibling();
    }

    private static void CallAppraisalCustomer() => CallCustomer(StoreClientListAppraisal.CreateAppraisalRandom);
    private static void CallSecurityInspection() => CallCustomer(StoreClientList.CreateTrailerInspectionClient);

    private static void CallCustomer(Func<StoreClient> factory)
    {
        if (_source == null || !PlayerStore.HasInstance) return;
        var store = PlayerStore.Instance;
        var customer = factory();
        store.storeClientManager.AddNextClient(customer);
        var queue = store.storeClientManager.clientStack;
        // AddNextClient can reject a visit; never call an unrelated queued customer instead.
        if (queue.Count == 0 || !ReferenceEquals(queue[0], customer)) return;
        if (store.storeState == PlayerStore.StoreState.OPEN && store.lockNextBellFlag <= 0 && StoreUIManager.Instance != null)
            store.TryCallNextClient(); // Native eligibility also guards active customers, shootouts and arrests.
    }

    internal static IEnumerable<(string Id, int? Charge, Sample Sample)> Contents()
    {
        yield return ("gamblers_coin", null, Sample.Stolen);
        foreach (var id in MachineIds) yield return (id, null, Sample.Normal);
        for (int charge = 0; charge <= 100; charge += 10) yield return ("energy_credit", charge, Sample.Normal);
        for (int i = 0; i < 3; i++) yield return ("metal_ingot", null, Sample.Normal);
        for (int i = 0; i < 3; i++) yield return ("scrap_metal", null, Sample.Normal);
        yield return ("nutrifruit_seed", null, Sample.Normal);
        yield return ("plant_dna_disk", null, Sample.Normal);
        yield return ("plant_dna_disk", null, Sample.WrittenDna);
        yield return ("printer_chip_internal_springs_improved", null, Sample.Normal);
        yield return ("large_bottled_water", null, Sample.EmptyBottle);
        yield return ("large_bottled_water", null, Sample.BaseWater);
        yield return (Fish.fishIdentifier, null, Sample.AdultFish);
        yield return (Fish.roeIdentifier, null, Sample.Normal);
        yield return (Fish.roeIdentifier, null, Sample.FertilizedRoe);
        yield return ("crypto_module_sec", null, Sample.Normal);
        yield return ("blank_keycard", null, Sample.Normal);
    }

    private static void Spawn()
    {
        if (_source == null || !PlayerStore.HasInstance || EmporiumEntry.Instance?.invElement == null) return;
        if (_overflowWindow?.handler != null) _overflowWindow.Show();
        foreach (var entry in Contents())
        {
            if (!DirectoryMaster.Has<GameItem>(entry.Id))
                throw new InvalidOperationException("Missing warning-test item factory: " + entry.Id);
            var item = entry.Charge.HasValue ? PreBuiltItemHelper.EnergyCredit(entry.Charge.Value) : entry.Sample switch
            {
                Sample.BaseWater => WaterPremadeHelper.AccurateBaseWater(entry.Id),
                Sample.AdultFish => AquacultureItems.SpawnFish(Fish, "FEMALE", Fish.stageStartDays[(int)FishStage.Adult]),
                Sample.FertilizedRoe => AquacultureItems.SpawnFertilizedRoe(Fish),
                _ => DirectoryMaster.Item(entry.Id)
            };
            item.SetAmount(1);
            if (entry.Sample == Sample.EmptyBottle) WaterHelper.EmptyContainer(item);
            if (entry.Sample == Sample.WrittenDna)
                PlantGeneticsHelper.WriteDisk(item, PlantTraitRegistry.Get(DnaTrait), rank: 1, uses: 1);
            if (entry.Sample == Sample.Stolen)
            {
                item.SetShape(new GridShapeBuilder(1, 1).SetDataFill(1));
                StolenHelper.SetHeat(item, 100);
            }
            ConfigureMachine(item);
            Place(item);
        }
        Log.Info("Spawned item warning test. Overflow is session-only; move wanted items to the store before leaving. Use Spawn security check (or queue) to test stolen warnings.");
    }

    internal static void ConfigureMachine(GameItem item)
    {
        if (item.IsTag(MachineryHelper.MACHINERY_TAG))
            item.ModifyTag(MachineryHelper.IS_MACHINERY_POWERED, t => t.Enable().SetBool(true));
        // The bare RAS factory has no tank; use the same tank that vanilla supplies with sold machines.
        if (item.identifier == "ras" && MachineRAS.GetWaterSlot(item) is { childItem: null } slot)
        {
            var tank = DirectoryMaster.Item("ras_water_tank");
            if (GraphUtils.TryAcceptAll(slot, tank) != 0)
                throw new InvalidOperationException("Could not install the warning-test RAS water tank.");
        }
    }

    private static void Place(GameItem item)
    {
        GeneralHelper.SetItemOwned(item, true);
        var store = EmporiumEntry.Instance.invElement;
        if (TryPlace(store, store.inventoryShape.globalWidth, store.inventoryShape.globalHeight, item)) return;
        if (_overflowWindow?.handler == null)
        {
            _overflow = new GameGridScrollableInventory(32, 16, 64);
            _overflowWindow = new PixelWindow(canMove: true, title: "Item warning test overflow (temporary / session only)");
            _overflowWindow.Attach(_overflow);
        }
        var overflow = _overflow!;
        // Reserve a complete new row band if the current inventory cannot fit this item.
        if (!TryPlace(overflow, overflow.width, overflow.height, item))
        {
            overflow.SetShape(Math.Max(overflow.width, item.modifiedShape.globalWidth), 16,
                overflow.height + item.modifiedShape.globalHeight + 1);
            if (!TryPlace(overflow, overflow.width, overflow.height, item))
                throw new InvalidOperationException("Could not place warning-test item: " + item.identifier);
        }
        _overflowWindow.Show(Input.mousePosition, fixTooltipBounds: true);
    }

    private static bool TryPlace(GameInventory inventory, int width, int height, GameItem item)
    {
        var shape = new GridShapeBuilder(item.modifiedShape);
        // Direct grid slots keep batteries distinct and never auto-fill the freshly spawned machines.
        for (int y = 0; y <= height - shape.globalHeight; y++)
        for (int x = 0; x <= width - shape.globalWidth; x++)
        {
            shape.SetPosition(x, y);
            var slot = inventory.TryInventorySlot(item, shape);
            if (slot != null && slot.IsValid() && slot.TryAcceptOnce(1) == 1) return true;
        }
        return false;
    }
}
