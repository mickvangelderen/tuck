using System;
using System.Buffers;

namespace Tuck.BitBoard;

// The first four values are quarter-turn counts, normalized by the public rotation methods.
internal enum BoardTransform { Identity, Rotate90, Rotate180, Rotate270, FlipH, FlipV }

internal static class BitBoardOperations
{
    // A header-only board has no tiles even when its other dimension is huge; return before tile-row loops.
    internal static int Width(ReadOnlySpan<ulong> board) => (int)(uint)board[0];
    internal static int Height(ReadOnlySpan<ulong> board) => (int)(board[0] >> 32);
    internal static int TileLength(int length) => (int)(((long)length + 7) / 8);

    private static ulong Header(int width, int height) => (uint)width | ((ulong)(uint)height << 32);
    private static ulong TransformedHeader(ReadOnlySpan<ulong> source, BoardTransform transform) =>
        transform is BoardTransform.Rotate90 or BoardTransform.Rotate270 ? Header(Height(source), Width(source)) : source[0];

    internal static ReadOnlySpan<ulong> GetStorage(IBitBoard source)
    {
        if (source is BitBoard immutable) return immutable.Storage;
        if (source is BitBoardMut mutable) return mutable.Storage;
        if (source is null) throw new ArgumentNullException(nameof(source));

        // Snapshot other implementations: a wrapper can alias the destination without exposing its storage.
        var snapshot = Create(source.Width, source.Height, filled: false);
        if (snapshot.Length == 1) return snapshot;
        int columns = TileLength(Width(snapshot)), rows = TileLength(Height(snapshot));
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++) snapshot[1 + y * columns + x] = source.GetTile(x, y).Bits;
        MaskEdges(snapshot);
        return snapshot;
    }

    private static long TileCount(int width, int height)
    {
        if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));
        return (long)TileLength(width) * TileLength(height);
    }

    internal static ulong[] Create(int width, int height, bool filled)
    {
        var board = new ulong[checked((int)(TileCount(width, height) + 1))];
        board[0] = Header(width, height);
        if (filled) Fill(board);
        return board;
    }

    internal static ulong[] FromParts(int width, int height, ReadOnlySpan<ulong> tiles)
    {
        if (TileCount(width, height) != tiles.Length)
            throw new ArgumentException("Tile count must match the board dimensions.", nameof(tiles));
        var board = Create(width, height, filled: false);
        tiles.CopyTo(board.AsSpan(1));
        MaskEdges(board);
        return board;
    }

    internal static bool GetCell(ReadOnlySpan<ulong> board, int x, int y)
    {
        int index = CellIndex(board, x, y);
        return new BitTile(board[index])[x & 7, y & 7];
    }

    internal static void SetCell(Span<ulong> board, int x, int y, bool value)
    {
        int index = CellIndex(board, x, y);
        ulong bit = 1UL << ((y & 7) * 8 + (x & 7));
        if (value) board[index] |= bit;
        else board[index] &= ~bit;
    }

    private static int CellIndex(ReadOnlySpan<ulong> board, int x, int y)
    {
        if ((uint)x >= (uint)Width(board)) throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)Height(board)) throw new ArgumentOutOfRangeException(nameof(y));
        return 1 + (y / 8) * TileLength(Width(board)) + x / 8;
    }

    internal static BitTile GetTile(ReadOnlySpan<ulong> board, int tileX, int tileY)
    {
        int columns = TileLength(Width(board));
        if ((uint)tileX >= (uint)columns) throw new ArgumentOutOfRangeException(nameof(tileX));
        if ((uint)tileY >= (uint)TileLength(Height(board))) throw new ArgumentOutOfRangeException(nameof(tileY));
        return new(board[1 + tileY * columns + tileX]);
    }

    internal static long Count(ReadOnlySpan<ulong> board)
    {
        long count = 0;
        for (int i = 1; i < board.Length; i++) count += new BitTile(board[i]).Count;
        return count;
    }

    private static ulong LowColumnsMask(int width) => 0x0101010101010101UL * ((1UL << width) - 1);

    private static void MaskEdges(Span<ulong> board)
    {
        if (board.Length == 1) return;
        int width = Width(board), height = Height(board), columns = TileLength(width);
        if ((width & 7) != 0)
        {
            ulong mask = LowColumnsMask(width & 7);
            int rows = TileLength(height);
            for (int y = 0; y < rows; y++) board[(y + 1) * columns] &= mask;
        }
        if ((height & 7) != 0)
        {
            ulong mask = (1UL << ((height & 7) * 8)) - 1;
            for (int i = board.Length - columns; i < board.Length; i++) board[i] &= mask;
        }
    }

    internal static void Clear(Span<ulong> board) => board.Slice(1).Clear();

    internal static void Fill(Span<ulong> board)
    {
        if (board.Length == 2) board[1] = ulong.MaxValue;
        else board.Slice(1).Fill(ulong.MaxValue);
        MaskEdges(board);
    }

    internal static void Invert(Span<ulong> board)
    {
        InvertInto(board, board);
    }

    internal static ulong[] Inverted(ReadOnlySpan<ulong> source)
    {
        var result = new ulong[source.Length];
        result[0] = source[0];
        InvertInto(source, result);
        return result;
    }

    internal static void InvertInto(ReadOnlySpan<ulong> source, Span<ulong> destination)
    {
        if (source[0] != destination[0])
            throw new ArgumentException("Destination dimensions must match the source.", nameof(destination));
        for (int i = 1; i < source.Length; i++) destination[i] = BitTile.Inverted(source[i]);
        MaskEdges(destination);
    }

    // Read an unaligned 8 by 8 window from at most four tiles, treating outside cells as zero.
    private static ulong Window(ReadOnlySpan<ulong> board, long x, long y)
    {
        if (x <= -8 || y <= -8 || x >= Width(board) || y >= Height(board)) return 0;
        long tileX = x >> 3, tileY = y >> 3;
        int shiftX = (int)(x & 7), shiftY = (int)(y & 7);
        ulong top = RowWindow(board, tileX, tileY, shiftX);
        if (shiftY == 0) return top;
        return (top >> (shiftY * 8)) | (RowWindow(board, tileX, tileY + 1, shiftX) << ((8 - shiftY) * 8));
    }

    private static ulong RowWindow(ReadOnlySpan<ulong> board, long tileX, long tileY, int shift)
    {
        ulong left = TileOrZero(board, tileX, tileY);
        if (shift == 0) return left;
        ulong right = TileOrZero(board, tileX + 1, tileY);
        return ((left >> shift) & LowColumnsMask(8 - shift)) | ((right << (8 - shift)) & ~LowColumnsMask(8 - shift));
    }

    private static ulong TileOrZero(ReadOnlySpan<ulong> board, long tileX, long tileY)
    {
        int columns = TileLength(Width(board));
        if (tileX < 0 || tileY < 0 || tileX >= columns || tileY >= TileLength(Height(board))) return 0;
        return board[1 + (int)(tileY * columns + tileX)];
    }

    internal static bool Overlaps(ReadOnlySpan<ulong> board, ReadOnlySpan<ulong> source, int offsetX, int offsetY)
    {
        if (offsetX == 0 && offsetY == 0 && board[0] == source[0])
        {
            for (int i = 1; i < board.Length; i++)
                if (BitTile.Overlaps(board[i], source[i])) return true;
            return false;
        }
        // Small placements often collide immediately; avoid clipping setup on that path.
        if (offsetX < 8 && offsetY < 8 && board.Length > 1 &&
            BitTile.Overlaps(board[1], Window(source, -(long)offsetX, -(long)offsetY))) return true;
        var area = Intersection(board, source, offsetX, offsetY);
        int columns = TileLength(Width(board));
        for (int y = area.Top; y < area.Bottom; y++)
        {
            int i = 1 + y * columns + area.Left;
            for (int x = area.Left; x < area.Right; x++, i++)
                if (BitTile.Overlaps(board[i], Window(source, x * 8L - offsetX, y * 8L - offsetY))) return true;
        }
        return false;
    }

    internal static bool Contains(ReadOnlySpan<ulong> board, ReadOnlySpan<ulong> source, int offsetX, int offsetY)
    {
        if (offsetX == 0 && offsetY == 0 && board[0] == source[0])
        {
            for (int i = 1; i < source.Length; i++)
                if (!BitTile.Contains(board[i], source[i])) return false;
            return true;
        }
        if (source.Length == 1) return true;
        if (!BitTile.Contains(Window(board, offsetX, offsetY), source[1])) return false;
        // Preserve the fast first-tile rejection without testing that tile again in the scan.
        int columns = TileLength(Width(source)), rows = TileLength(Height(source)), index = 2;
        int firstColumn = 1;
        for (int y = 0; y < rows; y++)
        {
            for (int x = firstColumn; x < columns; x++, index++)
                if (!BitTile.Contains(Window(board, x * 8L + offsetX, y * 8L + offsetY), source[index])) return false;
            firstColumn = 0;
        }
        return true;
    }

    private static (int Left, int Top, int Right, int Bottom) Intersection(
        ReadOnlySpan<ulong> board, ReadOnlySpan<ulong> source, int offsetX, int offsetY)
    {
        long left = Math.Max(0L, offsetX), top = Math.Max(0L, offsetY);
        long right = Math.Min(Width(board), (long)offsetX + Width(source));
        long bottom = Math.Min(Height(board), (long)offsetY + Height(source));
        if (left >= right || top >= bottom) return default;
        return ((int)(left / 8), (int)(top / 8), (int)((right + 7) / 8), (int)((bottom + 7) / 8));
    }

    internal static void Combine(Span<ulong> destination, ReadOnlySpan<ulong> source, int offsetX, int offsetY, bool subtract)
    {
        if (offsetX == 0 && offsetY == 0 && destination[0] == source[0])
        {
            for (int i = 1; i < destination.Length; i++)
                destination[i] = subtract ? BitTile.Subtracted(destination[i], source[i]) : BitTile.Unioned(destination[i], source[i]);
            return;
        }
        var area = Intersection(destination, source, offsetX, offsetY);
        if (area.Right == 0) return;
        // Shifted self-writes must read the original tiles, including through mutable aliases.
        if ((offsetX != 0 || offsetY != 0) && source.Overlaps(destination)) source = source.ToArray();
        int columns = TileLength(Width(destination));
        for (int y = area.Top; y < area.Bottom; y++)
        {
            int i = 1 + y * columns + area.Left;
            for (int x = area.Left; x < area.Right; x++, i++)
            {
                ulong bits = Window(source, x * 8L - offsetX, y * 8L - offsetY);
                destination[i] = subtract ? BitTile.Subtracted(destination[i], bits) : BitTile.Unioned(destination[i], bits);
            }
        }
        if (!subtract) MaskEdges(destination);
    }

    internal static ulong[] Transformed(ReadOnlySpan<ulong> source, BoardTransform transform)
    {
        var result = new ulong[source.Length];
        Transform(source, result, transform);
        return result;
    }

    internal static void TransformInto(ReadOnlySpan<ulong> source, Span<ulong> destination, BoardTransform transform)
    {
        if (destination[0] != TransformedHeader(source, transform))
            throw new ArgumentException("Destination dimensions must match the transformed source.", nameof(destination));
        if (source.Overlaps(destination)) TransformInPlace(destination, transform);
        else Transform(source, destination, transform);
    }

    internal static void TransformInPlace(Span<ulong> board, BoardTransform transform)
    {
        if (transform == BoardTransform.Identity) return;
        ulong[]? rented = null;
        // Bound stack use; larger boards borrow only temporary storage, never their owned array.
        Span<ulong> snapshot = board.Length <= 128 ? stackalloc ulong[board.Length]
            : (rented = ArrayPool<ulong>.Shared.Rent(board.Length)).AsSpan(0, board.Length);
        try
        {
            board.CopyTo(snapshot);
            Transform(snapshot, board, transform);
        }
        finally
        {
            if (rented != null) ArrayPool<ulong>.Shared.Return(rented);
        }
    }

    // Source and destination must not overlap. Every destination word, including the header, is overwritten.
    private static void Transform(ReadOnlySpan<ulong> source, Span<ulong> destination, BoardTransform transform)
    {
        if (transform == BoardTransform.Identity)
        {
            source.CopyTo(destination);
            return;
        }
        int width = Width(source), height = Height(source);
        destination[0] = TransformedHeader(source, transform);
        if (destination.Length == 1) return;
        int columns = TileLength(Width(destination)), rows = TileLength(Height(destination)), index = 1;
        for (int tileY = 0; tileY < rows; tileY++)
        {
            for (int tileX = 0; tileX < columns; tileX++, index++)
            {
                long x = tileX * 8L, y = tileY * 8L;
                var tile = new BitTile(transform switch
                {
                    BoardTransform.Rotate90 => Window(source, width - y - 8, x),
                    BoardTransform.Rotate180 => Window(source, width - x - 8, height - y - 8),
                    BoardTransform.Rotate270 => Window(source, y, height - x - 8),
                    BoardTransform.FlipH => Window(source, width - x - 8, y),
                    BoardTransform.FlipV => Window(source, x, height - y - 8),
                    _ => throw new ArgumentOutOfRangeException(nameof(transform))
                });
                destination[index] = (transform switch
                {
                    BoardTransform.FlipH => tile.FlippedH(),
                    BoardTransform.FlipV => tile.FlippedV(),
                    _ => tile.Rotated((int)transform)
                }).Bits;
            }
        }
        MaskEdges(destination);
    }

    // The packed header includes dimensions; edge padding is canonical, so every word participates.
    internal static bool ContentEquals(ReadOnlySpan<ulong> left, ReadOnlySpan<ulong> right) => left.SequenceEqual(right);

    internal static int ContentHash(ReadOnlySpan<ulong> board)
    {
        var hash = new HashCode();
        foreach (ulong word in board) hash.Add(word);
        return hash.ToHashCode();
    }
}
