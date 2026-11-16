using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Tuck.Mod;

internal sealed class WarningAnimation : IDisposable
{
    internal const int Size = 16;
    internal const string BatteryFile = "warning-empty-battery.png";
    internal const string StolenFile = "warning-stolen.png";
    internal const string IngredientsFile = "warning-missing-ingredients.png";
    internal const string WaterFile = "warning-hydrate.png";
    internal const string SpillFile = "warning-spill.png";
    internal const string FilterFile = "warning-filter.png";
    private readonly Texture2D _texture;
    internal Sprite[] Frames { get; private set; } = Array.Empty<Sprite>();

    // Scaled Unity time supplies one shared phase, including newly visible overlays.
    internal static int FrameAt(double time, int frameCount) => (int)(Math.Floor(time) % frameCount);

    internal static int FrameCount(int width, int height)
    {
        if (height != Size || width <= 0 || width % Size != 0)
            throw new ArgumentException($"Warning sheets must be a horizontal strip of {Size}x{Size} frames.");
        return width / Size;
    }

    internal WarningAnimation(string modId, string file)
    {
        _texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        try
        {
            if (!ImageConversion.LoadImage(_texture, ModHelper.ReadModFileBytes(modId, "assets/warnings/" + file)))
                throw new InvalidOperationException($"Could not decode {file}.");
            Frames = new Sprite[FrameCount(_texture.width, _texture.height)];
            for (int i = 0; i < Frames.Length; i++)
                Frames[i] = Sprite.Create(_texture, new Rect(i * Size, 0, Size, Size),
                    new Vector2(0f, 1f), 100f, 0, SpriteMeshType.FullRect);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var frame in Frames)
            if (frame != null) Object.Destroy(frame);
        Object.Destroy(_texture);
    }
}
