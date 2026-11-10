using System.Diagnostics;

namespace Tuck.PackSolver.Benchmarks;

// Best-effort local scheduling, not isolation: do not kill or suspend another user's work.
// CPU deltas distinguish idle compiler servers from an active build/render/test process.
internal static class IdleGate
{
    // Explicit, recorded desktop workload allowance; still included in combined CPU accounting.
    internal static string? BackgroundProcess { get; set; }
    internal sealed record Snapshot(long Timestamp, Dictionary<int, (string Name, double Cpu)> Processes);

    internal static Snapshot Capture()
    {
        var values = new Dictionary<int, (string, double)>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId || process.Id == 0) continue;
                try { values[process.Id] = (process.ProcessName, process.TotalProcessorTime.TotalSeconds); }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        return new(Stopwatch.GetTimestamp(), values);
    }

    internal static string? Busy(Snapshot before, Snapshot after)
    {
        double seconds = Stopwatch.GetElapsedTime(before.Timestamp, after.Timestamp).TotalSeconds;
        var deltas = after.Processes.Where(p => before.Processes.ContainsKey(p.Key))
            .Select(p => (p.Value.Name, Cores: Math.Max(0, p.Value.Cpu - before.Processes[p.Key].Cpu) / seconds)).ToArray();
        // This workstation's editor/chat utility processes continuously consume about one core.
        // Allow that desktop baseline, but still gate a multicore spike in either application.
        var heavy = deltas.Where(p => p.Cores >= (p.Name is "Code" or "Paseo" || p.Name == BackgroundProcess ? 2 : 0.5)).ToArray();
        if (heavy.Length != 0) return string.Join(", ", heavy.Select(p => $"{p.Name} ({p.Cores:F1} cores)"));
        return deltas.Sum(p => p.Cores) >= Math.Max(2, Environment.ProcessorCount * 0.1)
            ? "combined background CPU exceeds quiet threshold" : null;
    }

    internal static void Wait()
    {
        int quiet = 0;
        while (quiet < 2)
        {
            var before = Capture();
            Thread.Sleep(500);
            string? busy = Busy(before, Capture());
            if (busy == null) { quiet++; continue; }
            Console.WriteLine($"Waiting for quiet CPU: {busy}");
            quiet = 0;
            Thread.Sleep(3000);
        }
    }
}
