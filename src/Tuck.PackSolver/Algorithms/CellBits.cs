using System;

namespace Tuck.PackSolver.Algorithms;

/// <summary>Shared operations on row-major cell bitsets of matching dimensions.</summary>
internal static class CellBits
{
    public static ulong[] Create(int width, int height) => new ulong[(checked(width * height) - 1) / 64 + 1];

    public static bool Contains(ulong[] words, int index) => (words[index / 64] & (1UL << (index % 64))) != 0;

    public static void Set(ulong[] words, int index) => words[index / 64] |= 1UL << (index % 64);

    public static bool Overlap(ulong[] left, ulong[] right)
    {
        for (int w = 0; w < left.Length; w++)
            if ((left[w] & right[w]) != 0) return true;
        return false;
    }

    public static ulong[] BlockedCells(PackingContainer container)
    {
        var words = container.UsableCells.CopyWords();
        for (int w = 0; w < words.Length; w++) words[w] = ~words[w];
        foreach (var cell in container.OccupiedCells.Cells)
            Set(words, cell.Y * container.UsableCells.Width + cell.X);
        return words;
    }
}
