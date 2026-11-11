using System;
using System.Collections.Generic;

namespace Tuck.BitBoard;

/// <summary>A mutable tiled binary rectangle. Assignment shares storage; Clone makes an independent copy.</summary>
/// <remarks>Equality and hashing use object identity. Use ContentEquals to compare dimensions and bits.</remarks>
public sealed class BitBoardMut : IBitBoard
{
    private readonly ulong[] _storage;

    /// <summary>Creates an empty or filled rectangle. Dimensions may be zero.</summary>
    public BitBoardMut(int width, int height, bool filled = false) => _storage = BitBoardOperations.Create(width, height, filled);
    internal BitBoardMut(ulong[] ownedStorage) => _storage = ownedStorage;
    internal Span<ulong> Storage => _storage;

    /// <summary>Copies row-major 8 by 8 tiles into an owned board, clearing unused edge bits. Dimensions may be zero.</summary>
    /// <remarks>The input excludes the dimensions header and must contain exactly ceil(width / 8) * ceil(height / 8) tiles.</remarks>
    public static BitBoardMut FromParts(int width, int height, ReadOnlySpan<ulong> tiles) =>
        new(BitBoardOperations.FromParts(width, height, tiles));

    /// <inheritdoc/>
    public int Width => BitBoardOperations.Width(_storage);
    /// <inheritdoc/>
    public int Height => BitBoardOperations.Height(_storage);
    /// <inheritdoc/>
    public long Count => BitBoardOperations.Count(_storage);
    /// <inheritdoc/>
    /// <remarks>Assignment sets or clears the cell.</remarks>
    public bool this[int x, int y]
    {
        get => BitBoardOperations.GetCell(_storage, x, y);
        set => BitBoardOperations.SetCell(_storage, x, y, value);
    }
    /// <inheritdoc/>
    public BitTile GetTile(int tileX, int tileY) => BitBoardOperations.GetTile(_storage, tileX, tileY);

    /// <inheritdoc cref="IBitBoard.Cells"/>
    /// <remarks>This is a live view. Direct foreach allocates nothing. Do not mutate during enumeration.</remarks>
    public SetCellEnumerable Cells => new(_storage);
    IEnumerable<(int X, int Y)> IBitBoard.Cells => Cells;

    public BitBoardMut Clone() => new(_storage.AsSpan().ToArray());
    public BitBoard ToImmutable() => new(_storage.AsSpan().ToArray());

    public void Clear() => BitBoardOperations.Clear(_storage);
    public void Fill() => BitBoardOperations.Fill(_storage);
    public void Invert() => BitBoardOperations.Invert(_storage);

    /// <summary>Rotates this board as described by <see cref="IBitBoard.Rotated"/>, swapping dimensions for odd turns.</summary>
    public void Rotate(int quarterTurns = 1) => BitBoardOperations.TransformInPlace(_storage, (BoardTransform)(quarterTurns & 3));
    /// <summary>Flips this board left/right.</summary>
    public void FlipH() => BitBoardOperations.TransformInPlace(_storage, BoardTransform.FlipH);
    /// <summary>Flips this board top/bottom.</summary>
    public void FlipV() => BitBoardOperations.TransformInPlace(_storage, BoardTransform.FlipV);

    /// <inheritdoc cref="IBitBoard.Rotated"/>
    /// <remarks>Returns an independent mutable copy, including for identity rotations.</remarks>
    public BitBoardMut Rotated(int quarterTurns = 1) => new(BitBoardOperations.Transformed(_storage, (BoardTransform)(quarterTurns & 3)));
    /// <inheritdoc cref="IBitBoard.FlippedH"/>
    public BitBoardMut FlippedH() => new(BitBoardOperations.Transformed(_storage, BoardTransform.FlipH));
    /// <inheritdoc cref="IBitBoard.FlippedV"/>
    public BitBoardMut FlippedV() => new(BitBoardOperations.Transformed(_storage, BoardTransform.FlipV));
    /// <inheritdoc cref="IBitBoard.Inverted"/>
    public BitBoardMut Inverted() => new(BitBoardOperations.Inverted(_storage));

    IBitBoard IBitBoard.Rotated(int quarterTurns) => Rotated(quarterTurns);
    IBitBoard IBitBoard.FlippedH() => FlippedH();
    IBitBoard IBitBoard.FlippedV() => FlippedV();
    IBitBoard IBitBoard.Inverted() => Inverted();

    /// <inheritdoc/>
    /// <remarks>Aliases use the in-place path; distinct boards need no scratch.</remarks>
    public void RotateInto(BitBoardMut destination, int quarterTurns = 1) =>
        BitBoardOperations.TransformInto(_storage, destination.Storage, (BoardTransform)(quarterTurns & 3));
    /// <inheritdoc/>
    /// <remarks>Aliases use the in-place path; distinct boards need no scratch.</remarks>
    public void FlipHInto(BitBoardMut destination) =>
        BitBoardOperations.TransformInto(_storage, destination.Storage, BoardTransform.FlipH);
    /// <inheritdoc/>
    /// <remarks>Aliases use the in-place path; distinct boards need no scratch.</remarks>
    public void FlipVInto(BitBoardMut destination) =>
        BitBoardOperations.TransformInto(_storage, destination.Storage, BoardTransform.FlipV);
    /// <inheritdoc/>
    /// <remarks>Writes without allocating, including when aliased.</remarks>
    public void InvertInto(BitBoardMut destination) => BitBoardOperations.InvertInto(_storage, destination.Storage);

    /// <inheritdoc/>
    public bool Overlaps(IBitBoard source, int offsetX = 0, int offsetY = 0) => BitBoardOperations.Overlaps(_storage, BitBoardOperations.GetStorage(source), offsetX, offsetY);

    /// <inheritdoc/>
    public bool Contains(IBitBoard source, int offsetX = 0, int offsetY = 0) => BitBoardOperations.Contains(_storage, BitBoardOperations.GetStorage(source), offsetX, offsetY);

    /// <inheritdoc/>
    /// <remarks>Does not change this type's object-identity equality.</remarks>
    public bool ContentEquals(IBitBoard? other) => other is not null && BitBoardOperations.ContentEquals(_storage, BitBoardOperations.GetStorage(other));

    /// <summary>Unions translated source cells into this board, clipping at its bounds. Aliased sources are supported.</summary>
    public void Union(IBitBoard source, int offsetX = 0, int offsetY = 0) => BitBoardOperations.Combine(_storage, BitBoardOperations.GetStorage(source), offsetX, offsetY, subtract: false);

    /// <summary>Clears translated source cells from this board, clipping at its bounds. Aliased sources are supported.</summary>
    public void Subtract(IBitBoard source, int offsetX = 0, int offsetY = 0) => BitBoardOperations.Combine(_storage, BitBoardOperations.GetStorage(source), offsetX, offsetY, subtract: true);
}
