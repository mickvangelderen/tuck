using System;
using System.Collections.Generic;

namespace Tuck.BitBoard;

/// <summary>An immutable tiled binary rectangle with content equality, including dimensions.</summary>
public sealed class BitBoard : IBitBoard, IEquatable<BitBoard>
{
    private readonly ulong[] _storage;

    /// <summary>Creates an empty or filled rectangle. Dimensions may be zero.</summary>
    public BitBoard(int width, int height, bool filled = false) => _storage = BitBoardOperations.Create(width, height, filled);
    internal BitBoard(ulong[] ownedStorage) => _storage = ownedStorage;
    internal ReadOnlySpan<ulong> Storage => _storage;

    /// <summary>Copies row-major 8 by 8 tiles into an owned board, clearing unused edge bits. Dimensions may be zero.</summary>
    /// <remarks>The input excludes the dimensions header and must contain exactly ceil(width / 8) * ceil(height / 8) tiles.</remarks>
    public static BitBoard FromParts(int width, int height, ReadOnlySpan<ulong> tiles) =>
        new(BitBoardOperations.FromParts(width, height, tiles));

    /// <inheritdoc/>
    public int Width => BitBoardOperations.Width(_storage);
    /// <inheritdoc/>
    public int Height => BitBoardOperations.Height(_storage);
    /// <inheritdoc/>
    public long Count => BitBoardOperations.Count(_storage);
    /// <inheritdoc/>
    public bool this[int x, int y] => BitBoardOperations.GetCell(_storage, x, y);
    /// <inheritdoc/>
    public BitTile GetTile(int tileX, int tileY) => BitBoardOperations.GetTile(_storage, tileX, tileY);
    /// <inheritdoc cref="IBitBoard.Cells"/>
    /// <remarks>Direct foreach iteration allocates no memory.</remarks>
    public SetCellEnumerable Cells => new(_storage);
    IEnumerable<(int X, int Y)> IBitBoard.Cells => Cells;

    public BitBoardMut ToMutable() => new(_storage.AsSpan().ToArray());

    /// <inheritdoc cref="IBitBoard.Rotated"/>
    /// <remarks>Identity rotations return this immutable instance.</remarks>
    public BitBoard Rotated(int quarterTurns = 1) => (quarterTurns & 3) == 0 ? this : new(BitBoardOperations.Transformed(_storage, (BoardTransform)(quarterTurns & 3)));
    /// <inheritdoc cref="IBitBoard.FlippedH"/>
    public BitBoard FlippedH() => new(BitBoardOperations.Transformed(_storage, BoardTransform.FlipH));
    /// <inheritdoc cref="IBitBoard.FlippedV"/>
    public BitBoard FlippedV() => new(BitBoardOperations.Transformed(_storage, BoardTransform.FlipV));
    /// <inheritdoc cref="IBitBoard.Inverted"/>
    public BitBoard Inverted() => new(BitBoardOperations.Inverted(_storage));

    IBitBoard IBitBoard.Rotated(int quarterTurns) => Rotated(quarterTurns);
    IBitBoard IBitBoard.FlippedH() => FlippedH();
    IBitBoard IBitBoard.FlippedV() => FlippedV();
    IBitBoard IBitBoard.Inverted() => Inverted();

    /// <inheritdoc/>
    /// <remarks>Writes without allocating.</remarks>
    public void RotateInto(BitBoardMut destination, int quarterTurns = 1) =>
        BitBoardOperations.TransformInto(_storage, destination.Storage, (BoardTransform)(quarterTurns & 3));
    /// <inheritdoc/>
    /// <remarks>Writes without allocating.</remarks>
    public void FlipHInto(BitBoardMut destination) =>
        BitBoardOperations.TransformInto(_storage, destination.Storage, BoardTransform.FlipH);
    /// <inheritdoc/>
    /// <remarks>Writes without allocating.</remarks>
    public void FlipVInto(BitBoardMut destination) =>
        BitBoardOperations.TransformInto(_storage, destination.Storage, BoardTransform.FlipV);
    /// <inheritdoc/>
    /// <remarks>Writes without allocating.</remarks>
    public void InvertInto(BitBoardMut destination) => BitBoardOperations.InvertInto(_storage, destination.Storage);

    /// <inheritdoc/>
    public bool Overlaps(IBitBoard source, int offsetX = 0, int offsetY = 0) => BitBoardOperations.Overlaps(_storage, BitBoardOperations.GetStorage(source), offsetX, offsetY);

    /// <inheritdoc/>
    public bool Contains(IBitBoard source, int offsetX = 0, int offsetY = 0) => BitBoardOperations.Contains(_storage, BitBoardOperations.GetStorage(source), offsetX, offsetY);

    /// <inheritdoc/>
    public bool ContentEquals(IBitBoard? other) => other is not null && BitBoardOperations.ContentEquals(_storage, BitBoardOperations.GetStorage(other));

    public bool Equals(BitBoard? other) => ReferenceEquals(this, other) ||
        other is not null && BitBoardOperations.ContentEquals(_storage, other._storage);
    public override bool Equals(object? obj) => obj is BitBoard other && Equals(other);
    public override int GetHashCode() => BitBoardOperations.ContentHash(_storage);
    public static bool operator ==(BitBoard? left, BitBoard? right) => left is null ? right is null : left.Equals(right);
    public static bool operator !=(BitBoard? left, BitBoard? right) => !(left == right);
}
