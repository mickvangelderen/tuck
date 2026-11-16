using System.Reflection;
using HarmonyLib;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class StolenWarningSettingsTests : IDisposable
{
    private readonly string _id = "tuck-stolen-test-" + Guid.NewGuid().ToString("N");
    private ModConfig _config = null!;
    private Options? _options;
    private ConfigEntry<string> _entry = null!;
    private static Dictionary<string, ModConfig> Configs =>
        (Dictionary<string, ModConfig>)AccessTools.Field(typeof(ModConfig), "configs").GetValue(null)!;

    [Fact]
    public void DropdownHasThreeChoicesAndChangesWarningsOnTheNextEvaluation()
    {
        Create(null);
        var row = GameOptionRegistry.Find(ModHelper.GameOptionId(_id, "stolen_warnings"));
        Assert.Equal(GameOptionRegistry.Kind.Dropdown, row.kind);
        Assert.Equal(new[] { "Disabled", "On Inspection", "Always" }, row.options());
        Assert.Equal(row.options(), _entry.Choices);
        Assert.Equal("On Inspection", _entry.Default);
        Assert.Equal(1, row.getIndex());
        Assert.False(_entry.RequiresRestart);
        Assert.Single(_config.Entries, entry => entry.Key == "stolen_warnings");
        var item = new Item();
        item.modifiedState.EnableTag(StolenHelper.STOLEN_TAG);
        foreach (int index in new[] { 2, 0, 1, 2, 1 })
        {
            row.setIndex(index);
            Assert.Equal(index, row.getIndex());
            Assert.Equal((StolenWarningMode)index, _options!.StolenWarnings.Value);
            Assert.Equal(index == 2 ? ItemWarnings.Warning.Stolen : ItemWarnings.Warning.None,
                ItemWarnings.Applicable(item, _options.StolenWarnings.Value, false, false, false, outputOccupiedPercent: 50));
            Assert.Equal(index == 0 ? ItemWarnings.Warning.None : ItemWarnings.Warning.Stolen,
                ItemWarnings.Applicable(item, _options.StolenWarnings.Value, false, false, true, outputOccupiedPercent: 50));
        }
        row.setIndex(-1);
        Assert.Equal(1, row.getIndex());
        _entry.Set("Always");
        _entry.Reset();
        Assert.Equal(StolenWarningMode.OnInspection, _options!.StolenWarnings.Value);
    }

    [Theory]
    [InlineData(null, StolenWarningMode.OnInspection)]
    [InlineData("Disabled", StolenWarningMode.Disabled)]
    [InlineData("On Inspection", StolenWarningMode.OnInspection)]
    [InlineData("Always", StolenWarningMode.Always)]
    [InlineData("unknown", StolenWarningMode.OnInspection)]
    public void SavedSelectionsSurviveReload(string? saved, object expected)
    {
        Create(saved);
        Assert.Equal((StolenWarningMode)expected, _options!.StolenWarnings.Value);
        Assert.Equal("On Inspection", _entry.Default);
        foreach (int index in new[] { 2, 0, 1 })
        {
            var row = GameOptionRegistry.Find(ModHelper.GameOptionId(_id, "stolen_warnings"));
            row.setIndex(index);
            string serialized = _entry.Serialize();
            Assert.Equal(new[] { "Disabled", "On Inspection", "Always" }[index], serialized);
            // Feed native serialization back through a fresh ModConfig, as its file loader does.
            Create(serialized);
            Assert.Equal((StolenWarningMode)index, _options.StolenWarnings.Value);
            Assert.Equal(serialized, _entry.Serialize());
        }
    }

    private void Create(string? saved)
    {
        Unregister();
        _config = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: [_id], culture: null)!;
        if (saved != null)
            ((Dictionary<string, string>)AccessTools.Field(typeof(ModConfig), "rawValues").GetValue(_config)!).Add("stolen_warnings", saved);
        Configs[_id] = _config;
        var english = LocalizationTests.ReadEnglish();
        _options = new Options(new ModManifest { ID = _id, Name = "Tuck" }, (key, args) => string.Format(english[key], args));
        _options.StolenWarnings.Register();
        _entry = (ConfigEntry<string>)_config.Get("stolen_warnings");
        // Exercise native callbacks/serialization without Unity's persistentDataPath and file-writing services.
        AccessTools.Property(typeof(ConfigEntry), nameof(ConfigEntry.Config)).SetValue(_entry, null);
    }

    private void Unregister()
    {
        if (_options == null) return;
        AccessTools.Property(typeof(ConfigEntry), nameof(ConfigEntry.Config)).SetValue(_entry, _config);
        _options.StolenWarnings.Unregister();
        Assert.Null(GameOptionRegistry.Find(ModHelper.GameOptionId(_id, "stolen_warnings")));
    }

    public void Dispose()
    {
        Unregister();
        Configs.Remove(_id);
    }

    private sealed class Item() : GameItem("stolen-test")
    {
        public override void Validate() { }
        public override void Destroy() => throw new InvalidOperationException("Warnings must not destroy items.");
    }
}
