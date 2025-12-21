using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

const int Seconds = 3;
var workers = Math.Max(4, Environment.ProcessorCount * 2);
var gate = new object();
var start = new ManualResetEventSlim(false);
var tasks = new List<Task>(workers);

for (var i = 0; i < workers; i++)
{
    tasks.Add(Task.Run(() =>
    {
        start.Wait();
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(Seconds))
        {
            lock (gate)
            {
                Thread.Sleep(2);
            }
        }
    }));
}

start.Set();
Task.WaitAll(tasks.ToArray());

Console.WriteLine($"Completed {workers} workers with contention.");
Console.WriteLine($"Lock contention count: {Monitor.LockContentionCount}");
