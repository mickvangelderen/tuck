using BenchmarkDotNet.Attributes;

namespace Tuck.BitBoard.Bench;

[MemoryDiagnoser]
public class TileBenchmarks
{
    public const int BatchSize = 256;
    private BitTile[] _tiles = null!;

    [GlobalSetup] public void Setup() => _tiles = Inputs.Tiles(BatchSize);

    // Independent inputs measure throughput; checksum and array traversal are included.
    [Benchmark(OperationsPerInvoke = BatchSize)]
    public int Count()
    {
        int checksum = 0;
        foreach (var tile in _tiles) checksum += tile.Count;
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public ulong Rotated90()
    {
        ulong checksum = 0;
        foreach (var tile in _tiles) checksum = unchecked(checksum + tile.Rotated(1).Bits);
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public ulong Rotated180()
    {
        ulong checksum = 0;
        foreach (var tile in _tiles) checksum = unchecked(checksum + tile.Rotated(2).Bits);
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public ulong Rotated270()
    {
        ulong checksum = 0;
        foreach (var tile in _tiles) checksum = unchecked(checksum + tile.Rotated(3).Bits);
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public ulong FlippedH()
    {
        ulong checksum = 0;
        foreach (var tile in _tiles) checksum = unchecked(checksum + tile.FlippedH().Bits);
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public ulong FlippedV()
    {
        ulong checksum = 0;
        foreach (var tile in _tiles) checksum = unchecked(checksum + tile.FlippedV().Bits);
        return checksum;
    }
}
