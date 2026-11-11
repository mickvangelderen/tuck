using System.Collections.Generic;
using System.Linq;
using global::Tuck.BitBoard;

namespace Tuck.Mod;

internal static class FilterSwapCandidates
{
    internal static List<(GridShape Shape, BitBoardMut Footprint)> Shapes(GridShape original)
    {
        var result = new List<(GridShape Shape, BitBoardMut Footprint)>();
        // Prefer the current flip, then the fewest quarter turns; positive turns break ties.
        foreach (bool flipped in new[] { original.flipped, !original.flipped })
            foreach (int turn in new[] { 0, 1, 3, 2 })
            {
                GridShape shape = new GridShapeBuilder(original)
                    .SetTransform(0, 0, flipped, original.orientation + turn);
                // Include all cell values: the game's collision rules also compare values > 1.
                if (result.Any(candidate => candidate.Shape.globalWidth == shape.globalWidth &&
                    candidate.Shape.globalHeight == shape.globalHeight &&
                    shape.FullyIntersects(candidate.Shape, (a, b) => a == b))) continue;
                // Keep only the preferred transform, including for orientation-sensitive game rules.
                result.Add((shape, Mask(shape, 1)));
            }
        return result;
    }

    internal static (long X, long Y, long Count) OccupiedCellSums(BitBoardMut footprint)
    {
        long sumX = 0, sumY = 0, count = 0;
        foreach (var (x, y) in footprint.Cells)
        {
            sumX += x;
            sumY += y;
            count++;
        }
        // Empty footprints have no mass; use the bounding-box center instead.
        return count == 0 ? (footprint.Width - 1, footprint.Height - 1, 2) : (sumX, sumY, count);
    }

    internal static BitBoardMut Mask(GridShape shape, byte included)
    {
        var board = new BitBoardMut(shape.globalWidth, shape.globalHeight);
        for (int y = 0; y < board.Height; y++)
            for (int x = 0; x < board.Width; x++)
                board[x, y] = shape.Get(shape.minX + x, shape.minY + y) == included;
        return board;
    }
}
