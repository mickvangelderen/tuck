using System;

namespace Tuck.Mod;

/// <summary>A bounded numeric mod setting plus its slider in Options > Gameplay.</summary>
internal sealed class ScalarSetting
{
    private readonly ConfigEntry<float> _setting;
    private readonly Func<string> _menuLabel;
    private readonly float _step;
    private readonly Func<float, string>? _valueText;

    public float Value => Snap(float.IsNaN(_setting.Value) ? _setting.Default : _setting.Value);

    public ScalarSetting(ModManifest manifest, string id, Func<string> label, Func<string> tooltip,
        float defaultValue, float min, float max, float step = 0f, Func<float, string>? valueText = null)
    {
        if (step < 0f || float.IsNaN(step) || float.IsInfinity(step))
            throw new ArgumentOutOfRangeException(nameof(step));
        _step = step;
        _setting = ModHelper.GetConfig(manifest.ID).Define(id, defaultValue, "Gameplay", label,
            min, max, tooltip);
        _menuLabel = () => Options.FormatMenuLabel(manifest, label());
        _valueText = valueText;
    }

    public void Register()
    {
        ModHelper.AddGameOptionSlider(
            _setting.Config.ModId, ModHelper.OPTION_SECTION_GAMEPLAY, _setting.Key,
            _menuLabel,
            () => ToSlider(Value),
            value => _setting.Set(FromSlider(value)),
            ToSlider(_setting.Min), ToSlider(_setting.Max), _step > 0f, _setting.Tooltip,
            value => _valueText?.Invoke(FromSlider(value)) ?? ConfigEntry.Format(FromSlider(value)));
    }

    public void Unregister() => ModHelper.RemoveGameOption(_setting.Config.ModId, _setting.Key);

    // Unity sliders support whole-number ticks, not arbitrary step sizes. Config values remain in real units.
    private float ToSlider(float value) => _step > 0f ? (float)Math.Round((value - _setting.Min) / _step) : value;

    private float FromSlider(float value) => Snap(_step > 0f ? _setting.Min + value * _step : value);

    // Also snap values entered through the separate Mods settings window or edited in the config file.
    private float Snap(float value) => _step > 0f
        ? Math.Max(_setting.Min, Math.Min(_setting.Max,
            _setting.Min + (float)Math.Round((value - _setting.Min) / _step, MidpointRounding.AwayFromZero) * _step))
        : value;
}
