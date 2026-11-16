using System;
using System.Linq;

namespace Tuck.Mod;

internal enum StolenWarningMode { Disabled, OnInspection, Always }

internal sealed class StolenWarningSetting
{
    private static readonly (string Id, string LabelKey)[] Choices =
    {
        ("Disabled", "disabled"), ("On Inspection", "on_inspection"), ("Always", "always")
    };
    private readonly ConfigEntry<string> _setting;
    private readonly Func<string> _menuLabel;
    private readonly Options _options;
    public StolenWarningMode Value => _setting.Value switch
    {
        "Disabled" => StolenWarningMode.Disabled,
        "Always" => StolenWarningMode.Always,
        _ => StolenWarningMode.OnInspection
    };

    public StolenWarningSetting(ModManifest manifest, Options options)
    {
        _options = options;
        var config = ModHelper.GetConfig(manifest.ID);
        _setting = config.Define("stolen_warnings", Choices[1].Id, "Gameplay",
            () => options.Text("stolen_item_warnings"), Choices.Select(choice => choice.Id).ToArray(),
            () => options.Text("choose_when_stolen_item_warnings_appear"));
        _menuLabel = () => Options.FormatMenuLabel(manifest, _setting.Label());
    }

    public void Register() => ModHelper.AddGameOptionDropdown(
        _setting.Config.ModId, ModHelper.OPTION_SECTION_GAMEPLAY, _setting.Key, _menuLabel,
        () => Choices.Select(choice => _options.Text(choice.LabelKey)).ToArray(), () => (int)Value,
        index => _setting.Set(Choices[index >= 0 && index < Choices.Length ? index : 1].Id), _setting.Tooltip);

    public void Unregister() => ModHelper.RemoveGameOption(_setting.Config.ModId, _setting.Key);
}
