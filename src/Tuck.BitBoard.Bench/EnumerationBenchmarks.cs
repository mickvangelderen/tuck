using BenchmarkDotNet.Attributes;

namespace Tuck.BitBoard.Bench;

[MemoryDiagnoser]
public class EnumerationBenchmarks
{
    public IEnumerable<BoardCase> Cases =>
        [new(13, 11, 50), new(257, 129, 0), new(257, 129, 1), new(257, 129, 50), new(257, 129, 100)];
    [ParamsSource(nameof(Cases))] public BoardCase Case { get; set; } = null!;
    private BitBoard _board = null!;

    [GlobalSetup] public void Setup() => _board = Inputs.Board(Case.CreateCells()).ToImmutable();

    [Benchmark]
    public long EnumerateCells()
    {
        long checksum = 0;
        foreach (var (x, y) in _board.Cells) checksum += x + (long)y * Case.Width;
        return checksum;
    }
}
