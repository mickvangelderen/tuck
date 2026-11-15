using System.Reflection;
using HarmonyLib;

namespace Tuck.Mod.Tests;

public class HoldTimeoutTests
{
    [Theory]
    [InlineData(0.5f, 10.9f, false)] // Not ready yet.
    [InlineData(0.5f, 11.49f, false)]
    [InlineData(0.5f, 11.5f, true)]
    [InlineData(1.5f, 12.49f, false)]
    [InlineData(1.5f, 12.5f, true)]
    [InlineData(1.5f, 20f, true)] // Releasing after expiry must also cancel.
    [InlineData(null, 100f, false)]
    public void TimeoutStartsWhenReadyAndIncludesTheExpiryFrame(float? timeout, float now, bool expired)
    {
        Assert.Equal(expired, HoldTimeout.HasExpired(10f, 1f, timeout, now));
    }

    [Theory]
    [InlineData(typeof(VanillaHoldTimeouts))]
    [InlineData(typeof(VanillaHoldRing))]
    [InlineData(typeof(HoldRingPresenter))]
    public void HoldPatchesMatchTheGamesPrivateMethodsAndHoldFields(Type patchType)
    {
        foreach (var method in patchType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
        {
            var patch = method.GetCustomAttribute<HarmonyPatch>();
            if (patch == null) continue;
            var type = patch.info.declaringType;
            Assert.NotNull(AccessTools.DeclaredMethod(type, patch.info.methodName));
            foreach (var parameter in method.GetParameters().Where(p => p.Name!.StartsWith("___")))
            {
                var field = AccessTools.Field(type, parameter.Name![3..]);
                Assert.NotNull(field);
                Assert.Equal(parameter.ParameterType, field.FieldType);
            }
        }
        Assert.NotNull(AccessTools.DeclaredMethod(typeof(StoreBell), "CancelKeyHold"));
        Assert.NotNull(AccessTools.DeclaredMethod(typeof(Interactable), "CancelHold"));
    }
}
