using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class HotkeyGlyphTests
{
    [Fact]
    public void RegistrationWaitsForOurAssetsAndUnregistersTheHook()
    {
        var hook = typeof(ModHook).GetField(nameof(ModHook.OnModAssetsLoaded), BindingFlags.Static | BindingFlags.NonPublic)!;
        var attempted = typeof(HotkeyGlyphs).GetField("_attempted", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousHandlers = hook.GetValue(null);
        Assert.False((bool)attempted.GetValue(null)!);
        try
        {
            // There is no registered mod or Unity renderer: OnEnable must only subscribe.
            HotkeyGlyphs.Register("tuck-glyph-test");
            Assert.False((bool)attempted.GetValue(null)!);
            var handlers = (Action<LoadedMod>)hook.GetValue(null)!;
            var handler = Assert.Single(handlers.GetInvocationList(), h => h.Method.DeclaringType == typeof(HotkeyGlyphs));
            ((Action<LoadedMod>)handler)(new LoadedMod { Manifest = new ModManifest { ID = "another-mod" } });
            Assert.False((bool)attempted.GetValue(null)!);
        }
        finally
        {
            HotkeyGlyphs.Unregister();
        }
        Assert.Equal(previousHandlers, hook.GetValue(null));
    }

    private static HotkeyGlyphs.Manifest ReadManifest() => HotkeyGlyphs.ParseManifest(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets", "hotkeys", "hotkeys.json")));

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    public void MissingManifestIsRejected(string json)
    {
        Assert.Throws<InvalidOperationException>(() => HotkeyGlyphs.ParseManifest(json));
    }

    [Fact]
    public void PackagedManifestMapsUniqueGameKeycodesToAtlasRectanglesOnBothPlatforms()
    {
        var manifest = ReadManifest();
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "hotkeys", HotkeyGlyphs.FileName(manifest));
        byte[] png = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        Assert.Equal(manifest.width, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
        Assert.Equal(manifest.height, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
        Assert.Equal(8, png[24]); // Eight bits per channel.
        Assert.Equal(6, png[25]); // RGB with alpha.
        Assert.Equal("bottom-left", manifest.origin);
        Assert.Equal(143, manifest.keys.Length);
        var keys = new HashSet<KeyCode>();
        foreach (var entry in manifest.keys)
        {
            var key = (KeyCode)entry.keycode;
            Assert.True(Enum.IsDefined(key), entry.name);
            Assert.NotEqual(KeyCode.None, key);
            Assert.Equal(key, Enum.Parse<KeyCode>(entry.name));
            Assert.True(keys.Add(key), entry.name);
        }
        Assert.Contains(KeyCode.H, keys);
        Assert.Contains(KeyCode.Mouse6, keys);
        Assert.Contains(KeyCode.KeypadEnter, keys);
        foreach (bool mac in new[] { false, true })
        {
            var sprites = HotkeyGlyphs.CreateSprites(manifest, mac);
            Assert.Equal(manifest.keys.Length, sprites.Count);
            for (int i = 0; i < sprites.Count; i++)
            {
                var entry = manifest.keys[i];
                var expected = mac ? entry.variants.mac ?? entry.variants.@base! : entry.variants.@base!;
                var sprite = sprites[i];
                Assert.Equal(i, sprite.id);
                Assert.Equal(entry.keycode.ToString(), sprite.name);
                Assert.Equal((expected.x, expected.y, expected.width, expected.height),
                    (sprite.x, sprite.y, sprite.width, sprite.height));
                Assert.Equal(expected.width + 2, sprite.xAdvance);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TmpLookupResolvesEachKeyToItsOwnGlyphAfterLegacyConversion(bool mac)
    {
        var sprites = HotkeyGlyphs.CreateSprites(ReadManifest(), mac);
        var glyphs = sprites.Select(sprite => new TMP_SpriteGlyph((uint)sprite.id,
            new GlyphMetrics(sprite.width, sprite.height, sprite.xOffset, sprite.yOffset, sprite.xAdvance),
            new GlyphRect((int)sprite.x, (int)sprite.y, (int)sprite.width, (int)sprite.height), 1, 0)).ToList();
        // Reproduce the game's legacy conversion: glyph is assigned, but glyphIndex stays zero.
        var characters = sprites.Select(sprite => new TMP_SpriteCharacter
        {
            name = sprite.name, glyph = glyphs[sprite.id], scale = sprite.scale, unicode = 0xFFFE
        }).ToList();
        var asset = (TMP_SpriteAsset)RuntimeHelpers.GetUninitializedObject(typeof(TMP_SpriteAsset));
        typeof(TMP_SpriteAsset).GetField("m_SpriteGlyphTable", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(asset, glyphs);
        typeof(TMP_SpriteAsset).GetField("m_SpriteCharacterTable", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(asset, characters);

        asset.UpdateLookupTables();
        var c = characters[asset.GetSpriteIndexFromName(((int)KeyCode.C).ToString())];
        Assert.Same(glyphs[0], c.glyph); // The observed Backspace-for-C failure.

        HotkeyGlyphs.UpdateLookupTables(asset);
        foreach (var sprite in sprites)
        {
            var character = characters[asset.GetSpriteIndexFromName(sprite.name)];
            Assert.Equal((uint)sprite.id, character.glyphIndex);
            Assert.Same(glyphs[sprite.id], character.glyph);
            Assert.Equal(sprite.x, character.glyph.glyphRect.x);
            Assert.Equal(sprite.y, character.glyph.glyphRect.y);
        }
        Assert.NotSame(glyphs[0], c.glyph);
        asset.UpdateLookupTables();
        Assert.Same(glyphs[(int)c.glyphIndex], c.glyph);
    }

    [Fact]
    public void KeycapLegendsAlignToTextAndTallKeysKeepTheirProportionsAtEitherResolution()
    {
        var manifest = ReadManifest();
        foreach (int divisor in new[] { 1, 2 })
        {
            var sprites = HotkeyGlyphs.CreateSprites(manifest, false);
            var c = Assert.Single(sprites, sprite => sprite.name == ((int)KeyCode.C).ToString());
            var tall = Assert.Single(sprites, sprite => sprite.name == ((int)KeyCode.KeypadPlus).ToString());
            Assert.Equal(38f / divisor, c.height);
            Assert.Equal(26f / divisor, c.yOffset, 4);
            Assert.Equal(1.4f, c.scale, 4);
            Assert.Equal(2 * c.height, tall.height);
            Assert.Equal(c.scale / c.height, tall.scale / tall.height, 4);
            Assert.Equal(c.yOffset - c.height / 2, tall.yOffset - tall.height / 2, 4);

            foreach (var entry in manifest.keys)
                foreach (var rect in new[] { entry.variants.@base, entry.variants.mac })
                    if (rect != null)
                    {
                        rect.x /= 2;
                        rect.y /= 2;
                        rect.width /= 2;
                        rect.height /= 2;
                    }
        }
    }

    [Theory]
    [InlineData(KeyCode.LeftControl, true)]
    [InlineData(KeyCode.RightAlt, true)]
    [InlineData(KeyCode.LeftCommand, true)]
    [InlineData(KeyCode.H, false)]
    public void PlatformVariantsUseTheManifestMapping(KeyCode key, bool hasMacVariant)
    {
        var manifest = ReadManifest();
        var entry = Assert.Single(manifest.keys, entry => entry.keycode == (int)key);
        Assert.Equal(hasMacVariant, entry.variants.mac != null);
        var windows = Assert.Single(HotkeyGlyphs.CreateSprites(manifest, false), sprite => sprite.name == ((int)key).ToString());
        var mac = Assert.Single(HotkeyGlyphs.CreateSprites(manifest, true), sprite => sprite.name == ((int)key).ToString());
        Assert.Equal(hasMacVariant, (windows.x, windows.y) != (mac.x, mac.y));
    }

    [Theory]
    [InlineData("../H.png")]
    [InlineData("sub\\H.png")]
    [InlineData("C:H.png")]
    [InlineData("H.jpg")]
    [InlineData("")]
    public void InvalidImagePathsAreRejected(string file)
    {
        var manifest = ReadManifest();
        manifest.atlas = file;
        Assert.Throws<InvalidOperationException>(() => HotkeyGlyphs.FileName(manifest));
    }

    [Theory]
    [InlineData(-1, 0, 1, 1)]
    [InlineData(0, -1, 1, 1)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(0, 0, 1, -1)]
    [InlineData(1023, 0, 2, 1)]
    [InlineData(0, 511, 1, 2)]
    [InlineData(0, 0, int.MaxValue, 1)]
    [InlineData(0, int.MaxValue, 1, 1)]
    public void InvalidRectanglesAreRejectedOnEitherPlatform(int x, int y, int width, int height)
    {
        var manifest = ReadManifest();
        foreach (bool mac in new[] { false, true })
        {
            var invalid = new HotkeyGlyphs.SpriteRectangle { x = x, y = y, width = width, height = height };
            if (mac) manifest.keys[0].variants.mac = invalid;
            else manifest.keys[0].variants.@base = invalid;
            Assert.Throws<InvalidOperationException>(() => HotkeyGlyphs.CreateSprites(manifest, mac));
        }
    }

    [Fact]
    public void MissingBaseRectangleIsRejectedButAnExactEdgeFitIsAllowed()
    {
        var manifest = ReadManifest();
        manifest.keys[0].variants.@base = null;
        Assert.Throws<InvalidOperationException>(() => HotkeyGlyphs.CreateSprites(manifest, false));
        Assert.Throws<InvalidOperationException>(() => HotkeyGlyphs.CreateSprites(manifest, true));
        manifest.keys[0].variants.@base = new() { x = manifest.width - 1, y = manifest.height - 1, width = 1, height = 1 };
        Assert.NotEmpty(HotkeyGlyphs.CreateSprites(manifest, false));
    }

    [Theory]
    [InlineData(0, 512, "bottom-left")]
    [InlineData(1024, -1, "bottom-left")]
    [InlineData(1024, 512, "top-left")]
    [InlineData(1024, 512, "")]
    public void InvalidAtlasMetadataIsRejected(int width, int height, string origin)
    {
        var manifest = ReadManifest();
        manifest.width = width;
        manifest.height = height;
        manifest.origin = origin;
        Assert.Throws<InvalidOperationException>(() => HotkeyGlyphs.CreateSprites(manifest, false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    [InlineData((int)KeyCode.Tab)]
    public void InvalidOrDuplicateKeycodesAreRejected(int code)
    {
        var manifest = ReadManifest();
        manifest.keys[0].keycode = code;
        Assert.Throws<InvalidOperationException>(() => HotkeyGlyphs.CreateSprites(manifest, false));
    }

    [Fact]
    public void EmptyManifestIsRejected()
    {
        var manifest = ReadManifest();
        manifest.keys = [];
        Assert.Throws<InvalidOperationException>(() => HotkeyGlyphs.CreateSprites(manifest, false));
    }

    [Fact]
    public void UnavailableGlyphsLeaveTheTextFallbackAvailable()
    {
        Assert.Null(HotkeyGlyphs.GetTag(KeyCode.JoystickButton0));
        Assert.Null(HotkeyGlyphs.GetTag(KeyCode.None));
    }
}
