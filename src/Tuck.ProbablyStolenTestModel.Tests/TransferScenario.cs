namespace Tuck.ProbablyStolenTestModel.Tests;

// Uses only the original game's public API: also compiled against Assembly-CSharp during verification.
// Fixture construction stays outside this code, just as it will for shared algorithm source.
internal static class TransferScenario
{
    internal static int Move(GameItem item, GameInventory destination, int requested = -1)
        => GraphUtils.TryAcceptAll(destination, item, requested);

    internal static SlotMarker Place(GameItem item, GameInventory destination, int x, int y,
        int orientation = 0, bool flipped = false)
        => destination.TryInventorySlot(item, item.unitCount,
            new GridShapeBuilder(item.modifiedShape).SetTransform(x, y, flipped, orientation));
}
