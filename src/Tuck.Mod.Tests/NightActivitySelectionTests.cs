using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine.UI;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class NightActivitySelectionTests
{
    [Fact]
    public void InvokesExistingTravelListenersOnlyWhenTheClickedDestinationIsSelected()
    {
        string modId = "tuck-night-activity-test-" + Guid.NewGuid().ToString("N");
        var configs = (Dictionary<string, ModConfig>)typeof(ModConfig)
            .GetField("configs", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var config = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: [modId], culture: null)!;
        configs.Add(modId, config);
        var selection = new NightActivitySelection(new Options(new ModManifest { ID = modId, Name = "Tuck" }));
        try
        {
            selection.Register();
            // Use the real button event without constructing Unity renderers.
            var frame = (TransitFrame)RuntimeHelpers.GetUninitializedObject(typeof(TransitFrame));
            frame.travelButton = (Button)RuntimeHelpers.GetUninitializedObject(typeof(Button));
            frame.travelButton.onClick = new Button.ButtonClickedEvent();
            var calls = new List<string>();
            frame.travelButton.onClick.AddListener(() => calls.Add("first listener"));
            frame.travelButton.onClick.AddListener(() => calls.Add("second listener"));
            var location = new DelegateAfterhourLocation { id = "clicked" };
            var previous = new DelegateAfterhourLocation { id = "previous" };

            NightActivitySelection.OnSelected(location, null, frame);
            NightActivitySelection.OnSelected(location, previous, frame);
            Assert.Empty(calls);

            NightActivitySelection.OnSelected(location, location, frame);
            Assert.Equal(new[] { "first listener", "second listener" }, calls);
        }
        finally
        {
            selection.Unregister();
            configs.Remove(modId);
        }
    }
}
