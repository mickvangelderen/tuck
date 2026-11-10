// Ported from dependencies/probably-stolen/decompiled/GameInventory.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Total inventory value calculation unchanged, including traversal through nested containers.
#nullable disable
public abstract partial class GameInventory
{
    public long GetTotalValue(List<GameCharacterItem> party = null, GameCharacterItem vendor = null)
    {
        return (from item in GraphUtils.FindAllChildrenType<GameItem>(this)
                select item.GetValue(party, vendor)).DefaultIfEmpty(0L).Sum();
    }
}
