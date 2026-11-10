// Ported from dependencies/probably-stolen/decompiled/GraphNodeWindow.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Original data/geometry semantics; Unity serialization attributes and diagnostic logging removed.
#nullable disable
using System.Collections.Generic;

public interface GraphNodeWindow : GraphNodeStorage
{
    List<GameItem> parentItems { get; }

    GraphNodeStorage child { get; }

    int xPadding { get; }

    int yPadding { get; }
}

