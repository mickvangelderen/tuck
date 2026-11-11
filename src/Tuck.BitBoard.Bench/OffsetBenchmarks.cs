using BenchmarkDotNet.Attributes;

namespace Tuck.BitBoard.Bench;

[MemoryDiagnoser]
public class OffsetBenchmarks
{
    public const int BatchSize = 1024;
    public IEnumerable<OffsetCase> Cases =>
    [
        new(13, 11, 13, 11, 0, 0, "same-layout"),
        new(257, 129, 257, 129, 0, 0, "same-layout"),
        new(129, 65, 33, 17, 8, 16, "aligned"),
        new(129, 65, 33, 17, 11, 19, "unaligned"),
        new(129, 65, 33, 17, -3, -5, "clip-neg"),
        new(129, 65, 33, 17, 126, 60, "clip-pos"),
        new(129, 65, 129, 65, 8, 16, "self-aligned", Aliased: true),
        new(129, 65, 129, 65, 3, 5, "self-unaligned", Aliased: true)
    ];
    [ParamsSource(nameof(Cases))] public OffsetCase Case { get; set; } = null!;
    private BitBoard _original = null!;
    private BitBoardMut[] _destinations = null!, _sources = null!;
    internal IReadOnlyList<BitBoardMut> Destinations => _destinations;

    [GlobalSetup]
    public void Setup()
    {
        var cells = Inputs.MutationCells(Case);
        _original = Inputs.Board(cells.Destination).ToImmutable();
        var source = Inputs.Board(cells.Source);
        _destinations = new BitBoardMut[BatchSize];
        _sources = new BitBoardMut[BatchSize];
        for (int i = 0; i < BatchSize; i++)
        {
            _destinations[i] = new(Case.Width, Case.Height);
            _sources[i] = Case.Aliased ? _destinations[i] : source;
        }
    }

    // IterationSetup forces one invocation; each board is mutated once in that measured batch.
    [IterationSetup]
    public void Reset()
    {
        foreach (var destination in _destinations) _original.RotateInto(destination, 0);
    }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public BitBoardMut[] Union()
    {
        for (int i = 0; i < BatchSize; i++) _destinations[i].Union(_sources[i], Case.X, Case.Y);
        return _destinations;
    }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public BitBoardMut[] Subtract()
    {
        for (int i = 0; i < BatchSize; i++) _destinations[i].Subtract(_sources[i], Case.X, Case.Y);
        return _destinations;
    }
}
