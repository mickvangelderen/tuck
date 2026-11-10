using HarmonyLib;

namespace Tuck.Mod;

/// <summary>Moves selected documents to the trash after the game gives them to the player.</summary>
[HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.TryAddDirectSellingItemToTable),
    new[] { typeof(GameItem), typeof(bool), typeof(bool), typeof(bool), typeof(int) })]
internal static class DocumentDiscard
{
    private static Options? _options;

    public static void Register(Options options) => _options = options;

    public static void Unregister() => _options = null;

    private static void Postfix(GameItem gameItem, bool isOwend, bool __result)
    {
        if (!__result || !isOwend || _options == null || gameItem.identifier != "slip" ||
            (!_options.DiscardAppraisalSlips.Value && !_options.DiscardEventReceipts.Value))
            return;

        // Both GiveAppraisalFee and StoreEvent.CreateAndAddSlip issue a generic "slip" through this method.
        // Match their localized names at issuance; other slips (notes, repossession, etc.) must stay put.
        if (!IsSelectedDocument(gameItem,
                LocHelper.GetLocalizedMechanic("appraisal_slip_name"),
                LocHelper.GetLocalizedMechanic("mech_intel_slip_name")))
            return;

        MoveToTrash(gameItem, EmporiumEntry.Instance?.trashcan?.contentWindow?.child as GameInventory);
    }

    internal static bool IsSelectedDocument(GameItem item, string appraisalSlipName, string eventReceiptName) =>
        _options != null && item.identifier == "slip" &&
        ((_options.DiscardAppraisalSlips.Value && item.name == appraisalSlipName) ||
         (_options.DiscardEventReceipts.Value && item.name == eventReceiptName));

    internal static void MoveToTrash(GameItem item, GameInventory? trash)
    {
        if (trash == null || item.parentInventory == trash || !GeneralHelper.IsItemOwned(item) || !item.MayRemove())
            return;

        // A missing/full/restricted trash inventory leaves the document where the game placed it.
        // Never destroy it or retry later: the player can retrieve and keep documents from the trash.
        GraphUtils.TryAcceptAll(trash, item);
    }
}
