using System.Reflection;
using System.Runtime.CompilerServices;

namespace Tuck.Mod.Tests;

// The game's shared config dictionary is not safe for concurrent fixture setup/cleanup.
[Collection("Game config")]
public class DocumentDiscardTests : IDisposable
{
    private readonly string _modId = "tuck-discard-test-" + Guid.NewGuid().ToString("N");
    private readonly Options _options;
    private readonly ModConfig _config;
    private static Dictionary<string, ModConfig> Configs =>
        (Dictionary<string, ModConfig>)typeof(ModConfig).GetField("configs", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;

    public DocumentDiscardTests()
    {
        // Use the game's config API without Unity's persistent-data path or disk writes.
        _config = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: [_modId], culture: null)!;
        Configs.Add(_modId, _config);
        _options = new Options(new ModManifest { ID = _modId, Name = "Tuck" });
        DocumentDiscard.Register(_options);
    }

    [Fact]
    public void DefaultsDiscardBothDocumentsAndDisablingModStopsDiscarding()
    {
        Assert.True(_options.DiscardAppraisalSlips.Value);
        Assert.True(_options.DiscardEventReceipts.Value);
        Assert.True(Selected("Appraisal Slip"));
        Assert.True(Selected("Event Receipt"));
        DocumentDiscard.Unregister();
        Assert.False(Selected("Appraisal Slip"));
        Assert.False(Selected("Event Receipt"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DocumentChoicesAreIndependent(bool appraisal, bool events)
    {
        Set("discard_appraisal_slips", appraisal);
        Set("discard_event_receipts", events);
        Assert.Equal(appraisal, Selected("Appraisal Slip"));
        Assert.Equal(events, Selected("Event Receipt"));

        Set("discard_appraisal_slips", false);
        Set("discard_event_receipts", false);
        Assert.False(Selected("Appraisal Slip"));
        Assert.False(Selected("Event Receipt"));
    }

    [Fact]
    public void MatchesLocalizedNamesButLeavesOtherDocumentsAlone()
    {
        Set("discard_appraisal_slips", true);
        Set("discard_event_receipts", true);
        Assert.True(DocumentDiscard.IsSelectedDocument(new FixtureItem("slip", "Évaluation"), "Évaluation", "Événement"));
        Assert.True(DocumentDiscard.IsSelectedDocument(new FixtureItem("slip", "Événement"), "Évaluation", "Événement"));
        Assert.False(Selected("Repo Slip"));
        Assert.False(Selected("Note"));
        Assert.False(Selected("Appraisal Slip", "appraisal_certificate"));
        Assert.False(Selected("Event Receipt", "procurement_slip"));
    }

    [Fact]
    public void MissingOrFullTrashLeavesDocumentIntactOnCounter()
    {
        var item = new FixtureItem("slip", "Appraisal Slip");
        var counter = Slot(item);
        DocumentDiscard.MoveToTrash(item, null);
        Assert.Same(counter, item.parentInventory);

        var occupant = new FixtureItem("other", "Other item");
        var fullTrash = Slot(occupant);
        DocumentDiscard.MoveToTrash(item, fullTrash);
        Assert.Same(counter, item.parentInventory);
        Assert.Same(item, counter.childItem);
        Assert.Same(occupant, fullTrash.childItem);
        Assert.Equal(1, item.unitCount);
    }

    [Fact]
    public void UnownedOrRemovalLockedDocumentsDoNotAttemptTransfer()
    {
        var item = new FixtureItem("slip", "Appraisal Slip");
        var counter = Slot(item);
        var trash = Raw<ProbeInventory>();
        counter.LockInv();
        DocumentDiscard.MoveToTrash(item, trash);
        counter.UnlockInv();
        item.modifiedState.DisableTag("IS_OWNED_TAG");
        DocumentDiscard.MoveToTrash(item, trash);
        Assert.Same(counter, item.parentInventory);
    }

    [Fact]
    public void AlreadyTrashedDocumentDoesNotAttemptAnotherTransfer()
    {
        var item = new FixtureItem("slip", "Appraisal Slip");
        var trash = Raw<ProbeInventory>();
        item.parentInventory = trash;
        DocumentDiscard.MoveToTrash(item, trash);
        Assert.Same(trash, item.parentInventory);
    }

    private static bool Selected(string name, string id = "slip") =>
        DocumentDiscard.IsSelectedDocument(new FixtureItem(id, name), "Appraisal Slip", "Event Receipt");

    private void Set(string key, bool value)
    {
        var entry = (ConfigEntry<bool>)_config.Get(key);
        var property = typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!;
        property.SetValue(entry, null);
        try { entry.Set(value); }
        finally { property.SetValue(entry, _config); }
    }

    private static T Raw<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static GameSlotInventory Slot(GameItem item)
    {
        var slot = Raw<GameSlotInventory>();
        typeof(GameSlotInventory).GetField("currentItem", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(slot, item);
        item.parentInventory = slot;
        return slot;
    }

    private sealed class FixtureItem : GameItem
    {
        public FixtureItem(string id, string title) : base(id)
        {
            name = title;
            unitCount = 1;
            state.EnableTag("IS_OWNED_TAG");
            modifiedState = state.Clone();
            modifiedShape = new GridShapeBuilder(1, 1).SetDataFill(1);
        }
        public override void Validate() { }
        public override void Destroy() => throw new InvalidOperationException("Documents must not be destroyed.");
    }

    private sealed class ProbeInventory : GameSlotInventory
    {
        public override SlotMarker TryFindOneValidInventorySlot(GameItem item) =>
            throw new InvalidOperationException("Transfer should not be attempted.");
    }

    public void Dispose()
    {
        DocumentDiscard.Unregister();
        Configs.Remove(_modId);
    }
}
