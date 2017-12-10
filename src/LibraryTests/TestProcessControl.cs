using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mastersign.Tasks.Test
{
    public static class TestProcessControl
    {
        public const int MAX_CPU_COUNT = 8; // currently only 

        private static readonly int _randomCpuCount;

        static TestProcessControl()
        {
            var rand = new Random();
            _randomCpuCount = rand.Next(
                1, Math.Min(Environment.ProcessorCount, MAX_CPU_COUNT) + 1);
        }

        public static void SetProcessAffinity(int? cpuCount = null)
        {
            var n = Math.Min(cpuCount ?? _randomCpuCount, MAX_CPU_COUNT);
            var mask = (1L << n) - 1L;
            Console.WriteLine($"CPU AFFINITY COUNT: {n}");
            var proc = Process.GetCurrentProcess();
            proc.ProcessorAffinity = (IntPtr)mask;
        }
    }
}
