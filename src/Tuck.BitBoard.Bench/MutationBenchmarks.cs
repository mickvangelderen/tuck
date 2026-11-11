using BenchmarkDotNet.Attributes;

namespace Tuck.BitBoard.Bench;

[MemoryDiagnoser]
public class MutationBenchmarks
{
    // These aligned rectangles bracket the stack-scratch limit (121 versus 129 storage words).
    public IEnumerable<BoardCase> Cases =>
        [new(13, 11, 50), new(120, 64, 50), new(128, 64, 50), new(257, 129, 50)];
    [ParamsSource(nameof(Cases))] public BoardCase Case { get; set; } = null!;
    private BitBoardMut _board = null!;

    [GlobalSetup] public void Setup() => _board = Inputs.Board(Case.CreateCells());

    // Each benchmark starts independently. Repeated calls cycle orientations without a reset or copy.
    [Benchmark] public BitBoardMut Rotate90InPlace() { _board.Rotate(1); return _board; }
    [Benchmark] public BitBoardMut Rotate180InPlace() { _board.Rotate(2); return _board; }
    [Benchmark] public BitBoardMut Rotate270InPlace() { _board.Rotate(3); return _board; }
    [Benchmark] public BitBoardMut FlipHInPlace() { _board.FlipH(); return _board; }
    [Benchmark] public BitBoardMut FlipVInPlace() { _board.FlipV(); return _board; }
    [Benchmark] public BitBoardMut InvertInPlace() { _board.Invert(); return _board; }
}
