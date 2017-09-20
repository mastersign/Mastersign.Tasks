using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mastersign.Tasks.Test
{
    static class StateAssertions
    {
        public static void AssertState(WorkerThread wt,
            bool isDisposed, bool isAlive, bool busy)
        {
            Assert.AreEqual(isDisposed, wt.IsDisposed, $"The disposed state of the {nameof(WorkerThread)} is unexpected.");
            Assert.AreEqual(isAlive, wt.IsAlive, $"The alive state of the {nameof(WorkerThread)} is unexpected.");
            Assert.AreEqual(busy, wt.Busy, $"The busy state of the {nameof(WorkerThread)} is unexpected.");
        }

        public static void AssertState(WorkingLine wl,
            bool isDisposed, bool busy)
        {
            Assert.AreEqual(isDisposed, wl.IsDisposed, $"The disposed state of the {nameof(WorkerThread)} is unexpected.");
            Assert.AreEqual(busy, wl.Busy, $"The busy state of the {nameof(WorkingLine)} is unexpected.");
        }

        public static void AssertThreadsState(WorkingLine wl,
            bool isDisposed, bool isAlive, bool busy)
        {
            foreach (var wt in wl.WorkerThreads)
            {
                AssertState(wt, isDisposed, isAlive, busy);
            }
        }
    }
}
