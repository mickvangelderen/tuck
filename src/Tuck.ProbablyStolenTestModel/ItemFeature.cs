// Data subset of ItemFeature.cs; valuation methods are in ItemFeature.Port.cs.
// Conditions may supply values, but live condition activation actions are explicitly unsupported.
#nullable disable
public partial class ItemFeature
{
    public enum FeatureType { Normale, Event, Special, Temporary, TemporaryBuying, TemporarySelling, ClientTemporary }
    public enum ValueStage { Innate, Market, Final }
    public FeatureType featureType;
    public ValueStage valueStage;
    public bool isDisabled;
    public string identifier { get; set; }
    public string category { get; set; } = "";
    public ItemCondition fakeCondition;
    public ItemCondition realCondition;
    public bool useCondition;
    private int valueModifier;
    private bool isFeatureExposed;
    private bool usePreExposeValue;
    private int preExposeValueModifier;
    private static readonly HashSet<string> legacyFinalStageIds = new()
    {
        "bargainDiscount", "bargainMarkup", "bargainBuyingMarkup", "bargainBuyingDiscount",
        "blackmailDiscount", "blackmailMarkup", "threatenDiscount", "threatenMarkup"
    };
    public ItemFeature() { }
    public ItemFeature(string category) => this.category = category;
    public ItemFeature(FeatureType featureType) => this.featureType = featureType;
    public void SetExposed(bool value)
    {
        if (useCondition && (value ? realCondition : fakeCondition).HasOnActivateAction())
            throw new NotSupportedException("Live item-condition activation actions are not modeled.");
        isFeatureExposed = value;
    }
}

// Fields consumed by ItemFeature's original value logic. Other condition behavior is out of scope.
public class ItemCondition
{
    public string identifier;
    public int modValue;
    public string onActivateActionId;
    public bool HasOnActivateAction() => !string.IsNullOrEmpty(onActivateActionId);
}
