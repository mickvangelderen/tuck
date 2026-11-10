using System.Reflection;
using HarmonyLib;

namespace Tuck.Mod.Tests;

public class HarmonyDiscoveryTests
{
    [Fact]
    public void HelpersAreNotAccidentallyDiscoveredAsLifecycleHooks()
    {
        var harmony = new Harmony("tuck.tests.discovery");
        var hookField = typeof(PatchClassProcessor).GetField("auxilaryMethods",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var patchTypes = typeof(ModEntry).Assembly.GetTypes()
            .Where(type => type.IsDefined(typeof(HarmonyPatch), inherit: false)).ToArray();
        Assert.Contains(typeof(DragAmounts), patchTypes);
        foreach (var type in patchTypes)
        {
            // Run the game's actual Harmony discovery without installing native detours
            // in the .NET test host. Prepare/Cleanup/TargetMethod(s) are reserved names.
            var processor = harmony.CreateClassProcessor(type);
            var hooks = (Dictionary<Type, MethodInfo>)hookField.GetValue(processor)!;
            foreach (var (attribute, method) in hooks)
                Assert.True(method.IsDefined(attribute, inherit: false),
                    $"{type.FullName}.{method.Name} was implicitly discovered as {attribute.Name}. " +
                    "Rename the helper, or explicitly annotate an intentional lifecycle hook.");
        }
    }
}
