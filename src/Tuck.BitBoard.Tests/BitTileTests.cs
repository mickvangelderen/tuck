namespace Tuck.BitBoard.Tests;

public class BitTileTests
{
    [Fact]
    public void EverySingleBitTransformsToTheExpectedCell()
    {
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                var tile = new BitTile(1UL << (y * 8 + x));
                Assert.True(tile[x, y]);
                Assert.Equal(1, tile.Count);
                Assert.Equal(new BitTile(1UL << (y * 8 + 7 - x)), tile.FlippedH());
                Assert.Equal(new BitTile(1UL << ((7 - y) * 8 + x)), tile.FlippedV());
                int rotatedX = x, rotatedY = y;
                for (int turn = 0; turn < 4; turn++)
                {
                    Assert.Equal(new BitTile(1UL << (rotatedY * 8 + rotatedX)), tile.Rotated(turn));
                    Assert.Equal(tile.Rotated(turn), tile.Rotated(turn - 4));
                    (rotatedX, rotatedY) = (rotatedY, 7 - rotatedX);
                }
            }
    }

    [Fact]
    public void CountsAndEqualityMatchTheBits()
    {
        var random = new Random(1729);
        var bytes = new byte[8];
        for (int sample = 0; sample < 500; sample++)
        {
            random.NextBytes(bytes);
            ulong bits = BitConverter.ToUInt64(bytes);
            var tile = new BitTile(bits);
            Assert.Equal(Enumerable.Range(0, 64).Count(i => (bits & (1UL << i)) != 0), tile.Count);
            Assert.Equal(tile, tile.Rotated(4));
            Assert.Equal(tile, tile.FlippedH().FlippedH());
            Assert.Equal(tile, tile.FlippedV().FlippedV());
            Assert.Equal(tile, tile.Inverted().Inverted());
            Assert.Equal(tile.Rotated(2), tile.FlippedH().FlippedV());
            Assert.Equal(tile, tile.Rotated(1).Rotated(3));
            Assert.Equal(tile, tile.Rotated().Rotated().Rotated().Rotated());
            Assert.True(tile == new BitTile(bits));
            Assert.True(tile != new BitTile(~bits));
            Assert.True(tile.Equals((object)new BitTile(bits)));
            Assert.Equal(tile.GetHashCode(), new BitTile(bits).GetHashCode());
        }
        Assert.Equal(0, default(BitTile).Count);
        Assert.Equal(64, new BitTile(ulong.MaxValue).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BitTile(0)[-1, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BitTile(0)[0, 8]);
    }

    [Fact]
    public void SetOperationsMatchIndividualCells()
    {
        var random = new Random(1741);
        var bytes = new byte[16];
        for (int sample = 0; sample < 200; sample++)
        {
            random.NextBytes(bytes);
            var left = new BitTile(BitConverter.ToUInt64(bytes, 0));
            var right = new BitTile(BitConverter.ToUInt64(bytes, 8));
            var union = left.Unioned(right);
            var difference = left.Subtracted(right);
            var inverse = left.Inverted();
            bool overlaps = false, contains = true;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    bool a = left[x, y], b = right[x, y];
                    overlaps |= a && b;
                    contains &= !b || a;
                    Assert.Equal(a || b, union[x, y]);
                    Assert.Equal(a && !b, difference[x, y]);
                    Assert.Equal(!a, inverse[x, y]);
                }
            Assert.Equal(overlaps, left.Overlaps(right));
            Assert.Equal(contains, left.Contains(right));
            Assert.True(left.Contains(default));
            Assert.True(left.Contains(left));
            Assert.False(left.Overlaps(left.Inverted()));
            Assert.Equal(default, left.Subtracted(left));
        }
    }
}
