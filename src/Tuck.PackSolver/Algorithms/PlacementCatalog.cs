using System;
using System.Collections.Generic;
using System.Threading;

namespace Tuck.PackSolver.Algorithms;

internal sealed class ItemPlacement
{
    public PackingItem Item { get; }
    public PackingContainer Container { get; }
    public PackingOrientation Orientation { get; }
    public int X { get; }
    public int Y { get; }
    public int CellCost => Container.IsSlot ? 1 : Item.Area;
    internal int ItemIndex { get; }
    internal int ContainerIndex { get; }
    internal ulong[] Words { get; }

    internal ItemPlacement(PackProblem problem, int item, int container, PackingOrientation orientation,
        int x, int y, ulong[] words)
    {
        Item = problem.Items[item];
        Container = problem.Containers[container];
        ItemIndex = item;
        ContainerIndex = container;
        Orientation = orientation;
        X = x;
        Y = y;
        Words = words;
    }
}

/// <summary>All admitted placements that fit around fixed obstacles, before movable items are added.</summary>
internal sealed class PlacementCatalog
{
    public PackProblem Problem { get; }
    public List<ItemPlacement>[] ByItem { get; }
    public List<ItemPlacement>[] SlotsByItem { get; }
    // Minimum occupancy cost across eligible destinations. Pooling slot tokens and cells is an upper-bound relaxation.
    public int[] MinimumCellCosts { get; }

    public PlacementCatalog(PackProblem problem, CancellationToken cancellationToken)
    {
        Problem = problem;
        ByItem = new List<ItemPlacement>[problem.Items.Count];
        SlotsByItem = new List<ItemPlacement>[problem.Items.Count];
        MinimumCellCosts = new int[problem.Items.Count];
        var blocked = new ulong[problem.Containers.Count][];
        for (int c = 0; c < blocked.Length; c++) blocked[c] = CellBits.BlockedCells(problem.Containers[c]);
        for (int i = 0; i < problem.Items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = problem.Items[i];
            var placements = ByItem[i] = new List<ItemPlacement>();
            var slots = SlotsByItem[i] = new List<ItemPlacement>();
            MinimumCellCosts[i] = item.Area;
            for (int c = 0; c < problem.Containers.Count; c++)
            {
                var container = problem.Containers[c];
                if (!item.CanEnter(container.Id) || container.OwnerItemId == item.Id) continue;
                if (container.IsSlot)
                {
                    if (container.FreeCellCount > 0)
                    {
                        // The caller supplies legal orientations; a flexible slot imposes no geometric size bound.
                        var placement = new ItemPlacement(problem, i, c, item.Orientations[0], 0, 0, new[] { 1UL });
                        placements.Add(placement);
                        slots.Add(placement);
                        MinimumCellCosts[i] = 1;
                    }
                    continue;
                }
                if (item.Area > container.FreeCellCount) continue;
                int width = container.UsableCells.Width;
                int height = container.UsableCells.Height;
                var words = CellBits.Create(width, height);
                foreach (var orientation in item.Orientations)
                    for (int y = 0; y <= height - orientation.Shape.Height; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        for (int x = 0; x <= width - orientation.Shape.Width; x++)
                        {
                            Array.Clear(words, 0, words.Length);
                            foreach (var cell in orientation.Shape.Cells)
                            {
                                int index = (y + cell.Y) * width + x + cell.X;
                                CellBits.Set(words, index);
                            }
                            if (CellBits.Overlap(words, blocked[c])) continue;
                            placements.Add(new ItemPlacement(problem, i, c, orientation, x, y, (ulong[])words.Clone()));
                        }
                    }
            }
            // Favor small destinations and compact top-left layouts; the shuffle search explores alternatives.
            placements.Sort((a, b) =>
            {
                int order = a.Container.FreeCellCount.CompareTo(b.Container.FreeCellCount);
                if (order == 0) order = a.ContainerIndex.CompareTo(b.ContainerIndex);
                if (order == 0) order = a.Y.CompareTo(b.Y);
                if (order == 0) order = a.X.CompareTo(b.X);
                if (order == 0) order = a.Orientation.QuarterTurns.CompareTo(b.Orientation.QuarterTurns);
                if (order == 0) order = a.Orientation.Flipped.CompareTo(b.Orientation.Flipped);
                return order;
            });
        }
    }
}

internal sealed class PackingLayout
{
    private readonly ulong[][] _occupied;
    private readonly Dictionary<string, string> _parents = new(StringComparer.Ordinal);
    public int[] FreeCells { get; }

    public PackingLayout(PackProblem problem)
    {
        _occupied = new ulong[problem.Containers.Count][];
        FreeCells = new int[problem.Containers.Count];
        for (int c = 0; c < problem.Containers.Count; c++)
        {
            var container = problem.Containers[c];
            _occupied[c] = CellBits.BlockedCells(container);
            FreeCells[c] = container.FreeCellCount;
        }
    }

    public bool Fits(ItemPlacement placement)
    {
        if (CellBits.Overlap(_occupied[placement.ContainerIndex], placement.Words)) return false;
        string? owner = placement.Container.OwnerItemId;
        while (owner != null)
        {
            if (owner == placement.Item.Id) return false;
            owner = _parents.TryGetValue(owner, out string? parent) ? parent : null;
        }
        return true;
    }

    public void Add(ItemPlacement placement)
    {
        var occupied = _occupied[placement.ContainerIndex];
        for (int w = 0; w < occupied.Length; w++) occupied[w] |= placement.Words[w];
        FreeCells[placement.ContainerIndex] -= placement.CellCost;
        if (placement.Container.OwnerItemId != null)
            _parents.Add(placement.Item.Id, placement.Container.OwnerItemId);
    }

    public void Remove(ItemPlacement placement)
    {
        var occupied = _occupied[placement.ContainerIndex];
        for (int w = 0; w < occupied.Length; w++) occupied[w] &= ~placement.Words[w];
        FreeCells[placement.ContainerIndex] += placement.CellCost;
        _parents.Remove(placement.Item.Id);
    }

    public int BoundaryContacts(ItemPlacement placement)
    {
        if (placement.Container.IsSlot) return 4;
        int contacts = 0;
        foreach (var cell in placement.Orientation.Shape.Cells)
        {
            int x = placement.X + cell.X;
            int y = placement.Y + cell.Y;
            if (Blocked(placement, x - 1, y)) contacts++;
            if (Blocked(placement, x + 1, y)) contacts++;
            if (Blocked(placement, x, y - 1)) contacts++;
            if (Blocked(placement, x, y + 1)) contacts++;
        }
        return contacts;
    }

    private bool Blocked(ItemPlacement placement, int x, int y)
    {
        var mask = placement.Container.UsableCells;
        if (x < 0 || x >= mask.Width || y < 0 || y >= mask.Height) return true;
        int index = y * mask.Width + x;
        return CellBits.Contains(_occupied[placement.ContainerIndex], index);
    }
}
