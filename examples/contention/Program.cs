using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

internal static class Program
{
    private const int Seconds = 3;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WorkWithLock(object gate, TimeSpan duration)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < duration)
        {
            lock (gate)
            {
                Thread.Sleep(2);
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunWorkers(int workers, int seconds)
    {
        var gate = new object();
        var start = new ManualResetEventSlim(false);
        var tasks = new List<Task>(workers);
        var duration = TimeSpan.FromSeconds(seconds);

        for (var i = 0; i < workers; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                start.Wait();
                WorkWithLock(gate, duration);
            }));
        }

        start.Set();
        Task.WaitAll(tasks.ToArray());
    }

    public static void Main()
    {
        var workerCount = Math.Max(4, Environment.ProcessorCount * 2);
        RunWorkers(workerCount, Seconds);

        Console.WriteLine($"Completed {workerCount} workers with contention.");
        Console.WriteLine($"Lock contention count: {Monitor.LockContentionCount}");
    }
}
