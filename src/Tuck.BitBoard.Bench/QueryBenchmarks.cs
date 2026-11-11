using BenchmarkDotNet.Attributes;

namespace Tuck.BitBoard.Bench;

[MemoryDiagnoser]
public class QueryBenchmarks
{
    public IEnumerable<OffsetCase> Cases =>
    [
        new(8, 8, 8, 8, 0, 0, "same-layout"),
        new(33, 33, 33, 33, 0, 0, "same-layout"),
        new(256, 256, 256, 256, 0, 0, "same-layout"),
        new(11, 9, 3, 5, 3, 2, "tiny"),
        new(21, 35, 1, 17, 11, 9, "thin"),
        new(64, 48, 33, 17, 8, 16, "aligned"),
        new(64, 48, 33, 17, 3, 5, "unaligned"),
        new(64, 48, 33, 17, -3, -5, "clip-neg"),
        new(64, 48, 33, 17, 45, 39, "clip-pos"),
        new(288, 160, 257, 129, 3, 5, "large-unaligned")
    ];
    [ParamsSource(nameof(Cases))] public OffsetCase Case { get; set; } = null!;
    private BitBoard _source = null!, _noOverlap = null!, _earlyOverlap = null!;
    private BitBoard _allContained = null!, _earlyHole = null!;

    [GlobalSetup]
    public void Setup()
    {
        var cells = new bool[Case.SourceHeight, Case.SourceWidth];
        int firstX = Math.Max(0, -Case.X), firstY = Math.Max(0, -Case.Y);
        int endX = Math.Min(Case.SourceWidth, Case.Width - Case.X);
        int endY = Math.Min(Case.SourceHeight, Case.Height - Case.Y);
        var all = new BitBoardMut(Case.Width, Case.Height);
        var none = new BitBoardMut(Case.Width, Case.Height, filled: true);
        // Nonempty, nonfull patterns avoid empty/full shortcuts; clipped-away source cells stay unset.
        for (int y = firstY; y < endY; y++)
            for (int x = firstX; x < endX; x++)
            {
                bool set = ((x + y) & 1) == 0 || (x == firstX && y == firstY);
                cells[y, x] = set;
                all[x + Case.X, y + Case.Y] = set || ((x + y) & 3) == 1;
                none[x + Case.X, y + Case.Y] = !set;
            }
        _source = Inputs.Board(cells).ToImmutable();
        _allContained = all.ToImmutable();
        _noOverlap = none.ToImmutable();
        none[firstX + Case.X, firstY + Case.Y] = true;
        _earlyOverlap = none.ToImmutable();
        all[firstX + Case.X, firstY + Case.Y] = false;
        _earlyHole = all.ToImmutable();
    }

    [Benchmark] public bool OverlapFullScan() => _noOverlap.Overlaps(_source, Case.X, Case.Y);
    [Benchmark] public bool OverlapEarlyExit() => _earlyOverlap.Overlaps(_source, Case.X, Case.Y);
    [Benchmark] public bool ContainsFullScan() => _allContained.Contains(_source, Case.X, Case.Y);
    [Benchmark] public bool ContainsEarlyExit() => _earlyHole.Contains(_source, Case.X, Case.Y);
}
