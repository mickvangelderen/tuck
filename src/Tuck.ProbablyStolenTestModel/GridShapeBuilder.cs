// Ported from dependencies/probably-stolen/decompiled/GridShapeBuilder.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Original data/geometry semantics; Unity serialization attributes and diagnostic logging removed.
#nullable disable
using System;
using System.Collections.Generic;

[Serializable]
public class GridShapeBuilder : GridShape
{
    private byte[] data;

    public byte outside;
    public int minX { get; set; }
    public int minY { get; set; }

    public int globalWidth
    {
        get
        {
            if (orientation % 2 != 0)
            {
                return height;
            }
            return width;
        }
    }

    public int globalHeight
    {
        get
        {
            if (orientation % 2 != 0)
            {
                return width;
            }
            return height;
        }
    }

    public int maxX => minX + globalWidth - 1;

    public int maxY => minY + globalHeight - 1;
    public bool flipped { get; set; }
    public int orientation { get; private set; }
    public int width { get; set; }
    public int height { get; set; }

    public GridShape shape => this;

    private static int GetHeight(int length, int width)
    {
        return length / width + Math.Sign(length % width);
    }

    public GridShapeBuilder()
        : this(1, 1)
    {
    }

    public GridShapeBuilder(int width, int height)
    {
        this.width = width;
        this.height = height;
        data = new byte[width * height];
    }

    public GridShapeBuilder(int x, int y, int width, int height, bool flipped, int orientation)
        : this(width, height)
    {
        if (orientation < 0)
        {
            orientation = orientation % 4 + 4;
        }
        minX = x;
        minY = y;
        this.flipped = flipped;
        this.orientation = orientation % 4;
    }

    private GridShapeBuilder(int x, int y, int width, bool flipped, int orientation, byte[] data, byte outside)
        : this(x, y, width, GetHeight(data.Length, width), flipped, orientation)
    {
        Array.Copy(data, this.data, data.Length);
        this.outside = outside;
    }

    public GridShapeBuilder(GridShape shape)
        : this(shape.width, shape.height)
    {
        Set(shape);
    }

    public GridShapeBuilder(GridShapeBuilder shape)
        : this(shape.width, shape.height)
    {
        Set(shape);
    }

    public GridShapeBuilder SetPosition(int minX, int minY)
    {
        this.minX = minX;
        this.minY = minY;
        return this;
    }

    public GridShapeBuilder SetOrientation(int orientation)
    {
        if (orientation < 0)
        {
            orientation = orientation % 4 + 4;
        }
        this.orientation = orientation % 4;
        return this;
    }

    public GridShapeBuilder SetFlipped(bool flipped)
    {
        this.flipped = flipped;
        return this;
    }

    public GridShapeBuilder SetTransform(GridShape shape)
    {
        return SetTransform(shape.minX, shape.minY, shape.flipped, shape.orientation);
    }

    public GridShapeBuilder SetTransform(int minX, int minY, bool flipped, int orientation)
    {
        if (orientation < 0)
        {
            orientation = orientation % 4 + 4;
        }
        this.minX = minX;
        this.minY = minY;
        this.flipped = flipped;
        this.orientation = orientation % 4;
        return this;
    }

    public GridShapeBuilder SetData(byte[] newData, int newWidth)
    {
        width = newWidth;
        height = GetHeight(newData.Length, newWidth);
        if (data.Length != width * height)
        {
            data = new byte[width * height];
        }
        Array.Copy(newData, data, data.Length);
        return this;
    }

    public GridShapeBuilder SetData(string newData, int newWidth)
    {
        width = newWidth;
        height = GetHeight(newData.Length, newWidth);
        if (data.Length != width * height)
        {
            data = new byte[width * height];
        }
        for (int i = 0; i < newData.Length; i++)
        {
            data[i] = (byte)(newData[i] - 48);
        }
        return this;
    }

    public GridShapeBuilder SetDataFill(int newWidth, int newHeight, byte b = 1)
    {
        width = newWidth;
        height = newHeight;
        if (data.Length != width * height)
        {
            data = new byte[width * height];
        }
        return SetDataFill(b);
    }

    public GridShapeBuilder SetDataFill(byte b)
    {
        Array.Fill(data, b);
        return this;
    }

    public GridShapeBuilder SetDataOutside(byte b)
    {
        outside = b;
        return this;
    }

    public GridShapeBuilder Set(GridShapeBuilder shape)
    {
        SetTransform(shape.minX, shape.minY, shape.flipped, shape.orientation);
        width = shape.width;
        height = shape.height;
        if (data.Length != shape.data.Length)
        {
            data = new byte[width * height];
        }
        Array.Copy(shape.data, data, shape.data.Length);
        outside = shape.outside;
        return this;
    }

    public GridShapeBuilder Set(GridShape shape)
    {
        if (shape is GridShapeBuilder)
        {
            return Set((GridShapeBuilder)shape);
        }
        SetTransform(shape.minX, shape.minY, shape.flipped, shape.orientation);
        width = shape.width;
        height = shape.height;
        if (data.Length != width * height)
        {
            data = new byte[width * height];
        }
        for (int i = 0; i < data.Length; i++)
        {
            int localX = i % width;
            int localY = i / width;
            data[i] = shape.GetLocal(localX, localY);
        }
        outside = shape.GetOutsideBounds();
        return this;
    }

    public GridShape Clone()
    {
        return new GridShapeBuilder(this);
    }

    public GridShape Build()
    {
        return Clone();
    }

    public byte GetLocal(int localX, int localY)
    {
        if (localX < 0 || localX >= width || localY < 0 || localY >= height)
        {
            return outside;
        }
        return data[localY * width + localX];
    }

    public (int, int) MergeWithLocalShapes(List<GridShape> shapes)
    {
        if (shapes.Count < 1)
        {
            return (0, 0);
        }
        int num = 0;
        int num2 = 0;
        int num3 = width - 1;
        int num4 = height - 1;
        foreach (GridShape shape in shapes)
        {
            num = Math.Min(num, shape.minX);
            num2 = Math.Min(num2, shape.minY);
            num3 = Math.Max(num3, shape.maxX);
            num4 = Math.Max(num4, shape.maxY);
        }
        int num5 = num3 - num + 1;
        int num6 = num4 - num2 + 1;
        byte[] array = new byte[num5 * num6];
        for (int i = 0; i < data.Length; i++)
        {
            int num7 = i % width;
            int num8 = i / width;
            int num9 = (num8 - num2) * num5 + (num7 - num);
            array[num9] = this.shape.GetLocal(num7, num8);
        }
        foreach (GridShape shape2 in shapes)
        {
            for (int j = num2; j <= num4; j++)
            {
                for (int k = num; k <= num3; k++)
                {
                    int num10 = (j - num2) * num5 + (k - num);
                    byte b = shape2.Get(k, j);
                    if (b == 1 || b > array[num10])
                    {
                        array[num10] = b;
                    }
                }
            }
        }
        int num11 = -num;
        int num12 = -num2;
        int num13 = num3 - (width - 1);
        int num14 = num4 - (height - 1);
        if (flipped)
        {
            int num15 = num13;
            int num16 = num11;
            num11 = num15;
            num13 = num16;
        }
        switch (orientation)
        {
            case 1:
            {
                int num19 = num12;
                int num17 = num13;
                num11 = num19;
                num12 = num17;
                break;
            }
            case 2:
            {
                int num18 = num13;
                int num17 = num14;
                num11 = num18;
                num12 = num17;
                break;
            }
            case 3:
            {
                int num17 = num11;
                num11 = num14;
                num12 = num17;
                break;
            }
        }
        data = array;
        width = num5;
        height = num6;
        minX -= num11;
        minY -= num12;
        return (-num, -num2);
    }
}

