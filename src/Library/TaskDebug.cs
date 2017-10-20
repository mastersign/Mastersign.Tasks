using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Mastersign.Tasks
{
    internal static class TaskDebug
    {
        [Conditional("VERBOSE")]
        public static void Verbose(string message)
        {
            Debug.WriteLine($"[{System.Threading.Thread.CurrentThread.Name}] {message}");
        }
    }
}
