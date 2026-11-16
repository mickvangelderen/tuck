using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UI;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class MagnifierPositionTests
{
    [Theory]
    [InlineData(20, 30, 20, 30)]
    [InlineData(-10, -20, 0, 0)]
    [InlineData(110, 220, 100, 200)]
    [InlineData(-1000, 1000, 0, 200)]
    [InlineData(1000, -1000, 100, 0)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(100, 200, 100, 200)]
    [InlineData(1, 199, 1, 199)]
    public void ClampsOnlyTheCenterToScreenEdgesWithoutAMargin(float x, float y, float expectedX, float expectedY)
    {
        var center = MagnifierPosition.ClampCenter(new Vector2(x, y), 100, 200);
        Assert.Equal(expectedX, center.x);
        Assert.Equal(expectedY, center.y);
    }

    [Fact]
    public void RecoversRememberedCenterAfterScreenShrinks()
    {
        var center = MagnifierPosition.ClampCenter(new Vector2(1800, 1000), 1920, 1080);
        Assert.Equal(1800, center.x);
        Assert.Equal(1000, center.y);
        center = MagnifierPosition.ClampCenter(center, 1280, 720);
        Assert.Equal(1280, center.x);
        Assert.Equal(720, center.y);
    }

    [Fact]
    public void RestoringGlassKeepsImageInPlaceWithoutRepeatingTheMovement()
    {
        var corrected = MagnifierPosition.CorrectImage(new Vector3(140, 80, 0),
            new Vector3(140, 80, 0), new Vector3(40, 20, 0), 1);
        Assert.Equal(100, corrected.Position.x);
        Assert.Equal(60, corrected.Position.y);
        Assert.Equal(140, corrected.LastParentPosition.x);
        Assert.Equal(80, corrected.LastParentPosition.y);
    }

    [Fact]
    public void OptionControlsPositionMemoryAndMagnifierRaycasts()
    {
        string id = "tuck-magnifier-test-" + Guid.NewGuid().ToString("N");
        var configs = (Dictionary<string, ModConfig>)typeof(ModConfig)
            .GetField("configs", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var config = (ModConfig)Activator.CreateInstance(typeof(ModConfig),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [id], null)!;
        configs.Add(id, config);
        try
        {
            CheckDisabled();
            var options = new Options(new ModManifest { ID = id, Name = "Tuck" });
            var entry = (ConfigEntry<bool>)config.Get("remember_magnifier_position");
            typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!.SetValue(entry, null);
            Assert.True(entry.Value);
            entry.Set(false);
            MagnifierPosition.Register(options);
            CheckDisabled();
            entry.Set(true);
            CheckMagnifierRaycasts();
            var rememberedGlass = typeof(MagnifierPosition).GetField("_lastGlass", BindingFlags.Static | BindingFlags.NonPublic)!;
            var glass = (GameObject)RuntimeHelpers.GetUninitializedObject(typeof(GameObject));
            rememberedGlass.SetValue(null, glass);
            entry.Set(false);
            Assert.Null(rememberedGlass.GetValue(null));
            CheckDisabled();
            entry.Set(true);
            Assert.Null(rememberedGlass.GetValue(null));
            rememberedGlass.SetValue(null, glass);
            MagnifierPosition.Unregister();
            Assert.Null(rememberedGlass.GetValue(null));
            CheckDisabled();
        }
        finally
        {
            MagnifierPosition.Unregister();
            configs.Remove(id);
        }
    }

    private static void CheckDisabled()
    {
        MagnifierPosition.BeforeOpen(null!, out var position);
        Assert.Null(position);
        MagnifierPosition.AfterOpen(null!, new Vector3(30, 40, 0));
        var previous = new Vector3(30, 40, 0);
        MagnifierPosition.BeforeCounterMove(null!, ref previous, 1);
        Assert.Equal(30, previous.x);
        Assert.Equal(40, previous.y);
        MagnifierPosition.BeforeRaycast(null!, out var state);
        bool result = false;
        Assert.True(MagnifierPosition.CheckImageRaycast(null!, ref result));
        Assert.False(result);
        MagnifierPosition.AfterRaycast(state);
    }

    private static void CheckMagnifierRaycasts()
    {
        var glass = (Image)RuntimeHelpers.GetUninitializedObject(typeof(Image));
        var tray = (Image)RuntimeHelpers.GetUninitializedObject(typeof(Image));
        var other = (Image)RuntimeHelpers.GetUninitializedObject(typeof(Image));
        typeof(MagnifierPosition).GetField("_glassImage", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, glass);
        typeof(MagnifierPosition).GetField("_trayImage", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, tray);

        MagnifierPosition.BeforeRaycast(glass, out var previous);
        try
        {
            bool result = false;
            Assert.True(MagnifierPosition.CheckImageRaycast(glass, ref result)); // Preserve the glass's alpha test.
            Assert.False(result);
            Assert.True(MagnifierPosition.CheckImageRaycast(other, ref result));
            Assert.False(result);
            Assert.False(MagnifierPosition.CheckImageRaycast(tray, ref result));
            Assert.True(result); // An opaque handle outside the tray must not be rejected by the tray image.

            MagnifierPosition.BeforeRaycast(tray, out var nested);
            try
            {
                result = false;
                Assert.True(MagnifierPosition.CheckImageRaycast(tray, ref result)); // Tray clicks still use its alpha test.
                Assert.False(result);
            }
            finally { MagnifierPosition.AfterRaycast(nested); }
            result = false;
            Assert.False(MagnifierPosition.CheckImageRaycast(tray, ref result));
            Assert.True(result);
        }
        finally { MagnifierPosition.AfterRaycast(previous); }

        bool outsideResult = false;
        Assert.True(MagnifierPosition.CheckImageRaycast(tray, ref outsideResult));
        Assert.False(outsideResult);
    }
}
