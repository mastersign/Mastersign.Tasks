using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mastersign.Tasks.Test
{
    static class Waiter
    {
        public static void WaitFor(Func<bool> criterion, int timeout)
        {
            var sw = new Stopwatch();
            sw.Start();
            while (!criterion())
            {
                if (sw.ElapsedMilliseconds > timeout)
                {
                    throw new TimeoutException();
                }
                Thread.Sleep(10);
            }
        }
    }
}
