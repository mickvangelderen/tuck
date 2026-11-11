using BenchmarkDotNet.Attributes;

namespace Tuck.BitBoard.Bench;

[MemoryDiagnoser]
public class TransformBenchmarks
{
    public IEnumerable<BoardCase> Cases =>
        [new(3, 5, 50), new(1, 17, 50), new(13, 11, 50), new(64, 64, 50), new(257, 129, 50)];
    [ParamsSource(nameof(Cases))] public BoardCase Case { get; set; } = null!;
    private BitBoard _source = null!;
    private BitBoardMut _rotatedDestination = null!, _sameSizeDestination = null!;

    [GlobalSetup]
    public void Setup()
    {
        _source = Inputs.Board(Case.CreateCells()).ToImmutable();
        _rotatedDestination = new(Case.Height, Case.Width);
        _sameSizeDestination = new(Case.Width, Case.Height);
    }

    [Benchmark] public BitBoardMut Rotate90Into() { _source.RotateInto(_rotatedDestination, 1); return _rotatedDestination; }
    [Benchmark] public BitBoardMut Rotate180Into() { _source.RotateInto(_sameSizeDestination, 2); return _sameSizeDestination; }
    [Benchmark] public BitBoardMut Rotate270Into() { _source.RotateInto(_rotatedDestination, 3); return _rotatedDestination; }
    [Benchmark] public BitBoardMut FlipHInto() { _source.FlipHInto(_sameSizeDestination); return _sameSizeDestination; }
    [Benchmark] public BitBoardMut FlipVInto() { _source.FlipVInto(_sameSizeDestination); return _sameSizeDestination; }
    [Benchmark] public BitBoardMut InvertInto() { _source.InvertInto(_sameSizeDestination); return _sameSizeDestination; }
}
