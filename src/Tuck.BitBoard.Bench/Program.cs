using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Tuck.BitBoard.Bench;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is ["--check"])
        {
            try
            {
                SmokeChecks.Run();
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
        if (args is ["--usage"])
        {
            Console.WriteLine("""
                Run from the repository root:
                  dotnet run -c Release --project src/Tuck.BitBoard.Bench -- --list flat
                  dotnet run -c Release --project src/Tuck.BitBoard.Bench -- --filter '*TransformBenchmarks.Rotate*'
                  dotnet run -c Release --project src/Tuck.BitBoard.Bench -- --filter '*QueryBenchmarks*same-layout*'
                  dotnet run -c Release --project src/Tuck.BitBoard.Bench -- --filter '*OffsetBenchmarks*'
                  dotnet run -c Release --project src/Tuck.BitBoard.Bench -- --filter '*TransformBenchmarks.Rotate90Into*257x129*' --profiler EP
                  dotnet run -c Release --project src/Tuck.BitBoard.Bench -- --filter '*TileBenchmarks.Rotated90*' --disasm --disasmDepth 3
                  dotnet run -c Release --project src/Tuck.BitBoard.Bench -- --check
                BenchmarkDotNet options: --help. --job Dry checks execution; it is not a performance measurement.
                Reports: artifacts/bitboard-bench (override with --artifacts DIR).

                Each suite has inputs for its own implementation choices:
                  Board: population count, one public wrapper.
                  Enumeration: fixed-size density sweep plus a small board.
                  Transform: all three rotations, both flips, and inversion into existing destinations.
                  Mutation: in-place transforms, including both sides of the stack-scratch threshold.
                  Query: controlled early exits and full scans, with same-layout, translated, and clipped cases.
                  Offset: union/subtraction of fresh destination batches, including shifted self-aliases.
                  Access: varied, precomputed coordinates, with one read or read-and-toggle per operation.
                  Tile: count, rotations, and flips across independent inputs.
                  Allocation: a few allocating rotation/inversion controls, not isolated kernel timings.

                Tile and Access process 256 inputs per invocation and report per-input throughput.
                Their array traversal and checksums are included; this is not isolated instruction latency.
                Offset processes 1024 independent destinations per invocation. IterationSetup copies the
                original state into existing boards outside timing; each destination is then mutated once.
                Keep InvocationCount=1 and UnrollFactor=1 for Offset so repeated invocations cannot become no-ops.
                Its working set is a batch of boards, not one repeatedly reused board. Aliased operations
                include any snapshots taken inside the library. Setup/cloning is otherwise excluded.
                These fixed batches can still produce short iterations: inspect warnings and variability,
                and compare candidates with the same batch size and working set. Do not subtract clone timings.

                RotateInPlace timings average a cycle of orientations, including swapped dimensions.
                Flip and invert mutations alternate states. Enumeration includes coordinate checksums.
                Same-layout query fixtures are nonempty/nonfull and use different contents for containment.
                Correctness/edge-case and API-overload coverage remains in tests, not a timing cross-product.

                Results measure warmed .NET 10 workloads, not the game runtime. Warmup also warms scratch
                pools at the tested size; zero B/op does not measure first-use/growth or retained memory.
                --profiler EP records CPU samples to .nettrace and .speedscope.json for PerfView/SpeedScope.
                Use a separate run without profiling for timing comparisons, since the profiler adds overhead.

                For alternatives (including a naive implementation), reuse identical fixtures and semantics.
                Add direct benchmark methods for each implementation; choose baselines within an operation.
                Preserve mutation, aliasing, and ownership behavior, and validate outside timing with --check.
                Use --filter for the operation being investigated. Old measurements are not directly comparable
                when fixtures, batch sizes, or measured work changed; the earlier allocation-heavy gaps remain
                unresolved by this benchmark cleanup.
                """);
            return 0;
        }

        var config = DefaultConfig.Instance.WithArtifactsPath("artifacts/bitboard-bench");
        var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        return summaries.Any(s => s.HasCriticalValidationErrors || s.Reports.Any(r => !r.Success)) ? 1 : 0;
    }
}
