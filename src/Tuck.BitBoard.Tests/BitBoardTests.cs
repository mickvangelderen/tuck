using System.Runtime.CompilerServices;

namespace Tuck.BitBoard.Tests;

public class BitBoardTests
{
    public static TheoryData<int, int> Dimensions => new()
    {
        { 0, 0 }, { 0, 17 }, { 17, 0 }, { 1, 1 }, { 1, 19 }, { 19, 1 },
        { 7, 9 }, { 8, 8 }, { 9, 7 }, { 9, 9 }, { 15, 17 }, { 16, 16 }, { 17, 15 }, { 31, 33 },
        { 10, 14 }, { 11, 13 }, { 12, 12 }, { 13, 11 }, { 14, 10 }
    };

    [Theory]
    [MemberData(nameof(Dimensions))]
    public void FromPartsCopiesRowMajorTilesAndMasksEdges(int width, int height)
    {
        int columns = (width + 7) / 8, rows = (height + 7) / 8;
        var tiles = new ulong[columns * rows];
        for (int i = 0; i < tiles.Length; i++) tiles[i] = unchecked(0x9E3779B97F4A7C15UL * (ulong)(i + 1));
        var expected = new bool[height, width];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                expected[y, x] = (tiles[y / 8 * columns + x / 8] & (1UL << ((y & 7) * 8 + (x & 7)))) != 0;

        var original = (ulong[])tiles.Clone();
        var immutable = BitBoard.FromParts(width, height, (ReadOnlySpan<ulong>)tiles);
        var mutable = BitBoardMut.FromParts(width, height, tiles.AsSpan());
        Assert.Equal(original, tiles);
        tiles.AsSpan().Clear();
        AssertBoard(expected, immutable);
        AssertBoard(expected, mutable.ToImmutable());
        mutable.Invert();
        AssertBoard(expected, immutable);
    }

    [Fact]
    public void FromPartsAcceptsEveryTileBitPatternButValidatesDimensionsAndCount()
    {
        ulong[] tiles = [ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue];
        var expected = new BitBoard(9, 9, filled: true);
        Assert.Equal(expected, BitBoard.FromParts(9, 9, tiles));
        Assert.Equal(expected, BitBoardMut.FromParts(9, 9, tiles).ToImmutable());
        Assert.Equal(expected.GetHashCode(), BitBoard.FromParts(9, 9, tiles).GetHashCode());

        foreach (var (width, height) in new[] { (-1, 8), (8, -1) })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BitBoard.FromParts(width, height, []));
            Assert.Throws<ArgumentOutOfRangeException>(() => BitBoardMut.FromParts(width, height, []));
        }
        foreach (var (width, height, parts) in new[]
        {
            (8, 8, Array.Empty<ulong>()), (8, 8, new ulong[2]), (0, 8, new ulong[1]),
            (int.MaxValue, int.MaxValue, Array.Empty<ulong>())
        })
        {
            Assert.Throws<ArgumentException>(() => BitBoard.FromParts(width, height, parts));
            Assert.Throws<ArgumentException>(() => BitBoardMut.FromParts(width, height, parts));
        }
    }

    [Theory]
    [MemberData(nameof(Dimensions))]
    public void ConstructionMutationAndInvertMatchCells(int width, int height)
    {
        var expected = new bool[height, width];
        var board = new BitBoardMut(width, height);
        AssertBoard(expected, board.ToImmutable());
        AssertBoard(expected, new BitBoard(width, height));
        var random = new Random(width * 31 + height);
        for (int i = 0; i < 200 && width > 0 && height > 0; i++)
        {
            int x = random.Next(width), y = random.Next(height);
            bool value = random.Next(2) == 1;
            board[x, y] = value;
            expected[y, x] = value;
        }
        AssertBoard(expected, board.ToImmutable());
        Assert.Equal(Coordinates(expected), board.Cells.ToArray());
        Assert.Equal((long)Coordinates(expected).Length, board.Count);

        var invert = board.ToImmutable().Inverted();
        var invertedMutable = board.Inverted();
        var invertInto = new BitBoardMut(width, height, filled: true);
        board.InvertInto(invertInto);
        Assert.Equal(invert, invertInto.ToImmutable());
        invertInto.Clear();
        board.ToImmutable().InvertInto(invertInto);
        Assert.Equal(invert, invertInto.ToImmutable());
        Assert.Equal(invert, invertedMutable.ToImmutable());
        invertedMutable.InvertInto(invertedMutable);
        AssertBoard(expected, invertedMutable.ToImmutable());
        board.Invert();
        var inverted = Map(expected, (x, y) => !expected[y, x]);
        AssertBoard(inverted, invert);
        AssertBoard(inverted, board.ToImmutable());
        board.Invert();
        AssertBoard(expected, board.ToImmutable());

        var filled = Map(expected, (_, _) => true);
        board.Fill();
        AssertBoard(filled, board.ToImmutable());
        AssertBoard(filled, new BitBoard(width, height, filled: true));
        AssertBoard(filled, new BitBoardMut(width, height, filled: true).ToImmutable());
        board.Clear();
        AssertBoard(new bool[height, width], board.ToImmutable());
    }

    [Theory]
    [MemberData(nameof(Dimensions))]
    public void TransformsMatchCellReference(int width, int height)
    {
        var cells = RandomCells(new Random(width * 13 + height), width, height);
        var board = FromCells(cells);
        var snapshot = board.ToImmutable();
        for (int turns = -5; turns <= 5; turns++)
        {
            var expected = cells;
            for (int turn = 0; turn < (turns & 3); turn++) expected = Rotate(expected);
            AssertBoard(expected, snapshot.Rotated(turns), $"BitBoard.Rotated({turns})");
            AssertBoard(expected, board.Rotated(turns).ToImmutable(), $"BitBoardMut.Rotated({turns})");
            var inPlace = board.Clone();
            var alias = inPlace;
            var borrowed = inPlace.Storage;
            inPlace.Rotate(turns);
            AssertBoard(expected, alias.ToImmutable(), $"BitBoardMut.Rotate({turns}) through alias");
            Assert.Equal(inPlace.Storage[0], borrowed[0]);
            var destination = new BitBoardMut(expected.GetLength(1), expected.GetLength(0), filled: true);
            snapshot.RotateInto(destination, turns);
            AssertBoard(expected, destination.ToImmutable(), $"BitBoard.RotateInto({turns})");
            destination.Fill();
            board.RotateInto(destination, turns);
            AssertBoard(expected, destination.ToImmutable(), $"BitBoardMut.RotateInto({turns})");
        }
        var reflected = Map(cells, (x, y) => cells[y, width - 1 - x]);
        AssertBoard(reflected, snapshot.FlippedH());
        AssertBoard(reflected, board.FlippedH().ToImmutable());
        AssertBoard(snapshot, snapshot.Rotated().Rotated().Rotated().Rotated());
        AssertBoard(snapshot, snapshot.FlippedH().FlippedH());
        var reflectedInPlace = board.Clone();
        reflectedInPlace.FlipH();
        AssertBoard(reflected, reflectedInPlace.ToImmutable(), "BitBoardMut.FlipH");
        var reflectedInto = new BitBoardMut(width, height, filled: true);
        snapshot.FlipHInto(reflectedInto);
        AssertBoard(reflected, reflectedInto.ToImmutable(), "BitBoard.FlipHInto");
        reflectedInto.Fill();
        board.FlipHInto(reflectedInto);
        AssertBoard(reflected, reflectedInto.ToImmutable(), "BitBoardMut.FlipHInto");
        var flippedV = Map(cells, (x, y) => cells[height - 1 - y, x]);
        AssertBoard(flippedV, snapshot.FlippedV());
        AssertBoard(flippedV, board.FlippedV().ToImmutable());
        var inPlaceV = board.Clone();
        inPlaceV.FlipV();
        AssertBoard(flippedV, inPlaceV.ToImmutable(), "BitBoardMut.FlipV");
        snapshot.FlipVInto(reflectedInto);
        AssertBoard(flippedV, reflectedInto.ToImmutable(), "BitBoard.FlipVInto");
        reflectedInto.Fill();
        board.FlipVInto(reflectedInto);
        AssertBoard(flippedV, reflectedInto.ToImmutable(), "BitBoardMut.FlipVInto");
        AssertBoard(snapshot, snapshot.FlippedV().FlippedV());
        AssertBoard(snapshot.Rotated(2), snapshot.FlippedH().FlippedV());
        AssertBoard(cells, board.ToImmutable());
    }

    [Fact]
    public void ReusedTransformDestinationsAndPooledScratchMatchIndependentResults()
    {
        // These sizes exercise both sides of the 1 KiB stack-scratch limit and partial edge tiles.
        foreach (var (width, height) in new[] { (88, 88), (89, 89), (129, 65), (257, 129) })
        {
            var original = FromCells(RandomCells(new Random(width), width, height)).ToImmutable();
            var mutable = original.ToMutable();
            for (int turn = 1; turn <= 8; turn++)
            {
                mutable.Rotate();
                AssertBoard(original.Rotated(turn), mutable.ToImmutable(), $"BitBoardMut.Rotate repeated {turn} times");
            }
            mutable.FlipHInto(mutable);
            AssertBoard(original.FlippedH(), mutable.ToImmutable(), "BitBoardMut.FlipHInto self");
            mutable.FlipH();
            AssertBoard(original, mutable.ToImmutable(), "BitBoardMut.FlipH restores original");
            mutable.FlipVInto(mutable);
            AssertBoard(original.FlippedV(), mutable.ToImmutable(), "BitBoardMut.FlipVInto self");
            mutable.FlipV();
            AssertBoard(original, mutable.ToImmutable(), "BitBoardMut.FlipV restores original");
            mutable.RotateInto(mutable, 2);
            AssertBoard(original.Rotated(2), mutable.ToImmutable(), "BitBoardMut.RotateInto self, two turns");
        }
    }

    [Fact]
    public void IntoRejectsWrongDimensionsWithoutChangingTheDestination()
    {
        var source = new BitBoardMut(9, 7, filled: true);
        var destination = new BitBoardMut(7, 9);
        var before = destination.ToImmutable();
        Assert.Throws<ArgumentException>(() => source.RotateInto(destination, 2));
        Assert.Throws<ArgumentException>(() => source.FlipHInto(destination));
        Assert.Throws<ArgumentException>(() => source.FlipVInto(destination));
        Assert.Throws<ArgumentException>(() => source.InvertInto(destination));
        Assert.Equal(before, destination.ToImmutable());
        Assert.Throws<ArgumentException>(() => source.RotateInto(source));
        Assert.Equal(63, source.Count);
        var square = new BitBoardMut(9, 9);
        square[1, 2] = true;
        square.RotateInto(square);
        Assert.True(square[2, 7]);
        Assert.Equal(1, square.Count);
        var immutable = source.ToImmutable();
        Assert.Same(immutable, immutable.Rotated(4));
    }

    [Fact]
    public void SetCellEnumeratorsAreIndependentAndDirectIterationDoesNotAllocate()
    {
        var cells = RandomCells(new Random(19), 19, 23);
        var board = FromCells(cells);
        var expected = Coordinates(cells);
        var actual = new List<(int, int)>();
        foreach (var cell in board.Cells) actual.Add(cell);
        Assert.Equal(expected, actual);
        var first = board.Cells.GetEnumerator();
        var second = first;
        while (first.MoveNext()) { }
        Assert.False(first.MoveNext());
        Assert.True(second.MoveNext());
        Assert.Equal(expected[0], second.Current);
        Assert.Empty(default(SetCellEnumerable));

        long checksum = 0;
        for (int repeat = 0; repeat < 100; repeat++) checksum += Sum(board.Cells);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int repeat = 0; repeat < 100; repeat++) checksum += Sum(board.Cells);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(checksum > 0);
        Assert.Equal(0, allocated);

        static long Sum(SetCellEnumerable cells)
        {
            long result = 0;
            foreach (var (x, y) in cells) result += x + y;
            return result;
        }
    }

    [Fact]
    public void OffsetOperationsMatchCellReferenceAcrossTileBoundaries()
    {
        var random = new Random(1729);
        int[] offsets = [-25, -17, -16, -15, -9, -8, -7, -1, 0, 1, 7, 8, 9, 15, 16, 17, 25, int.MinValue, int.MaxValue];
        foreach (int offsetY in offsets)
            foreach (int offsetX in offsets)
                CheckOffsets(RandomCells(random, 19, 23), RandomCells(random, 17, 13), offsetX, offsetY);

        // Random sizes cover every edge remainder and unaligned offset, including empty dimensions.
        for (int sample = 0; sample < 300; sample++)
            CheckOffsets(RandomCells(random, random.Next(35), random.Next(35)),
                RandomCells(random, random.Next(35), random.Next(35)), random.Next(-35, 36), random.Next(-35, 36));
    }

    [Fact]
    public void ContainmentTestsSetCellsRatherThanTheBoundingRectangle()
    {
        var container = new BitBoard(9, 9, filled: true);
        var source = new BitBoardMut(17, 17);
        source[8, 8] = true;
        Assert.True(container.Contains(source, -8, -8));
        Assert.True(container.Contains(source, 0, 0));
        Assert.False(container.Contains(source, 1, 0));
        Assert.False(container.Contains(source, -9, -8));
        Assert.True(container.Contains(new BitBoard(100, 100), int.MaxValue, int.MinValue));
        Assert.False(container.Overlaps(new BitBoard(100, 100), 0, 0));
        var hole = container.ToMutable();
        hole[0, 0] = false;
        Assert.False(hole.Contains(source, -8, -8));
        Assert.False(hole.Overlaps(source, -8, -8));
    }

    [Theory]
    [MemberData(nameof(Dimensions))]
    public void MatchingLayoutsMatchCellReference(int width, int height)
    {
        var random = new Random(width * 37 + height);
        CheckOffsets(RandomCells(random, width, height), RandomCells(random, width, height), 0, 0);
    }

    [Theory]
    [InlineData(0, int.MaxValue)]
    [InlineData(int.MaxValue, 0)]
    public void EmptyExtremeDimensionsDoNotTraverseNonexistentTiles(int width, int height)
    {
        var board = new BitBoardMut(width, height);
        board.Fill();
        board.Invert();
        Assert.Equal(0, board.Count);
        var rotated = board.Rotated();
        Assert.Equal(height, rotated.Width);
        Assert.Equal(width, rotated.Height);
        Assert.Equal(board.ToImmutable(), board.Rotated(0).ToImmutable());
        Assert.Equal(board.ToImmutable(), board.FlippedH().ToImmutable());
        Assert.True(board.Contains(rotated, int.MinValue, int.MaxValue));
        Assert.False(board.Overlaps(rotated, int.MaxValue, int.MinValue));
        board.Union(rotated, int.MaxValue, int.MinValue);
        board.Subtract(rotated, int.MinValue, int.MaxValue);
        Assert.Equal(0, board.Count);
        board.Rotate();
        Assert.Equal(height, board.Width);
        Assert.Equal(width, board.Height);
        board.FlipH();
        board.FlipV();
        Assert.Equal(0, board.Count);
    }

    [Fact]
    public void ShiftedSelfWritesReadASnapshotThroughAliases()
    {
        var cells = RandomCells(new Random(99), 25, 27);
        int[] offsets = [-17, -8, -7, -1, 0, 1, 7, 8, 17];
        foreach (int y in offsets)
            foreach (int x in offsets)
            {
                var union = FromCells(cells);
                var alias = union;
                union.Union(alias, x, y);
                AssertBoard(Combine(cells, cells, x, y, subtract: false), union.ToImmutable());
                var subtract = FromCells(cells);
                subtract.Subtract(subtract, x, y);
                AssertBoard(Combine(cells, cells, x, y, subtract: true), subtract.ToImmutable());
            }
    }

    [Fact]
    public void SnapshotsAndClonesOwnStorageButAssignmentsAndBorrowedSpansShareIt()
    {
        var mutable = new BitBoardMut(9, 9);
        mutable[8, 8] = true;
        var alias = mutable;
        var clone = mutable.Clone();
        var snapshot = mutable.ToImmutable();
        int hash = snapshot.GetHashCode();
        var borrowed = mutable.Storage;
        var immutableBorrow = snapshot.Storage;
        alias.Clear();
        Assert.Equal(0UL, borrowed[4]);
        Assert.Equal(1UL, immutableBorrow[4]);
        Assert.False(mutable[8, 8]);
        Assert.True(clone[8, 8]);
        Assert.True(snapshot[8, 8]);
        clone.Clear();
        Assert.True(snapshot[8, 8]);
        var copy = snapshot.ToMutable();
        copy.Fill();
        Assert.Equal(1, snapshot.Count);
        Assert.Equal(hash, snapshot.GetHashCode());
        Assert.Equal(1UL, immutableBorrow[4]);
        Assert.Equal(new BitTile(1), snapshot.GetTile(1, 1));
        Assert.Equal(default, mutable.GetTile(1, 1));

        var transformed = copy.Rotated(0);
        transformed.Clear();
        Assert.Equal(81, copy.Count);
        var reflected = copy.FlippedH();
        reflected.Clear();
        Assert.Equal(81, copy.Count);
    }

    [Fact]
    public void EqualityAndHashIncludeDimensionsAndAllTiles()
    {
        var original = FromCells(RandomCells(new Random(17), 17, 19)).ToImmutable();
        var equal = original.ToMutable().ToImmutable();
        Assert.True(original.Equals(equal));
        Assert.True(original.Equals((object)equal));
        Assert.Equal(original.GetHashCode(), equal.GetHashCode());
        Assert.Single(new HashSet<BitBoard> { original, equal });
        Assert.False(original.Equals(null));
        Assert.False(original.Equals((object)original.ToMutable()));
        var changed = original.ToMutable();
        changed[16, 18] = !changed[16, 18];
        Assert.NotEqual(original, changed.ToImmutable());
        Assert.NotEqual(new BitBoard(1, 2), new BitBoard(2, 1));
        Assert.NotEqual(new BitBoard(0, 0), new BitBoard(0, 1));
        Assert.NotEqual(new BitBoard(8, 8), new BitBoard(7, 8));
    }

    [Fact]
    public void InvalidDimensionsAndCoordinatesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BitBoard(-1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BitBoardMut(2, -1));
        Assert.Throws<OverflowException>(() => new BitBoard(int.MaxValue, int.MaxValue));
        Assert.Throws<OverflowException>(() => new BitBoardMut(int.MaxValue, int.MaxValue));
        var mutable = new BitBoardMut(9, 7);
        var board = mutable.ToImmutable();
        foreach (var (x, y) in new[] { (-1, 0), (9, 0), (0, -1), (0, 7), (int.MinValue, int.MaxValue) })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => board[x, y]);
            Assert.Throws<ArgumentOutOfRangeException>(() => mutable[x, y]);
            Assert.Throws<ArgumentOutOfRangeException>(() => mutable[x, y] = true);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => board.GetTile(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => mutable.GetTile(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => board.GetTile(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BitBoard(0, 0)[0, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BitBoard(0, 0).GetTile(0, 0));
        Assert.Empty(new BitBoard(int.MaxValue, 0).Cells);
        Assert.Empty(new BitBoard(0, int.MaxValue).Cells);
    }

    private static void CheckOffsets(bool[,] destination, bool[,] source, int offsetX, int offsetY)
    {
        var mutable = FromCells(destination);
        var sourceMut = FromCells(source);
        var board = mutable.ToImmutable();
        var sourceBoard = sourceMut.ToImmutable();
        var coordinates = Coordinates(source);
        bool overlaps = coordinates.Any(p => Get(destination, (long)p.X + offsetX, (long)p.Y + offsetY));
        bool contains = coordinates.All(p => Get(destination, (long)p.X + offsetX, (long)p.Y + offsetY));
        Assert.Equal(overlaps, board.Overlaps(sourceBoard, offsetX, offsetY));
        Assert.Equal(overlaps, board.Overlaps(sourceMut, offsetX, offsetY));
        Assert.Equal(overlaps, mutable.Overlaps(sourceBoard, offsetX, offsetY));
        Assert.Equal(overlaps, mutable.Overlaps(sourceMut, offsetX, offsetY));
        Assert.Equal(contains, board.Contains(sourceBoard, offsetX, offsetY));
        Assert.Equal(contains, board.Contains(sourceMut, offsetX, offsetY));
        Assert.Equal(contains, mutable.Contains(sourceBoard, offsetX, offsetY));
        Assert.Equal(contains, mutable.Contains(sourceMut, offsetX, offsetY));
        var union = mutable.Clone();
        union.Union(sourceBoard, offsetX, offsetY);
        AssertBoard(Combine(destination, source, offsetX, offsetY, subtract: false), union.ToImmutable());
        union = mutable.Clone();
        union.Union(sourceMut, offsetX, offsetY);
        AssertBoard(Combine(destination, source, offsetX, offsetY, subtract: false), union.ToImmutable());
        var subtract = mutable.Clone();
        subtract.Subtract(sourceBoard, offsetX, offsetY);
        AssertBoard(Combine(destination, source, offsetX, offsetY, subtract: true), subtract.ToImmutable());
        subtract = mutable.Clone();
        subtract.Subtract(sourceMut, offsetX, offsetY);
        AssertBoard(Combine(destination, source, offsetX, offsetY, subtract: true), subtract.ToImmutable());
        AssertBoard(source, sourceMut.ToImmutable());
    }

    private static bool[,] Combine(bool[,] destination, bool[,] source, int offsetX, int offsetY, bool subtract) =>
        Map(destination, (x, y) => subtract
            ? destination[y, x] && !Get(source, (long)x - offsetX, (long)y - offsetY)
            : destination[y, x] || Get(source, (long)x - offsetX, (long)y - offsetY));

    private static bool Get(bool[,] cells, long x, long y) =>
        x >= 0 && y >= 0 && x < cells.GetLength(1) && y < cells.GetLength(0) && cells[(int)y, (int)x];

    private static bool[,] RandomCells(Random random, int width, int height) =>
        Map(new bool[height, width], (_, _) => random.Next(4) == 0);

    private static bool[,] Map(bool[,] cells, Func<int, int, bool> value)
    {
        var result = new bool[cells.GetLength(0), cells.GetLength(1)];
        for (int y = 0; y < result.GetLength(0); y++)
            for (int x = 0; x < result.GetLength(1); x++) result[y, x] = value(x, y);
        return result;
    }

    private static bool[,] Rotate(bool[,] cells)
    {
        int width = cells.GetLength(1), height = cells.GetLength(0);
        var result = new bool[width, height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) result[width - 1 - x, y] = cells[y, x];
        return result;
    }

    private static BitBoardMut FromCells(bool[,] cells)
    {
        var board = new BitBoardMut(cells.GetLength(1), cells.GetLength(0));
        for (int y = 0; y < board.Height; y++)
            for (int x = 0; x < board.Width; x++) board[x, y] = cells[y, x];
        return board;
    }

    private static (int X, int Y)[] Coordinates(bool[,] cells)
    {
        var result = new List<(int X, int Y)>();
        for (int y = 0; y < cells.GetLength(0); y++)
            for (int x = 0; x < cells.GetLength(1); x++)
                if (cells[y, x]) result.Add((x, y));
        return result.ToArray();
    }

    private static void AssertBoard(BitBoard expected, BitBoard actual, string? operation = null,
        [CallerArgumentExpression(nameof(actual))] string? actualExpression = null) =>
        AssertBoard(Map(new bool[expected.Height, expected.Width], (x, y) => expected[x, y]), actual, operation ?? actualExpression);

    private static void AssertBoard(bool[,] expected, BitBoard actual, string? operation = null,
        [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        int width = expected.GetLength(1), height = expected.GetLength(0);
        string context = $"{operation ?? actualExpression}: expected {width}x{height}, actual {actual.Width}x{actual.Height}";
        Assert.True(width == actual.Width && height == actual.Height, $"{context}: dimensions differ.");
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                Assert.True(expected[y, x] == actual[x, y],
                    $"{context}: cell ({x}, {y}) expected {expected[y, x]}, actual {actual[x, y]}.");
        var coordinates = Coordinates(expected);
        Assert.True(coordinates.LongLength == actual.Count, $"{context}: expected {coordinates.Length} set cells, actual {actual.Count}.");
        Assert.True(coordinates.SequenceEqual(actual.Cells), $"{context}: set-cell enumeration differs.");
        Assert.True(((uint)width | ((ulong)(uint)height << 32)) == actual.Storage[0], $"{context}: packed dimensions differ.");
        int columns = (width + 7) / 8, rows = (height + 7) / 8;
        Assert.True(1 + columns * rows == actual.Storage.Length, $"{context}: storage length differs.");
        for (int tileY = 0; tileY < rows; tileY++)
            for (int tileX = 0; tileX < columns; tileX++)
            {
                ulong bits = 0;
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++)
                        if (Get(expected, tileX * 8 + x, tileY * 8 + y)) bits |= 1UL << (y * 8 + x);
                Assert.True(bits == actual.GetTile(tileX, tileY).Bits, $"{context}: tile ({tileX}, {tileY}) differs, including padding.");
                Assert.True(bits == actual.Storage[1 + tileY * columns + tileX], $"{context}: stored tile ({tileX}, {tileY}) differs.");
            }
    }
}
