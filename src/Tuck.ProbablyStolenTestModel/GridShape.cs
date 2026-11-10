// Ported from dependencies/probably-stolen/decompiled/GridShape.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Original data/geometry semantics; Unity serialization attributes and diagnostic logging removed.
#nullable disable
using System;

public interface GridShape
{
    int minX { get; }

    int minY { get; }

    int globalWidth { get; }

    int globalHeight { get; }

    int maxX { get; }

    int maxY { get; }

    bool flipped { get; }

    int orientation { get; }

    int width { get; }

    int height { get; }

    GridShape Clone();

    byte GetLocal(int localX, int localY);

    byte GetOutsideBounds()
    {
        return GetLocal(-1, -1);
    }

    byte Get(int globalX, int globalY)
    {
        (globalX, globalY) = InverseTransform(globalX, globalY);
        return GetLocal(globalX, globalY);
    }

    (int, int) ForwardTransform(int localX, int localY)
    {
        if (flipped)
        {
            localX = width - 1 - localX;
        }
        switch (orientation % 4)
        {
            case 1:
            {
                int num2 = localY;
                int num4 = -localX + width - 1;
                localX = num2;
                localY = num4;
                break;
            }
            case 2:
            {
                int num2 = -localX + width - 1;
                int num3 = -localY + height - 1;
                localX = num2;
                localY = num3;
                break;
            }
            case 3:
            {
                int num = -localY + height - 1;
                int num2 = localX;
                localX = num;
                localY = num2;
                break;
            }
        }
        return (localX + minX, localY + minY);
    }

    (int, int) InverseTransform(int x, int y)
    {
        int num = x - minX;
        int num2 = y - minY;
        x = num;
        y = num2;
        int num4;
        int num5;
        if (orientation % 2 != 0)
        {
            int num3 = height;
            num = width;
            num4 = num3;
            num5 = num;
        }
        else
        {
            int num6 = width;
            num = height;
            num4 = num6;
            num5 = num;
        }
        switch (orientation % 4)
        {
            case 1:
            {
                int num9 = -y + num5 - 1;
                num = x;
                x = num9;
                y = num;
                break;
            }
            case 2:
            {
                num = -x + num4 - 1;
                int num8 = -y + num5 - 1;
                x = num;
                y = num8;
                break;
            }
            case 3:
            {
                num = y;
                int num7 = -x + num4 - 1;
                x = num;
                y = num7;
                break;
            }
        }
        if (flipped)
        {
            x = width - 1 - x;
        }
        return (x, y);
    }

    private bool IntersectsBounds(int minX, int minY, int maxX, int maxY)
    {
        if (maxX >= this.minX && minX <= this.maxX && maxY >= this.minY)
        {
            return minY <= this.maxY;
        }
        return false;
    }

    bool IntersectsBounds(int x, int y)
    {
        return IntersectsBounds(x, y, x, y);
    }

    bool IntersectsBounds(GridShape other)
    {
        return IntersectsBounds(other.minX, other.minY, other.maxX, other.maxY);
    }

    (int, int, int, int, bool) GetIntersection(GridShape other)
    {
        int num = Math.Max(minX, other.minX);
        int num2 = Math.Min(maxX, other.maxX);
        int item = Math.Max(minY, other.minY);
        int num3 = Math.Min(maxY, other.maxY);
        bool item2 = num2 >= num && num3 >= num;
        return (num, num2, item, num3, item2);
    }

    bool Intersects(int x, int y)
    {
        if (!IntersectsBounds(x, y))
        {
            return GetOutsideBounds() == 1;
        }
        return Get(x, y) == 1;
    }

    bool Intersects(int x, int y, Func<byte, bool> condition)
    {
        if (!IntersectsBounds(x, y))
        {
            return condition(GetOutsideBounds());
        }
        return condition(Get(x, y));
    }

    bool Intersects(GridShape other, Func<byte, byte, bool> condition = null)
    {
        if (condition == null)
        {
            condition = (byte b, byte bOther) => b > 0 && b == bOther;
        }
        if (!IntersectsBounds(other))
        {
            return condition(GetOutsideBounds(), other.GetOutsideBounds());
        }
        (int, int, int, int, bool) intersection = GetIntersection(other);
        int item = intersection.Item1;
        int item2 = intersection.Item2;
        int item3 = intersection.Item3;
        int item4 = intersection.Item4;
        for (int num = item3; num <= item4; num++)
        {
            for (int num2 = item; num2 <= item2; num2++)
            {
                if (condition(Get(num2, num), other.Get(num2, num)))
                {
                    return true;
                }
            }
        }
        return false;
    }

    bool FullyIntersects(GridShape other, Func<byte, byte, bool> condition = null)
    {
        if (condition == null)
        {
            condition = (byte b, byte bOther) => b == 0 || b == bOther;
        }
        for (int num = minY; num <= maxY; num++)
        {
            for (int num2 = minX; num2 <= maxX; num2++)
            {
                if (!condition(Get(num2, num), other.Get(num2, num)))
                {
                    return false;
                }
            }
        }
        return true;
    }

    bool FullyIntersectsRect(int width, int height, Func<byte, byte, bool> condition = null)
    {
        if (condition == null)
        {
            condition = (byte b, byte bOther) => b == 0 || b == bOther;
        }
        for (int num = minY; num <= maxY; num++)
        {
            for (int num2 = minX; num2 <= maxX; num2++)
            {
                byte arg = (byte)((num2 < 0 || num2 >= width || num < 0 || num >= height) ? 1 : 0);
                if (!condition(Get(num2, num), arg))
                {
                    return false;
                }
            }
        }
        return true;
    }

    bool IsTouchingTop()
    {
        if (minY <= 0)
        {
            return true;
        }
        return false;
    }

    bool IsTouchingLeft()
    {
        if (minX <= 0)
        {
            return true;
        }
        return false;
    }
}

