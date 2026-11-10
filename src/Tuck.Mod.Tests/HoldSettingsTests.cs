using System.Reflection;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class HoldSettingsTests : IDisposable
{
    private readonly string _modId = "tuck-hold-test-" + Guid.NewGuid().ToString("N");
    private readonly ModConfig _config;
    private readonly Options _options;
    private static Dictionary<string, ModConfig> Configs =>
        (Dictionary<string, ModConfig>)typeof(ModConfig).GetField("configs", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;

    public HoldSettingsTests()
    {
        // Keep the real config/option APIs, but bypass Unity's persistent-data path and file loading.
        _config = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: [_modId], culture: null)!;
        Configs.Add(_modId, _config);
        var english = LocalizationTests.ReadEnglish();
        _options = new Options(new ModManifest { ID = _modId, Name = "Tuck" },
            (key, args) => string.Format(english[key], args));
        _options.ReturnHoldDuration.Register();
        _options.BellHoldDuration.Register();
    }

    [Theory]
    [InlineData("return_hold_duration")]
    [InlineData("bell_hold_duration")]
    public void DurationSlidersExposeElevenStepsAndStoreSeconds(string key)
    {
        var slider = GameOptionRegistry.Find(ModHelper.GameOptionId(_modId, key));
        var entry = (ConfigEntry<float>)_config.Get(key);
        Assert.Equal(GameOptionRegistry.Kind.Slider, slider.kind);
        Assert.True(slider.wholeNumbers);
        Assert.Equal(0f, slider.min);
        Assert.Equal(10f, slider.max);
        Assert.Equal(4f, slider.getFloat());
        Assert.Equal(0.4f, entry.Default);
        for (int tick = 0; tick <= 10; tick++)
        {
            WithoutSaving(entry, () => slider.setFloat(tick));
            Assert.Equal(tick / 10f, entry.Value, 5);
            Assert.Equal((float)tick, slider.getFloat());
            Assert.Equal($"{tick / 10f:0.0} s", slider.valueText(tick));
        }
    }

    [Fact]
    public void ValuesFromModsSettingsAlsoSnapToTenths()
    {
        var entry = (ConfigEntry<float>)_config.Get("return_hold_duration");
        WithoutSaving(entry, () => entry.Set(0.36f));
        Assert.Equal(0.4f, _options.ReturnHoldDuration.Value, 5);
        WithoutSaving(entry, () => entry.Set(float.NaN));
        Assert.Equal(0.4f, _options.ReturnHoldDuration.Value, 5);
    }

    [Theory]
    [InlineData(0f, false)]
    [InlineData(0.1f, true)]
    [InlineData(0.4f, true)]
    [InlineData(1f, true)]
    public void BellDurationPreservesVanillaInstantModeAndDisablesHoldsAtZero(float seconds, bool requiresHold)
    {
        var entry = (ConfigEntry<float>)_config.Get("bell_hold_duration");
        WithoutSaving(entry, () => entry.Set(seconds));
        BellHold.Register(_options);

        float duration = 0.4f;
        BellHold.DurationPostfix(ref duration);
        Assert.Equal(seconds, duration, 5);

        bool vanillaRequiresHold = true;
        BellHold.RequireHoldPostfix(ref vanillaRequiresHold);
        Assert.Equal(requiresHold, vanillaRequiresHold);

        bool vanillaInstant = false;
        BellHold.RequireHoldPostfix(ref vanillaInstant);
        Assert.False(vanillaInstant);

        BellHold.Unregister();
        duration = 0.4f;
        vanillaRequiresHold = true;
        BellHold.DurationPostfix(ref duration);
        BellHold.RequireHoldPostfix(ref vanillaRequiresHold);
        Assert.Equal(0.4f, duration);
        Assert.True(vanillaRequiresHold);
    }

    private void WithoutSaving(ConfigEntry entry, Action change)
    {
        var configProperty = typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!;
        configProperty.SetValue(entry, null);
        try { change(); }
        finally { configProperty.SetValue(entry, _config); }
    }

    public void Dispose()
    {
        BellHold.Unregister();
        _options.ReturnHoldDuration.Unregister();
        _options.BellHoldDuration.Unregister();
        Configs.Remove(_modId);
    }
}
