namespace Tuck.BitBoard.Tests;

public class BitBoardEqualityTests
{
    [Fact]
    public void ImmutableOperatorsAgreeWithEqualsAndHashingIncludingNull()
    {
        var mutable = new BitBoardMut(9, 9);
        mutable[8, 8] = true;
        var original = mutable.ToImmutable();
        var equal = mutable.ToImmutable();
        mutable[8, 8] = false;
        mutable[7, 8] = true;
        BitBoard?[] values = [null, original, equal, mutable.ToImmutable(), new(8, 9), new(9, 8), new(0, 7), new(0, 8)];

        Assert.NotSame(original, equal);
        Assert.True(original == equal);
        Assert.False(original != equal);
        foreach (var left in values)
            foreach (var right in values)
            {
                bool expected = left?.Equals(right) ?? right is null;
                Assert.Equal(expected, left == right);
                Assert.Equal(!expected, left != right);
                Assert.Equal(expected, Equals(left, right));
                Assert.Equal(expected, EqualityComparer<BitBoard>.Default.Equals(left, right));
                if (expected && left is not null && right is not null)
                    Assert.Equal(left.GetHashCode(), right.GetHashCode());
            }
        var keys = new Dictionary<BitBoard, string> { [original] = "shape" };
        Assert.Equal("shape", keys[equal]);
    }

    [Fact]
    public void ContentEqualityCrossesMutabilityBoundariesWhileMutableIdentityStaysStable()
    {
        var mutable = new BitBoardMut(9, 9);
        mutable[8, 8] = true;
        var clone = mutable.Clone();
        var immutable = mutable.ToImmutable();
        var immutableCopy = mutable.ToImmutable();
        IBitBoard[] boards = [mutable, clone, immutable, immutableCopy];
        foreach (var left in boards)
        {
            Assert.False(left.ContentEquals(null));
            foreach (var right in boards) Assert.True(left.ContentEquals(right));
        }
        Assert.False(mutable == clone);
        Assert.False(mutable.Equals(clone));
        Assert.False(mutable.Equals(immutable));
        Assert.False(immutable.Equals((object)mutable));

        // Interface == remains reference equality; ContentEquals supplies the explicit common policy.
        IBitBoard first = immutable, second = immutableCopy;
        Assert.False(first == second);
        Assert.True(first.ContentEquals(second));
        Assert.True(first.Equals(second));

        var keys = new HashSet<BitBoardMut> { mutable, clone };
        Assert.Equal(2, keys.Count);
        int hash = mutable.GetHashCode();
        mutable.Clear();
        mutable[0, 0] = true;
        Assert.False(mutable.ContentEquals(clone));
        Assert.False(immutable.ContentEquals(mutable));
        Assert.True(clone.ContentEquals(immutable));
        Assert.Equal(hash, mutable.GetHashCode());
        Assert.Contains(mutable, keys);
    }

    [Fact]
    public void ContentEqualityIncludesDimensionsEvenWithEmptyOrIdenticalTilePayloads()
    {
        Assert.False(new BitBoard(8, 8).ContentEquals(new BitBoardMut(7, 8)));
        Assert.False(new BitBoardMut(7, 8).ContentEquals(new BitBoard(8, 8)));
        Assert.False(new BitBoard(0, 8).ContentEquals(new BitBoardMut(8, 0)));
        Assert.True(new BitBoardMut(0, 8).ContentEquals(new BitBoard(0, 8)));
        Assert.True(new BitBoard(0, 0).ContentEquals(new BitBoardMut(0, 0)));
    }
}
