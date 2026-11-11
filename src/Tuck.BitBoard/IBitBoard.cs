using System.Collections.Generic;

namespace Tuck.BitBoard;

/// <summary>Shared queries and transforms for a tiled binary rectangle. Implementations may be mutable.</summary>
/// <remarks>
/// Do not mutate the board while an operation or enumeration reads it.
/// Use ContentEquals to compare dimensions and bits across implementations; object equality depends on the concrete type.
/// BitBoard and BitBoardMut can read each other's storage directly. Other implementations are copied into a
/// temporary tile snapshot, including for queries. Mutations may also snapshot aliased sources.
/// Destination-writing methods may alias this board; writing then mutates that underlying board.
/// The destination must already have the result's dimensions, including for aliased rotations.
/// </remarks>
public interface IBitBoard
{
    /// <summary>Number of columns of cells. May be zero.</summary>
    int Width { get; }
    /// <summary>Number of rows of cells. May be zero.</summary>
    int Height { get; }
    /// <summary>The number of set cells.</summary>
    long Count { get; }
    /// <summary>Reads a cell within the board. X grows rightward and Y grows downward from the top-left corner.</summary>
    bool this[int x, int y] { get; }
    /// <summary>Returns an 8 by 8 tile; bits beyond the board's edges are zero.</summary>
    BitTile GetTile(int tileX, int tileY);
    /// <summary>Set-cell coordinates in row-major order.</summary>
    /// <remarks>Enumeration may allocate. Do not mutate the board during enumeration.</remarks>
    IEnumerable<(int X, int Y)> Cells { get; }

    /// <summary>Compares dimensions and all bits, regardless of mutability. Null is never equal.</summary>
    bool ContentEquals(IBitBoard? other);
    /// <summary>Tests intersection with the source origin at (offsetX, offsetY). Source cells outside this board are ignored.</summary>
    bool Overlaps(IBitBoard source, int offsetX = 0, int offsetY = 0);
    /// <summary>Tests whether every set source cell lies on a set cell here after translating its origin to (offsetX, offsetY).
    /// Set source cells outside this board fail containment; an empty source is contained.</summary>
    bool Contains(IBitBoard source, int offsetX = 0, int offsetY = 0);

    /// <summary>Returns a rotated board without changing this board. A positive quarter-turn is counterclockwise:
    /// (x, y) becomes (y, Width - 1 - x), with Y growing downward. Turns are normalized modulo four.</summary>
    /// <remarks>Identity rotations may reuse an immutable instance; mutable results are independent copies.</remarks>
    IBitBoard Rotated(int quarterTurns = 1);
    /// <summary>Returns a left/right flipped board without changing this board.</summary>
    IBitBoard FlippedH();
    /// <summary>Returns a top/bottom flipped board without changing this board.</summary>
    IBitBoard FlippedV();
    /// <summary>Returns inverted bits in a new board without changing this board.</summary>
    IBitBoard Inverted();

    /// <summary>Writes the rotation described by <see cref="Rotated"/> to a board of the rotated dimensions.</summary>
    void RotateInto(BitBoardMut destination, int quarterTurns = 1);
    /// <summary>Writes a left/right flip to a board of the same dimensions.</summary>
    void FlipHInto(BitBoardMut destination);
    /// <summary>Writes a top/bottom flip to a board of the same dimensions.</summary>
    void FlipVInto(BitBoardMut destination);
    /// <summary>Writes inverted bits to a board of the same dimensions.</summary>
    void InvertInto(BitBoardMut destination);
}
