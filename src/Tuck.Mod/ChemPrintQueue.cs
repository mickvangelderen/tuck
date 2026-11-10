using System;

namespace Tuck.Mod;

/// <summary>One print per update, with a real-time gap after the previous print finishes.</summary>
internal sealed class ChemPrintQueue
{
    internal enum PrintState { Ready, Printing }

    internal const double IntervalSeconds = 0.010;
    private readonly Func<double> _clock;
    private readonly Func<bool> _printOne;
    private double _nextPrint;

    internal PrintState State { get; private set; }
    internal long Completed { get; private set; }
    internal long Total { get; private set; }

    internal ChemPrintQueue(Func<double> clock, Func<bool> printOne)
    {
        _clock = clock;
        _printOne = printOne;
    }

    internal void Enqueue(int count)
    {
        if (count <= 0) return;
        if (State == PrintState.Ready)
        {
            Completed = Total = 0;
            State = PrintState.Printing;
        }
        Total = checked(Total + count);
    }

    internal bool Tick()
    {
        if (State != PrintState.Printing || _clock() < _nextPrint) return false;

        // A failed or throwing game callback stops the queue. Do not automatically retry it next frame.
        State = PrintState.Ready;
        if (_printOne())
        {
            Completed++;
            if (Completed < Total) State = PrintState.Printing;
        }
        // Sample after the synchronous game action: its execution time is not part of the 10 ms gap.
        _nextPrint = _clock() + IntervalSeconds;
        return true;
    }

    internal void Cancel()
    {
        State = PrintState.Ready;
        Completed = Total = 0;
    }
}
