using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Mastersign.Tasks
{
    public static class TaskDebug
    {
        public static Stopwatch Stopwatch { get; set; }

        [Conditional("VERBOSE")]
        public static void Verbose(string message)
        {
            var t = Stopwatch?.Elapsed ?? TimeSpan.Zero;
            Debug.WriteLine($"{t.TotalMilliseconds:000000.000} [{System.Threading.Thread.CurrentThread.Name}] {message}");
        }
    }
}
