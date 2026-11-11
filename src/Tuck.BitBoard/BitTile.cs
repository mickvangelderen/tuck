using System;
using System.Runtime.CompilerServices;

namespace Tuck.BitBoard;

/// <summary>An immutable 8 by 8 tile. Bit (y * 8 + x) represents cell (x, y).</summary>
public readonly struct BitTile : IEquatable<BitTile>
{
    public ulong Bits { get; }
    public BitTile(ulong bits) => Bits = bits;
    public int Count => PopCount(Bits);

    public bool this[int x, int y]
    {
        get
        {
            if ((uint)x >= 8) throw new ArgumentOutOfRangeException(nameof(x));
            if ((uint)y >= 8) throw new ArgumentOutOfRangeException(nameof(y));
            return (Bits & (1UL << (y * 8 + x))) != 0;
        }
    }

    /// <summary>Positive quarter turns rotate counterclockwise with Y growing downward: (x, y) becomes (y, 7 - x).</summary>
    // Fixed board orientations can eliminate the switch inside tile loops.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BitTile Rotated(int quarterTurns = 1)
    {
        return (quarterTurns & 3) switch
        {
            1 => new BitTile(Transpose(Bits)).FlippedV(),
            2 => FlippedH().FlippedV(),
            3 => new BitTile(Transpose(Bits)).FlippedH(),
            _ => this
        };
    }

    /// <summary>Flips left and right: (x, y) becomes (7 - x, y).</summary>
    public BitTile FlippedH()
    {
        ulong bits = Bits;
        bits = ((bits >> 1) & 0x5555555555555555UL) | ((bits & 0x5555555555555555UL) << 1);
        bits = ((bits >> 2) & 0x3333333333333333UL) | ((bits & 0x3333333333333333UL) << 2);
        return new(((bits >> 4) & 0x0F0F0F0F0F0F0F0FUL) | ((bits & 0x0F0F0F0F0F0F0F0FUL) << 4));
    }

    /// <summary>Flips top and bottom: (x, y) becomes (x, 7 - y).</summary>
    public BitTile FlippedV()
    {
        ulong bits = Bits;
        bits = (bits >> 32) | (bits << 32);
        bits = ((bits >> 16) & 0x0000FFFF0000FFFFUL) | ((bits & 0x0000FFFF0000FFFFUL) << 16);
        return new(((bits >> 8) & 0x00FF00FF00FF00FFUL) | ((bits & 0x00FF00FF00FF00FFUL) << 8));
    }

    public BitTile Inverted() => new(Inverted(Bits));
    public bool Overlaps(BitTile source) => Overlaps(Bits, source.Bits);
    public bool Contains(BitTile source) => Contains(Bits, source.Bits);
    public BitTile Unioned(BitTile source) => new(Unioned(Bits, source.Bits));
    public BitTile Subtracted(BitTile source) => new(Subtracted(Bits, source.Bits));

    // Raw-word overloads avoid struct receiver temporaries in board loops.
    internal static ulong Inverted(ulong bits) => ~bits;
    internal static bool Overlaps(ulong bits, ulong source) => (bits & source) != 0;
    internal static bool Contains(ulong bits, ulong source) => (source & ~bits) == 0;
    // Combine's large window loop can otherwise exhaust the JIT's inlining budget.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong Unioned(ulong bits, ulong source) => bits | source;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong Subtracted(ulong bits, ulong source) => bits & ~source;

    private static ulong Transpose(ulong bits)
    {
        ulong swap = (bits ^ (bits >> 7)) & 0x00AA00AA00AA00AAUL;
        bits ^= swap ^ (swap << 7);
        swap = (bits ^ (bits >> 14)) & 0x0000CCCC0000CCCCUL;
        bits ^= swap ^ (swap << 14);
        swap = (bits ^ (bits >> 28)) & 0x00000000F0F0F0F0UL;
        return bits ^ swap ^ (swap << 28);
    }

    // Scalar population count also works on the game's netstandard2.1 runtime.
    private static int PopCount(ulong bits)
    {
        unchecked
        {
            bits -= (bits >> 1) & 0x5555555555555555UL;
            bits = (bits & 0x3333333333333333UL) + ((bits >> 2) & 0x3333333333333333UL);
            bits = (bits + (bits >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((bits * 0x0101010101010101UL) >> 56);
        }
    }

    public bool Equals(BitTile other) => Bits == other.Bits;
    public override bool Equals(object? obj) => obj is BitTile other && Equals(other);
    public override int GetHashCode() => Bits.GetHashCode();
    public static bool operator ==(BitTile left, BitTile right) => left.Equals(right);
    public static bool operator !=(BitTile left, BitTile right) => !left.Equals(right);
}
