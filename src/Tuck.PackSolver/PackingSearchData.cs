using System;
using System.Collections.Generic;
using System.Linq;

namespace Tuck.PackSolver;

/// <summary>Immutable, invocation-local facts compiled from the public capture for placement search.</summary>
/// <remarks>
/// Whole-item packing preserves identities, counts, intrinsic values and inventory definitions.
/// Compile their ordering and values once; only locations belong in a search key. Effective
/// geometry and return membership may still change with containment and remain the simulator's
/// responsibility. Public input/output retain stable string identities for capture and replay;
/// numeric indexes are private to one compilation and are never serialized into a plan.
/// </remarks>
internal sealed class PackingSearchData
{
    internal readonly string[] ItemIds;
    internal readonly string[] ActionOrder;
    internal readonly InventoryState[] Inventories;
    internal readonly decimal PositiveValueBound;
    private readonly Dictionary<string, int> _inventories;
    private readonly Dictionary<string, decimal> _values;

    internal PackingSearchData(TransferSimulation initial)
    {
        ItemIds = initial.Items.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Inventories = initial.Inventories.Values.OrderBy(i => i.Id, StringComparer.Ordinal).ToArray();
        _inventories = Inventories.Select((inventory, index) => (inventory.Id, index))
            .ToDictionary(p => p.Id, p => p.index, StringComparer.Ordinal);
        _values = ItemIds.ToDictionary(id => id,
            id => (decimal)initial.View.PackUnitValue(id) * initial.Items[id].UnitCount, StringComparer.Ordinal);
        ActionOrder = ItemIds.OrderByDescending(id => _values[id]).ThenBy(id => id, StringComparer.Ordinal).ToArray();
        PositiveValueBound = _values.Values.Sum(value => Math.Max(0, value));
    }

    internal double Value(string id) => (double)_values[id];

    internal PackingStateKey Key(TransferSimulation state)
    {
        var locations = new int[ItemIds.Length * 5];
        for (int i = 0; i < ItemIds.Length; i++)
        {
            var location = state.Items[ItemIds[i]].Location;
            int offset = i * 5;
            locations[offset] = _inventories[location.InventoryId];
            if (location.Placement is Placement.Grid grid)
            {
                locations[offset + 1] = grid.X;
                locations[offset + 2] = grid.Y;
                locations[offset + 3] = grid.Orientation;
                locations[offset + 4] = grid.Flipped ? 1 : 0;
            }
        }
        return new(this, locations);
    }
}

/// <summary>Exact locations in a fixed identity order; hashes accelerate equality, never replace it.</summary>
internal sealed class PackingStateKey : IEquatable<PackingStateKey>
{
    private readonly PackingSearchData _domain;
    private readonly int[] _locations;
    private readonly int _hash;

    internal PackingStateKey(PackingSearchData domain, int[] locations)
    {
        _domain = domain; _locations = locations;
        unchecked
        {
            int hash = 17;
            foreach (int value in locations) hash = hash * 31 + value;
            _hash = hash;
        }
    }

    public bool Equals(PackingStateKey? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other == null || !ReferenceEquals(_domain, other._domain) || _hash != other._hash) return false;
        return _locations.AsSpan().SequenceEqual(other._locations);
    }
    public override bool Equals(object? obj) => obj is PackingStateKey other && Equals(other);
    public override int GetHashCode() => _hash;
}
