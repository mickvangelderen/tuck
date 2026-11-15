namespace Tuck.Mod.Tests;

public class HoldRingPresenterTests
{
    [Fact]
    public void NoPendingHoldsMeansNoRing()
    {
        Assert.Null(HoldRingPresenter.GetProgress([], null, 10f));
    }

    [Theory]
    [InlineData(9f, 1f, 0f)]
    [InlineData(10f, 1f, 0f)]
    [InlineData(10.5f, 1f, 0.5f)]
    [InlineData(12f, 1f, 1f)]
    [InlineData(10f, 0f, 1f)]
    public void NativeHoldProgressIsClampedAndZeroDurationIsReady(float now, float requiredSeconds, float expected)
    {
        Assert.Equal(expected, HoldRingPresenter.GetProgress([], new PendingHold(10f, requiredSeconds), now));
    }
}
