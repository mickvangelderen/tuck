using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class HotkeyTooltipTests : IDisposable
{
    private readonly string _id = "tuck-tooltip-test-" + Guid.NewGuid().ToString("N");
    private readonly ModConfig _config;
    private readonly Options _options;
    private readonly Dictionary<string, string> _english = LocalizationTests.ReadEnglish();
    private readonly Dictionary<string, KeyCode> _bound;
    private readonly Dictionary<string, KeyCode> _savedBindings;
    private readonly bool _loaded;
    private readonly Dictionary<KeyBindHelper.Binding, Func<string>> _labels;
    private static Dictionary<string, ModConfig> Configs =>
        (Dictionary<string, ModConfig>)AccessTools.Field(typeof(ModConfig), "configs").GetValue(null)!;

    public HotkeyTooltipTests()
    {
        _config = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: [_id], culture: null)!;
        Configs.Add(_id, _config);
        _options = new Options(new ModManifest { ID = _id, Name = "Tuck" },
            (key, args) => string.Format(_english[key], args));
        _options.ShowHotkeysInTooltips.Register();
        // Exercise real vanilla key lookup without Unity's save paths or localization engine.
        _loaded = (bool)AccessTools.Field(typeof(KeyBindHelper), "loaded").GetValue(null)!;
        AccessTools.Field(typeof(KeyBindHelper), "loaded").SetValue(null, true);
        _bound = (Dictionary<string, KeyCode>)AccessTools.Field(typeof(KeyBindHelper), "bound").GetValue(null)!;
        _savedBindings = new(_bound);
        _bound.Clear();
        _labels = KeyBindHelper.Bindings.ToDictionary(binding => binding, binding => binding.label);
        foreach (var binding in KeyBindHelper.Bindings) binding.label = () => binding.id;
    }

    [Fact]
    public void ToggleDefaultsOnAndSuppressesPackHintsWithoutChangingText()
    {
        var row = GameOptionRegistry.Find(ModHelper.GameOptionId(_id, "show_hotkeys_in_tooltips"));
        Assert.Equal(GameOptionRegistry.Kind.Toggle, row.kind);
        Assert.True(row.getBool());
        Assert.Equal("Tuck: hello (G)", HotkeyTooltips.AppendHint(_options, "Tuck: hello", _options.Pack.Key));
        Set("show_hotkeys_in_tooltips", false);
        Assert.False(row.getBool());
        Assert.Equal("<b>Tuck: hello</b>", HotkeyTooltips.AppendHint(_options, "<b>Tuck: hello</b>", KeyCode.G));
        Set("show_hotkeys_in_tooltips", true);
        Assert.Equal("hello (G)", HotkeyTooltips.AppendHint(_options, "hello", KeyCode.G));
    }

    [Fact]
    public void GlyphHintsReplaceParenthesizedKeysAndPreserveHoldInstructionsAndTranslations()
    {
        string glyph = HotkeyGlyphs.Tag(KeyCode.H);
        Assert.Equal("<sprite=\"Tuck.Hotkeys\" name=\"104\">", glyph);
        Assert.Equal($"Shutters {glyph}", HotkeyTooltips.FormatHint(_options, "Shutters", KeyCode.H, false, glyph));
        Assert.Equal($"Return (hold and release {glyph})",
            HotkeyTooltips.FormatHint(_options, "Return", KeyCode.H, true, glyph));
        _english["text_with_key_hint"] = "{1}: {0}";
        _english["hold_and_release_key"] = "hold {0}, then release";
        Assert.Equal($"hold {glyph}, then release: Return",
            HotkeyTooltips.FormatHint(_options, "Return", KeyCode.H, true, glyph));
        Assert.Equal("Return", HotkeyTooltips.FormatHint(_options, "Return", KeyCode.None, true, glyph));
        Set("show_hotkeys_in_tooltips", false);
        Assert.Equal("Shutters", HotkeyTooltips.FormatHint(_options, "Shutters", KeyCode.H, false, glyph));
    }

    [Fact]
    public void ModHintsFollowRebindingsAndUnbindingWithoutHoldInstructions()
    {
        Assert.Equal("Return (R)",
            HotkeyTooltips.ButtonHint(_options, HotkeyTooltips.ButtonAction.Return));
        Set("return_key", "Mouse4");
        Set("return_hold_duration", 0f);
        Assert.Equal("Return (Mouse 5)",
            HotkeyTooltips.ButtonHint(_options, HotkeyTooltips.ButtonAction.Return));
        Set("return_key", "None");
        Assert.Equal("", HotkeyTooltips.ButtonHint(_options, HotkeyTooltips.ButtonAction.Return));
        Set("pack_key", "F8");
        Assert.Equal("hello (F8)", HotkeyTooltips.AppendHint(_options, "hello", _options.Pack.Key));
        Assert.Equal("hello", HotkeyTooltips.AppendHint(_options, "hello", KeyCode.None));
        Set("scavenge_key", "None");
        Assert.Equal("", HotkeyTooltips.ButtonHint(_options, HotkeyTooltips.ButtonAction.Scavenge));
    }

    [Fact]
    public void VanillaHintsAndTranslationsAreResolvedAtDisplayTime()
    {
        KeyBindHelper.Set(KeyBindHelper.CONTROL_HINTS, KeyCode.K);
        Assert.Equal($"{KeyBindHelper.CONTROL_HINTS} (K)", HotkeyTooltips.ButtonHint(_options, HotkeyTooltips.ButtonAction.ControlHints));
        KeyBindHelper.Set(KeyBindHelper.CONTROL_HINTS, KeyCode.F10);
        KeyBindHelper.Find(KeyBindHelper.CONTROL_HINTS).label = () => "translated control hints";
        _english["text_with_key_hint"] = "{1}: {0}";
        Assert.Equal("(F10): translated control hints", HotkeyTooltips.ButtonHint(_options, HotkeyTooltips.ButtonAction.ControlHints));
        KeyBindHelper.Set(KeyBindHelper.CONTROL_HINTS, KeyCode.None);
        Assert.Equal("", HotkeyTooltips.ButtonHint(_options, HotkeyTooltips.ButtonAction.ControlHints));
    }

    [Theory]
    [InlineData(typeof(MapUIManager), "ScavengeButtonClick", "Scavenge")]
    [InlineData(typeof(DumpingGroundLocation), "OnScavenge", "Scavenge")]
    [InlineData(typeof(MapUIManager), "Return", "Return")]
    [InlineData(typeof(MapUIManager), "LeaveScavenging", "Return")]
    [InlineData(typeof(MapUIManager), "LeaveCommissary", "Return")]
    [InlineData(typeof(MapUIManager), "LeaveUpgradeMerchant", "Return")]
    [InlineData(typeof(GameOptionUIManager), "OpenUI", "None")]
    [InlineData(typeof(StoreUIManager), "ToggleControlHintOverlay", "ControlHints")]
    public void ButtonActionsMatchExistingGameCallbacks(Type type, string method, string expected)
    {
        Assert.NotNull(AccessTools.DeclaredMethod(type, method));
        Assert.Equal(expected, HotkeyTooltips.IdentifyAction(type, method).ToString());
        Assert.Equal(HotkeyTooltips.ButtonAction.None, HotkeyTooltips.IdentifyAction(typeof(HotkeyTooltipTests), method));
    }

    [Fact]
    public void PatchesTargetMethodsDeclaredOnTheSpecifiedGameTypes()
    {
        foreach (var method in typeof(HotkeyTooltips).GetMethods(BindingFlags.NonPublic | BindingFlags.Static))
        {
            var patch = method.GetCustomAttribute<HarmonyPatch>();
            if (patch == null) continue;
            Assert.NotNull(AccessTools.DeclaredMethod(patch.info.declaringType, patch.info.methodName, patch.info.argumentTypes));
        }
        // Button inherits this hook; patching Button itself would not find a declared method.
        Assert.NotNull(AccessTools.DeclaredMethod(typeof(Selectable), "OnEnable"));
    }

    private void Set<T>(string key, T value)
    {
        var entry = (ConfigEntry<T>)_config.Get(key);
        typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!.SetValue(entry, null);
        try { entry.Set(value); }
        finally { typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!.SetValue(entry, _config); }
    }

    public void Dispose()
    {
        _options.ShowHotkeysInTooltips.Unregister();
        Configs.Remove(_id);
        _bound.Clear();
        foreach (var pair in _savedBindings) _bound.Add(pair.Key, pair.Value);
        foreach (var pair in _labels) pair.Key.label = pair.Value;
        AccessTools.Field(typeof(KeyBindHelper), "loaded").SetValue(null, _loaded);
    }
}
