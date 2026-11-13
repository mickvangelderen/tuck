using System.Reflection;

namespace Tuck.Mod.Tests;

[Collection("Game config")]
public class AppraisalStepsTests
{
    [Fact]
    public void DisabledFeatureDoesNotAccessTheInspectionUI()
    {
        string modId = "tuck-appraisal-test-" + Guid.NewGuid().ToString("N");
        var configs = (Dictionary<string, ModConfig>)typeof(ModConfig)
            .GetField("configs", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var config = (ModConfig)Activator.CreateInstance(typeof(ModConfig),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [modId], null)!;
        configs.Add(modId, config);

        try
        {
            // A disabled postfix must return before inspecting any game object, including before Init.
            AppraisalSteps.Postfix(null!);

            var options = new Options(new ModManifest { ID = modId, Name = "Tuck" });
            var entry = (ConfigEntry<bool>)config.Get("appraisal_fifty_buttons");
            // Exercise real setting change notifications without writing a config file through Unity.
            typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Config))!.SetValue(entry, null);
            Assert.True(entry.Value);
            entry.Set(false);
            AppraisalSteps.Register(options);
            AppraisalSteps.Postfix(null!);

            entry.Set(true);
            entry.Set(false);
            AppraisalSteps.Postfix(null!);

            entry.Set(true);
            AppraisalSteps.Unregister();
            AppraisalSteps.Postfix(null!);

            entry.Set(false);
            entry.Set(true);
            AppraisalSteps.Postfix(null!);
        }
        finally
        {
            AppraisalSteps.Unregister();
            configs.Remove(modId);
        }
    }
}
