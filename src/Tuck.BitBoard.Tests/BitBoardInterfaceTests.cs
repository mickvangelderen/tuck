namespace Tuck.BitBoard.Tests;

public class BitBoardInterfaceTests
{
    [Fact]
    public void OtherImplementationsCanSupplyTilesWithoutExposingStorage()
    {
        var source = new BitBoardMut(13, 11);
        source[1, 2] = source[12, 10] = true;
        var wrapped = new BoardWrapper(source);
        Assert.Equal(new[] { (1, 2), (12, 10) }, ((IBitBoard)wrapped).Cells.ToArray());
        foreach (IBitBoard board in new IBitBoard[] { source.Clone(), source.ToImmutable() })
        {
            Assert.True(board.ContentEquals(wrapped));
            Assert.True(board.Overlaps(wrapped));
            Assert.True(board.Contains(wrapped));
            Assert.False(board.Overlaps(wrapped, 1, 0));
            Assert.False(board.Contains(wrapped, 1, 0));
            Assert.False(board.ContentEquals(new BoardWrapper(new BitBoard(11, 13))));
            Assert.False(board.ContentEquals(null));
            Assert.Throws<ArgumentNullException>(() => board.Overlaps(null!));
            Assert.Throws<ArgumentNullException>(() => board.Contains(null!));
        }
        source.Clear();
        Assert.Empty(((IBitBoard)wrapped).Cells);
        Assert.True(new BitBoard(13, 11).ContentEquals(wrapped));
        foreach (var (width, height) in new[] { (0, 0), (0, 17), (17, 0) })
            Assert.True(new BitBoard(width, height).ContentEquals(new BoardWrapper(new BitBoard(width, height))));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(-1, -1)]
    [InlineData(8, 8)]
    public void WrappedAliasedSourcesAreSnapshottedBeforeMutation(int offsetX, int offsetY)
    {
        var original = new BitBoardMut(19, 19);
        for (int y = 0; y < original.Height; y++)
            for (int x = 0; x < original.Width; x++) original[x, y] = (x + 2 * y) % 3 == 0;
        foreach (bool subtract in new[] { false, true })
        {
            var board = original.Clone();
            var wrapped = new BoardWrapper(board);
            if (subtract) board.Subtract(wrapped, offsetX, offsetY);
            else board.Union(wrapped, offsetX, offsetY);
            for (int y = 0; y < board.Height; y++)
                for (int x = 0; x < board.Width; x++)
                {
                    int sx = x - offsetX, sy = y - offsetY;
                    bool bit = sx >= 0 && sy >= 0 && sx < original.Width && sy < original.Height && original[sx, sy];
                    Assert.Equal(subtract ? original[x, y] && !bit : original[x, y] || bit, board[x, y]);
                }
        }
    }

    [Fact]
    public void SharedQueriesPreserveMutableAccessAndImmutableSnapshotSemantics()
    {
        var mutable = new BitBoardMut(13, 11);
        mutable[1, 2] = mutable[12, 10] = true;
        IBitBoard live = mutable, snapshot = mutable.ToImmutable();
        IBitBoard point = new BitBoard(1, 1, filled: true);
        foreach (var board in new[] { live, snapshot })
        {
            Assert.Equal(13, board.Width);
            Assert.Equal(11, board.Height);
            Assert.Equal(2, board.Count);
            Assert.True(board[12, 10]);
            Assert.True(board.GetTile(1, 1)[4, 2]);
            Assert.Equal(new[] { (1, 2), (12, 10) }, board.Cells.ToArray());
            Assert.True(board.Overlaps(point, 1, 2));
            Assert.True(board.Contains(point, 12, 10));
            Assert.False(board.Overlaps(point));
            Assert.False(board.Contains(point));
        }
        mutable.Clear();
        Assert.Equal(0, live.Count);
        Assert.Equal(2, snapshot.Count);
        Assert.False(live.ContentEquals(snapshot));
    }

    [Fact]
    public void SharedTransformsKeepConcreteResultTypesAndIndependentMutableStorage()
    {
        var mutable = new BitBoardMut(13, 11);
        mutable[1, 2] = mutable[12, 10] = true;
        var snapshot = mutable.ToImmutable();
        foreach (IBitBoard board in new IBitBoard[] { mutable, snapshot })
        {
            var identity = board.Rotated(0);
            if (board is BitBoard) Assert.Same(board, identity);
            else Assert.NotSame(board, identity);
            IBitBoard[] results = [identity, board.Rotated(), board.Rotated(2), board.Rotated(3), board.FlippedH(), board.FlippedV(), board.Inverted()];
            BitBoard[] expected = [snapshot, snapshot.Rotated(), snapshot.Rotated(2), snapshot.Rotated(3), snapshot.FlippedH(), snapshot.FlippedV(), snapshot.Inverted()];
            for (int i = 0; i < results.Length; i++)
            {
                Assert.Equal(board.GetType(), results[i].GetType());
                Assert.True(results[i].ContentEquals(expected[i]));
                if (results[i] is BitBoardMut result) result.Clear();
                Assert.True(board.ContentEquals(snapshot));
            }

            var rotated = new BitBoardMut(board.Height, board.Width);
            board.RotateInto(rotated, 3);
            Assert.True(rotated.ContentEquals(snapshot.Rotated(3)));
            var destination = new BitBoardMut(board.Width, board.Height);
            board.FlipHInto(destination);
            Assert.True(destination.ContentEquals(snapshot.FlippedH()));
            board.FlipVInto(destination);
            Assert.True(destination.ContentEquals(snapshot.FlippedV()));
            board.InvertInto(destination);
            Assert.True(destination.ContentEquals(snapshot.Inverted()));
        }

        IBitBoard alias = mutable;
        alias.InvertInto(mutable);
        Assert.True(mutable.ContentEquals(snapshot.Inverted()));
    }

    private sealed class BoardWrapper(IBitBoard board) : IBitBoard
    {
        public int Width => board.Width;
        public int Height => board.Height;
        public long Count => board.Count;
        public bool this[int x, int y] => board[x, y];
        public BitTile GetTile(int tileX, int tileY) => board.GetTile(tileX, tileY);
        // Supply an ordinary iterator without access to the library's storage or concrete enumerable.
        public IEnumerable<(int X, int Y)> Cells
        {
            get
            {
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                        if (this[x, y]) yield return (x, y);
            }
        }
        public bool ContentEquals(IBitBoard? other) => board.ContentEquals(other);
        public bool Overlaps(IBitBoard source, int offsetX = 0, int offsetY = 0) => board.Overlaps(source, offsetX, offsetY);
        public bool Contains(IBitBoard source, int offsetX = 0, int offsetY = 0) => board.Contains(source, offsetX, offsetY);
        public IBitBoard Rotated(int quarterTurns = 1) => board.Rotated(quarterTurns);
        public IBitBoard FlippedH() => board.FlippedH();
        public IBitBoard FlippedV() => board.FlippedV();
        public IBitBoard Inverted() => board.Inverted();
        public void RotateInto(BitBoardMut destination, int quarterTurns = 1) => board.RotateInto(destination, quarterTurns);
        public void FlipHInto(BitBoardMut destination) => board.FlipHInto(destination);
        public void FlipVInto(BitBoardMut destination) => board.FlipVInto(destination);
        public void InvertInto(BitBoardMut destination) => board.InvertInto(destination);
    }
}
