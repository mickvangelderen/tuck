using UnityEngine;

namespace Tuck.Mod.Tests;

public class TuckPawMaskTests
{
    [Theory]
    [InlineData(255, 255, 200)]
    [InlineData(0, 255, 0)]
    [InlineData(255, 0, 0)]
    [InlineData(128, 255, 100)]
    public void MaskSelectsForegroundAlphaAndPreservesColor(byte selection, byte maskAlpha, byte expectedAlpha)
    {
        var color = new[] { new Color32(81, 62, 73, 200), new Color32(81, 62, 73, 0) };
        var mask = Enumerable.Repeat(new Color32(selection, selection, selection, maskAlpha), 2).ToArray();

        TuckPawMask.Apply(color, mask, 2, 1);

        for (int i = 0; i < color.Length; i++)
        {
            Assert.Equal((byte)81, color[i].r);
            Assert.Equal((byte)62, color[i].g);
            Assert.Equal((byte)73, color[i].b);
            Assert.Equal(i == 0 ? expectedAlpha : (byte)0, color[i].a);
        }
    }

    [Fact]
    public void IncorrectMaskDimensionsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => TuckPawMask.Apply(new Color32[2], new Color32[1], 2, 1));
        Assert.Throws<ArgumentException>(() => TuckPawMask.Apply(new Color32[2], new Color32[2], 3, 1));
    }
}
