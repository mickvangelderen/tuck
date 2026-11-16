using System.Reflection;
using System.Runtime.CompilerServices;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class HotkeyTests : IDisposable
{
    private readonly string _modId = "tuck-hotkey-test-" + Guid.NewGuid().ToString("N");
    private readonly ModConfig _config;
    private readonly Options _options;
    private static Dictionary<string, ModConfig> Configs =>
        (Dictionary<string, ModConfig>)typeof(ModConfig).GetField("configs", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;

    public HotkeyTests()
    {
        // Keep live setting objects, while bypassing Unity's config paths and file writes.
        _config = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: [_modId], culture: null)!;
        Configs.Add(_modId, _config);
        _options = new Options(new ModManifest { ID = _modId, Name = "Tuck" });
        Set("return_hold_duration", 1f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrZeroHoldDurationFiresOnlyOnPress(bool hasDurationSetting)
    {
        Set("return_hold_duration", 0f);
        var hotkey = new Hotkey(_options.Return, hasDurationSetting ? _options.ReturnHoldDuration : null);

        Assert.True(Press(hotkey, 10f));
        Assert.False(Hold(hotkey, 11f));
        Assert.False(Release(hotkey, 12f));
        Assert.True(Press(hotkey, 13f));
    }

    [Theory]
    [InlineData(10.5f, false)]
    [InlineData(11f, true)]
    [InlineData(12.49f, true)]
    [InlineData(12.5f, false)]
    public void TimedHotkeyFiresOnlyOnReleaseWithinTheReadyWindow(float releaseTime, bool fires)
    {
        var hotkey = CreateHeldHotkey();
        Assert.False(Press(hotkey, 10f));
        Assert.Equal(fires, Release(hotkey, releaseTime));
        Assert.False(Release(hotkey, releaseTime + 1f));
    }

    [Theory]
    [InlineData(10.49f, false)]
    [InlineData(10.5f, true)]
    [InlineData(10.99f, true)]
    [InlineData(11f, false)]
    public void PendingHoldUsesCurrentSettings(float releaseTime, bool fires)
    {
        var hotkey = CreateHeldHotkey();
        Assert.False(Press(hotkey, 10f));
        Set("return_hold_duration", 0.5f);
        Set("hold_timeout_duration", 0.5f);
        Assert.Equal(fires, Release(hotkey, releaseTime));
    }

    [Fact]
    public void SettingDurationToZeroDuringAHoldStillWaitsForRelease()
    {
        var hotkey = CreateHeldHotkey();
        Assert.False(Press(hotkey, 10f));
        Set("return_hold_duration", 0f);
        Assert.False(Hold(hotkey, 10.25f));
        Assert.True(Release(hotkey, 10.25f));
        Assert.True(Press(hotkey, 20f));
    }

    [Fact]
    public void TimeoutCancelsWhileHeldAndRequiresAFreshPress()
    {
        var hotkey = CreateHeldHotkey();
        Assert.False(Press(hotkey, 10f));
        Assert.False(Hold(hotkey, 12.5f));
        Assert.False(Hold(hotkey, 13f));
        Assert.False(Release(hotkey, 14f));
        Assert.False(Press(hotkey, 20f));
        Assert.True(Release(hotkey, 21f));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrDisabledTimeoutKeepsTheHoldPendingUntilRelease(bool hasTimeoutSetting)
    {
        var hotkey = hasTimeoutSetting ? CreateHeldHotkey() : new Hotkey(_options.Return, _options.ReturnHoldDuration);
        Assert.False(Press(hotkey, 10f));
        Set("hold_timeout_duration", 3f);
        Assert.False(Hold(hotkey, 100f));
        Assert.True(Release(hotkey, 101f));
    }

    [Fact]
    public void UnavailabilityCancelsAndCannotResumeTheSamePress()
    {
        var hotkey = CreateHeldHotkey();
        Assert.False(Press(hotkey, 10f));
        Assert.False(hotkey.Poll(canExecute: false, now: 11f, pressed: false, held: true, released: false));
        Assert.False(Hold(hotkey, 11.1f));
        Assert.False(Release(hotkey, 11.2f));
        Assert.False(hotkey.Poll(canExecute: false, now: 20f, pressed: true, held: true, released: false));
        Assert.False(Release(hotkey, 21f));
        Assert.False(Press(hotkey, 30f));
        Assert.True(Release(hotkey, 31f));
    }

    [Fact]
    public void UnbindingCancelsThePendingHold()
    {
        var hotkey = CreateHeldHotkey();
        Assert.False(Press(hotkey, 10f));
        Set("return_key", "None");
        Assert.False(Release(hotkey, 11f));
        Set("return_key", "T");
        Assert.False(Press(hotkey, 20f));
        Assert.True(Release(hotkey, 21f));
    }

    [Fact]
    public void UnboundAndUnavailableInstantHotkeysCannotFire()
    {
        var hotkey = new Hotkey(_options.Tablet);
        Assert.False(hotkey.Poll(canExecute: false, now: 10f, pressed: true, held: true, released: false));
        Set("tablet_key", "None");
        Assert.False(Press(hotkey, 20f));
        Set("tablet_key", "T");
        Assert.True(Press(hotkey, 30f));
    }

    [Fact]
    public void LostReleaseAndExplicitCancellationCannotExecute()
    {
        var hotkey = CreateHeldHotkey();
        Assert.False(Press(hotkey, 10f));
        Assert.False(hotkey.Poll(canExecute: true, now: 11f, pressed: false, held: false, released: false));
        Assert.False(Release(hotkey, 11.1f));
        Assert.False(Press(hotkey, 20f));
        hotkey.Cancel();
        Assert.False(Release(hotkey, 21f));
    }

    [Fact]
    public void OverlappingHotkeysKeepIndependentState()
    {
        var first = CreateHeldHotkey();
        var second = CreateHeldHotkey();
        Assert.False(Press(first, 10f));
        Assert.False(Press(second, 10f));
        first.Cancel();
        Assert.False(Release(first, 11f));
        Assert.True(Release(second, 11f));

        var instant = new Hotkey(_options.Return);
        Assert.False(Press(second, 20f));
        Assert.True(Press(instant, 20f));
        Assert.True(Release(second, 21f));
    }

    [Fact]
    public void PresenterReadsFixedHotkeysWithoutConsumingOrCancellingActions()
    {
        var older = CreateHeldHotkey();
        var newer = CreateHeldHotkey();
        Hotkey[] hotkeys = [older, newer];

        Assert.Null(HoldRingPresenter.GetProgress(hotkeys, null, 10f));
        Assert.False(Press(older, 10f));
        Assert.Equal(0.5f, HoldRingPresenter.GetProgress(hotkeys, null, 10.5f));
        Assert.False(Press(newer, 10.5f));
        Assert.Equal(0.25f, HoldRingPresenter.GetProgress(hotkeys, null, 10.75f));
        newer.Cancel();
        Assert.Equal(0.75f, HoldRingPresenter.GetProgress(hotkeys, null, 10.75f));
        Assert.True(Release(older, 11f));
        Assert.Null(HoldRingPresenter.GetProgress(hotkeys, null, 11f));
    }

    [Fact]
    public void SharedHotkeysReadLiveSettingsAndCanBeReusedAfterLeavingTheirContext()
    {
        var returnHotkey = CreateHeldHotkey();
        var tablet = new Hotkey(_options.Tablet);
        Hotkey[] hotkeys = [returnHotkey, tablet];
        Assert.True(Press(tablet, 10f));
        Assert.Null(HoldRingPresenter.GetProgress(hotkeys, null, 10f));
        Assert.False(Press(returnHotkey, 10f));
        Assert.Equal(0.25f, HoldRingPresenter.GetProgress(hotkeys, null, 10.25f));
        Set("return_hold_duration", 0.5f);
        Assert.Equal(0.5f, HoldRingPresenter.GetProgress(hotkeys, null, 10.25f));

        // Leaving the location makes Return unavailable, while its instance stays with the manager.
        Assert.False(returnHotkey.Poll(canExecute: false, now: 10.25f, pressed: false, held: true, released: false));
        Assert.Null(HoldRingPresenter.GetProgress(hotkeys, null, 10.25f));
        Assert.False(Release(returnHotkey, 11f));
        Assert.False(Press(returnHotkey, 20f));
        Assert.Equal(0.5f, HoldRingPresenter.GetProgress(hotkeys, null, 20.25f));
        Assert.True(Release(returnHotkey, 20.5f));
        Assert.Null(HoldRingPresenter.GetProgress(hotkeys, null, 20.5f));
    }

    [Fact]
    public void NativeAndModHoldsUseTheSameNewestHoldRule()
    {
        var returnHotkey = CreateHeldHotkey();
        Hotkey[] hotkeys = [returnHotkey];
        Assert.False(Press(returnHotkey, 10f));
        var nativeHold = new PendingHold(10.25f, 2f);

        Assert.Equal(0.125f, HoldRingPresenter.GetProgress(hotkeys, nativeHold, 10.5f));
        Assert.Equal(0.5f, HoldRingPresenter.GetProgress(hotkeys, null, 10.5f));

        returnHotkey.Cancel();
        Assert.False(Press(returnHotkey, 11f));
        Assert.Equal(0.25f, HoldRingPresenter.GetProgress(hotkeys, nativeHold, 11.25f));
        returnHotkey.Cancel();
        Assert.Equal(0.5f, HoldRingPresenter.GetProgress(hotkeys, nativeHold, 11.25f));
    }

    [Fact]
    public void EqualStartTimesKeepTheFirstHotkeyUntilItEnds()
    {
        Set("bell_hold_duration", 0.5f);
        var first = CreateHeldHotkey();
        var second = new Hotkey(_options.Return, _options.BellHoldDuration, _options.HoldTimeoutDuration);
        Hotkey[] hotkeys = [first, second];
        Assert.False(Press(first, 10f));
        Assert.False(Press(second, 10f));
        Assert.Equal(0.25f, HoldRingPresenter.GetProgress(hotkeys, null, 10.25f));
        Assert.Equal(0.5f, HoldRingPresenter.GetProgress(hotkeys, null, 10.5f));
        first.Cancel();
        Assert.Equal(1f, HoldRingPresenter.GetProgress(hotkeys, null, 10.5f));
    }

    [Fact]
    public void FocusLossCancelsEveryPendingHoldUntilAFreshPress()
    {
        var first = CreateHeldHotkey();
        var second = CreateHeldHotkey();
        var instant = new Hotkey(_options.Tablet);
        Hotkey[] hotkeys = [first, second, instant];
        var manager = (Hotkeys)RuntimeHelpers.GetUninitializedObject(typeof(Hotkeys));
        typeof(Hotkeys).GetField("_all", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, hotkeys);
        var onFocusChanged = (Action<bool>)typeof(Hotkeys)
            .GetMethod("OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate(typeof(Action<bool>), manager);

        Assert.False(Press(first, 10f));
        Assert.False(Press(second, 10.75f));
        onFocusChanged(true);
        Assert.NotNull(first.ReadHold());
        Assert.NotNull(second.ReadHold());

        // One callback cancels both a ready hold and one still charging.
        onFocusChanged(false);
        Assert.Null(HoldRingPresenter.GetProgress(hotkeys, null, 11.25f));
        onFocusChanged(true);
        Assert.False(Hold(first, 11.25f));
        Assert.False(Release(first, 11.5f));
        Assert.False(Release(second, 11.75f));

        Assert.False(Press(first, 20f));
        Assert.True(Release(first, 21f));
        Assert.False(Press(second, 20f));
        Assert.True(Release(second, 21f));
        Assert.True(Press(instant, 21f));
    }

    [Fact]
    public void DisablingTheManagerCancelsPendingHoldsWithoutFiringThem()
    {
        var hotkey = CreateHeldHotkey();
        var manager = (Hotkeys)RuntimeHelpers.GetUninitializedObject(typeof(Hotkeys));
        typeof(Hotkeys).GetField("_all", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, new[] { hotkey });
        var onDisable = (Action)typeof(Hotkeys)
            .GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate(typeof(Action), manager);

        Assert.False(Press(hotkey, 10f));
        onDisable();
        Assert.Null(hotkey.ReadHold());
        Assert.False(Release(hotkey, 11f));
    }

    private Hotkey CreateHeldHotkey() => new(_options.Return, _options.ReturnHoldDuration,
        timeoutAfterReady: _options.HoldTimeoutDuration);

    private static bool Press(Hotkey hotkey, float now) =>
        hotkey.Poll(canExecute: true, now: now, pressed: true, held: true, released: false);

    private static bool Hold(Hotkey hotkey, float now) =>
        hotkey.Poll(canExecute: true, now: now, pressed: false, held: true, released: false);

    private static bool Release(Hotkey hotkey, float now) =>
        hotkey.Poll(canExecute: true, now: now, pressed: false, held: false, released: true);

    private void Set<T>(string key, T value)
    {
        var entry = (ConfigEntry<T>)_config.Get(key);
        var configProperty = typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!;
        configProperty.SetValue(entry, null);
        try { entry.Set(value); }
        finally { configProperty.SetValue(entry, _config); }
    }

    public void Dispose() => Configs.Remove(_modId);
}
