// Signature subset for GetValue/GetTotalValue. Character/vendor simulation is not implemented.
#nullable disable
public abstract class GameCharacterItem : GameItem
{
    public static string TAG_NOT_PURCHASED = "not_purchased";
    protected GameCharacterItem(string identifier) : base(identifier) { }
}
