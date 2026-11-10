namespace Tuck.Mod;

/// <summary>A boolean mod setting plus its row in Options > Gameplay.</summary>
internal sealed class ToggleSetting
{
    private readonly ConfigEntry<bool> _setting;
    // The Options menu mixes our rows with the game's, so it gets the mod name; the Mods settings window doesn't need it.
    private readonly string _menuLabel;

    public bool Value => _setting.Value;

    public event System.Action<bool> OnChanged
    {
        add => _setting.OnChanged += value;
        remove => _setting.OnChanged -= value;
    }

    public ToggleSetting(ModManifest manifest, string id, string label, string tooltip, bool defaultValue = false)
    {
        _setting = ModHelper.GetConfig(manifest.ID).Define(id, defaultValue, "Gameplay", () => label, () => tooltip);
        _menuLabel = Options.FormatMenuLabel(manifest, label);
    }

    public void Register()
    {
        ModHelper.AddGameOptionToggle(
            _setting.Config.ModId, ModHelper.OPTION_SECTION_GAMEPLAY, _setting.Key,
            () => _menuLabel,
            () => Value,
            _setting.Set,
            _setting.Tooltip);
    }

    public void Unregister() => ModHelper.RemoveGameOption(_setting.Config.ModId, _setting.Key);
}
