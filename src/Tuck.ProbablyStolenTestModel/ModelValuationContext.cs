namespace Tuck.ProbablyStolenTestModel;

/// <summary>
/// Captured external inputs to the game's valuation formulas, not captured final prices.
/// Share one instance between fixture items to model the same market without global singletons.
/// Feature installation/removal by live clients is outside the model; capture itemFeatures as well.
/// </summary>
public sealed class ModelValuationContext
{
    public int RetailMarkup { get; set; }
    public bool ClientAcceptsMarkup { get; set; }
    public List<ModelMarketModifier> EventModifiers { get; } = new();
}

public sealed record ModelMarketModifier(string ItemTag, int Percent);
