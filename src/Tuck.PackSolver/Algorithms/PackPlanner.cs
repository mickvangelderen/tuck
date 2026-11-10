using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace Tuck.PackSolver.Algorithms;

/// <summary>Deterministic work limits for geometry search; catalog generation and the DP are not counted.</summary>
internal sealed class PackingOptions
{
    public int MaxShuffleNodes { get; }
    public int MaxPlacementChecks { get; }
    public int MaxRepairPasses { get; }
    public int MaxContainerArrangements { get; }

    public PackingOptions(int maxShuffleNodes = 20_000, int maxPlacementChecks = 2_000_000, int maxRepairPasses = 4,
        int maxContainerArrangements = 128)
    {
        if (maxShuffleNodes < 0) throw new ArgumentOutOfRangeException(nameof(maxShuffleNodes));
        if (maxPlacementChecks < 0) throw new ArgumentOutOfRangeException(nameof(maxPlacementChecks));
        if (maxRepairPasses < 0) throw new ArgumentOutOfRangeException(nameof(maxRepairPasses));
        if (maxContainerArrangements < 0) throw new ArgumentOutOfRangeException(nameof(maxContainerArrangements));
        MaxShuffleNodes = maxShuffleNodes;
        MaxPlacementChecks = maxPlacementChecks;
        MaxRepairPasses = maxRepairPasses;
        MaxContainerArrangements = maxContainerArrangements;
    }
}

/// <summary>A legal final layout for the snapshot; not a sequence of game inventory transfers.</summary>
internal sealed class PackPlan
{
    public ReadOnlyCollection<ItemPlacement> Placements { get; }
    public ReadOnlyCollection<PackingItem> HandItems { get; }
    public ReadOnlyCollection<PackingItem> LeftItems { get; }
    public long TotalValue { get; }

    internal PackPlan(PackProblem problem, IEnumerable<ItemPlacement> placements, IEnumerable<int> hands)
    {
        var packed = placements.OrderBy(p => p.ItemIndex).ToArray();
        var handIndices = hands.ToArray();
        var returned = new HashSet<int>(packed.Select(p => p.ItemIndex).Concat(handIndices));
        Placements = Array.AsReadOnly(packed);
        HandItems = Array.AsReadOnly(handIndices.Select(i => problem.Items[i]).ToArray());
        LeftItems = Array.AsReadOnly(problem.Items.Where((_, i) => !returned.Contains(i)).ToArray());
        TotalValue = problem.FixedValue + packed.Sum(p => p.Item.Value) + HandItems.Sum(i => i.Value);
    }

    // A branch contains only selected container shells. Return identities, destination indices and LeftItems
    // from the original problem so callers never need to understand the search's temporary copies.
    internal PackPlan Rebase(PackProblem original)
    {
        var items = original.Items.Select((item, index) => (item.Id, index)).ToDictionary(p => p.Id, p => p.index);
        var containers = original.Containers.Select((container, index) => (container.Id, index))
            .ToDictionary(p => p.Id, p => p.index);
        return new PackPlan(original, Placements.Select(p => new ItemPlacement(original, items[p.Item.Id],
            containers[p.Container.Id], p.Orientation, p.X, p.Y, p.Words)), HandItems.Select(i => items[i.Id]));
    }
}

internal sealed class PackingResult
{
    /// <summary>Null proves the required items cannot fit even in the area relaxation.</summary>
    public CellAllocation? CellAllocation { get; }
    /// <summary>Null means no legal plan retaining all required items was found, not necessarily infeasibility.</summary>
    public PackPlan? Plan { get; }
    public bool ReachedCellUpperBound => Plan != null && CellAllocation != null && Plan.TotalValue == CellAllocation.TotalValue;
    /// <summary>True if container selection or any geometric search was cut short, even if a later attempt succeeded.</summary>
    public bool SearchBudgetExhausted { get; }
    public int ShuffleNodes { get; }
    public int PlacementChecks { get; }
    public int RepairPasses { get; }
    /// <summary>Number of retained-container subsets evaluated; zero for a problem without owned storage.</summary>
    public int ContainerArrangements { get; }

    internal PackingResult(CellAllocation? allocation, PackPlan? plan, bool exhausted,
        int shuffleNodes, int placementChecks, int repairPasses, int containerArrangements = 0)
    {
        CellAllocation = allocation;
        Plan = plan;
        SearchBudgetExhausted = exhausted;
        ShuffleNodes = shuffleNodes;
        PlacementChecks = placementChecks;
        RepairPasses = repairPasses;
        ContainerArrangements = containerArrangements;
    }
}

internal static class PackPlanner
{
    /// <summary>Selects containers and packs a rooted, acyclic layout. No Unity access or inventory mutation.</summary>
    public static PackingResult Plan(PackProblem problem, PackingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (problem == null) throw new ArgumentNullException(nameof(problem));
        if (problem.HasOwnedContainers)
            return new ContainerArrangementSearch(problem, options ?? new PackingOptions(), cancellationToken).Run();
        return PlanArrangement(problem, options ?? new PackingOptions(), cancellationToken);
    }

    // Every storage owner in this subproblem has been selected and made Required by the outer search.
    internal static PackingResult PlanArrangement(PackProblem problem, PackingOptions options,
        CancellationToken cancellationToken, long incumbentValue = -1)
    {
        var catalog = new PlacementCatalog(problem, cancellationToken);
        var allocation = CellKnapsackSolver.Solve(catalog, null, cancellationToken);
        if (allocation == null) return new PackingResult(null, null, false, 0, 0, 0);
        if (allocation.TotalValue <= incumbentValue) return new PackingResult(allocation, null, false, 0, 0, 0);
        return GeometricPacker.Pack(catalog, allocation, options, cancellationToken);
    }
}

/// <summary>Turns a cell allocation into a legal layout, with bounded reshuffling and replacement selection.</summary>
internal static class GeometricPacker
{
    public static PackingResult Pack(CellAllocation allocation, PackingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (allocation == null) throw new ArgumentNullException(nameof(allocation));
        if (allocation.Problem.HasOwnedContainers)
            return PackPlanner.Plan(allocation.Problem, options, cancellationToken);
        return Pack(new PlacementCatalog(allocation.Problem, cancellationToken), allocation,
            options ?? new PackingOptions(), cancellationToken);
    }

    internal static PackingResult Pack(PlacementCatalog catalog, CellAllocation allocation,
        PackingOptions options, CancellationToken cancellationToken)
    {
        return new PackingSearch(catalog, options, cancellationToken).Run(allocation);
    }
}

internal sealed class PackingSearch
{
    private readonly PlacementCatalog _catalog;
    private readonly PackProblem _problem;
    private readonly PackingOptions _options;
    private readonly CancellationToken _cancellationToken;
    private PackPlan? _best;
    private int _placementChecks;
    private int _shuffleNodes;
    private bool _exhausted;
    private long _upperBound;

    public PackingSearch(PlacementCatalog catalog, PackingOptions options, CancellationToken cancellationToken)
    {
        _catalog = catalog;
        _problem = catalog.Problem;
        _options = options;
        _cancellationToken = cancellationToken;
    }

    public PackingResult Run(CellAllocation initial)
    {
        _upperBound = initial.TotalValue;
        var excluded = new bool[_problem.Items.Count];
        var allocation = initial;
        int repairs = 0;
        // Even a zero-budget call can return a valid hands-only plan when the required items allow it.
        Consider(Array.Empty<ItemPlacement>());
        while (true)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            for (int strategy = 0; strategy < 4 && !AtUpperBound; strategy++)
            {
                // This ordering ignores the allocation and would reproduce the first pass's layout.
                if (strategy == 1 && repairs > 0) continue;
                BuildGreedy(allocation, strategy);
                if (_placementChecks >= _options.MaxPlacementChecks) break;
            }
            if (!AtUpperBound && (_best == null || _best.TotalValue < allocation.TotalValue))
                Shuffle(allocation);
            if (AtUpperBound || repairs >= _options.MaxRepairPasses
                || _placementChecks >= _options.MaxPlacementChecks) break;

            // Geometry failure is not proof of infeasibility. Explore a different DP selection by excluding
            // a low-density optional selected item, including one already packed that may be blocking
            // a more valuable item. Keep the original upper bound and the best legal plan.
            int rejected = Enumerable.Range(0, _problem.Items.Count)
                .Where(i => allocation.Choices[i] != 0 && !_problem.Items[i].Required && !excluded[i])
                .OrderBy(i => (decimal)_problem.Items[i].Value / _catalog.MinimumCellCosts[i])
                .ThenByDescending(i => _problem.Items[i].Area).ThenBy(i => i).DefaultIfEmpty(-1).First();
            if (rejected < 0) break;
            excluded[rejected] = true;
            var next = CellKnapsackSolver.Solve(_catalog, excluded, _cancellationToken);
            if (next == null) break;
            allocation = next;
            repairs++;
        }
        bool exhausted = _exhausted || (!AtUpperBound && _placementChecks >= _options.MaxPlacementChecks);
        return new PackingResult(initial, _best, exhausted, _shuffleNodes, _placementChecks, repairs);
    }

    private bool AtUpperBound => _best != null && _best.TotalValue == _upperBound;

    private void BuildGreedy(CellAllocation allocation, int strategy)
    {
        var layout = new PackingLayout(_problem);
        var placed = new List<ItemPlacement>();
        var order = Enumerable.Range(0, _problem.Items.Count).ToArray();
        Array.Sort(order, (a, b) => CompareItems(a, b, allocation, strategy));
        // One start preserves the DP's proposed hands; others may pack them and hold something else.
        PlaceInOrder(order.Where(i => (strategy != 0 || allocation.Choices[i] != 2)
            && (_problem.Items[i].Value > 0 || _problem.Items[i].Required)), layout, placed, strategy < 2);
        Consider(placed);
    }

    private int CompareItems(int a, int b, CellAllocation allocation, int strategy)
    {
        var left = _problem.Items[a];
        var right = _problem.Items[b];
        int order = right.Required.CompareTo(left.Required);
        if (order != 0) return order;
        // One unconstrained density start provides a fallback when the relaxed selection packs poorly.
        if (strategy != 1)
        {
            order = (allocation.Choices[b] != 0).CompareTo(allocation.Choices[a] != 0);
            if (order != 0) return order;
        }
        if (strategy == 2)
            order = _catalog.ByItem[a].Count.CompareTo(_catalog.ByItem[b].Count);
        else if (strategy == 3)
            order = right.Area.CompareTo(left.Area);
        else
            order = ((decimal)right.Value / _catalog.MinimumCellCosts[b])
                .CompareTo((decimal)left.Value / _catalog.MinimumCellCosts[a]);
        if (order == 0) order = right.Value.CompareTo(left.Value);
        if (order == 0) order = a.CompareTo(b);
        return order;
    }

    private ItemPlacement? FindPlacement(int item, PackingLayout layout)
    {
        ItemPlacement? best = null;
        int bestContacts = -1;
        foreach (var placement in _catalog.ByItem[item])
        {
            if (!SpendPlacementCheck()) break;
            if (!layout.Fits(placement)) continue;
            int contacts = layout.BoundaryContacts(placement);
            if (best == null || placement.CellCost < best.CellCost
                || (placement.CellCost == best.CellCost && (contacts > bestContacts || (contacts == bestContacts
                    && layout.FreeCells[placement.ContainerIndex] < layout.FreeCells[best.ContainerIndex]))))
            {
                best = placement;
                bestContacts = contacts;
            }
        }
        return best;
    }

    private bool SpendPlacementCheck()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (_placementChecks >= _options.MaxPlacementChecks)
        {
            _exhausted = true;
            return false;
        }
        _placementChecks++;
        return true;
    }

    private void Shuffle(CellAllocation allocation)
    {
        var selected = Enumerable.Range(0, _problem.Items.Count).Where(i => allocation.Choices[i] != 0)
            .OrderBy(i => _problem.Items[i].CanUseHand)
            .ThenBy(i => _catalog.ByItem[i].Count)
            .ThenByDescending(i => _problem.Items[i].Area).ThenBy(i => i).ToArray();
        var layout = new PackingLayout(_problem);
        var placed = new List<ItemPlacement>();
        var held = new HashSet<int>();
        var previousEquivalent = new int[selected.Length];
        var choiceRanks = new int[selected.Length];
        for (int depth = 0; depth < selected.Length; depth++)
        {
            previousEquivalent[depth] = -1;
            for (int earlier = depth - 1; earlier >= 0; earlier--)
                if (Interchangeable(selected[earlier], selected[depth]))
                {
                    previousEquivalent[depth] = earlier;
                    break;
                }
        }
        // Share backtracking effort across replacement selections, instead of exhausting it on the first one.
        int allowance = (int)Math.Max(1L, _options.MaxShuffleNodes / ((long)_options.MaxRepairPasses + 1));
        int nodeLimit = (int)Math.Min(_options.MaxShuffleNodes, (long)_shuffleNodes + allowance);

        bool Search(int depth, int hands)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (_shuffleNodes >= nodeLimit)
            {
                _exhausted = true;
                return false;
            }
            _shuffleNodes++;
            if (depth == selected.Length)
            {
                // Record the complete selection before spending any remaining budget filling spare cells.
                Consider(placed);
                FillRemainder(layout, placed, held);
                return true;
            }
            if (!EnoughArea(selected, depth, hands, layout)) return false;
            int item = selected[depth];
            var placements = _catalog.ByItem[item];
            // Interchangeable items have identically ordered catalogs. Keep their choices in increasing
            // order, with hands last, so exchanging their identities does not create another search branch.
            int minimumRank = previousEquivalent[depth] < 0 ? 0 : choiceRanks[previousEquivalent[depth]];

            bool TryHand()
            {
                if (hands == 0 || !_problem.Items[item].CanUseHand) return false;
                choiceRanks[depth] = placements.Count;
                held.Add(item);
                bool found = Search(depth + 1, hands - 1);
                held.Remove(item);
                return found;
            }

            bool handFirst = allocation.Choices[item] == 2;
            if (handFirst && TryHand()) return true;
            for (int rank = minimumRank; rank < placements.Count; rank++)
            {
                var placement = placements[rank];
                if (!SpendPlacementCheck()) return false;
                if (!layout.Fits(placement)) continue;
                // Only a feasible placement needs another node. Colliding placements do not constitute
                // skipped search work when we finish exactly at the node limit.
                if (_shuffleNodes >= nodeLimit)
                {
                    _exhausted = true;
                    return false;
                }
                choiceRanks[depth] = rank;
                layout.Add(placement);
                placed.Add(placement);
                bool found = Search(depth + 1, hands);
                if (found) return true;
                placed.RemoveAt(placed.Count - 1);
                layout.Remove(placement);
            }
            return !handFirst && TryHand();
        }

        Search(0, _problem.AvailableHands);
    }

    private bool Interchangeable(int first, int second)
    {
        var a = _problem.Items[first];
        var b = _problem.Items[second];
        // Owners with identical shells still activate different inventories and have different cycle constraints.
        if (_problem.Containers.Any(c => c.OwnerItemId == a.Id || c.OwnerItemId == b.Id)) return false;
        if (a.Value != b.Value || a.Area != b.Area || a.Required != b.Required || a.CanUseHand != b.CanUseHand
            || a.Orientations.Count != b.Orientations.Count) return false;
        foreach (var container in _problem.Containers)
            if (a.CanEnter(container.Id) != b.CanEnter(container.Id)) return false;
        for (int o = 0; o < a.Orientations.Count; o++)
        {
            var left = a.Orientations[o];
            var right = b.Orientations[o];
            if (left.QuarterTurns != right.QuarterTurns || left.Flipped != right.Flipped
                || left.Shape.Width != right.Shape.Width || left.Shape.Height != right.Shape.Height
                || !left.Shape.Cells.SequenceEqual(right.Shape.Cells)) return false;
        }
        return true;
    }

    private bool EnoughArea(int[] selected, int depth, int hands, PackingLayout layout)
    {
        long area = 0;
        int largest = 0, second = 0;
        for (int p = depth; p < selected.Length; p++)
        {
            int index = selected[p];
            var item = _problem.Items[index];
            int cost = _catalog.MinimumCellCosts[index];
            area += cost;
            if (!item.CanUseHand) continue;
            if (cost > largest) { second = largest; largest = cost; }
            else if (cost > second) second = cost;
        }
        if (hands > 0) area -= largest;
        if (hands > 1) area -= second;
        return area <= layout.FreeCells.Sum();
    }

    private void FillRemainder(PackingLayout layout, List<ItemPlacement> placed, HashSet<int> held)
    {
        var packed = new HashSet<int>(placed.Select(p => p.ItemIndex));
        var remaining = Enumerable.Range(0, _problem.Items.Count)
            .Where(i => !packed.Contains(i) && !held.Contains(i) && _problem.Items[i].Value > 0)
            .OrderByDescending(i => (decimal)_problem.Items[i].Value / _catalog.MinimumCellCosts[i])
            .ThenBy(i => i);
        PlaceInOrder(remaining, layout, placed, true);
        Consider(placed);
    }

    private void PlaceInOrder(IEnumerable<int> order, PackingLayout layout, List<ItemPlacement> placed,
        bool adaptToSlots = false)
    {
        int[] pending = order.ToArray();
        int AvailableCost(int i) => _catalog.SlotsByItem[i].Any(layout.Fits) ? 1 : _problem.Items[i].Area;
        var densityOrder = Comparer<int>.Create((a, b) =>
        {
            int comparison = _problem.Items[b].Required.CompareTo(_problem.Items[a].Required);
            if (comparison == 0) comparison = ((decimal)_problem.Items[b].Value / AvailableCost(b))
                .CompareTo((decimal)_problem.Items[a].Value / AvailableCost(a));
            return comparison != 0 ? comparison : a.CompareTo(b);
        });
        for (int next = 0; next < pending.Length; next++)
        {
            int i = pending[next];
            var placement = FindPlacement(i, layout);
            if (placement != null)
            {
                layout.Add(placement);
                placed.Add(placement);
                // Once a flexible slot is used, other bulky items may need their full grid area. Re-rank
                // density rather than continuing to price all of them at the DP's optimistic one-token cost.
                if (adaptToSlots && placement.Container.IsSlot)
                    Array.Sort(pending, next + 1, pending.Length - next - 1, densityOrder);
            }
            // Avoid scanning the rest of the order after consuming the shared geometry budget.
            if (_placementChecks >= _options.MaxPlacementChecks) break;
        }
    }

    private void Consider(IEnumerable<ItemPlacement> placements)
    {
        var packed = placements.ToArray();
        var packedIndices = new HashSet<int>(packed.Select(p => p.ItemIndex));
        var remaining = Enumerable.Range(0, _problem.Items.Count).Where(i => !packedIndices.Contains(i)).ToArray();
        var required = remaining.Where(i => _problem.Items[i].Required).ToArray();
        if (required.Length > _problem.AvailableHands || required.Any(i => !_problem.Items[i].CanUseHand)) return;
        var hands = required.Concat(remaining.Where(i => !_problem.Items[i].Required && _problem.Items[i].CanUseHand
                && _problem.Items[i].Value > 0)
            .OrderByDescending(i => _problem.Items[i].Value).ThenBy(i => i)
            .Take(_problem.AvailableHands - required.Length));
        var plan = new PackPlan(_problem, packed, hands);
        if (_best == null || plan.TotalValue > _best.TotalValue
            || (plan.TotalValue == _best.TotalValue && plan.Placements.Count < _best.Placements.Count)) _best = plan;
    }
}
