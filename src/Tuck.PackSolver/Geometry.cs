using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Board = global::Tuck.BitBoard.BitBoard;

namespace Tuck.PackSolver;

public abstract class Placement
{
    private Placement() { }
    public sealed class Slot : Placement
    {
        public override bool Equals(object? obj) => obj is Slot;
        public override int GetHashCode() => 0;
    }
    /// <summary>The minimum transformed BASE-shape corner, not the effective bounding box.</summary>
    public sealed class Grid : Placement
    {
        public int X { get; }
        public int Y { get; }
        public int Orientation { get; }
        public bool Flipped { get; }
        public Grid(int x = 0, int y = 0, int orientation = 0, bool flipped = false)
        {
            if (orientation < 0 || orientation > 3) throw new ArgumentOutOfRangeException(nameof(orientation));
            X = x; Y = y; Orientation = orientation; Flipped = flipped;
        }
        public override bool Equals(object? obj) => obj is Grid other && X == other.X && Y == other.Y
            && Orientation == other.Orientation && Flipped == other.Flipped;
        public override int GetHashCode() => HashCode.Combine(X, Y, Orientation, Flipped);
    }
}

public sealed class ShapeState
{
    private Board?[]? _occupied;
    private Board? _usable;
    public int Width { get; }
    public int Height { get; }
    public ReadOnlyCollection<byte> Cells { get; }
    public byte OutsideBounds { get; }
    internal int Area { get; }
    internal int UsableArea { get; }
    internal bool IsBinary { get; } = true;
    public ShapeState(int width, int height, IEnumerable<byte> cells, byte outsideBounds = 0)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));
        var copy = cells.ToArray();
        if (copy.Length != checked(width * height)) throw new ArgumentException("Shape dimensions do not match cells.");
        Width = width; Height = height; Cells = Array.AsReadOnly(copy); OutsideBounds = outsideBounds;
        foreach (byte cell in copy)
        {
            if (cell == 1) Area++;
            else if (cell == 0) UsableArea++;
            else IsBinary = false;
        }
    }
    public byte GetLocal(int x, int y) => x < 0 || y < 0 || x >= Width || y >= Height ? OutsideBounds : Cells[y * Width + x];
    public static ShapeState Rectangle(int width, int height, byte cell = 1, byte outside = 0) =>
        new(width, height, Enumerable.Repeat(cell, checked(width * height)), outside);

    // Derived masks belong to these immutable facts, so branches may share them even when another
    // item's effective shape changes. Publish only complete boards for concurrent solver invocations.
    internal Board Occupied(int orientation = 0, bool flipped = false)
    {
        var masks = Volatile.Read(ref _occupied);
        if (masks == null)
        {
            var created = new Board?[8];
            masks = Interlocked.CompareExchange(ref _occupied, created, null) ?? created;
        }
        int index = orientation + (flipped ? 4 : 0);
        var mask = Volatile.Read(ref masks[index]);
        if (mask != null) return mask;
        mask = orientation != 0 ? Occupied(0, flipped).Rotated(orientation)
            : flipped ? Occupied().FlippedH() : CreateMask(1);
        return Interlocked.CompareExchange(ref masks[index], mask, null) ?? mask;
    }

    internal Board Usable
    {
        get
        {
            var mask = Volatile.Read(ref _usable);
            if (mask != null) return mask;
            mask = CreateMask(0);
            return Interlocked.CompareExchange(ref _usable, mask, null) ?? mask;
        }
    }

    private Board CreateMask(byte included)
    {
        int columns = (Width - 1) / 8 + 1, length = checked(columns * ((Height - 1) / 8 + 1));
        Span<ulong> tiles = length <= 128 ? stackalloc ulong[length] : new ulong[length];
        tiles.Clear();
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (Cells[y * Width + x] == included)
                    tiles[(y / 8) * columns + x / 8] |= 1UL << ((y & 7) * 8 + (x & 7));
        return Board.FromParts(Width, Height, tiles);
    }
}

/// <summary>A transient geometry view. Its transform uses EFFECTIVE bounding-box coordinates.</summary>
public sealed class PlacedShape
{
    public ShapeState Shape { get; }
    public Placement.Grid Transform { get; }
    public int Width => Shape.Width;
    public int Height => Shape.Height;
    public int GlobalWidth => Transform.Orientation % 2 == 0 ? Width : Height;
    public int GlobalHeight => Transform.Orientation % 2 == 0 ? Height : Width;
    internal Board Occupied => Shape.Occupied(Transform.Orientation, Transform.Flipped);
    public PlacedShape(ShapeState shape, Placement.Grid transform) { Shape = shape; Transform = transform; }
    public byte Get(int x, int y)
    {
        x -= Transform.X; y -= Transform.Y;
        var local = Transform.Orientation switch
        {
            0 => (x, y),
            1 => (Width - 1 - y, x),
            2 => (Width - 1 - x, Height - 1 - y),
            _ => (y, Height - 1 - x)
        };
        return Shape.GetLocal(Transform.Flipped ? Width - 1 - local.Item1 : local.Item1, local.Item2);
    }
    public IEnumerable<(int X, int Y)> Positions(byte included)
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (Shape.Cells[y * Width + x] == included) yield return Forward(x, y);
    }
    public (int X, int Y) Forward(int x, int y)
    {
        if (Transform.Flipped) x = Width - 1 - x;
        var p = Transform.Orientation switch
        {
            0 => (x, y),
            1 => (y, Width - 1 - x),
            2 => (Width - 1 - x, Height - 1 - y),
            _ => (Height - 1 - y, x)
        };
        return (p.Item1 + Transform.X, p.Item2 + Transform.Y);
    }
    internal bool Intersects(PlacedShape b)
    {
        int left = Math.Max(Transform.X, b.Transform.X), top = Math.Max(Transform.Y, b.Transform.Y);
        int right = Math.Min(Transform.X + GlobalWidth, b.Transform.X + b.GlobalWidth);
        int bottom = Math.Min(Transform.Y + GlobalHeight, b.Transform.Y + b.GlobalHeight);
        if (left >= right || top >= bottom) return Shape.OutsideBounds > 0 && Shape.OutsideBounds == b.Shape.OutsideBounds;
        if (Shape.IsBinary && b.Shape.IsBinary)
            return Occupied.Overlaps(b.Occupied, b.Transform.X - Transform.X, b.Transform.Y - Transform.Y);
        for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
            { byte cell = Get(x, y); if (cell > 0 && cell == b.Get(x, y)) return true; }
        return false;
    }

    internal bool Fits(ShapeState grid, bool checkBounds = true)
    {
        if (checkBounds || grid.OutsideBounds != 0)
            return grid.Usable.Contains(Occupied, Transform.X, Transform.Y);
        // A*'s heuristic permits outside cells when their recorded value is zero. Legality does not.
        foreach (var (x, y) in Positions(1))
            if (grid.GetLocal(x, y) != 0) return false;
        return true;
    }
}
