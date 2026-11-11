using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Tuck.Mod;

/// <summary>One session-owned atlas, referenced only by explicitly named sprite tags.</summary>
internal static class HotkeyGlyphs
{
    internal const string AssetName = "Tuck.Hotkeys";
    private static Dictionary<KeyCode, string> _tags = new();
    private static bool _attempted;
    private static string? _modId;

    internal static void Register(string modId)
    {
        _modId = modId;
        // OnEnable runs before the loader registers the folder used by ReadModFile*.
        ModHook.OnModAssetsLoaded += OnAssetsLoaded;
    }

    internal static void Unregister()
    {
        ModHook.OnModAssetsLoaded -= OnAssetsLoaded;
        _modId = null;
    }

    private static void OnAssetsLoaded(LoadedMod mod)
    {
        if (_modId != null && mod.Manifest.ID == _modId) Initialize(_modId);
    }

    [Serializable]
    internal sealed class Entry
    {
        public int keycode;
        public string name = "";
        public Variants variants = new();
    }

    [Serializable]
    internal sealed class Variants
    {
        public SpriteRectangle? @base;
        public SpriteRectangle? mac;
    }

    [Serializable]
    internal sealed class SpriteRectangle
    {
        public int x;
        public int y;
        public int width;
        public int height;
    }

    [Serializable]
    internal sealed class Manifest
    {
        public string atlas = "";
        public int width;
        public int height;
        public string origin = "";
        public Entry[] keys = Array.Empty<Entry>();
    }

    internal static Manifest ParseManifest(string json) =>
        PaperJson.Deserialize<Manifest>(json) ?? throw new InvalidOperationException("Missing hotkey atlas manifest.");

    internal static string FileName(Manifest manifest)
    {
        string file = manifest.atlas;
        if (string.IsNullOrEmpty(file) || file.IndexOfAny(new[] { '/', '\\', ':' }) >= 0
            || !file.EndsWith(".png", StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid hotkey atlas filename.");
        return file;
    }

    internal static List<TMP_Sprite> CreateSprites(Manifest manifest, bool mac)
    {
        if (manifest.width <= 0 || manifest.height <= 0 || manifest.origin != "bottom-left")
            throw new InvalidOperationException("Invalid hotkey atlas dimensions or coordinate origin.");
        if (manifest.keys == null || manifest.keys.Length == 0)
            throw new InvalidOperationException("Empty hotkeys.json.");
        var keys = new HashSet<KeyCode>();
        var sprites = new List<TMP_Sprite>();
        float standardHeight = float.MaxValue;
        foreach (var entry in manifest.keys)
        {
            var key = (KeyCode)entry.keycode;
            if (key == KeyCode.None || !Enum.IsDefined(typeof(KeyCode), key) || !keys.Add(key))
                throw new InvalidOperationException($"Invalid or duplicate hotkey code {entry.keycode}.");
            var rect = mac ? entry.variants.mac ?? entry.variants.@base : entry.variants.@base;
            if (rect == null || rect.width <= 0 || rect.height <= 0 || rect.x < 0 || rect.y < 0
                || rect.width > manifest.width || rect.height > manifest.height
                || rect.x > manifest.width - rect.width || rect.y > manifest.height - rect.height)
                throw new InvalidOperationException($"Invalid hotkey rectangle for {entry.name}.");
            standardHeight = Math.Min(standardHeight, rect.height);
            sprites.Add(new TMP_Sprite
            {
                id = sprites.Count,
                name = entry.keycode.ToString(CultureInfo.InvariantCulture),
                x = rect.x,
                y = rect.y,
                width = rect.width,
                height = rect.height,
                xOffset = 0,
                xAdvance = rect.width + 2,
            });
        }
        foreach (var sprite in sprites)
        {
            // The legend baseline is 13/19 down a standard keycap; center tall keys on the same line.
            sprite.yOffset = sprite.height / 2f + standardHeight * (13f / 19f - 0.5f);
            // TMP scales sprites to the font ascent. Leave room for the frame around the letter.
            sprite.scale = 1.4f * sprite.height / standardHeight;
        }
        return sprites;
    }

    internal static void UpdateLookupTables(TMP_SpriteAsset asset)
    {
        asset.UpdateLookupTables();
        // This TMP version's legacy conversion leaves every character's glyphIndex at zero.
        for (int i = 0; i < asset.spriteCharacterTable.Count; i++)
            asset.spriteCharacterTable[i].glyphIndex = (uint)i;
        asset.UpdateLookupTables();
    }

    internal static string Tag(KeyCode key) =>
        $"<sprite=\"{AssetName}\" name=\"{((int)key).ToString(CultureInfo.InvariantCulture)}\">";

    internal static string? GetTag(KeyCode key) => _tags.TryGetValue(key, out string tag) ? tag : null;

    internal static void Initialize(string modId)
    {
        if (_attempted) return;
        _attempted = true;
        Texture2D? atlas = null;
        Material? material = null;
        TMP_SpriteAsset? asset = null;
        try
        {
            int hash = TMP_TextUtilities.GetSimpleHashCode(AssetName);
            if (MaterialReferenceManager.TryGetSpriteAsset(hash, out _)
                || MaterialReferenceManager.TryGetMaterial(hash, out _))
                throw new InvalidOperationException($"The TMP name {AssetName} is already registered.");
            var shader = Shader.Find("TextMeshPro/Sprite");
            if (shader == null) throw new InvalidOperationException("TextMeshPro/Sprite shader is unavailable.");
            string json = ModHelper.ReadModFileText(modId, "assets/hotkeys/hotkeys.json");
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("Missing hotkeys.json.");
            var manifest = ParseManifest(json);
            bool mac = Application.platform == RuntimePlatform.OSXPlayer;
            var sprites = CreateSprites(manifest, mac);
            var tags = new Dictionary<KeyCode, string>();
            foreach (var entry in manifest.keys)
            {
                var key = (KeyCode)entry.keycode;
                tags.Add(key, Tag(key));
            }

            string path = "assets/hotkeys/" + FileName(manifest);
            byte[] bytes = ModHelper.ReadModFileBytes(modId, path);
            atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = AssetName,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            if (bytes == null || !ImageConversion.LoadImage(atlas, bytes, true))
                throw new InvalidOperationException($"Could not decode {path}.");
            if (atlas.width != manifest.width || atlas.height != manifest.height)
                throw new InvalidOperationException("Hotkey atlas dimensions do not match hotkeys.json.");
            asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            asset.name = AssetName;
            asset.hashCode = hash;
            asset.spriteSheet = atlas;
            asset.spriteInfoList = sprites;
            material = new Material(shader) { name = AssetName, mainTexture = atlas };
            asset.material = material;
            UpdateLookupTables(asset);
            atlas.hideFlags = HideFlags.HideAndDontSave;
            material.hideFlags = HideFlags.HideAndDontSave;
            asset.hideFlags = HideFlags.HideAndDontSave;
            MaterialReferenceManager.AddSpriteAsset(hash, asset);
            _tags = tags;
            // TMP has no public unregister API. Keep these alive for cached text and scene changes.
            Log.Info($"Loaded {tags.Count} hotkey glyphs from {path}.");
        }
        catch (Exception exception)
        {
            if (asset != null) Object.Destroy(asset);
            if (material != null) Object.Destroy(material);
            if (atlas != null) Object.Destroy(atlas);
            Log.Warning($"Hotkey glyphs unavailable; using text hints. {exception.Message}");
        }
    }
}
