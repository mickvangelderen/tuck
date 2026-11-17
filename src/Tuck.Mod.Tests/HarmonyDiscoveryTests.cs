using System.Reflection;
using HarmonyLib;

namespace Tuck.Mod.Tests;

public class HarmonyDiscoveryTests
{
    [Fact]
    public void LoaderPatchAllInstallsNothingBeforeSceneLoad()
    {
        var harmony = new Harmony("tuck.tests.startup");
        try
        {
            harmony.PatchAll(typeof(ModEntry).Assembly);
            Assert.Empty(harmony.GetPatchedMethods());
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    [Fact]
    public void EveryPatchIsRegisteredForSceneLoadAndHasAnExplicitGameMethod()
    {
        var registered = GamePatches.Types.Append(typeof(OverlappingKeyBindings)).ToArray();
        var methods = typeof(ModEntry).Assembly.GetTypes().SelectMany(AccessTools.GetDeclaredMethods)
            .Where(method => method.IsDefined(typeof(HarmonyPatch), inherit: false)).ToArray();
        Assert.NotEmpty(methods);
        Assert.Equal(registered.Length, registered.Distinct().Count());
        Assert.Equal(registered.OrderBy(type => type.FullName),
            methods.Select(method => method.DeclaringType!).Distinct().OrderBy(type => type.FullName));
        foreach (var method in methods)
        {
            var info = HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromMethod(method));
            var original = info.methodType == MethodType.Getter
                ? AccessTools.PropertyGetter(info.declaringType, info.methodName)
                : AccessTools.Method(info.declaringType, info.methodName, info.argumentTypes);
            Assert.True(original != null, $"Could not resolve the game method for {method.DeclaringType}.{method.Name}.");
        }
    }

    [Fact]
    public void HelpersAreNotAccidentallyDiscoveredAsLifecycleHooks()
    {
        var harmony = new Harmony("tuck.tests.discovery");
        var hookField = typeof(PatchClassProcessor).GetField("auxilaryMethods",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var patchTypes = GamePatches.Types.Append(typeof(OverlappingKeyBindings)).ToArray();
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
