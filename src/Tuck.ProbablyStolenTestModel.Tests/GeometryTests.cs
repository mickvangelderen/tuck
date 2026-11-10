using System.Text.Json;
using Tuck.ProbablyStolenTestModel;

namespace Tuck.ProbablyStolenTestModel.Tests;

public class GeometryTests
{
    [Theory]
    [InlineData(0, false, 5, 7)]
    [InlineData(1, false, 5, 9)]
    [InlineData(2, false, 7, 8)]
    [InlineData(3, false, 6, 7)]
    [InlineData(0, true, 7, 7)]
    [InlineData(1, true, 5, 7)]
    [InlineData(2, true, 5, 8)]
    [InlineData(3, true, 6, 9)]
    public void TransformsMatchGameCoordinates(int rotation, bool flipped, int x, int y)
    {
        GridShape shape = new GridShapeBuilder(3, 2).SetTransform(5, 7, flipped, rotation);
        Assert.Equal((x, y), shape.ForwardTransform(0, 0));
        for (int localY = 0; localY < 2; localY++)
            for (int localX = 0; localX < 3; localX++)
            {
                var global = shape.ForwardTransform(localX, localY);
                Assert.Equal((localX, localY), shape.InverseTransform(global.Item1, global.Item2));
            }
    }

    [Fact]
    public void CollisionUsesCellsRatherThanBoundingRectangles()
    {
        GridShape left = new GridShapeBuilder().SetData("1001", 2);
        GridShape right = new GridShapeBuilder().SetData("0110", 2);
        Assert.True(left.IntersectsBounds(right));
        Assert.False(left.Intersects(right));
        Assert.True(left.Intersects(left.Clone()));
    }

    [Fact]
    public void JsonRoundTripPreservesBytesOutsideAndTransformAndDoesNotAlias()
    {
        var source = new GridShapeBuilder().SetData("102301", 3).SetDataOutside(7)
            .SetTransform(-2, 4, true, 3);
        var snapshot = ShapeSnapshot.Capture(source);
        var json = JsonSerializer.Serialize(snapshot);
        var restored = JsonSerializer.Deserialize<ShapeSnapshot>(json)!.Restore();
        source.SetDataFill(9);
        snapshot.Cells[0] = 8;

        Assert.Equal((3, 2, -2, 4, true, 3),
            (restored.width, restored.height, restored.minX, restored.minY, restored.flipped, restored.orientation));
        Assert.Equal(7, restored.GetOutsideBounds());
        Assert.Equal(new byte[] { 1, 0, 2, 3, 0, 1 }, ShapeSnapshot.Capture(restored).Cells);
    }

    [Fact]
    public void BuilderCloneHasIndependentCells()
    {
        var source = new GridShapeBuilder().SetData("1011", 2);
        var clone = source.Clone();
        source.SetDataFill(0);
        Assert.Equal(1, clone.GetLocal(0, 0));
        Assert.Equal(0, clone.GetLocal(1, 0));
    }

    [Fact]
    public void ShapeSnapshotRejectsTruncatedData()
    {
        var snapshot = new ShapeSnapshot { Width = 2, Height = 2, Cells = [1, 1, 1] };
        Assert.Throws<ArgumentException>(() => snapshot.Restore());
    }

    [Fact]
    public void TagSystemCloneRetainsOriginalShallowCopySemantics()
    {
        var tags = new TagSystem().EnableTag("owned");
        var clone = tags.Clone();
        clone.DisableTag("owned");
        Assert.False(tags.IsTag("owned"));
        var independentTag = tags.GetTag("owned").Clone();
        independentTag.Enable();
        Assert.False(tags.IsTag("owned"));
    }
}
