using System;
using System.Collections.Generic;
using System.Threading;

var payloads = new List<byte[]>();

for (var i = 0; i < 50_000; i++)
{
    payloads.Add(new byte[256]);
}

Console.WriteLine(payloads.Count.ToString());
Thread.Sleep(15000);
GC.KeepAlive(payloads);
