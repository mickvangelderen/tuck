using System;
using UnityEngine;

namespace Tuck.Mod;

internal static class TuckPawMask
{
    // Select the foreground paws from a copy of the color sprite: white keeps, black hides.
    // Both arrays use Unity's pixel order, so no coordinate conversion or contact bands are needed.
    internal static void Apply(Color32[] color, Color32[] foregroundMask, int width, int height)
    {
        if (width <= 0 || height <= 0 || color.Length != width * height || foregroundMask.Length != color.Length)
            throw new ArgumentException("Color sprite and foreground mask must have matching dimensions.");
        for (int i = 0; i < color.Length; i++)
            color[i].a = (byte)(color[i].a * foregroundMask[i].r * foregroundMask[i].a / (255 * 255));
    }
}
