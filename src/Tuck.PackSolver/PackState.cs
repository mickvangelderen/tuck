using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Tuck.PackSolver;

public sealed class ItemLocation : IEquatable<ItemLocation>
{
    public string InventoryId { get; }
    public Placement Placement { get; }
    public ItemLocation(string inventoryId, Placement placement)
    { InventoryId = inventoryId; Placement = placement ?? throw new ArgumentNullException(nameof(placement)); }
    public bool Equals(ItemLocation? other) => other != null && InventoryId == other.InventoryId && Placement.Equals(other.Placement);
    public override bool Equals(object? obj) => obj is ItemLocation other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(InventoryId, Placement);
}

/// <summary>Only the tag fields read by packing rules/value, copied from base state.</summary>
public readonly struct TagValue : IEquatable<TagValue>
{
    public bool Enabled { get; }
    public int Int { get; }
    public TagValue(bool enabled, int value = 0) { Enabled = enabled; Int = value; }
    public bool Equals(TagValue other) => Enabled == other.Enabled && Int == other.Int;
}

public sealed class ItemFacts
{
    public ReadOnlyDictionary<string, TagValue> Tags { get; }
    public ReadOnlyCollection<string> Types { get; }
    public ItemFacts(IEnumerable<KeyValuePair<string, TagValue>>? tags = null, IEnumerable<string>? types = null)
    {
        Tags = new((tags ?? Array.Empty<KeyValuePair<string, TagValue>>()).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
        Types = Array.AsReadOnly((types ?? Array.Empty<string>()).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToArray());
    }
    public bool IsTag(string name) => Tags.TryGetValue(name, out var tag) && tag.Enabled;
    public int Int(string name) => Tags.TryGetValue(name, out var tag) ? tag.Int : 0;
}

public sealed class ItemState
{
    public string Id { get; }
    public string Identifier { get; }
    public ItemLocation Location { get; }
    public int UnitCount { get; }
    public long UnitValue { get; }
    // Independent game field used by power-source valuation.
    public long UnitBaseValue { get; }
    public ShapeState Shape { get; }
    public ItemFacts State { get; }
    public ItemState(string id, string identifier, ItemLocation location, int unitCount, long unitValue,
        ShapeState shape, ItemFacts state, long unitBaseValue = 0)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(identifier)) throw new ArgumentException("Missing item identity.");
        if (unitCount < 1) throw new ArgumentOutOfRangeException(nameof(unitCount));
        Id = id; Identifier = identifier; Location = location; UnitCount = unitCount; UnitValue = unitValue;
        Shape = shape; State = state; UnitBaseValue = unitBaseValue;
    }
    internal ItemState At(ItemLocation location) => new(Id, Identifier, location, UnitCount, UnitValue, Shape, State, UnitBaseValue);
}

public enum AdmissionKind { Owned, Ground, Backpack, Container, Toolbox, Tagged, Unsupported }

public sealed class AdmissionRule
{
    public AdmissionKind Kind { get; }
    public ReadOnlyCollection<string>? AllowedTags { get; }
    public ReadOnlyCollection<string>? AllowedIdentifiers { get; }
    public int SlotIndex { get; }
    public bool AllowsHazardousWaste { get; }
    public AdmissionRule(AdmissionKind kind, IEnumerable<string>? allowedTags = null,
        IEnumerable<string>? allowedIdentifiers = null, int slotIndex = 0, bool allowsHazardousWaste = false)
    {
        Kind = kind; SlotIndex = slotIndex; AllowsHazardousWaste = allowsHazardousWaste;
        AllowedTags = allowedTags == null ? null : Array.AsReadOnly(allowedTags.ToArray());
        AllowedIdentifiers = allowedIdentifiers == null ? null : Array.AsReadOnly(allowedIdentifiers.ToArray());
    }
}

/// <summary>
/// Limits supplied by this owner, used only to decide whether an entire existing stack can move.
/// Null maximum means no callback; the game falls back to one only if both owners omit it.
/// These limits do not create quantity choices in the output plan.
/// </summary>
public sealed class QuantityRules
{
    public int? Maximum { get; }
    public int Minimum { get; }
    public int MaxAdd { get; }
    public int MaxRemove { get; }
    public QuantityRules(int? maximum = null, int minimum = 0, int maxAdd = int.MaxValue, int maxRemove = int.MaxValue)
    {
        if (maximum < 1 || minimum < 0 || maxAdd < 0 || maxRemove < 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        Maximum = maximum; Minimum = minimum; MaxAdd = maxAdd; MaxRemove = maxRemove;
    }
}

public enum ParentShapeRule { None, GunAttachment, Unsupported }

/// <summary>Definition-level rules needed to move existing items; no item construction or stacking rules.</summary>
public sealed class ItemRules
{
    public QuantityRules Quantity { get; }
    public ParentShapeRule ParentShape { get; }
    public bool UnsupportedParentState { get; }
    public string? UnsupportedTransfer { get; }
    public ItemRules(QuantityRules? quantity = null,
        ParentShapeRule parentShape = ParentShapeRule.None, bool unsupportedParentState = false, string? unsupportedTransfer = null)
    {
        Quantity = quantity ?? new(); ParentShape = parentShape;
        UnsupportedParentState = unsupportedParentState; UnsupportedTransfer = unsupportedTransfer;
    }
}

public sealed class ItemDefinition
{
    public string Identifier { get; }
    public ItemRules Rules { get; }
    public ItemDefinition(string identifier, ItemRules? rules = null)
    { Identifier = identifier; Rules = rules ?? new(); }
}

public abstract class InventoryLayout
{
    private InventoryLayout() { }
    public sealed class Grid : InventoryLayout
    {
        public ShapeState Cells { get; }
        public Grid(ShapeState cells) => Cells = cells;
    }
    public sealed class Slot : InventoryLayout
    {
        public int Orientation { get; }
        public Slot(int orientation = 0)
        { if (orientation < 0 || orientation > 3) throw new ArgumentOutOfRangeException(nameof(orientation)); Orientation = orientation; }
    }
}

public sealed class InventoryState
{
    public string Id { get; }
    public string? OwnerItemId { get; }
    public InventoryLayout Layout { get; }
    public bool OverrideLockInsert { get; }
    public bool OverrideLockRemove { get; }
    public AdmissionRule Admission { get; }
    public QuantityRules Quantity { get; }
    public string? UnsupportedTransfer { get; }
    public bool UnsupportedParentEffects { get; }
    public InventoryState(string id, InventoryLayout layout, AdmissionRule admission, string? ownerItemId = null,
        bool overrideLockInsert = false, bool overrideLockRemove = false, QuantityRules? quantity = null,
        string? unsupportedTransfer = null, bool unsupportedParentEffects = false)
    {
        Id = id; Layout = layout; Admission = admission; OwnerItemId = ownerItemId;
        OverrideLockInsert = overrideLockInsert; OverrideLockRemove = overrideLockRemove;
        Quantity = quantity ?? new(); UnsupportedTransfer = unsupportedTransfer; UnsupportedParentEffects = unsupportedParentEffects;
    }
}

public sealed class PackRoots
{
    public string Ground { get; }
    public string Backpack { get; }
    public string Left { get; }
    public string Right { get; }
    public PackRoots(string ground, string backpack, string left, string right)
    { Ground = ground; Backpack = backpack; Left = left; Right = right; }
    public bool IsReturn(string id) => id == Backpack || id == Left || id == Right;
}

/// <summary>Immutable base facts. Reverse edges, effective attributes and observations are not stored here.</summary>
public sealed class PackState
{
    public string StateId { get; }
    public PackRoots Roots { get; }
    public ReadOnlyCollection<ItemState> Items { get; }
    public ReadOnlyCollection<InventoryState> Inventories { get; }
    public ReadOnlyDictionary<string, ItemDefinition> Definitions { get; }
    public PackState(string stateId, PackRoots roots, IEnumerable<ItemState> items,
        IEnumerable<InventoryState> inventories, IEnumerable<ItemDefinition> definitions)
    {
        StateId = stateId; Roots = roots;
        Items = Array.AsReadOnly(items.ToArray()); Inventories = Array.AsReadOnly(inventories.ToArray());
        Definitions = new(definitions.ToDictionary(d => d.Identifier, StringComparer.Ordinal));
        var itemMap = Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var invMap = Inventories.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var rootIds = new[] { roots.Ground, roots.Backpack, roots.Left, roots.Right };
        if (rootIds.Distinct().Count() != 4 || rootIds.Any(id => !invMap.ContainsKey(id) || invMap[id].OwnerItemId != null))
            throw new ArgumentException("Four distinct root inventories are required.");
        if (Items.Any(i => !invMap.ContainsKey(i.Location.InventoryId) || !Definitions.ContainsKey(i.Identifier))
            || Inventories.Any(i => i.OwnerItemId != null && !itemMap.ContainsKey(i.OwnerItemId)))
            throw new ArgumentException("Dangling containment/definition reference.");
        if (Inventories.Any(i => i.OwnerItemId == null && !rootIds.Contains(i.Id))
            || invMap[roots.Ground].Layout is not InventoryLayout.Grid || rootIds.Skip(1).Any(id => invMap[id].Layout is not InventoryLayout.Slot))
            throw new ArgumentException("Only the ground and three carry slots can be roots.");
        foreach (var item in Items)
        {
            var seen = new HashSet<string> { item.Id };
            var inv = invMap[item.Location.InventoryId];
            if ((inv.Layout is InventoryLayout.Grid) != (item.Location.Placement is Placement.Grid))
                throw new ArgumentException("Placement does not match inventory kind.");
            while (inv.OwnerItemId != null)
            {
                if (!seen.Add(inv.OwnerItemId)) throw new ArgumentException("Containment cycle.");
                inv = invMap[itemMap[inv.OwnerItemId].Location.InventoryId];
            }
        }
    }
}
