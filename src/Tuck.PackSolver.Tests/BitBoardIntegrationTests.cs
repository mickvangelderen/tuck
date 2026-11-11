using global::Tuck.PackSolver;
using A = global::Tuck.PackSolver.Algorithms;
using static Tuck.PackSolver.Tests.PackSolverTests;

namespace Tuck.PackSolver.Tests;

public class BitBoardIntegrationTests
{
    [Theory]
    [InlineData(3, 5)]
    [InlineData(9, 17)]
    [InlineData(1290, 9)]
    public void CompiledMasksPreserveLabelsDimensionsAndAllOrientations(int width, int height)
    {
        var bytes = Enumerable.Range(0, width * height).Select(i => (byte)(i % 3)).ToArray();
        var shape = new ShapeState(width, height, bytes, 2);
        Assert.False(shape.IsBinary);
        Assert.Equal(bytes.Count(b => b == 1), shape.Area);
        Assert.Equal(bytes.Count(b => b == 0), shape.UsableArea);
        Assert.Equal(shape.UsableArea, shape.Usable.Count);
        for (int flip = 0; flip < 2; flip++)
            for (int turn = 0; turn < 4; turn++)
            {
                var placed = new PlacedShape(shape, new(orientation: turn, flipped: flip != 0));
                var mask = shape.Occupied(turn, flip != 0);
                Assert.Equal(placed.GlobalWidth, mask.Width);
                Assert.Equal(placed.GlobalHeight, mask.Height);
                Assert.Equal(shape.Area, mask.Count);
                Assert.Equal(placed.Positions(1).OrderBy(p => p.Y).ThenBy(p => p.X), mask.Cells);
                Assert.Same(mask, shape.Occupied(turn, flip != 0));
            }
        Assert.Equal(bytes, shape.Cells);
    }

    [Fact]
    public void SharedFactsPublishCompleteMasksToConcurrentSolvers()
    {
        var shape = new ShapeState(9, 13, Enumerable.Range(0, 117).Select(i => (byte)(i % 2)));
        Parallel.For(0, 64, i =>
        {
            var mask = shape.Occupied(i % 4, i % 8 >= 4);
            Assert.Equal(58, mask.Count);
            Assert.Equal(59, shape.Usable.Count);
            var expected = new PlacedShape(shape, new(orientation: i % 4, flipped: i % 8 >= 4));
            Assert.Equal(expected.Positions(1).OrderBy(p => p.Y).ThenBy(p => p.X), mask.Cells);
        });
    }

    [Fact]
    public void FitsAndCollisionsMatchByteGeometryIncludingOutsideValues()
    {
        var random = new Random(67153);
        for (int example = 0; example < 250; example++)
        {
            var a = Placed(RandomShape(random, example % 2 == 0 ? 2 : 4, 17));
            var b = Placed(RandomShape(random, example % 2 == 0 ? 2 : 4, 17));
            var grid = RandomShape(random, 3, 18);
            Assert.Equal(ReferenceIntersects(a, b), a.Intersects(b));
            Assert.Equal(a.Positions(1).All(p => p.X >= 0 && p.Y >= 0 && p.X < grid.Width && p.Y < grid.Height
                && grid.GetLocal(p.X, p.Y) == 0), a.Fits(grid));
            Assert.Equal(a.Positions(1).All(p => grid.GetLocal(p.X, p.Y) == 0), a.Fits(grid, checkBounds: false));
        }

        PlacedShape Placed(ShapeState shape) => new(shape,
            new(random.Next(-10, 11), random.Next(-10, 11), random.Next(4), random.Next(2) != 0));
    }

    private static ShapeState RandomShape(Random random, int labels, int maxSize)
    {
        int width = random.Next(1, maxSize + 1), height = random.Next(1, maxSize + 1);
        return new(width, height, Enumerable.Range(0, width * height).Select(_ => (byte)random.Next(labels)),
            (byte)random.Next(3));
    }

    // The game compares equal positive labels inside the bounding intersection, and outside labels
    // only for disjoint bounds. Keep this cell oracle independent of the compiled mask path.
    private static bool ReferenceIntersects(PlacedShape a, PlacedShape b)
    {
        int left = Math.Max(a.Transform.X, b.Transform.X), top = Math.Max(a.Transform.Y, b.Transform.Y);
        int right = Math.Min(a.Transform.X + a.GlobalWidth, b.Transform.X + b.GlobalWidth);
        int bottom = Math.Min(a.Transform.Y + a.GlobalHeight, b.Transform.Y + b.GlobalHeight);
        if (left >= right || top >= bottom) return a.Shape.OutsideBounds > 0 && a.Shape.OutsideBounds == b.Shape.OutsideBounds;
        for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
                if (a.Get(x, y) > 0 && a.Get(x, y) == b.Get(x, y)) return true;
        return false;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PositionEnumerationKeepsByteSymmetryRulesAndOrder(bool fixedGeometry)
    {
        var shapes = new[]
        {
            ShapeState.Rectangle(2, 3),
            new ShapeState(3, 3, new byte[] { 0, 0, 0, 0, 1, 0, 0, 0, 0 }),
            new ShapeState(3, 3, new byte[] { 0, 2, 0, 0, 1, 0, 0, 0, 0 }),
            new ShapeState(9, 2, new byte[] { 1, 0, 1, 0, 0, 1, 0, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0 }, 1),
            ShapeState.Rectangle(2, 1, 2)
        };
        var grid = new ShapeState(11, 5, Enumerable.Range(0, 55).Select(i => (byte)(i % 11 == 0 ? 1 : 0)), 1);
        foreach (var shape in shapes)
        {
            var inventories = Inventories();
            inventories[0] = new("ground", new InventoryLayout.Grid(grid), new(AdmissionKind.Ground));
            var simulation = new TransferSimulation(State([Item("moving", "ground", 1, shape: shape)], inventories), fixedGeometry);
            var expected = ReferencePositions(shape, grid, fixedGeometry).ToArray();
            Assert.Equal(expected, simulation.Positions(simulation.Items["moving"], inventories[0]));
        }
    }

    private static IEnumerable<Placement> ReferencePositions(ShapeState shape, ShapeState grid, bool deduplicate)
    {
        var usable = new PlacedShape(grid, new()).Positions(0).ToArray();
        var seen = new HashSet<string>();
        for (int flip = 0; flip < 2; flip++)
            for (int turn = 0; turn < 4; turn++)
            {
                var placed = new PlacedShape(shape, new(orientation: turn, flipped: flip != 0));
                var cells = placed.Positions(1).ToArray();
                if (cells.Length == 0) continue;
                string signature = $"{placed.GlobalWidth}:{placed.GlobalHeight}:" + string.Join(",",
                    Enumerable.Range(0, placed.GlobalHeight).SelectMany(y =>
                        Enumerable.Range(0, placed.GlobalWidth).Select(x => placed.Get(x, y))));
                if (deduplicate && !seen.Add(signature)) continue;
                for (int y = usable.Min(p => p.Y) - cells.Min(p => p.Y); y <= usable.Max(p => p.Y) - cells.Max(p => p.Y); y++)
                    for (int x = usable.Min(p => p.X) - cells.Min(p => p.X); x <= usable.Max(p => p.X) - cells.Max(p => p.X); x++)
                        yield return new Placement.Grid(x, y, turn, flip != 0);
            }
    }

    [Fact]
    public void CatalogAndUndoMatchCellSetsAcrossTileEdgesAndFlexibleSlots()
    {
        var usable = new A.CellMask(13, 9, Enumerable.Range(0, 117).Where(i => i % 17 != 0)
            .Select(i => new A.GridCell(i % 13, i / 13)));
        var shape = new A.CellMask(3, 2, new[] { new A.GridCell(0, 0), new A.GridCell(1, 0), new A.GridCell(1, 1) });
        var item = new A.PackingItem("item", 1, A.PackingOrientation.FromShape(shape, allowReflection: true));
        var container = new A.PackingContainer("grid", usable, new[] { new A.GridCell(1, 0) });
        var problem = new A.PackProblem(new[] { item }, new[] { container, A.PackingContainer.Slot("slot") }, 0);
        var catalog = new A.PlacementCatalog(problem, default);
        var placements = catalog.ByItem[0];
        var expected = new List<(int X, int Y, int Turns, bool Flip)>();
        foreach (var orientation in item.Orientations)
            for (int y = 0; y <= usable.Height - orientation.Shape.Height; y++)
                for (int x = 0; x <= usable.Width - orientation.Shape.Width; x++)
                    if (orientation.Shape.Cells.All(p => usable.Contains(x + p.X, y + p.Y)
                        && !container.OccupiedCells.Contains(x + p.X, y + p.Y)))
                        expected.Add((x, y, orientation.QuarterTurns, orientation.Flipped));
        Assert.Equal(expected.OrderBy(p => p.Y).ThenBy(p => p.X).ThenBy(p => p.Turns).ThenBy(p => p.Flip),
            placements.Where(p => !p.Container.IsSlot).Select(p => (p.X, p.Y, p.Orientation.QuarterTurns, p.Orientation.Flipped)));
        var slot = Assert.Single(catalog.SlotsByItem[0]);
        var layout = new A.PackingLayout(problem);
        Assert.True(layout.Fits(slot));
        layout.Add(slot);
        Assert.False(layout.Fits(slot));
        Assert.Equal(0, layout.FreeCells[1]);
        layout.Remove(slot);
        Assert.Equal(1, layout.FreeCells[1]);
        var placed = placements.First(p => !p.Container.IsSlot && p.X >= 7);
        var occupied = placed.Orientation.Shape.Cells.Select(c => (c.X + placed.X, c.Y + placed.Y)).ToHashSet();
        layout.Add(placed);
        foreach (var candidate in placements.Where(p => !p.Container.IsSlot))
            Assert.Equal(!candidate.Orientation.Shape.Cells.Any(c => occupied.Contains((c.X + candidate.X, c.Y + candidate.Y))),
                layout.Fits(candidate));
        layout.Remove(placed);
        Assert.All(placements, p => Assert.True(layout.Fits(p)));
        Assert.Equal(container.FreeCellCount, layout.FreeCells[0]);
    }
}
