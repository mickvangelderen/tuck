namespace Tuck.ProbablyStolenTestModel;

/// <summary>Serializable geometry for fixtures. Cell bytes are row-major and are not coerced to booleans.</summary>
public sealed class ShapeSnapshot
{
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[] Cells { get; set; } = [];
    public byte Outside { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Orientation { get; set; }
    public bool Flipped { get; set; }

    public static ShapeSnapshot Capture(GridShape shape)
    {
        var cells = new byte[checked(shape.width * shape.height)];
        for (int y = 0; y < shape.height; y++)
            for (int x = 0; x < shape.width; x++)
                cells[y * shape.width + x] = shape.GetLocal(x, y);
        return new()
        {
            Width = shape.width,
            Height = shape.height,
            Cells = cells,
            Outside = shape.GetOutsideBounds(),
            X = shape.minX,
            Y = shape.minY,
            Orientation = shape.orientation,
            Flipped = shape.flipped
        };
    }

    public GridShape Restore()
    {
        if (Width <= 0 || Height <= 0 || Cells == null || Cells.Length != checked(Width * Height))
            throw new ArgumentException("Shape dimensions must match the cell array.");
        if (Orientation is < 0 or > 3)
            throw new ArgumentException("Shape orientation must be between 0 and 3.");
        return new GridShapeBuilder(Width, Height).SetData(Cells, Width).SetDataOutside(Outside)
            .SetTransform(X, Y, Flipped, Orientation).Build();
    }
}
