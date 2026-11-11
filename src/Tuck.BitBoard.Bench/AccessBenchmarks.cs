using BenchmarkDotNet.Attributes;

namespace Tuck.BitBoard.Bench;

[MemoryDiagnoser]
public class AccessBenchmarks
{
    public const int BatchSize = 256;
    public IEnumerable<BoardCase> Cases => [new(13, 11, 50), new(257, 129, 50)];
    [ParamsSource(nameof(Cases))] public BoardCase Case { get; set; } = null!;
    private BitBoard _board = null!;
    private BitBoardMut _mutable = null!;
    private (int X, int Y)[] _coordinates = null!;

    [GlobalSetup]
    public void Setup()
    {
        _mutable = Inputs.Board(Case.CreateCells());
        _board = _mutable.ToImmutable();
        _coordinates = Inputs.Coordinates(Case, BatchSize);
    }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public int ReadCells()
    {
        int checksum = 0;
        foreach (var (x, y) in _coordinates) checksum += _board[x, y] ? 1 : 0;
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public int ReadAndToggleCells()
    {
        int checksum = 0;
        foreach (var (x, y) in _coordinates)
        {
            bool previous = _mutable[x, y];
            _mutable[x, y] = !previous;
            checksum += previous ? 1 : 0;
        }
        return checksum;
    }
}
