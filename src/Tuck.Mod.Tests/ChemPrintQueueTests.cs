namespace Tuck.Mod.Tests;

public class ChemPrintQueueTests
{
    [Fact]
    public void PrintsOnePerTickAndWaitsTenMillisecondsAfterTheActionFinishes()
    {
        double now = 0;
        int prints = 0;
        var queue = new ChemPrintQueue(() => now, () => { prints++; now += 0.1; return true; });
        queue.Enqueue(20);
        Assert.Equal(0, prints); // Enqueue never blocks the input callback to print.
        Assert.Equal(ChemPrintQueue.PrintState.Printing, queue.State);
        Assert.True(queue.Tick());
        Assert.Equal(1, prints);
        Assert.Equal(1, queue.Completed);
        Assert.Equal(20, queue.Total);
        Assert.False(queue.Tick());
        now = 0.109;
        Assert.False(queue.Tick());
        now = 0.111;
        Assert.True(queue.Tick());
        Assert.Equal(2, prints);

        now = 100; // A long frame or pause must never cause a catch-up loop.
        Assert.True(queue.Tick());
        Assert.Equal(3, prints);
    }

    [Fact]
    public void ButtonsAddToCurrentTotalWithoutResettingProgressOrDelay()
    {
        double now = 0;
        var queue = new ChemPrintQueue(() => now, () => true);
        queue.Enqueue(20);
        queue.Tick();
        queue.Enqueue(5);
        queue.Enqueue(1);
        Assert.Equal(1, queue.Completed);
        Assert.Equal(26, queue.Total);
        Assert.False(queue.Tick());
        for (int frame = 0; frame < 25; frame++)
        {
            now += 0.02;
            Assert.True(queue.Tick());
        }
        Assert.Equal(ChemPrintQueue.PrintState.Ready, queue.State);
        Assert.Equal(26, queue.Completed);
        Assert.False(queue.Tick());
        queue.Enqueue(5);
        Assert.Equal(0, queue.Completed);
        Assert.Equal(5, queue.Total);
    }

    [Fact]
    public void ExhaustionDropsRemainingWorkAndRefillingDoesNotAutomaticallyResume()
    {
        double now = 0;
        int materials = 2;
        var queue = new ChemPrintQueue(() => now, () =>
        {
            if (materials == 0) return false;
            materials--;
            return true;
        });
        queue.Enqueue(20);
        for (int frame = 0; frame < 3; frame++)
        {
            queue.Tick();
            now += 0.02;
        }
        Assert.Equal(2, queue.Completed);
        Assert.Equal(ChemPrintQueue.PrintState.Ready, queue.State);
        materials = 10;
        now = 10;
        Assert.False(queue.Tick());
        Assert.Equal(10, materials);
        queue.Enqueue(1);
        Assert.True(queue.Tick());
        Assert.Equal(9, materials);
        Assert.Equal(1, queue.Completed);
        Assert.Equal(1, queue.Total);
    }

    [Fact]
    public void ClosingCancelsAndReopeningDoesNotResumeWithoutAnotherClick()
    {
        double now = 0;
        int prints = 0;
        var queue = new ChemPrintQueue(() => now, () => { prints++; return true; });
        queue.Enqueue(5);
        queue.Tick();
        queue.Cancel();
        now = 100;
        Assert.Equal(ChemPrintQueue.PrintState.Ready, queue.State);
        Assert.False(queue.Tick());
        Assert.Equal(1, prints);
        now = 200; // More updates after reopening still cannot print the cancelled work.
        Assert.False(queue.Tick());
        Assert.Equal(1, prints);
        queue.Enqueue(1);
        Assert.True(queue.Tick());
        Assert.Equal(2, prints);
        Assert.False(queue.Tick());
    }

    [Fact]
    public void CancelDropsActiveWork()
    {
        var queue = new ChemPrintQueue(() => 0, () => throw new Exception("Cancelled work must not run"));
        queue.Enqueue(20);
        queue.Cancel(); // Recipe change, feature disable, or destruction.
        Assert.False(queue.Tick());
        Assert.Equal(ChemPrintQueue.PrintState.Ready, queue.State);
        Assert.Equal(0, queue.Total);
        Assert.Equal(0, queue.Completed);
    }

    [Fact]
    public void CallbackExceptionPropagatesWithoutAutomaticRetries()
    {
        var failure = new InvalidOperationException("Game callback failed");
        var queue = new ChemPrintQueue(() => 0, () => throw failure);
        queue.Enqueue(20);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => queue.Tick()));
        Assert.Equal(ChemPrintQueue.PrintState.Ready, queue.State);
        Assert.False(queue.Tick());
        Assert.Equal(0, queue.Completed);
    }
}
