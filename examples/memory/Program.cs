using System;
using System.Collections.Generic;

var total = 0L;
var buffers = new List<byte[]>(capacity: 10_000);

for (var i = 0; i < 20_000; i++)
{
    var buffer = new byte[1024];
    buffer[0] = (byte)(i % 256);
    buffers.Add(buffer);
    total += buffer[0];

    var text = new string('x', 200);
    if (text.Length == 0)
    {
        total++;
    }
}

Console.WriteLine(total.ToString());
