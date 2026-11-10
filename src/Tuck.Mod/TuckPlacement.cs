using UnityEngine;

namespace Tuck.Mod;

/// <summary>Native-pixel placements on the centered 960x540 afterhour backgrounds.</summary>
internal sealed class TuckPlacement(string spriteName, int left, int top, Rect body, bool shadow = false, bool pawMask = false)
{
    internal const float PixelsPerUnit = 100f;
    internal string SpritePath { get; } = $"assets/tuck/{spriteName}.png";
    internal string? ForegroundMaskPath { get; } = pawMask ? $"assets/tuck/{spriteName}-mask.png" : null;
    internal string? WireSpritePath { get; } = pawMask ? $"assets/tuck/{spriteName}-wire.png" : null;
    internal int Left { get; } = left;
    internal int Top { get; } = top;
    // Pixel-edge coordinates from the sprite's top-left; excludes the long tail.
    internal Rect Body { get; } = body;
    internal bool HasShadow { get; } = shadow;
    internal Vector3 LocalPosition => new((Left - 480) / PixelsPerUnit, (270 - Top) / PixelsPerUnit, -0.01f);

    internal static TuckPlacement? ForLocation(string locationId) => locationId switch
    {
        // Copied from the reviewed pixel-art-v1 placement JSONs; no runtime JSON dependency.
        "dumping_ground" => new("rail-walk", 768, 321, new Rect(20, 1, 36, 29)),
        // Complete color sprite, native wire cutout, and masked paws share the same top-left origin.
        "commissary" => new("hang", 166, 22, new Rect(2, 1, 23, 48), pawMask: true),
        "dr_jackson" => new("stand", 617, 270, new Rect(1, 1, 39, 55), shadow: true),
        _ => null
    };
}
