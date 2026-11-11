using System.Reflection;
using BenchmarkDotNet.Attributes;

namespace Tuck.BitBoard.Bench;

internal static class SmokeChecks
{
    internal static void Run()
    {
        int workloads = 0;
        Exercise(new TileBenchmarks(), (method, _, result) =>
        {
            ulong expected = 0;
            foreach (var tile in Inputs.Tiles(TileBenchmarks.BatchSize))
            {
                var cells = new bool[8, 8];
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++) cells[y, x] = (tile.Bits & (1UL << (y * 8 + x))) != 0;
                if (method == nameof(TileBenchmarks.Count)) expected += (ulong)Count(cells);
                else
                {
                    var transformed = Transform(cells, method);
                    ulong bits = 0;
                    for (int y = 0; y < 8; y++)
                        for (int x = 0; x < 8; x++)
                            if (transformed[y, x]) bits |= 1UL << (y * 8 + x);
                    expected = unchecked(expected + bits);
                }
            }
            Require(Convert.ToUInt64(result) == expected);
        });
        foreach (var input in new BoardBenchmarks().Cases)
            Exercise(new BoardBenchmarks { Case = input },
                (_, _, result) => Require((long)result == Count(input.CreateCells())));
        foreach (var input in new EnumerationBenchmarks().Cases)
        {
            var cells = input.CreateCells();
            long expected = 0;
            for (int y = 0; y < input.Height; y++)
                for (int x = 0; x < input.Width; x++)
                    if (cells[y, x]) expected += x + (long)y * input.Width;
            Exercise(new EnumerationBenchmarks { Case = input }, (_, _, result) => Require((long)result == expected));
        }
        foreach (var input in new AccessBenchmarks().Cases)
        {
            var coordinates = Inputs.Coordinates(input, AccessBenchmarks.BatchSize);
            Require(coordinates.Distinct().Count() > 1);
            Exercise(new AccessBenchmarks { Case = input }, (method, repeat, result) =>
            {
                var cells = input.CreateCells();
                int expected = 0;
                for (int invocation = 0; invocation <= repeat; invocation++)
                {
                    expected = 0;
                    foreach (var (x, y) in coordinates)
                    {
                        expected += cells[y, x] ? 1 : 0;
                        if (method == nameof(AccessBenchmarks.ReadAndToggleCells)) cells[y, x] = !cells[y, x];
                    }
                }
                Require((int)result == expected);
            });
        }
        foreach (var input in new TransformBenchmarks().Cases)
            Exercise(new TransformBenchmarks { Case = input }, (method, _, result) =>
                RequireBoard(Transform(input.CreateCells(), method), (BitBoardMut)result));
        foreach (var input in new AllocationBenchmarks().Cases)
            Exercise(new AllocationBenchmarks { Case = input }, (method, _, result) =>
                RequireBoard(Transform(input.CreateCells(), method), (BitBoard)result));
        foreach (var input in new MutationBenchmarks().Cases)
            Exercise(new MutationBenchmarks { Case = input }, (method, repeat, result) =>
            {
                var expected = input.CreateCells();
                for (int invocation = 0; invocation <= repeat; invocation++) expected = Transform(expected, method);
                RequireBoard(expected, (BitBoardMut)result);
            });
        foreach (var input in new QueryBenchmarks().Cases)
            Exercise(new QueryBenchmarks { Case = input }, (method, _, result) =>
                Require((bool)result == (method is nameof(QueryBenchmarks.OverlapEarlyExit) or nameof(QueryBenchmarks.ContainsFullScan))));
        foreach (var input in new OffsetBenchmarks().Cases)
        {
            var offsets = new OffsetBenchmarks { Case = input };
            var (cells, source) = Inputs.MutationCells(input);
            var original = Inputs.Board(cells).ToImmutable();
            var union = Combine(cells, source, input.X, input.Y, subtract: false);
            var subtract = Combine(cells, source, input.X, input.Y, subtract: true);
            var expectedUnion = Inputs.Board(union).ToImmutable();
            var expectedSubtract = Inputs.Board(subtract).ToImmutable();
            // Both workloads must change cells on their first invocation, including at zero offset.
            if (original.Equals(expectedUnion) || original.Equals(expectedSubtract))
                throw new InvalidOperationException($"Mutation fixture must change cells for both operations: {input}.");
            Exercise(offsets, (method, _, result) =>
            {
                var expected = method == nameof(OffsetBenchmarks.Union) ? expectedUnion : expectedSubtract;
                var batch = (BitBoardMut[])result;
                Require(batch.Length == OffsetBenchmarks.BatchSize);
                foreach (var board in batch) Require(expected.ContentEquals(board));
            }, before: () =>
            {
                Require(offsets.Destinations.Distinct().Count() == OffsetBenchmarks.BatchSize);
                foreach (var board in offsets.Destinations) Require(original.ContentEquals(board));
            });
        }
        Console.WriteLine($"Checked {workloads} benchmark/parameter combinations against cell references, repeated calls, and fresh-batch resets. No timings collected.");

        void Exercise(object suite, Action<string, int, object> verify, Action? before = null)
        {
            var type = suite.GetType();
            var setup = type.GetMethods().SingleOrDefault(m => m.GetCustomAttribute<GlobalSetupAttribute>() != null)
                ?? throw new InvalidOperationException($"{type.Name} needs a GlobalSetup method for fixture checks.");
            var reset = type.GetMethods().SingleOrDefault(m => m.GetCustomAttribute<IterationSetupAttribute>() != null);
            foreach (var method in type.GetMethods().Where(m => m.GetCustomAttribute<BenchmarkAttribute>() != null))
            {
                // Match BenchmarkDotNet: independent GlobalSetup per method, reset before each measured batch.
                setup.Invoke(suite, null);
                for (int repeat = 0; repeat < 4; repeat++)
                {
                    try
                    {
                        reset?.Invoke(suite, null);
                        before?.Invoke();
                        verify(method.Name, repeat, method.Invoke(suite, null)!);
                    }
                    catch (Exception error)
                    {
                        throw new InvalidOperationException($"Fixture failed: {type.Name}.{method.Name}, repetition {repeat}.", error);
                    }
                }
                workloads++;
            }
        }
    }

    private static long Count(bool[,] cells)
    {
        long count = 0;
        foreach (bool set in cells) if (set) count++;
        return count;
    }

    private static bool[,] Transform(bool[,] cells, string method)
    {
        int turns = method switch
        {
            "Rotated90" or "Rotate90Into" or "Rotate90InPlace" => 1,
            "Rotated180" or "Rotate180Into" or "Rotate180InPlace" => 2,
            "Rotated270" or "Rotate270Into" or "Rotate270InPlace" => 3,
            _ => 0
        };
        if (turns != 0)
        {
            for (int turn = 0; turn < turns; turn++)
            {
                int width = cells.GetLength(1), height = cells.GetLength(0);
                var rotated = new bool[width, height];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++) rotated[width - 1 - x, y] = cells[y, x];
                cells = rotated;
            }
            return cells;
        }
        int w = cells.GetLength(1), h = cells.GetLength(0);
        var result = new bool[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                result[y, x] = method switch
                {
                    "FlippedH" or "FlipHInto" or "FlipHInPlace" => cells[y, w - 1 - x],
                    "FlippedV" or "FlipVInto" or "FlipVInPlace" => cells[h - 1 - y, x],
                    "Inverted" or "InvertInto" or "InvertInPlace" => !cells[y, x],
                    _ => throw new ArgumentException($"Unknown transform: {method}")
                };
        return result;
    }

    private static bool[,] Combine(bool[,] destination, bool[,] source, int offsetX, int offsetY, bool subtract)
    {
        var result = (bool[,])destination.Clone();
        for (int y = 0; y < result.GetLength(0); y++)
            for (int x = 0; x < result.GetLength(1); x++)
            {
                int sx = x - offsetX, sy = y - offsetY;
                bool bit = sx >= 0 && sy >= 0 && sx < source.GetLength(1) && sy < source.GetLength(0) && source[sy, sx];
                result[y, x] = subtract ? result[y, x] && !bit : result[y, x] || bit;
            }
        return result;
    }

    private static void RequireBoard(bool[,] expected, IBitBoard actual) =>
        Require(Inputs.Board(expected).ContentEquals(actual));

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Benchmark fixture check failed.");
    }
}
