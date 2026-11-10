using System.Reflection;
using UnityEngine;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class DragAmountSettingsTests
{
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
            Check(options.DragAllMatching, "drag_all_matching_key", KeyCode.LeftShift);
            Check(options.DragEntireInventory, "drag_entire_inventory_key", KeyCode.LeftControl);
            Check(options.DragPrecisePlacement, "drag_precise_placement_key", KeyCode.LeftAlt);
        }
        finally
        {
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
