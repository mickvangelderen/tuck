using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class OverlappingKeyBindingsTests
{
    [Theory]
    [InlineData("keybind_rotate_cw", "keybind_rotate_ccw")]
    [InlineData("keybind_rotate_cw", "mod_pack_key")]
    [InlineData("mod_pack_key", "mod_scavenge_key")]
    public void ConflictsAreAllowedByDefaultAndRestoredWhenDisabled(string owner, string target)
    {
        string id = "tuck-overlapping-keys-test-" + Guid.NewGuid().ToString("N");
        var configs = (Dictionary<string, ModConfig>)AccessTools.Field(typeof(ModConfig), "configs").GetValue(null)!;
        var config = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: [id], culture: null)!;
        configs.Add(id, config);
        var options = new Options(new ModManifest { ID = id, Name = "Tuck" });
        options.AllowOverlappingKeyBindings.Register();
        var entry = (ConfigEntry<bool>)config.Get("allow_overlapping_keybindings");
        // Exercise the real conflict lookup without constructing Unity UI or saving config files.
        var ui = (GameOptionUIManager)RuntimeHelpers.GetUninitializedObject(typeof(GameOptionUIManager));
        var pending = new Dictionary<string, KeyCode> { [owner] = KeyCode.E, [target] = KeyCode.Q };
        AccessTools.Field(typeof(GameOptionUIManager), "pendingKeys").SetValue(ui, pending);
        var findOwner = AccessTools.Method(typeof(GameOptionUIManager), "FindPendingKeyOwner");
        typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!.SetValue(entry, null);
        try
        {
            var row = GameOptionRegistry.Find(ModHelper.GameOptionId(id, entry.Key));
            Assert.Equal(GameOptionRegistry.Kind.Toggle, row.kind);
            Assert.True(entry.Default);
            Assert.True(row.getBool());
            Assert.Equal(owner, FindOwner(KeyCode.E));

            OverlappingKeyBindings.Register(options);
            Assert.Null(FindOwner(KeyCode.E));
            Assert.Null(FindOwner(KeyCode.Q)); // Rebinding to the current key remains valid.
            Assert.Null(FindOwner(KeyCode.Z)); // Unused keys remain valid.

            row.setBool(false);
            Assert.False(options.AllowOverlappingKeyBindings.Value);
            Assert.Equal(owner, FindOwner(KeyCode.E));
            Assert.Null(FindOwner(KeyCode.Q));
            Assert.Null(FindOwner(KeyCode.Z));

            row.setBool(true);
            Assert.Null(FindOwner(KeyCode.E));
            OverlappingKeyBindings.Unregister();
            Assert.Equal(owner, FindOwner(KeyCode.E));
            Assert.Equal(KeyCode.E, pending[owner]);
            Assert.Equal(KeyCode.Q, pending[target]);
        }
        finally
        {
            OverlappingKeyBindings.Unregister();
            typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!.SetValue(entry, config);
            options.AllowOverlappingKeyBindings.Unregister();
            Assert.Null(GameOptionRegistry.Find(ModHelper.GameOptionId(id, entry.Key)));
            configs.Remove(id);
        }

        string? FindOwner(KeyCode key)
        {
            string? result = (string?)findOwner.Invoke(ui, [key, target]);
            OverlappingKeyBindings.Postfix(ref result);
            return result;
        }
    }
}
