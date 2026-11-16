using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine.UI;

namespace Tuck.Mod.Tests;

public class PanelButtonsTests
{
    private static Button Button() => (Button)RuntimeHelpers.GetUninitializedObject(typeof(Button));

    [Theory]
    [InlineData(typeof(NegociationUIManager), "OnBuyAllClick", "Accept")]
    [InlineData(typeof(NegociationUIManager), "OnSellAllClick", "Accept")]
    [InlineData(typeof(NegociationUIManager), "OnWholesaleClick", "Accept")]
    [InlineData(typeof(NegociationUIManager), "OnOfferAcceptClick", "Accept")]
    [InlineData(typeof(NegociationUIManager), "OnConfirmPanelYes", "Accept")]
    [InlineData(typeof(NegociationUIManager), "OnConfirmPanelNo", "Decline")]
    [InlineData(typeof(InspectionUIManager), "OnAppraisalServiceEstimated", "Accept")]
    [InlineData(typeof(InspectionUIManager), "OnConfront", "Accept")]
    [InlineData(typeof(GenericConfirmationUIManager), "ConfirmButtonPressed", "Accept")]
    [InlineData(typeof(GenericConfirmationUIManager), "CancelButtonPressed", "Decline")]
    [InlineData(typeof(ConfirmPanelUIManager), "OnYesClick", "Accept")]
    [InlineData(typeof(ConfirmPanelUIManager), "OnNoClick", "Decline")]
    [InlineData(typeof(NegociationUIManager), "CloseUI", "None")]
    [InlineData(typeof(InspectionUIManager), "CloseUI", "None")]
    [InlineData(typeof(InspectionUIManager), "CloseConfrontPanel", "None")]
    public void ExistingActionsAreRecognizedWithoutTreatingCloseAsDecline(Type type, string method, string expected)
    {
        Assert.NotNull(AccessTools.DeclaredMethod(type, method));
        Assert.Equal(expected, PanelButtons.IdentifyAction(type, method).ToString());
        Assert.Equal(PanelAction.None, PanelButtons.IdentifyAction(typeof(PanelButtonsTests), method));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void BuyWinsAndWholesaleBecomesTheTargetWhenBuyIsUnavailable(bool buyAvailable, bool wholesaleAvailable)
    {
        var buy = Button();
        var wholesale = Button();
        var expected = buyAvailable ? buy : wholesaleAvailable ? wholesale : null;
        Assert.Same(expected, PanelButtons.PreferredTarget([(buy, buyAvailable, false), (wholesale, wholesaleAvailable, true)]));
        Assert.Same(expected, PanelButtons.PreferredTarget([(wholesale, wholesaleAvailable, true), (buy, buyAvailable, false)]));
    }

    [Fact]
    public void ButtonDiscoveryPatchesMatchTheGame()
    {
        foreach (var method in typeof(PanelButtons).GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
        {
            var patch = method.GetCustomAttribute<HarmonyPatch>();
            if (patch == null) continue;
            Assert.NotNull(AccessTools.DeclaredMethod(patch.info.declaringType, patch.info.methodName));
        }
    }
}
