// Ported from dependencies/probably-stolen/decompiled/InvokeFuncExtensions.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Original data/geometry semantics; Unity serialization attributes and diagnostic logging removed.
#nullable disable
using System;

public static class InvokeFuncExtensions
{
    public static bool BoolReduceOR(bool a, bool b)
    {
        return a | b;
    }

    public static bool BoolReduceAND(bool a, bool b)
    {
        return a & b;
    }

    public static int BoolReduceMax(int a, int b)
    {
        return Math.Max(a, b);
    }

    public static int BoolReduceMin(int a, int b)
    {
        return Math.Min(a, b);
    }

    public static R InvokeAllReduce<R>(this Func<R> func, Func<R, R, R> reduce)
    {
        Delegate[] invocationList = func.GetInvocationList();
        R val = ((Func<R>)invocationList[0])();
        for (int i = 1; i < invocationList.Length; i++)
        {
            val = reduce(val, ((Func<R>)invocationList[i])());
        }
        return val;
    }

    public static R InvokeAllReduce<T0, R>(this Func<T0, R> func, T0 p0, Func<R, R, R> reduce)
    {
        Delegate[] invocationList = func.GetInvocationList();
        R val = ((Func<T0, R>)invocationList[0])(p0);
        for (int i = 1; i < invocationList.Length; i++)
        {
            val = reduce(val, ((Func<T0, R>)invocationList[i])(p0));
        }
        return val;
    }

    public static R InvokeAllReduce<T0, T1, R>(this Func<T0, T1, R> func, T0 p0, T1 p1, Func<R, R, R> reduce)
    {
        Delegate[] invocationList = func.GetInvocationList();
        R val = ((Func<T0, T1, R>)invocationList[0])(p0, p1);
        for (int i = 1; i < invocationList.Length; i++)
        {
            val = reduce(val, ((Func<T0, T1, R>)invocationList[i])(p0, p1));
        }
        return val;
    }

    public static R InvokeAllReduce<T0, T1, T2, R>(this Func<T0, T1, T2, R> func, T0 p0, T1 p1, T2 p2, Func<R, R, R> reduce)
    {
        Delegate[] invocationList = func.GetInvocationList();
        R val = ((Func<T0, T1, T2, R>)invocationList[0])(p0, p1, p2);
        for (int i = 1; i < invocationList.Length; i++)
        {
            val = reduce(val, ((Func<T0, T1, T2, R>)invocationList[i])(p0, p1, p2));
        }
        return val;
    }

    public static R InvokeAllReduce<T0, T1, T2, T3, R>(this Func<T0, T1, T2, T3, R> func, T0 p0, T1 p1, T2 p2, T3 p3, Func<R, R, R> reduce)
    {
        Delegate[] invocationList = func.GetInvocationList();
        R val = ((Func<T0, T1, T2, T3, R>)invocationList[0])(p0, p1, p2, p3);
        for (int i = 1; i < invocationList.Length; i++)
        {
            val = reduce(val, ((Func<T0, T1, T2, T3, R>)invocationList[i])(p0, p1, p2, p3));
        }
        return val;
    }

    public static R InvokeAllReduce<T0, T1, T2, T3, T4, R>(this Func<T0, T1, T2, T3, T4, R> func, T0 p0, T1 p1, T2 p2, T3 p3, T4 p4, Func<R, R, R> reduce)
    {
        Delegate[] invocationList = func.GetInvocationList();
        R val = ((Func<T0, T1, T2, T3, T4, R>)invocationList[0])(p0, p1, p2, p3, p4);
        for (int i = 1; i < invocationList.Length; i++)
        {
            val = reduce(val, ((Func<T0, T1, T2, T3, T4, R>)invocationList[i])(p0, p1, p2, p3, p4));
        }
        return val;
    }

    public static R InvokeAllReduce<T0, T1, T2, T3, T4, T5, R>(this Func<T0, T1, T2, T3, T4, T5, R> func, T0 p0, T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, Func<R, R, R> reduce)
    {
        Delegate[] invocationList = func.GetInvocationList();
        R val = ((Func<T0, T1, T2, T3, T4, T5, R>)invocationList[0])(p0, p1, p2, p3, p4, p5);
        for (int i = 1; i < invocationList.Length; i++)
        {
            val = reduce(val, ((Func<T0, T1, T2, T3, T4, T5, R>)invocationList[i])(p0, p1, p2, p3, p4, p5));
        }
        return val;
    }

    public static R InvokeAllReduce<T0, T1, T2, T3, T4, T5, T6, R>(this Func<T0, T1, T2, T3, T4, T5, T6, R> func, T0 p0, T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, Func<R, R, R> reduce)
    {
        Delegate[] invocationList = func.GetInvocationList();
        R val = ((Func<T0, T1, T2, T3, T4, T5, T6, R>)invocationList[0])(p0, p1, p2, p3, p4, p5, p6);
        for (int i = 1; i < invocationList.Length; i++)
        {
            val = reduce(val, ((Func<T0, T1, T2, T3, T4, T5, T6, R>)invocationList[i])(p0, p1, p2, p3, p4, p5, p6));
        }
        return val;
    }

    public static R InvokeAllReduce<T0, T1, T2, T3, T4, T5, T6, T7, R>(this Func<T0, T1, T2, T3, T4, T5, T6, T7, R> func, T0 p0, T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, Func<R, R, R> reduce)
    {
        Delegate[] invocationList = func.GetInvocationList();
        R val = ((Func<T0, T1, T2, T3, T4, T5, T6, T7, R>)invocationList[0])(p0, p1, p2, p3, p4, p5, p6, p7);
        for (int i = 1; i < invocationList.Length; i++)
        {
            val = reduce(val, ((Func<T0, T1, T2, T3, T4, T5, T6, T7, R>)invocationList[i])(p0, p1, p2, p3, p4, p5, p6, p7));
        }
        return val;
    }
}

