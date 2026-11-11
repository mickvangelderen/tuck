using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using global::Tuck.PackSolver;
using global::Tuck.PackSolver.Solvers;
using Tuck.Mod.DebugRecording;

namespace Tuck.PackSolver.Benchmarks;

internal static class Program
{
    private static IPackSolver[] Solvers = [new BaselinePackSolver(), new AnytimePackSolver(), new RelaxedAStarPackSolver(), new ContainerFirstPackSolver()];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly PlanningOptions Defaults = new();
    private sealed record Sample(string Scenario, string Solver, int Round, double Milliseconds, long AllocatedBytes,
        long? Value, int Moves, string Status, object? Metrics);
    private sealed record Outcome(string Solver, long? Value, int Moves, string Status);

    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (args.Contains("--help"))
        {
            Console.WriteLine("Release executable: [--output DIR] [--rounds 3] [--tiny 60] [--satchel 12] [--buffer 12] [--crowded 12] [--seed 1729] [--seconds 10] [--fuzz-only] [--capture FILE] [--solver TYPE] [--background-process NAME]");
            return 0;
        }
        string Get(string flag, string fallback) => Array.IndexOf(args, flag) is int i && i >= 0 ? args[i + 1] : fallback;
        int Number(string flag, int fallback) => int.Parse(Get(flag, fallback.ToString()));
        IdleGate.BackgroundProcess = args.Contains("--background-process") ? Get("--background-process", "") : null;
        if (args.Contains("--solver"))
        {
            string selected = Get("--solver", "");
            Solvers = Solvers.Where(s => s.GetType().Name == selected).ToArray();
            if (Solvers.Length == 0) throw new ArgumentException("Unknown solver: " + selected);
        }
        int rounds = Number("--rounds", 3), tiny = Number("--tiny", 60), satchel = Number("--satchel", 12);
        int seed = Number("--seed", 1729), seconds = Number("--seconds", 10);
        int buffer = Number("--buffer", 12);
        int crowded = Number("--crowded", 12);
        if (rounds < 1 || tiny < 0 || satchel < 0 || buffer < 0 || crowded < 0 || seconds < 1) throw new ArgumentException("Invalid run limits.");
        string output = Path.GetFullPath(Get("--output", "artifacts/solver-benchmark"));
        Directory.CreateDirectory(output);
        using var mutex = new Mutex(false, "Tuck.SequentialBenchmark");
        Console.WriteLine("Waiting for exclusive benchmark slot...");
        try { while (!mutex.WaitOne(1000)) { } }
        catch (AbandonedMutexException) { }
        try
        {
            var started = DateTimeOffset.UtcNow;
            string assembly = typeof(IPackSolver).Assembly.Location;
            var limits = Defaults;
            PackState[] scenarios;
            if (args.Contains("--capture"))
            {
                var capture = PackJson.Deserialize(File.ReadAllText(Get("--capture", "")));
                scenarios = [capture.State];
                limits = capture.Options;
            }
            else scenarios = Scenarios.Benchmarks().ToArray();
            var metadata = new
            {
                started,
                Framework = RuntimeInformation.FrameworkDescription,
                OS = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                LogicalProcessors = Environment.ProcessorCount,
                SolverAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly))),
                rounds,
                tiny,
                satchel,
                buffer,
                crowded,
                seed,
                seconds,
                Options = limits,
                Solvers = Solvers.Select(s => s.GetType().Name).ToArray(),
                IdleGate.BackgroundProcess,
                IdlePolicy = "Two quiet 500ms windows; no external process >=0.5 core (Code/Paseo desktop baseline allowed below 2 cores each); combined external CPU <max(2 cores,10% CPU). Noisy timing samples retried."
            };
            File.WriteAllText(Path.Combine(output, "environment.json"), JsonSerializer.Serialize(metadata, Json));
            var samples = new List<Sample>();
            if (!args.Contains("--fuzz-only"))
            {
                File.WriteAllText(Path.Combine(output, "samples.jsonl"), "");
                foreach (var scenario in scenarios)
                {
                    File.WriteAllText(Path.Combine(output, scenario.StateId + ".json"), PackJson.Serialize(scenario, limits));
                    // One warm-up per case/solver, then rotate order between measured rounds.
                    for (int round = -1; round < rounds; round++)
                    {
                        for (int offset = 0; offset < Solvers.Length; offset++)
                        {
                            var solver = Solvers[(offset + round + 1) % Solvers.Length];
                            Sample sample;
                            while (true)
                            {
                                IdleGate.Wait();
                                // Packing and validation are outside the timed region. This is
                                // per-call allocation traffic, not peak live memory or process RSS.
                                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                                var background = IdleGate.Capture();
                                var before = Copy(scenario);
                                using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
                                var problem = new PackProblem(scenario);
                                long allocated = GC.GetAllocatedBytesForCurrentThread();
                                long start = Stopwatch.GetTimestamp();
                                var result = solver.Solve(problem, limits, cancel.Token);
                                double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                                // Process CPU counters have coarse ticks: a sub-ms measurement
                                // would misclassify a single 15ms tick as several busy cores.
                                // This wait is outside both solver timing and allocation measurement.
                                if (elapsed < 500) Thread.Sleep((int)Math.Ceiling(500 - elapsed));
                                string? busy = IdleGate.Busy(background, IdleGate.Capture());
                                Validate(before, scenario, result);
                                if (busy != null)
                                {
                                    Console.WriteLine($"Discarding noisy sample ({busy}): {scenario.StateId}/{solver.GetType().Name}");
                                    continue;
                                }
                                sample = new(scenario.StateId, solver.GetType().Name, round, elapsed, allocated,
                                    result.FinalValue, result.Plan.Steps.Count, result.Status.ToString(),
                                    // Outside timing; permits this harness to compare an archived pre-metrics assembly.
                                    typeof(PlanningResult).GetProperty("Metrics")?.GetValue(result));
                                break;
                            }
                            Console.WriteLine($"{(round < 0 ? "warm" : round.ToString())} {sample.Scenario}/{sample.Solver}: {sample.Milliseconds:F2} ms, value {sample.Value}, {sample.Moves} moves, {sample.Status}");
                            if (round < 0) continue;
                            samples.Add(sample);
                            File.AppendAllText(Path.Combine(output, "samples.jsonl"), JsonSerializer.Serialize(sample) + Environment.NewLine);
                        }
                    }
                }
                WriteSummary(output, samples);
            }
            Fuzz(output, tiny, satchel, buffer, crowded, seed, seconds);
            Console.WriteLine($"Finished in {DateTimeOffset.UtcNow - started}. Artifacts: {output}");
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
            Console.Error.WriteLine(error);
            return 1;
        }
        finally { mutex.ReleaseMutex(); }
    }

    private static PackState Copy(PackState state) => new(state.StateId, state.Roots, state.Items, state.Inventories, state.Definitions.Values);

    private static void Validate(PackState before, PackState input, PlanningResult result)
    {
        if (StateComparison.Difference(before, input) is string difference) throw new InvalidOperationException("Mutated input: " + difference);
        var final = PlanReplay.Replay(input, result.Plan);
        if (result.Status == PlanningStatus.Unsupported)
        {
            if (result.Plan.Steps.Count != 0 || result.UnsupportedRules.Count == 0) throw new InvalidOperationException("Invalid unsupported result.");
            return;
        }
        if (result.InitialValue != PlanReplay.ReturnedValue(input) || result.FinalValue != PlanReplay.ReturnedValue(final)
            || result.FinalValue < result.InitialValue) throw new InvalidOperationException("Invalid returned value.");
        if (ReportedBound(result) is long bound && bound < result.FinalValue)
            throw new InvalidOperationException($"Upper bound {bound} excludes returned value {result.FinalValue}.");
        if (final.Items.Count != before.Items.Count) throw new InvalidOperationException("Item count changed.");
        foreach (var original in before.Items)
        {
            var item = final.Items.Single(i => i.Id == original.Id);
            if (item.Identifier != original.Identifier || item.UnitCount != original.UnitCount || item.UnitValue != original.UnitValue
                || item.UnitBaseValue != original.UnitBaseValue || !StateComparison.SameShape(item.Shape, original.Shape)
                || !StateComparison.SameFacts(item.State, original.State)) throw new InvalidOperationException("Intrinsic item changed.");
        }
    }

    // Reflection preserves comparison with archived assemblies predating optional search metrics.
    private static long? ReportedBound(PlanningResult result)
    {
        var metrics = typeof(PlanningResult).GetProperty("Metrics")?.GetValue(result);
        return (long?)metrics?.GetType().GetProperty("RelaxedValueBound")?.GetValue(metrics);
    }

    private static void WriteSummary(string output, List<Sample> samples)
    {
        var lines = new List<string> { "scenario,solver,median_ms,min_ms,max_ms,median_allocated_bytes,values,moves,statuses" };
        foreach (var group in samples.GroupBy(s => (s.Scenario, s.Solver)))
        {
            var times = group.Select(s => s.Milliseconds).Order().ToArray();
            var memory = group.Select(s => s.AllocatedBytes).Order().ToArray();
            double median = (times[(times.Length - 1) / 2] + times[times.Length / 2]) / 2;
            long medianBytes = memory[(memory.Length - 1) / 2] + (memory[memory.Length / 2] - memory[(memory.Length - 1) / 2]) / 2;
            lines.Add($"{group.Key.Scenario},{group.Key.Solver},{median:F3},{times.First():F3},{times.Last():F3},{medianBytes},"
                + string.Join('|', group.Select(s => s.Value).Distinct()) + "," + string.Join('|', group.Select(s => s.Moves).Distinct()) + ","
                + string.Join('|', group.Select(s => s.Status).Distinct()));
        }
        File.WriteAllLines(Path.Combine(output, "summary.csv"), lines);
    }

    private static void Fuzz(string output, int tiny, int satchel, int buffer, int crowded, int seed, int seconds)
    {
        int differences = 0, oracleMisses = 0, cases = 0;
        var records = new List<object>();
        foreach (var state in Enumerable.Range(seed, tiny).Select(Scenarios.Tiny)
            .Concat(Enumerable.Range(seed, buffer).Select(Scenarios.Buffer))
            .Concat(Enumerable.Range(seed, crowded).Select(Scenarios.Crowded))
            .Concat(Enumerable.Range(seed, satchel).Select(Scenarios.Satchel)))
        {
            IdleGate.Wait();
            bool small = state.StateId.StartsWith("tiny-") || state.StateId.StartsWith("buffer-");
            var expected = small ? Oracle(state) : (Value: state.StateId.StartsWith("crowded-")
                ? CrowdedTarget(state) : state.Items.Sum(i => i.UnitValue * i.UnitCount), Moves: -1);
            foreach (var (name, options) in new[] { ("tight", new PlanningOptions(32, 2000)), ("default", Defaults) })
            {
                var outcomes = new List<Outcome>();
                foreach (var solver in Solvers)
                {
                    // Also gates between expensive fuzz solves; no other solver or oracle runs concurrently.
                    IdleGate.Wait();
                    var before = Copy(state);
                    using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
                    PlanningResult result;
                    try
                    {
                        result = solver.Solve(new(state), options, cancel.Token);
                        Validate(before, state, result);
                        if (ReportedBound(result) is long bound && bound < expected.Value)
                            throw new InvalidOperationException($"Upper bound {bound} excludes oracle/witness value {expected.Value}.");
                    }
                    catch
                    {
                        File.WriteAllText(Path.Combine(output, $"failure-{state.StateId}-{name}.json"), PackJson.Serialize(state, options));
                        throw;
                    }
                    outcomes.Add(new(solver.GetType().Name, result.FinalValue, result.Plan.Steps.Count, result.Status.ToString()));
                }
                bool differs = outcomes.Select(o => (o.Value, o.Moves)).Distinct().Count() > 1;
                bool misses = outcomes.Any(o => o.Value != expected.Value || small && o.Moves != expected.Moves);
                cases++;
                if (differs) differences++;
                if (misses) oracleMisses++;
                records.Add(new
                {
                    Scenario = state.StateId,
                    Budget = name,
                    ExpectedValue = expected.Value,
                    ExpectedMoves = small ? (int?)expected.Moves : null,
                    Outcomes = outcomes,
                    Difference = differs,
                    Miss = misses
                });
                if (differs || misses)
                {
                    File.WriteAllText(Path.Combine(output, $"difference-{state.StateId}-{name}.json"), PackJson.Serialize(state, options));
                    Console.WriteLine($"Difference {state.StateId}/{name}: expected {expected}; " + string.Join(", ", outcomes));
                }
            }
            if (cases % 20 == 0) Console.WriteLine($"Fuzz: {cases} case/budget pairs, {differences} disagreements, {oracleMisses} oracle/witness misses.");
            File.WriteAllText(Path.Combine(output, "fuzz.json"), JsonSerializer.Serialize(new { cases, differences, oracleMisses, records }, Json));
        }
    }

    private static long CrowdedTarget(PackState state)
    {
        // Two hands + three bag cells: at most five unit-square loot items can be returned.
        // All generated ground values exceed the held values. A seven-transfer witness drops
        // the two cheap held items into the spare ground cells, then packs the best five.
        var top = state.Items.Where(i => i.Location.InventoryId == "ground").OrderByDescending(i => i.UnitValue).Take(5).ToArray();
        var steps = new List<TransferStep>
        {
            new("left", new("ground", new Placement.Grid(7))),
            new("right", new("ground", new Placement.Grid(8))),
            new(top[0].Id, new("left", new Placement.Slot())),
            new(top[1].Id, new("right", new Placement.Slot()))
        };
        for (int i = 2; i < 5; i++) steps.Add(new(top[i].Id, new("inside", new Placement.Grid(i - 2))));
        long target = 2 + top.Sum(i => i.UnitValue);
        if (PlanReplay.ReturnedValue(PlanReplay.Replay(state, new(state.StateId, steps))) != target)
            throw new InvalidOperationException("Crowded target witness failed.");
        return target;
    }

    // Deliberately independent from each solver's candidates, pruning, keys and heuristic.
    // Replay supplies only common legal-transfer semantics. BFS proves minimum moves at maximum
    // value on these tiny one-row rectangular graphs. Vertical 2x1 rotations cannot fit a
    // one-row grid, and all other rectangle transforms have identical occupancy. Slots have
    // fixed orientation. It is not an oracle for arbitrary captures.
    private static (long Value, int Moves) Oracle(PackState initial)
    {
        string Key(PackState state) => string.Join(";", state.Items.OrderBy(i => i.Id).Select(i =>
            $"{i.Id}:{i.Location.InventoryId}:" + (i.Location.Placement is Placement.Grid p ? $"{p.X},{p.Y}" : "slot")));
        var seen = new HashSet<string> { Key(initial) };
        var queue = new Queue<(PackState State, int Depth)>();
        queue.Enqueue((initial, 0));
        var best = (Value: PlanReplay.ReturnedValue(initial), Moves: 0);
        while (queue.TryDequeue(out var node))
        {
            long value = PlanReplay.ReturnedValue(node.State);
            if (value > best.Value) best = (value, node.Depth);
            foreach (var item in node.State.Items)
                foreach (var inventory in node.State.Inventories)
                {
                    IEnumerable<Placement> positions = inventory.Layout is InventoryLayout.Grid grid
                        ? Enumerable.Range(0, grid.Cells.Width).Select(x => (Placement)new Placement.Grid(x))
                        : [new Placement.Slot()];
                    foreach (var position in positions)
                    {
                        PackState next;
                        try { next = PlanReplay.Replay(node.State, new(node.State.StateId, [new(item.Id, new(inventory.Id, position))])); }
                        catch (InvalidOperationException) { continue; }
                        if (!seen.Add(Key(next))) continue;
                        if (seen.Count > 20000) throw new InvalidOperationException("Tiny oracle exceeded its expected finite domain.");
                        queue.Enqueue((next, node.Depth + 1));
                    }
                }
        }
        return best;
    }
}
