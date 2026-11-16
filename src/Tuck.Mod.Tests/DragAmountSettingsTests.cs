using System.Reflection;
using UnityEngine;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class DragAmountSettingsTests
{
    [Theory]
    [InlineData(KeyCode.None, KeyCode.None)]
    [InlineData(KeyCode.LeftAlt, KeyCode.None)]
    [InlineData(KeyCode.None, KeyCode.LeftControl)]
    [InlineData(KeyCode.LeftAlt, KeyCode.LeftControl)]
    public void UnboundFormationDisablesPlacementRegardlessOfExpansionBindings(
        KeyCode matching, KeyCode inventory)
    {
        string id = "tuck-drag-test-" + Guid.NewGuid().ToString("N");
        var configs = (Dictionary<string, ModConfig>)typeof(ModConfig)
            .GetField("configs", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var config = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: [id], culture: null)!;
        configs.Add(id, config);
        try
        {
            var options = new Options(new ModManifest { ID = id, Name = "Tuck" });
            SetKey("drag_precise_placement_key", KeyCode.None);
            SetKey("drag_all_matching_key", matching);
            SetKey("drag_entire_inventory_key", inventory);
            DragAmounts.Register(options);
            foreach (bool vanillaResult in new[] { false, true })
            {
                object[] args = [vanillaResult];
                typeof(DragAmounts).GetMethod("PreciseModifier", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, args);
                Assert.False((bool)args[0]);
            }
        }
        finally
        {
            DragAmounts.Unregister();
            configs.Remove(id);
        }

        void SetKey(string key, KeyCode value)
        {
            var entry = (ConfigEntry<string>)config.Get(key);
            // Exercise binding changes without persisting test settings or invoking Unity.
            typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!.SetValue(entry, null);
            try { entry.Set(value.ToString()); }
            finally { typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!.SetValue(entry, config); }
        }
    }

    [Fact]
    public void DragBindingsHaveRequestedDefaultsAndCanBeReboundOrDisabled()
    {
        string id = "tuck-drag-test-" + Guid.NewGuid().ToString("N");
        var configs = (Dictionary<string, ModConfig>)typeof(ModConfig)
            .GetField("configs", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var config = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: [id], culture: null)!;
        configs.Add(id, config);
        var options = new Options(new ModManifest { ID = id, Name = "Tuck" });
        options.DragAllMatching.Register();
        options.DragEntireInventory.Register();
        options.DragPrecisePlacement.Register();
        try
        {
            // Disabling Tuck itself restores vanilla's handling of both Shift keys.
            DragAmounts.Unregister();
            foreach (bool vanillaResult in new[] { false, true })
            {
                object[] args = [vanillaResult];
                typeof(DragAmounts).GetMethod("PreciseModifier", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, args);
                Assert.Equal(vanillaResult, (bool)args[0]);
            }
            Check(options.DragAllMatching, "drag_all_matching_key", KeyCode.LeftAlt);
            Check(options.DragEntireInventory, "drag_entire_inventory_key", KeyCode.LeftControl);
            Check(options.DragPrecisePlacement, "drag_precise_placement_key", KeyCode.LeftShift);
        }
        finally
        {
            DragAmounts.Unregister();
            options.DragPrecisePlacement.Unregister();
            options.DragEntireInventory.Unregister();
            options.DragAllMatching.Unregister();
            configs.Remove(id);
        }

        void Check(KeyBinding binding, string key, KeyCode defaultKey)
        {
            var entry = (ConfigEntry<string>)config.Get(key);
            var row = GameOptionRegistry.Find(ModHelper.GameOptionId(id, key));
            Assert.Equal(defaultKey.ToString(), entry.Default);
            Assert.Equal(defaultKey, binding.Key);
            Assert.Equal(defaultKey, row.getKey());
            // Avoid persistence/Unity paths while exercising the real option callback.
            typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!.SetValue(entry, null);
            try
            {
                row.setKey(KeyCode.RightControl);
                Assert.Equal(KeyCode.RightControl, binding.Key);
                row.setKey(KeyCode.None);
                Assert.Equal(KeyCode.None, binding.Key);
            }
            finally { typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!.SetValue(entry, config); }
        }
    }
}
