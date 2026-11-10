using System;
using UnityEngine;

namespace Tuck.Mod;

/// <summary>A rebindable key: a mod setting plus its row in Options › Keybind.</summary>
internal sealed class KeyBinding
{
    // Stored as the key name rather than an enum so the Mods settings window shows a text field, not ~300 keys.
    private readonly ConfigEntry<string> _setting;
    // The Options menu mixes our rows with the game's, so it gets the mod name; the Mods settings window doesn't need it.
    private readonly Func<string> _menuLabel;

    public KeyCode Key { get; private set; }
    public event Action? Changed;

    public KeyBinding(ModManifest manifest, string id, Func<string> label, KeyCode defaultKey = KeyCode.None)
    {
        _setting = ModHelper.GetConfig(manifest.ID).Define(id, defaultKey.ToString(), "Keybind", label);
        _menuLabel = () => Options.FormatMenuLabel(manifest, label());
        Key = Parse();
        _setting.OnChanged += _ =>
        {
            Key = Parse();
            Changed?.Invoke();
        };
    }

    public string Format() => Key == KeyCode.None ? "" : $"({KeyBindHelper.KeyName(Key)})";

    public void Register()
    {
        ModHelper.AddGameOptionKeyBind(
            _setting.Config.ModId, ModHelper.OPTION_SECTION_KEYBIND, _setting.Key,
            _menuLabel,
            () => Key,
            key => _setting.Set(key.ToString()));
    }

    public void Unregister() => ModHelper.RemoveGameOption(_setting.Config.ModId, _setting.Key);

    private KeyCode Parse()
    {
        // TryParse also accepts any number ("9999") and ORs comma-separated names together ("A, Z"), which can
        // produce values that aren't keys; IsDefined rejects those. The game reserves Escape and left click: its
        // Options key capture refuses them, but the Mods settings text field doesn't.
        if (Enum.TryParse(_setting.Value, ignoreCase: true, out KeyCode key) && Enum.IsDefined(typeof(KeyCode), key)
            && !KeyBindHelper.IsReserved(key))
        {
            return key;
        }
        else
        {
            Log.Warning($"Key binding '{_setting.Key}' is set to '{_setting.Value}', which is not a key that can be bound. Rebind it in the Options.");
            return KeyCode.None;
        }
    }
}
