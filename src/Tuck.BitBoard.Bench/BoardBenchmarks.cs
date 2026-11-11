using BenchmarkDotNet.Attributes;

namespace Tuck.BitBoard.Bench;

[MemoryDiagnoser]
public class BoardBenchmarks
{
    public IEnumerable<BoardCase> Cases => [new(8, 8, 50), new(13, 11, 50), new(64, 64, 50), new(257, 129, 50)];
    [ParamsSource(nameof(Cases))] public BoardCase Case { get; set; } = null!;
    private BitBoard _board = null!;

    [GlobalSetup] public void Setup() => _board = Inputs.Board(Case.CreateCells()).ToImmutable();
    [Benchmark] public long Count() => _board.Count;
}
