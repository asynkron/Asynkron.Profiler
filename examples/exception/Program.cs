using System;

var handled = 0;

for (var i = 0; i < 20_000; i++)
{
    try
    {
        if (i % 3 == 0)
        {
            throw new InvalidOperationException("boom");
        }

        if (i % 7 == 0)
        {
            throw new ArgumentException("bad");
        }
    }
    catch (InvalidOperationException)
    {
        handled += 1;
    }
    catch (ArgumentException)
    {
        handled += 2;
    }
}

Console.WriteLine(handled.ToString());
