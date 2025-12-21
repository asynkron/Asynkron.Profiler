using System;
using System.Runtime.CompilerServices;

internal static class Program
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Fib(int n)
    {
        var a = 0;
        var b = 1;
        for (var i = 0; i < n; i++)
        {
            var tmp = a + b;
            a = b;
            b = tmp;
        }

        return a;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long CpuWork(int iterations)
    {
        long sum = 0;
        for (var i = 0; i < iterations; i++)
        {
            var n = (i % 30) + 1;
            sum += Fib(n);
        }

        return sum;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunWork()
    {
        var total = CpuWork(1_000_000);
        Console.WriteLine(total.ToString());
    }

    public static void Main()
    {
        RunWork();
    }
}
