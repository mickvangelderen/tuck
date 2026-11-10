using System;
using System.Collections.Generic;

namespace Tuck.Mod;

/// <summary>Relative placements that overlap the outgoing footprint; contains no live game state.</summary>
internal static class FilterSwapCandidates
{
    private const int CacheLimit = 128;
    private static readonly Dictionary<(ShapeKey Incoming, ShapeKey Outgoing, int Orientation, bool Flipped),
        GridShape[][]> Cache = new();

    internal static void Clear() => Cache.Clear();

    internal static GridShape[][] Get(GridShape incoming, GridShape outgoing)
    {
        var key = (new ShapeKey(incoming), new ShapeKey(outgoing), outgoing.orientation, outgoing.flipped);
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var outgoingCells = Cells(new GridShapeBuilder(outgoing).SetPosition(0, 0));
        var candidates = new List<(GridShape Shape, int Turn)>();
        for (int turn = 0; turn < 4; turn++)
        {
            GridShape rotated = new GridShapeBuilder(incoming)
                .SetTransform(0, 0, outgoing.flipped, outgoing.orientation + turn);
            var incomingCells = Cells(rotated);
            var offsets = new HashSet<(int X, int Y)> { (0, 0) };
            // Every overlapping placement aligns at least one occupied cell from each shape.
            foreach (var a in outgoingCells)
                foreach (var b in incomingCells)
                    offsets.Add((a.X - b.X, a.Y - b.Y));
            foreach (var offset in offsets)
                candidates.Add((new GridShapeBuilder(rotated).SetPosition(offset.X, offset.Y), turn));
        }
        candidates.Sort((a, b) =>
        {
            int order = (Math.Abs(a.Shape.minX) + Math.Abs(a.Shape.minY))
                .CompareTo(Math.Abs(b.Shape.minX) + Math.Abs(b.Shape.minY));
            if (order == 0) order = a.Turn.CompareTo(b.Turn);
            if (order == 0) order = a.Shape.minY.CompareTo(b.Shape.minY);
            if (order == 0) order = a.Shape.minX.CompareTo(b.Shape.minX);
            return order;
        });

        // Equal oriented grids share collision work, but retain transforms for orientation-sensitive rules.
        // Include every cell value and the bounds: the game's intersection checks also compare values > 1.
        var footprints = new Dictionary<ShapeKey, List<GridShape>>();
        var groups = new List<List<GridShape>>();
        foreach (var candidate in candidates)
        {
            var footprintKey = new ShapeKey(candidate.Shape, placed: true);
            if (!footprints.TryGetValue(footprintKey, out var group))
            {
                group = new List<GridShape>();
                footprints.Add(footprintKey, group);
                groups.Add(group);
            }
            group.Add(candidate.Shape);
        }
        var result = groups.ConvertAll(group => group.ToArray()).ToArray();
        if (Cache.Count >= CacheLimit) Cache.Clear();
        // Lookup keys borrow live shapes without allocating; stored keys own stable snapshots.
        Cache.Add((new ShapeKey(incoming.Clone()), new ShapeKey(outgoing.Clone()),
            outgoing.orientation, outgoing.flipped), result);
        return result;
    }

    private static List<(int X, int Y)> Cells(GridShape shape)
    {
        var cells = new List<(int X, int Y)>();
        for (int y = shape.minY; y <= shape.maxY; y++)
            for (int x = shape.minX; x <= shape.maxX; x++)
                if (shape.Get(x, y) == 1) cells.Add((x, y));
        return cells;
    }

    private readonly struct ShapeKey : IEquatable<ShapeKey>
    {
        private readonly GridShape _shape;
        private readonly bool _placed;

        // Local data identifies a shape pair; placed data identifies an equivalent candidate footprint.
        internal ShapeKey(GridShape shape, bool placed = false) { _shape = shape; _placed = placed; }
        private int X => _placed ? _shape.minX : 0;
        private int Y => _placed ? _shape.minY : 0;
        private int Width => _placed ? _shape.globalWidth : _shape.width;
        private int Height => _placed ? _shape.globalHeight : _shape.height;
        private byte Cell(int x, int y) => _placed ? _shape.Get(X + x, Y + y) : _shape.GetLocal(x, y);

        public bool Equals(ShapeKey other)
        {
            if (_placed != other._placed || X != other.X || Y != other.Y ||
                Width != other.Width || Height != other.Height ||
                _shape.GetOutsideBounds() != other._shape.GetOutsideBounds()) return false;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (Cell(x, y) != other.Cell(x, y)) return false;
            return true;
        }

        public override bool Equals(object? obj) => obj is ShapeKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(_placed);
            hash.Add(X);
            hash.Add(Y);
            hash.Add(Width);
            hash.Add(Height);
            hash.Add(_shape.GetOutsideBounds());
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    hash.Add(Cell(x, y));
            return hash.ToHashCode();
        }
    }
}
