// Ported from dependencies/probably-stolen/decompiled/GraphNodeStorage.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Original data/geometry semantics; Unity serialization attributes and diagnostic logging removed.
#nullable disable
using System.Collections.Generic;

public interface GraphNodeStorage
{
    List<GraphNodeStorage> parents { get; }

    List<GraphNodeStorage> children { get; }

    void Validate();

    bool MayHaveValidInventorySlot(GameItem item);

    SlotMarker TryFindOneValidInventorySlot(GameItem item);

    bool AddParent(GraphNodeStorage node);

    bool RemoveParent(GraphNodeStorage node);
}

