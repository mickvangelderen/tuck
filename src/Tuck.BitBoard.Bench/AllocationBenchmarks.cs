using BenchmarkDotNet.Attributes;

namespace Tuck.BitBoard.Bench;

// End-to-end controls; use Into benchmarks to compare transformation kernels.
[MemoryDiagnoser]
public class AllocationBenchmarks
{
    public IEnumerable<BoardCase> Cases => [new(13, 11, 50), new(257, 129, 50)];
    [ParamsSource(nameof(Cases))] public BoardCase Case { get; set; } = null!;
    private BitBoard _board = null!;

    [GlobalSetup] public void Setup() => _board = Inputs.Board(Case.CreateCells()).ToImmutable();
    [Benchmark] public BitBoard Rotated90() => _board.Rotated(1);
    [Benchmark] public BitBoard Inverted() => _board.Inverted();
}
