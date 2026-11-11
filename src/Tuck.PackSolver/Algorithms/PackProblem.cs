using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using global::Tuck.BitBoard;
using Board = global::Tuck.BitBoard.BitBoard;

namespace Tuck.PackSolver.Algorithms;

internal readonly struct GridCell : IEquatable<GridCell>
{
    public int X { get; }
    public int Y { get; }

    public GridCell(int x, int y) { X = x; Y = y; }
    public bool Equals(GridCell other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is GridCell other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
}

/// <summary>An immutable cell mask. False cells, including holes, are not part of the shape.</summary>
internal sealed class CellMask
{
    internal Board Board { get; }
    public int Width => Board.Width;
    public int Height => Board.Height;
    public ReadOnlyCollection<GridCell> Cells { get; }
    public int Area => Cells.Count;

    public CellMask(int width, int height, IEnumerable<GridCell> cells)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (cells == null) throw new ArgumentNullException(nameof(cells));
        _ = checked(width * height);
        var board = new BitBoardMut(width, height);
        var copy = new List<GridCell>();
        foreach (var cell in cells)
        {
            if (cell.X < 0 || cell.X >= width || cell.Y < 0 || cell.Y >= height)
                throw new ArgumentException("A cell lies outside the mask.", nameof(cells));
            if (board[cell.X, cell.Y])
                throw new ArgumentException("Duplicate cell.", nameof(cells));
            board[cell.X, cell.Y] = true;
            copy.Add(cell);
        }
        copy.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        Cells = copy.AsReadOnly();
        Board = board.ToImmutable();
    }

    internal CellMask(Board board)
    {
        Board = board;
        var cells = new List<GridCell>();
        foreach (var (x, y) in board.Cells) cells.Add(new GridCell(x, y));
        Cells = cells.AsReadOnly();
    }

    public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height && Board[x, y];

    public static CellMask Rectangle(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        _ = checked(width * height);
        return new CellMask(new Board(width, height, filled: true));
    }
}

/// <summary>A permitted orientation, with the transform needed to reproduce it in the game.</summary>
internal sealed class PackingOrientation
{
    public CellMask Shape { get; }
    public int QuarterTurns { get; }
    public bool Flipped { get; }

    public PackingOrientation(CellMask shape, int quarterTurns = 0, bool flipped = false)
    {
        Shape = shape ?? throw new ArgumentNullException(nameof(shape));
        if (shape.Area == 0) throw new ArgumentException("An item must occupy cells.", nameof(shape));
        if (quarterTurns < 0 || quarterTurns > 3) throw new ArgumentOutOfRangeException(nameof(quarterTurns));
        QuarterTurns = quarterTurns;
        Flipped = flipped;
    }

    public static IReadOnlyList<PackingOrientation> FromShape(CellMask shape,
        bool allowRotation = true, bool allowReflection = false)
    {
        if (shape == null) throw new ArgumentNullException(nameof(shape));
        var result = new List<PackingOrientation>();
        for (int mirror = 0; mirror < (allowReflection ? 2 : 1); mirror++)
        {
            // Matches GridShape.ForwardTransform: mirror the original X axis, then rotate.
            var source = mirror == 0 ? shape.Board : shape.Board.FlippedH();
            for (int rotation = 0; rotation < (allowRotation ? 4 : 1); rotation++)
            {
                var variant = source.Rotated(rotation);
                if (result.Any(o => o.Shape.Board.Equals(variant))) continue;
                result.Add(new PackingOrientation(new CellMask(variant), rotation, mirror != 0));
            }
        }
        return result.AsReadOnly();
    }
}

/// <summary>
/// One indivisible item or fixed stack. Value excludes separately modeled contents.
/// Include immovable contents not modeled as items in their owning item's value.
/// Admission and legal orientations must be captured by the caller, not guessed from geometry.
/// </summary>
internal sealed class PackingItem
{
    private readonly HashSet<string>? _allowedContainers;
    public string Id { get; }
    public long Value { get; }
    public int Area { get; }
    public ReadOnlyCollection<PackingOrientation> Orientations { get; }
    public bool CanUseHand { get; }
    public bool Required { get; }

    public PackingItem(string id, long value, IEnumerable<PackingOrientation> orientations,
        bool canUseHand = true, bool required = false, IEnumerable<string>? allowedContainerIds = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("An item needs an ID.", nameof(id));
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        var variants = (orientations ?? throw new ArgumentNullException(nameof(orientations))).ToArray();
        if (variants.Length == 0 || variants.Any(o => o == null))
            throw new ArgumentException("An item needs at least one orientation.", nameof(orientations));
        if (variants.Any(o => o.Shape.Area != variants[0].Shape.Area))
            throw new ArgumentException("Orientations must have equal area.", nameof(orientations));
        Id = id;
        Value = value;
        Area = variants[0].Shape.Area;
        Orientations = Array.AsReadOnly(variants);
        CanUseHand = canUseHand;
        Required = required;
        _allowedContainers = allowedContainerIds == null ? null
            : new HashSet<string>(allowedContainerIds, StringComparer.Ordinal);
    }

    public bool CanEnter(string containerId) => _allowedContainers == null || _allowedContainers.Contains(containerId);

    internal PackingItem AsRequired() => Required ? this
        : new PackingItem(Id, Value, Orientations, CanUseHand, true, _allowedContainers);
}

/// <summary>Storage geometry or a flexible single-item slot; optionally available only when its owner is retained.</summary>
internal sealed class PackingContainer
{
    public string Id { get; }
    /// <summary>Null for storage that is already carried and fixed. Otherwise references a PackingItem.Id.</summary>
    public string? OwnerItemId { get; }
    /// <summary>A slot accepts one admitted item regardless of its area. Its 1-cell mask is an occupancy token.</summary>
    public bool IsSlot { get; }
    public CellMask UsableCells { get; }
    public CellMask OccupiedCells { get; }
    public int FreeCellCount => UsableCells.Area - OccupiedCells.Area;

    public PackingContainer(string id, CellMask usableCells, IEnumerable<GridCell>? occupiedCells = null,
        string? ownerItemId = null) : this(id, usableCells, occupiedCells, ownerItemId, false) { }

    private PackingContainer(string id, CellMask usableCells, IEnumerable<GridCell>? occupiedCells,
        string? ownerItemId, bool isSlot)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A container needs an ID.", nameof(id));
        Id = id;
        if (ownerItemId != null && string.IsNullOrWhiteSpace(ownerItemId))
            throw new ArgumentException("An owner needs an item ID.", nameof(ownerItemId));
        OwnerItemId = ownerItemId;
        IsSlot = isSlot;
        UsableCells = usableCells ?? throw new ArgumentNullException(nameof(usableCells));
        OccupiedCells = new CellMask(usableCells.Width, usableCells.Height, occupiedCells ?? Array.Empty<GridCell>());
        if (!UsableCells.Board.Contains(OccupiedCells.Board))
            throw new ArgumentException("Occupied cells must be usable cells.", nameof(occupiedCells));
    }

    internal BitBoardMut BlockedCells()
    {
        var blocked = UsableCells.Board.ToMutable();
        blocked.Invert();
        blocked.Union(OccupiedCells.Board);
        return blocked;
    }

    /// <summary>Use for toolbox sockets and named carry slots. Capture admission rules on PackingItem.</summary>
    public static PackingContainer Slot(string id, string? ownerItemId = null, bool occupied = false) =>
        new(id, CellMask.Rectangle(1, 1), occupied ? new[] { new GridCell(0, 0) } : null, ownerItemId, true);
}

/// <summary>
/// Include movable contents and container shells once each as items. Owned storage is usable only if its
/// owner is retained in a hand or in storage ultimately connected to a fixed root; nesting cycles are forbidden.
/// Occupied cells represent immovable contents, whose value belongs to the owner or FixedValue, counted once.
/// Geometry/admission must remain stable while rearranging contents. Stack splitting and execution are external.
/// </summary>
internal sealed class PackProblem
{
    public ReadOnlyCollection<PackingItem> Items { get; }
    public ReadOnlyCollection<PackingContainer> Containers { get; }
    public int AvailableHands { get; }
    public int FreeCellCount { get; }
    public bool HasOwnedContainers { get; }
    /// <summary>Value guaranteed retained, excluding every modeled item and its separately modeled contents.</summary>
    public long FixedValue { get; }

    public PackProblem(IEnumerable<PackingItem> items, IEnumerable<PackingContainer> containers, int availableHands,
        long fixedValue = 0)
    {
        if (availableHands < 0 || availableHands > 2) throw new ArgumentOutOfRangeException(nameof(availableHands));
        if (fixedValue < 0) throw new ArgumentOutOfRangeException(nameof(fixedValue));
        Items = Array.AsReadOnly((items ?? throw new ArgumentNullException(nameof(items))).ToArray());
        Containers = Array.AsReadOnly((containers ?? throw new ArgumentNullException(nameof(containers))).ToArray());
        if (Items.Any(i => i == null) || Containers.Any(c => c == null))
            throw new ArgumentException("Snapshot entries cannot be null.");
        if (Items.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != Items.Count
            || Containers.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != Containers.Count)
            throw new ArgumentException("Item IDs and container IDs must each be unique.");
        var itemIds = new HashSet<string>(Items.Select(i => i.Id), StringComparer.Ordinal);
        if (Containers.Any(c => c.OwnerItemId != null && !itemIds.Contains(c.OwnerItemId)))
            throw new ArgumentException("Each storage owner must be a modeled item.", nameof(containers));
        HasOwnedContainers = Containers.Any(c => c.OwnerItemId != null);
        AvailableHands = availableHands;
        FixedValue = fixedValue;
        // Reject inputs whose objective or capacity cannot be represented, instead of wrapping scores.
        FreeCellCount = Containers.Sum(c => c.FreeCellCount);
        _ = checked(fixedValue + Items.Sum(i => i.Value));
    }
}
