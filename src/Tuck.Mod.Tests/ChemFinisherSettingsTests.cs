using System.Reflection;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class ChemFinisherSettingsTests : IDisposable
{
    private readonly string _modId = "tuck-chem-test-" + Guid.NewGuid().ToString("N");
    private readonly ModConfig _config;
    private readonly Options _options;
    private readonly ConfigEntry<bool> _entry;
    private static Dictionary<string, ModConfig> Configs =>
        (Dictionary<string, ModConfig>)typeof(ModConfig).GetField("configs", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;

    public ChemFinisherSettingsTests()
    {
        _config = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: [_modId], culture: null)!;
        Configs.Add(_modId, _config);
        _options = new Options(new ModManifest { ID = _modId, Name = "Tuck" });
        _entry = (ConfigEntry<bool>)_config.Get("chem_finisher_batches");
        _options.ChemFinisherBatches.Register();
    }

    [Fact]
    public void DisabledAtStartupDoesNotTouchTheMachineOrInstallControls()
    {
        Set(false);
        Assert.False(_options.ChemFinisherBatches.Value);
        ChemFinisherBatch.Register(_options);
        Assert.False(ChemFinisherBatch.IsEnabled);
        // A null sentinel would fail if the postfix tried to inspect any machine UI while disabled.
        ChemFinisherBatch.Postfix(null!);
    }

    [Fact]
    public void GameplayAndModsSettingsUseTheSameLiveToggle()
    {
        Assert.True(_entry.Default);
        Assert.False(_entry.RequiresRestart);
        ChemFinisherBatch.Register(_options);
        Assert.True(ChemFinisherBatch.IsEnabled);
        var row = GameOptionRegistry.Find(ModHelper.GameOptionId(_modId, _entry.Key));
        Assert.Equal(GameOptionRegistry.Kind.Toggle, row.kind);
        Assert.True(row.getBool());
        Assert.True(_options.ChemFinisherBatches.Value);

        WithoutSaving(() => row.setBool(false));
        Assert.False(_options.ChemFinisherBatches.Value);
        Assert.False(ChemFinisherBatch.IsEnabled);
        ChemFinisherBatch.Postfix(null!);

        WithoutSaving(() => row.setBool(true));
        Assert.True(_options.ChemFinisherBatches.Value);
        Assert.True(ChemFinisherBatch.IsEnabled);
        WithoutSaving(() => row.setBool(false));

        Set(true); // The Mods settings UI writes the config entry directly.
        Assert.True(row.getBool());
        Assert.True(ChemFinisherBatch.IsEnabled);
        Set(false);
        Assert.False(ChemFinisherBatch.IsEnabled);
        ChemFinisherBatch.Postfix(null!);
    }

    [Fact]
    public void UnregisterRemovesTheSettingListenerAndCannotReenableTheFeature()
    {
        ChemFinisherBatch.Register(_options);
        var changedField = typeof(ConfigEntry<bool>).GetField("OnChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Single(((Delegate)changedField.GetValue(_entry)!).GetInvocationList());
        ChemFinisherBatch.Unregister();
        Assert.Null(changedField.GetValue(_entry));
        Set(false);
        Set(true);
        Assert.False(ChemFinisherBatch.IsEnabled);
        ChemFinisherBatch.Postfix(null!);
    }

    private void Set(bool value) => WithoutSaving(() => _entry.Set(value));

    private void WithoutSaving(Action change)
    {
        var property = typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!;
        property.SetValue(_entry, null);
        try { change(); }
        finally { property.SetValue(_entry, _config); }
    }

    public void Dispose()
    {
        ChemFinisherBatch.Unregister();
        _options.ChemFinisherBatches.Unregister();
        Configs.Remove(_modId);
    }
}
