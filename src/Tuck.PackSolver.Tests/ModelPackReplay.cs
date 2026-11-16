using Tuck.ProbablyStolenTestModel;

namespace Tuck.PackSolver.Tests;

// Import fixture facts, then let the independently ported game inventory methods validate and
// execute moves. This runner never calls TransferSimulation, StateView or PlanReplay.
internal sealed class ModelPackReplay
{
    private readonly PackState initial;
    private readonly HashSet<string> factoryStorage = new();
    private readonly Dictionary<string, ItemFacts> initialFacts = new();
    internal Dictionary<string, ModelItem> Items { get; } = new();
    internal Dictionary<string, GameInventory> Inventories { get; } = new();

    internal ModelPackReplay(PackState state)
    {
        initial = state;
        foreach (var inventory in state.Inventories)
        {
            GameInventory model = inventory.Layout is InventoryLayout.Grid grid
                ? new GameGridInventory(Shape(grid.Cells))
                : new GameSlotInventory(((InventoryLayout.Slot)inventory.Layout).Orientation);
            model.identifier = inventory.Id;
            model.overrideLockInsert = inventory.OverrideLockInsert;
            model.overrideLockRemove = inventory.OverrideLockRemove;
            var quantity = inventory.Quantity;
            if (quantity.Maximum is int maximum) model.maxSlotAmountItemFunc = (_, _, _) => maximum;
            model.minSlotAmountItemFunc = (_, _, _) => quantity.Minimum;
            model.maxSlotAddItemFunc = (_, _, _) => quantity.MaxAdd;
            model.maxSlotRemoveItemFunc = (_, _, _) => quantity.MaxRemove;
            Inventories.Add(inventory.Id, model);
        }
        foreach (var item in state.Items)
        {
            var quantity = state.Definitions[item.Identifier].Rules.Quantity;
            var model = new ModelItem(item.Identifier, Shape(item.Shape), item.UnitCount,
                stackCapacity: Math.Max(item.UnitCount, quantity.Maximum ?? 1))
            { unitValue = item.UnitValue, unitBaseValue = item.UnitBaseValue };
            foreach (var tag in item.State.Tags)
                model.state.GetTag(tag.Key).SetEnabled(tag.Value.Enabled).SetInt(tag.Value.Int);
            model.modifiedState = model.state.Clone();
            model.itemTypes.AddRange(item.State.Types);
            model.maxSlotAmountItemFunc = quantity.Maximum is int maximum ? (_, _, _) => maximum : null;
            model.minSlotAmountItemFunc = (_, _, _) => quantity.Minimum;
            model.maxSlotAddItemFunc = (_, _, _) => quantity.MaxAdd;
            model.maxSlotRemoveItemFunc = (_, _, _) => quantity.MaxRemove;
            Items.Add(item.Id, model);
        }
        foreach (var group in state.Inventories.Where(i => i.OwnerItemId != null).GroupBy(i => i.OwnerItemId!))
        {
            var children = group.Select(i => (GraphNodeStorage)Inventories[i.Id]).ToArray();
            Items[group.Key].SetContentWindow(new PixelWindow(children.Length == 1 ? children[0] : new MachineryPanelRow(children)));
        }
        foreach (var inventory in state.Inventories) SetAdmission(inventory);
        // Initial placement is a fixture restore, not a proposed move: locked equipment is valid input.
        foreach (var item in state.Items)
        {
            var model = Items[item.Id];
            model.modifiedShape = At(model, item.Location);
            Assert.True(Inventories[item.Location.InventoryId].UncheckedAccept(model), $"Cannot restore {item.Id}.");
        }
        foreach (var item in state.Items)
            if (state.Definitions[item.Identifier].Rules.ParentShape == ParentShapeRule.GunAttachment)
                ModelGunGeometry.InitAttachment(Items[item.Id]);
        foreach (var item in state.Items)
        {
            ModelGunGeometry.Resolve(Items[item.Id]);
            Items[item.Id].modifiedShape = At(Items[item.Id], item.Location);
            initialFacts.Add(item.Id, Facts(Items[item.Id]));
        }
        foreach (var item in state.Items)
        {
            var rules = state.Definitions[item.Identifier].Rules;
            if (rules.ParentShape == ParentShapeRule.Unsupported || rules.UnsupportedParentState)
                Items[item.Id].UnsupportedBehavior = "Unrepresented parent shape/state modifier.";
        }
        foreach (var inventory in state.Inventories.Where(i => i.UnsupportedParentEffects))
            Inventories[inventory.Id].parentItemStateModifierForItemFunc = (Action)(() =>
                throw new NotSupportedException("Unrepresented inventory parent effect."));
    }

    private void SetAdmission(InventoryState inventory)
    {
        var model = Inventories[inventory.Id];
        var owner = inventory.OwnerItemId == null ? null : Items[inventory.OwnerItemId];
        var rule = inventory.Admission;
        // Reconstruct known game factories independently, even if capture classified the bay incorrectly.
        if (owner?.identifier is "machine_bay" or "machine_bay_ext" && model is GameGridInventory)
        {
            ModelStorageRules.InitMachineBay(model, owner);
            factoryStorage.Add(inventory.Id);
            return;
        }
        switch (rule.Kind)
        {
            case AdmissionKind.Container:
                ModelStorageRules.InitContainerItem(model, owner!, rule.AllowedTags?.ToList(), rule.AllowedIdentifiers?.ToList());
                break;
            case AdmissionKind.MachineBay:
                ModelStorageRules.InitMachineBay(model, owner!);
                break;
            case AdmissionKind.Toolbox:
                ModelStorageRules.InitToolboxSlot(model, owner!, rule.SlotIndex);
                break;
            case AdmissionKind.Backpack:
                ContainerHelper.AllowOnlyTaggedItem(model, "BACKPACK_TAG", ownedOnly: true);
                break;
            case AdmissionKind.Ground:
                model.mayInventoryAddItemFunc = (item, _) => GeneralHelper.IsItemOwned(item)
                    && (rule.AllowsHazardousWaste || !item.IsTag("HAZARDOUS_WASTE_TAG"));
                break;
            case AdmissionKind.Owned:
                ContainerHelper.AllowOnlyOwnedItems(model);
                break;
            case AdmissionKind.Tagged:
                model.mayInventoryAddItemFunc = (item, _) => GeneralHelper.IsItemOwned(item)
                    && rule.AllowedTags != null && item.IsOneOfTags(rule.AllowedTags.ToList());
                break;
            case AdmissionKind.SourceOnly:
            case AdmissionKind.Unsupported:
                model.mayInventoryAddItemFunc = (_, _) => false;
                break;
            default: throw new NotSupportedException($"No game-model fixture for {rule.Kind}.");
        }
    }

    internal void Execute(PackPlan plan)
    {
        Assert.Equal(initial.StateId, plan.InitialStateId);
        foreach (var step in plan.Steps)
        {
            var item = Items[step.SourceItemId];
            var destination = Inventories[step.Destination.InventoryId];
            var source = initial.Inventories.Single(i => Inventories[i.Id] == item.parentInventory);
            var target = initial.Inventories.Single(i => Inventories[i.Id] == destination);
            Assert.True(IsPackingStorage(source) && IsPackingStorage(target), "Packing cannot reconfigure equipment.");
            Assert.Null(initial.Definitions[item.identifier].Rules.UnsupportedTransfer);
            AssertSupportedTransfer(source);
            AssertSupportedTransfer(target);
            Assert.False(item.IsTag("LOCKED_TAG") || item.IsTag("MODULE_STUCK_TAG")
                || item.IsTag("DEBRIS_PILE_TAG") || item.IsTag("ITEM_BEHIND_LOCK_TAG"));
            Assert.DoesNotContain(Items.Values, child => child.IsTag("PINNED_TAG")
                && (ReferenceEquals(child, item) || GraphUtils.FindParentEquals(child, item)));
            Assert.True(GeneralHelper.IsItemEffectivelyOwned(item) && item.MayRemove(), $"Cannot remove {step.SourceItemId}.");
            var slot = destination.TryInventorySlot(item, item.unitCount, At(item, step.Destination));
            Assert.NotNull(slot);
            Assert.Null(slot.targetItem);
            Assert.True(slot.numTransfer >= item.unitCount,
                $"Game model refused {step.SourceItemId} -> {step.Destination.InventoryId}.");
            int count = item.unitCount;
            Assert.Equal(count, slot.TryAcceptOnce(count));
            Assert.Equal(count, item.unitCount);
            Assert.Same(destination, item.parentInventory);
            Assert.Equal(step.Destination, Location(item));
        }
    }

    private bool IsPackingStorage(InventoryState inventory) => factoryStorage.Contains(inventory.Id) || inventory.OwnerItemId == null
        || inventory.Admission.Kind is AdmissionKind.Container or AdmissionKind.Toolbox or AdmissionKind.MachineBay;

    private void AssertSupportedTransfer(InventoryState inventory)
    {
        // A missing classification cannot override a known vanilla factory in the independent runner.
        if (factoryStorage.Contains(inventory.Id)
            && inventory.UnsupportedTransfer == "Equipment or unclassified inventory is not packing storage.") return;
        Assert.Null(inventory.UnsupportedTransfer);
    }

    internal long ReturnedValue => Items.Values.Where(IsReturned).Sum(item =>
        checked(PackValue.UnitValue(item.identifier, item.GetValue(singleValue: true)) * item.unitCount));

    private bool IsReturned(GameItem item)
    {
        while (item.parentInventory.GetParentItem() is GameItem parent) item = parent;
        return new[] { initial.Roots.Backpack, initial.Roots.Left, initial.Roots.Right }
            .Any(id => Inventories[id] == item.parentInventory);
    }

    internal PackState Snapshot() => new(initial.StateId, initial.Roots, initial.Items.Select(original =>
    {
        var item = Items[original.Id];
        Assert.Equal(original.UnitCount, item.unitCount);
        Assert.Equal(original.UnitValue, item.unitValue);
        Assert.Equal(original.UnitBaseValue, item.unitBaseValue);
        Assert.True(StateComparison.SameFacts(initialFacts[original.Id], Facts(item)), $"Facts changed for {original.Id}.");
        var shape = new ShapeState(item.shape.width, item.shape.height,
            Enumerable.Range(0, item.shape.width * item.shape.height).Select(n => item.shape.GetLocal(n % item.shape.width, n / item.shape.width)),
            item.shape.GetLocal(-1, -1));
        return new ItemState(original.Id, item.identifier, Location(item), item.unitCount, item.unitValue,
            shape, original.State, item.unitBaseValue);
    }), initial.Inventories, initial.Definitions.Values);

    private ItemLocation Location(GameItem item)
    {
        string id = Inventories.Single(pair => pair.Value == item.parentInventory).Key;
        if (item.parentInventory is GameSlotInventory) return new(id, new Placement.Slot());
        var shape = item.modifiedShape;
        var a = shape.ForwardTransform(item.modifiedXOrigin, item.modifiedYOrigin);
        var b = shape.ForwardTransform(item.modifiedXOrigin + item.shape.width - 1, item.modifiedYOrigin + item.shape.height - 1);
        return new(id, new Placement.Grid(Math.Min(a.Item1, b.Item1), Math.Min(a.Item2, b.Item2), shape.orientation, shape.flipped));
    }

    private GridShape At(GameItem item, ItemLocation location)
    {
        if (location.Placement is not Placement.Grid grid)
            return new GridShapeBuilder(item.modifiedShape).SetTransform(0, 0, false,
                ((GameSlotInventory)Inventories[location.InventoryId]).defaultOrientation);
        var shape = new GridShapeBuilder(item.modifiedShape).SetTransform(0, 0, grid.Flipped, grid.Orientation);
        var a = ((GridShape)shape).ForwardTransform(item.modifiedXOrigin, item.modifiedYOrigin);
        var b = ((GridShape)shape).ForwardTransform(item.modifiedXOrigin + item.shape.width - 1, item.modifiedYOrigin + item.shape.height - 1);
        return shape.SetPosition(grid.X - Math.Min(a.Item1, b.Item1), grid.Y - Math.Min(a.Item2, b.Item2));
    }

    private static GridShape Shape(ShapeState shape) => new GridShapeBuilder().SetData(shape.Cells.ToArray(), shape.Width)
        .SetDataOutside(shape.OutsideBounds);

    private static ItemFacts Facts(GameItem item) => new(item.state.dict
        .Where(p => p.Value.valueEnabled || p.Value.valueInt != 0)
        .Select(p => new KeyValuePair<string, TagValue>(p.Key, new(p.Value.valueEnabled, p.Value.valueInt))), item.itemTypes);

    internal static PackState Replay(PackState initial, PackPlan plan)
    {
        var model = new ModelPackReplay(initial);
        model.Execute(plan);
        return model.Snapshot();
    }

    internal static bool IsReturned(PackState state, string itemId)
    {
        var model = new ModelPackReplay(state);
        return model.IsReturned(model.Items[itemId]);
    }
}
