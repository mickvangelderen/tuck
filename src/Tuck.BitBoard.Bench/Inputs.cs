namespace Tuck.BitBoard.Bench;

// Keep fixtures in cells so alternative implementations can consume exactly the same inputs.
public sealed record BoardCase(int Width, int Height, int PercentSet)
{
    public bool[,] CreateCells(int seed = 1729)
    {
        var random = new Random(seed);
        var cells = new bool[Height, Width];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++) cells[y, x] = random.Next(100) < PercentSet;
        return cells;
    }

    public override string ToString() => $"{Width}x{Height}-{PercentSet}pct";
}

public sealed record OffsetCase(int Width, int Height, int SourceWidth, int SourceHeight, int X, int Y, string Name, bool Aliased = false)
{
    public override string ToString() => $"{Width}x{Height}-{Name}";
}

internal static class Inputs
{
    internal static (bool[,] Destination, bool[,] Source) MutationCells(OffsetCase input)
    {
        var destination = new BoardCase(input.Width, input.Height, 50).CreateCells();
        if (input.Aliased) return (destination, destination);
        var source = new BoardCase(input.SourceWidth, input.SourceHeight, 25).CreateCells(1741);
        // Even a tiny clipped intersection must change a cell for both union and subtraction.
        int sx = Math.Max(0, -input.X), sy = Math.Max(0, -input.Y);
        source[sy, sx] = source[sy, sx + 1] = true;
        destination[sy + input.Y, sx + input.X] = false;
        destination[sy + input.Y, sx + input.X + 1] = true;
        return (destination, source);
    }

    internal static BitTile[] Tiles(int count)
    {
        var bytes = new byte[count * 8];
        new Random(1729).NextBytes(bytes);
        var tiles = new BitTile[count];
        for (int i = 0; i < count; i++) tiles[i] = new(BitConverter.ToUInt64(bytes, i * 8));
        return tiles;
    }

    internal static (int X, int Y)[] Coordinates(BoardCase input, int count)
    {
        var random = new Random(1733);
        var result = new (int, int)[count];
        for (int i = 0; i < count; i++) result[i] = (random.Next(input.Width), random.Next(input.Height));
        return result;
    }

    internal static BitBoardMut Board(bool[,] cells)
    {
        var board = new BitBoardMut(cells.GetLength(1), cells.GetLength(0));
        for (int y = 0; y < board.Height; y++)
            for (int x = 0; x < board.Width; x++) board[x, y] = cells[y, x];
        return board;
    }
}
