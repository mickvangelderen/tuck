using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Tuck.PackSolver.Algorithms;

/// <summary>
/// Enumerates which container items to retain. The inner packer chooses their parent inventories, positions and
/// orientations together with the loose contents. Selected owners are required, so their capacity cannot be used
/// unless their shells also fit; PackingLayout rejects cycles, leaving every retained tree connected to a root/hand.
/// </summary>
internal sealed class ContainerArrangementSearch
{
    private readonly PackProblem _problem;
    private readonly PackingOptions _options;
    private readonly CancellationToken _cancellationToken;
    private readonly HashSet<string> _owners;
    private readonly HashSet<string> _selected;
    private readonly string[] _optional;
    private readonly HashSet<string> _visited = new(StringComparer.Ordinal);
    private CellAllocation? _upperBound;
    private PackPlan? _best;
    private int _arrangements, _shuffleNodes, _placementChecks, _repairPasses;
    private bool _exhausted, _limitReached;

    public ContainerArrangementSearch(PackProblem problem, PackingOptions options, CancellationToken cancellationToken)
    {
        _problem = problem;
        _options = options;
        _cancellationToken = cancellationToken;
        _owners = new HashSet<string>(problem.Containers.Where(c => c.OwnerItemId != null)
            .Select(c => c.OwnerItemId!), StringComparer.Ordinal);
        _selected = new HashSet<string>(problem.Items.Where(i => i.Required && _owners.Contains(i.Id))
            .Select(i => i.Id), StringComparer.Ordinal);
        _optional = problem.Items.Where(i => _owners.Contains(i.Id) && !i.Required).Select(i => i.Id).ToArray();
    }

    public PackingResult Run()
    {
        // This deliberately grants every interior without requiring its shell: a valid optimistic bound.
        _upperBound = CellKnapsackSolver.Solve(_problem, _cancellationToken);
        if (_upperBound == null) return Result();

        // Seed both extremes and single-container changes before spending the budget on combinations. This gives
        // a low-budget run a baseline without optional containers as well as opportunities to acquire capacity.
        Evaluate();
        foreach (string id in _optional) _selected.Add(id);
        Evaluate();
        ClearOptional();
        foreach (string id in _optional)
        {
            if (Done) break;
            _selected.Add(id);
            Evaluate();
            _selected.Remove(id);
        }
        foreach (string id in _optional) _selected.Add(id);
        foreach (string id in _optional)
        {
            if (Done) break;
            _selected.Remove(id);
            Evaluate();
            _selected.Add(id);
        }
        ClearOptional();
        Enumerate(0);
        return Result();
    }

    private bool Done => _limitReached || (_best != null && _best.TotalValue == _upperBound!.TotalValue);

    private void ClearOptional()
    {
        foreach (string id in _optional) _selected.Remove(id);
    }

    private void Enumerate(int depth)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (Done) return;
        if (depth == _optional.Length) { Evaluate(); return; }
        string id = _optional[depth];
        _selected.Add(id);
        Enumerate(depth + 1);
        _selected.Remove(id);
        Enumerate(depth + 1);
    }

    private void Evaluate()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (Done) return;
        string key = new(_optional.Select(id => _selected.Contains(id) ? '1' : '0').ToArray());
        if (!_visited.Add(key)) return;
        if (_arrangements >= _options.MaxContainerArrangements)
        {
            _limitReached = _exhausted = true;
            return;
        }
        _arrangements++;
        var items = _problem.Items.Where(i => !_owners.Contains(i.Id) || _selected.Contains(i.Id))
            .Select(i => _owners.Contains(i.Id) ? i.AsRequired() : i);
        var containers = _problem.Containers.Where(c => c.OwnerItemId == null || _selected.Contains(c.OwnerItemId));
        var branch = new PackProblem(items, containers, _problem.AvailableHands, _problem.FixedValue);

        // Share the existing geometry budgets across branches. A difficult first arrangement must not consume
        // all the work before alternatives are considered. Zero budgets still permit hands-only solutions.
        int shares = Math.Min(_options.MaxContainerArrangements, 1 << Math.Min(3, _optional.Length));
        int checks = Math.Min(_options.MaxPlacementChecks - _placementChecks,
            Math.Max(1, _options.MaxPlacementChecks / shares));
        int nodes = Math.Min(_options.MaxShuffleNodes - _shuffleNodes, Math.Max(1, _options.MaxShuffleNodes / shares));
        var result = PackPlanner.PlanArrangement(branch,
            new PackingOptions(nodes, checks, _options.MaxRepairPasses), _cancellationToken, _best?.TotalValue ?? -1);
        _placementChecks += result.PlacementChecks;
        _shuffleNodes += result.ShuffleNodes;
        _repairPasses += result.RepairPasses;
        _exhausted |= result.SearchBudgetExhausted;
        if (result.Plan != null && (_best == null || result.Plan.TotalValue > _best.TotalValue))
            _best = result.Plan.Rebase(_problem);
    }

    private PackingResult Result() => new(_upperBound, _best, _exhausted,
        _shuffleNodes, _placementChecks, _repairPasses, _arrangements);
}
