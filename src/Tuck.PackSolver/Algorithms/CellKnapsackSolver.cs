using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace Tuck.PackSolver.Algorithms;

/// <summary>An exact solution to the pooled-area relaxation, not a geometric packing.</summary>
internal sealed class CellAllocation
{
    public PackProblem Problem { get; }
    public ReadOnlyCollection<PackingItem> PackedItems { get; }
    public ReadOnlyCollection<PackingItem> HandItems { get; }
    public long TotalValue { get; }
    /// <summary>Relaxed cell/token cost; a flexible slot costs one token regardless of item area.</summary>
    public int UsedCells { get; }
    internal byte[] Choices { get; } // 0 = leave, 1 = pack, 2 = hand

    internal CellAllocation(PackProblem problem, byte[] choices, long totalValue, int usedCells)
    {
        Problem = problem;
        Choices = choices;
        PackedItems = Array.AsReadOnly(problem.Items.Where((_, i) => choices[i] == 1).ToArray());
        HandItems = Array.AsReadOnly(problem.Items.Where((_, i) => choices[i] == 2).ToArray());
        TotalValue = totalValue + problem.FixedValue;
        UsedCells = usedCells;
    }
}

/// <summary>
/// Each item is left, packed using its minimum cell/token cost, or held using one hand. Uses O(n*A*(h+1)) time
/// and traceback bytes, plus O(A*(h+1)) scores. Geometry, slot competition and owner activation are relaxed.
/// </summary>
internal static class CellKnapsackSolver
{
    /// <returns>Null if even the area relaxation cannot retain every required item.</returns>
    public static CellAllocation? Solve(PackProblem problem, CancellationToken cancellationToken = default)
    {
        if (problem == null) throw new ArgumentNullException(nameof(problem));
        return Solve(new PlacementCatalog(problem, cancellationToken), null, cancellationToken);
    }

    internal static CellAllocation? Solve(PlacementCatalog catalog, bool[]? excluded, CancellationToken cancellationToken)
    {
        var problem = catalog.Problem;
        int count = problem.Items.Count;
        long usefulArea = 0;
        for (int i = 0; i < count; i++)
            if ((excluded == null || !excluded[i]) && catalog.ByItem[i].Count != 0) usefulArea += catalog.MinimumCellCosts[i];
        int capacity = (int)Math.Min(problem.FreeCellCount, usefulArea);
        int handStates = problem.AvailableHands + 1;
        int states = checked((capacity + 1) * handStates);
        var previous = new long[states]; // At-most capacity: empty prefix is feasible in every state.
        var current = new long[states];
        var decisions = new byte[checked(count * states)];

        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = problem.Items[i];
            int cost = catalog.MinimumCellCosts[i];
            bool available = excluded == null || !excluded[i];
            bool canPack = available && catalog.ByItem[i].Count != 0;
            for (int cells = 0; cells <= capacity; cells++)
                for (int hands = 0; hands < handStates; hands++)
                {
                    int state = cells * handStates + hands;
                    long best = item.Required ? -1 : previous[state];
                    byte choice = 0;
                    if (canPack && cells >= cost)
                    {
                        long prefix = previous[(cells - cost) * handStates + hands];
                        if (prefix >= 0 && prefix + item.Value > best)
                        {
                            best = prefix + item.Value;
                            choice = 1;
                        }
                    }
                    if (available && item.CanUseHand && hands > 0)
                    {
                        long prefix = previous[state - 1];
                        if (prefix >= 0 && prefix + item.Value > best)
                        {
                            best = prefix + item.Value;
                            choice = 2;
                        }
                    }
                    current[state] = best;
                    decisions[i * states + state] = choice;
                }
            (previous, current) = (current, previous);
        }

        long total = previous[states - 1];
        if (total < 0) return null;
        int remainingCells = capacity;
        int remainingHands = problem.AvailableHands;
        var choices = new byte[count];
        for (int i = count - 1; i >= 0; i--)
        {
            byte choice = decisions[i * states + remainingCells * handStates + remainingHands];
            choices[i] = choice;
            if (choice == 1) remainingCells -= catalog.MinimumCellCosts[i];
            if (choice == 2) remainingHands--;
        }
        return new CellAllocation(problem, choices, total, capacity - remainingCells);
    }
}
